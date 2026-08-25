using ChurchReport.Controllers;
using Microsoft.Extensions.DependencyInjection;
using System.ServiceModel;
using System.Text.Json;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;
using Xunit;

namespace ToolUtility.Dataverse.Tests;

/// <summary>
/// 驗證 SaveIntegrate 背景 runner 的安全結果紀錄。
/// 測試以可控的 scope、DI 與上傳委派注入失敗，不連線 CRM、不建立 Session，並在每個案例結束時
/// 釋放暫存 Trace 檔與 DI provider，防止測試資源或跨案例 trace context 殘留。
/// </summary>
public sealed class SaveIntegrateBackgroundUploadRunnerTests
{
    /// <summary>
    /// 保護背景工作只有 upload 正常完成才能記錄 succeeded；scope 結束的 bg.end 不得被當成成功證明。
    /// </summary>
    [Fact]
    public async Task RunAsync_RecordsSucceededOnlyAfterUploadCompletes()
    {
        var records = await RunScenarioAsync(
            createScope: CreateScope,
            resolveProvider: _ => new TestToolUtilityProvider(),
            uploadAsync: static () => Task.CompletedTask,
            cleanup: static () => { });

        Assert.Contains(records, record => IsOutcome(record, "upload", "succeeded", string.Empty));
        Assert.DoesNotContain(records, record => record.GetProperty("ev").GetString() == "bg.end"
            && record.TryGetProperty("outcome", out _));
    }

    /// <summary>
    /// 保護建立背景 DI scope 失敗時留下 scope-create/failed，而非只留下無語意的 bg.end。
    /// 故障注入不包含例外文字；決定性斷言為固定 errorClass，避免敏感內容寫入 JSONL。
    /// </summary>
    [Fact]
    public async Task RunAsync_RecordsScopeCreationFailureWithoutExceptionText()
    {
        var records = await RunScenarioAsync(
            createScope: static () => throw new InvalidOperationException("scope password=secret"),
            resolveProvider: _ => new TestToolUtilityProvider(),
            uploadAsync: static () => Task.CompletedTask,
            cleanup: static () => { });

        Assert.Contains(records, record => IsOutcome(record, "scope-create", "failed", "dependency-resolution"));
        Assert.DoesNotContain(records, record => record.ToString().Contains("password=secret", StringComparison.Ordinal));
    }

    /// <summary>
    /// 保護 provider／ToolUtility 初始化失敗會保留精確 stage 與粗粒度 dependency-resolution 分類。
    /// </summary>
    [Fact]
    public async Task RunAsync_RecordsProviderResolutionFailure()
    {
        var records = await RunScenarioAsync(
            createScope: CreateScope,
            resolveProvider: static _ => throw new InvalidOperationException("provider credential=secret"),
            uploadAsync: static () => Task.CompletedTask,
            cleanup: static () => { });

        Assert.Contains(records, record => IsOutcome(record, "provider-resolve", "failed", "dependency-resolution"));
        Assert.DoesNotContain(records, record => record.ToString().Contains("credential=secret", StringComparison.Ordinal));
    }

    /// <summary>
    /// 保護 ToolUtility 實例建立失敗會標記 toolutility-resolve，而不會錯誤歸類成上傳失敗。
    /// </summary>
    [Fact]
    public async Task RunAsync_RecordsToolUtilityResolutionFailure()
    {
        var records = await RunScenarioAsync(
            createScope: CreateScope,
            resolveProvider: _ => new ThrowingToolUtilityProvider(),
            uploadAsync: static () => Task.CompletedTask,
            cleanup: static () => { });

        Assert.Contains(records, record => IsOutcome(record, "toolutility-resolve", "failed", "dependency-resolution"));
        Assert.DoesNotContain(records, record => record.GetProperty("ev").GetString() == "bg.outcome"
            && record.GetProperty("stage").GetString() == "upload");
    }

    /// <summary>
    /// 保護 CRM 上傳失敗會記錄 upload/failed/crm-fault，且不會偽造 upload succeeded。
    /// </summary>
    [Fact]
    public async Task RunAsync_RecordsUploadFailureWithoutSucceededOutcome()
    {
        var records = await RunScenarioAsync(
            createScope: CreateScope,
            resolveProvider: _ => new TestToolUtilityProvider(),
            uploadAsync: static () => Task.FromException(new CommunicationException("CRM payload=secret")),
            cleanup: static () => { });

        Assert.Contains(records, record => IsOutcome(record, "upload", "failed", "crm-fault"));
        Assert.DoesNotContain(records, record => IsOutcome(record, "upload", "succeeded", string.Empty));
        Assert.DoesNotContain(records, record => record.ToString().Contains("payload=secret", StringComparison.Ordinal));
    }

    /// <summary>
    /// 保護背景副本清理失敗可被獨立辨識，但不改變已完成 upload 的事實。
    /// </summary>
    [Fact]
    public async Task RunAsync_RecordsCleanupFailureSeparately()
    {
        var records = await RunScenarioAsync(
            createScope: CreateScope,
            resolveProvider: _ => new TestToolUtilityProvider(),
            uploadAsync: static () => Task.CompletedTask,
            cleanup: static () => throw new TimeoutException("cleanup member=secret"));

        Assert.Contains(records, record => IsOutcome(record, "upload", "succeeded", string.Empty));
        Assert.Contains(records, record => IsOutcome(record, "cleanup", "failed", "timeout"));
        Assert.DoesNotContain(records, record => record.ToString().Contains("member=secret", StringComparison.Ordinal));
    }

    /// <summary>
    /// 在 request trace 範圍內執行 runner，並讀回已完整 flush 的 JSONL 記錄。
    /// </summary>
    private static async Task<IReadOnlyList<JsonElement>> RunScenarioAsync(
        Func<IServiceScope> createScope,
        Func<IServiceProvider, IToolUtilityProvider> resolveProvider,
        Func<Task> uploadAsync,
        Action cleanup)
    {
        var directory = Path.Combine(Path.GetTempPath(), "saveintegrate-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var trace = new DataverseTrace(new DataverseTraceOptions
            {
                Enabled = true,
                Path = Path.Combine(directory, "dataverse-trace.jsonl"),
                QueueCapacity = 128,
                FlushInterval = TimeSpan.FromMilliseconds(10)
            }))
            using (trace.BeginRequest("saveintegrate-runner", "runner-user", sessionId: null))
            {
                trace.RecordBackgroundAccepted("runner-operation");
                await SaveIntegrateBackgroundUploadRunner.RunAsync(
                    "runner-operation",
                    trace,
                    createScope,
                    static _ => NoopDisposable.Instance,
                    resolveProvider,
                    uploadAsync,
                    cleanup,
                    static _ => { });
            }

            return Directory.EnumerateFiles(directory, "*.jsonl")
                .SelectMany(File.ReadLines)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonDocument.Parse(line).RootElement.Clone())
                .ToArray();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// 建立不含 CRM 服務的短命測試 scope，並把 root provider 的唯一擁有權交給回傳 scope。
    /// runner 的 using 會先釋放子 scope 再釋放 root provider，確保測試不遺留 ServiceProvider、
    /// scoped service 或跨案例 DI 狀態；測試只驗證背景結果紀錄，不連線 CRM。
    /// </summary>
    private static IServiceScope CreateScope() => new TestOwnedServiceScope();

    /// <summary>比對固定背景結果 schema，避免測試以例外文字或實作細節判斷結果。</summary>
    private static bool IsOutcome(JsonElement record, string stage, string outcome, string errorClass)
        => record.GetProperty("ev").GetString() == "bg.outcome"
            && record.GetProperty("stage").GetString() == stage
            && record.GetProperty("outcome").GetString() == outcome
            && record.GetProperty("errorClass").GetString() == errorClass;

    /// <summary>測試 provider 不保存 ToolUtility 或使用者狀態；runner 只需確認解析成功。</summary>
    private sealed class TestToolUtilityProvider : IToolUtilityProvider
    {
        public ToolUtilityClass GetToolUtility() => null!;
    }

    /// <summary>故障注入 ToolUtility 解析例外；例外訊息刻意含敏感樣本以驗證不會外洩。</summary>
    private sealed class ThrowingToolUtilityProvider : IToolUtilityProvider
    {
        public ToolUtilityClass GetToolUtility() => throw new InvalidOperationException("token=secret");
    }

    /// <summary>無狀態 ambient scope，驗證 runner 不把 scope provider 保存到工作結束後。</summary>
    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }

    /// <summary>
    /// 封裝測試建立的 DI root provider 與其唯一子 scope，提供明確且可重複呼叫的釋放順序。
    /// </summary>
    /// <remarks>
    /// <see cref="IServiceScope"/> 本身通常不擁有 root provider；若只回傳 CreateScope() 的結果，
    /// root provider 會失去釋放 owner。此型別由 runner 的 using 唯一持有，Dispose 時先釋放 scope
    /// 使 scoped 服務完成清理，再釋放 root provider，避免測試資源或服務參考留到下一個案例。
    /// </remarks>
    private sealed class TestOwnedServiceScope : IServiceScope
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private bool _disposed;

        /// <summary>
        /// 建立只含 DI 基礎服務的 root provider 與其子 scope；未註冊任何 CRM 或使用者資料服務。
        /// </summary>
        public TestOwnedServiceScope()
        {
            _provider = new ServiceCollection().BuildServiceProvider();
            _scope = _provider.CreateScope();
        }

        /// <summary>
        /// 提供目前短命 scope 的服務解析入口；不得由測試外部保存至 runner 結束後。
        /// </summary>
        public IServiceProvider ServiceProvider => _scope.ServiceProvider;

        /// <summary>
        /// 依先子 scope、後 root provider 的順序釋放所有測試 DI 資源；重複呼叫安全且無副作用。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}
