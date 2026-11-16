/*
 * 檔案整體說明
 * 目的與定位：P5-12 失敗彙總的離線 Host 契約，使用真正 DI scope、worker 與共用
 * ExceptionDiagnostics；只替換底層筆數來源與 LINE sender，不連 Dataverse 或 LINE。
 * 主要流程：驗缺省停用不註冊、每封閉窗單次通知、教會分開計數，以及正在查詢時
 * 停止須取消、排空並釋放 scope。四種輸出組合與落檔故障逐筆檢查同一安全 JSON；
 * sender 回呼讀已 flush 日誌，不能用最終檔案存在冒充通知前落檔的順序。
 * 資源與隔離：每案 fixture 是 DI container、診斷目錄與替身唯一 owner，先停止
 * worker、await diagnostics drain，再關閉容器／刪檔。無真秘密、Session、原文、
 * 非受控背景工作或跨案例可變狀態；故障來源在 finally 遞減 active 並標記 Dispose。
 */
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Hosting.Messaging;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Hosting.Tests.Diagnostics;

/// <summary>以真實 owner 驗資料隔離、flush 保序與生命週期，不驗替身自行模擬的通知。</summary>
public sealed class FailureSummaryTests
{
    /// <summary>區段不存在或明確 false 時沒有 worker／來源解析，不因缺其他設定阻止主機。</summary>
    [Fact]
    public void FailureSummary_Disabled_NotRegistered()
    {
        var services = new ServiceCollection();
        services.AddMessageFailureSummary(new ConfigurationBuilder().Build());
        Assert.Empty(services);
    }

    /// <summary>同一封閉窗重入只讀一次並通知一次；下一窗仍可發出新的獨立事件。</summary>
    [Fact]
    public async Task FailureSummary_OverThreshold_NotifiesOncePerWindow()
    {
        await using var f = new Fixture();
        f.Rows = new[] { new FailureSummaryCount(f.ChurchA, 11) };
        await f.Worker.RunWindowAsync(Fixture.End, CancellationToken.None);
        await f.Worker.RunWindowAsync(Fixture.End, CancellationToken.None);
        await f.Worker.RunWindowAsync(Fixture.End.AddMinutes(5), CancellationToken.None);
        await f.Diagnostics.DisposeAsync();
        Assert.Equal(2, f.Reads);
        Assert.Equal(2, f.Sent.Count);
        Assert.Equal(2, f.DisposedSources);
        foreach (var message in f.Sent)
        {
            using var json = JsonDocument.Parse(message);
            Assert.Equal(f.ChurchA, json.RootElement.GetProperty("ChurchId").GetGuid());
            Assert.Equal(11, json.RootElement.GetProperty("Count").GetInt64());
            Assert.Equal(7, json.RootElement.EnumerateObject().Count());
        }
    }

    /// <summary>教會 A 超過十、B 等於十，必須只通知 A，不把跨教會筆數相加或洩漏 B。</summary>
    [Fact]
    public async Task FailureSummary_ChurchACountsExcludeChurchB()
    {
        await using var f = new Fixture();
        f.Rows = new[] { new FailureSummaryCount(f.ChurchA, 11), new FailureSummaryCount(f.ChurchB, 10) };
        await f.Worker.RunWindowAsync(Fixture.End, CancellationToken.None);
        await f.Diagnostics.DisposeAsync();
        var message = Assert.Single(f.Sent);
        Assert.DoesNotContain(f.ChurchB.ToString("D"), message, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(message);
        Assert.Equal(11, json.RootElement.GetProperty("Count").GetInt64());
        Assert.Equal(Fixture.End.AddMinutes(-5), json.RootElement.GetProperty("WindowStartUtc").GetDateTimeOffset());
        Assert.Equal(Fixture.End, json.RootElement.GetProperty("WindowEndUtc").GetDateTimeOffset());
    }

    /// <summary>來源重複正式教會 ID 時整批拒絕，不以 dictionary 覆寫或發出第一筆半套結果。</summary>
    [Fact]
    public async Task FailureSummary_DuplicateChurch_RejectsBeforePublication()
    {
        await using var f = new Fixture();
        f.Rows = new[] { new FailureSummaryCount(f.ChurchA, 11), new FailureSummaryCount(f.ChurchA, 12) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Worker.RunWindowAsync(Fixture.End, CancellationToken.None));
        await f.Diagnostics.DisposeAsync();
        Assert.Empty(f.Sent);
        Assert.False(File.Exists(Path.Combine(f.Directory, "Exception.log")));
        Assert.Equal(1, f.DisposedSources);
    }

    /// <summary>阻塞來源只在 caller 取消才結束，Stop 必須等待 finally 與 scope Dispose 完成。</summary>
    [Fact]
    public async Task FailureSummary_StopDrainsAndDisposes()
    {
        await using var f = new Fixture { Block = true };
        f.Ambient.Value = "synthetic caller context";
        await f.Worker.StartAsync(CancellationToken.None);
        await f.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Worker.StopAsync(CancellationToken.None);
        Assert.Equal(0, f.Active);
        Assert.Null(f.SeenAmbient);
        f.Ambient.Value = null;
        Assert.Equal(1, f.DisposedSources);
        Assert.Empty(f.Sent);
    }

    /// <summary>專用純值入口仍服從四種獨立開關，雙開先 flush；不建第二套通知 consumer。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task FailureSummary_OutputSwitches_UseSharedOrderedPipeline(bool write, bool send)
    {
        await using var f = new Fixture(write, send);
        Assert.Equal(write || send, f.Diagnostics.ReportFailureSummary(f.ChurchA, 11, Fixture.End.AddMinutes(-5), Fixture.End));
        await f.Diagnostics.DisposeAsync();
        Assert.Equal(send ? 1 : 0, f.Sent.Count);
        Assert.Equal(write, File.Exists(Path.Combine(f.Directory, "Exception.log")));
    }

    /// <summary>故意以檔案充當目錄，雙開落檔失敗不得傳 LINE；原始故障由既有 stderr 接管。</summary>
    [Fact]
    public async Task FailureSummary_FileFailure_PreventsLine()
    {
        var path = Path.GetTempFileName();
        var sends = 0;
        try
        {
            await using var diagnostics = new ExceptionDiagnostics(path);
            diagnostics.StartNotifications((_, _) => { sends++; return Task.CompletedTask; });
            Assert.False(diagnostics.ReportFailureSummary(Guid.NewGuid(), 11, Fixture.End.AddMinutes(-5), Fixture.End));
            await diagnostics.DisposeAsync();
            Assert.Equal(0, sends);
        }
        finally { File.Delete(path); }
    }

    /// <summary>每案共享的純測試 owner；來源可等待 caller token，但不能越過 scope 存活。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        internal static readonly DateTimeOffset End = new(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        internal readonly Guid ChurchA = Guid.NewGuid(), ChurchB = Guid.NewGuid();
        internal readonly string Directory = Path.Combine(Path.GetTempPath(), "p5-summary-" + Guid.NewGuid().ToString("N"));
        internal readonly ConcurrentQueue<string> Sent = new();
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IReadOnlyList<FailureSummaryCount> Rows = Array.Empty<FailureSummaryCount>();
        internal int Reads, Active, DisposedSources;
        internal bool Block;
        internal readonly AsyncLocal<string?> Ambient = new();
        internal string? SeenAmbient;
        internal readonly ExceptionDiagnostics Diagnostics;
        private readonly ServiceProvider provider;
        internal readonly FailureSummaryWorker Worker;

        /// <summary>真正組合根只持有同一診斷 owner；來源每輪由 DI 重新建立並處置。</summary>
        internal Fixture(bool write = true, bool send = true)
        {
            Diagnostics = new ExceptionDiagnostics(Directory, outputOptions: new ExceptionOutputOptions(write, send));
            Diagnostics.StartNotifications((message, _) =>
            {
                if (write)
                {
                    using var stream = new FileStream(Path.Combine(Directory, "Exception.log"), FileMode.Open,
                        FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    Assert.Contains(message, reader.ReadToEnd(), StringComparison.Ordinal);
                }
                Sent.Enqueue(message);
                return Task.CompletedTask;
            });
            var services = new ServiceCollection();
            services.AddSingleton(Diagnostics);
            services.AddScoped<IFailureSummarySource>(_ => new Source(this));
            services.AddMessageFailureSummary(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:FailureSummary:Enabled"] = "true", ["Messaging:FailureSummary:Threshold"] = "10"
            }).Build());
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            Worker = Assert.Single(provider.GetServices<IHostedService>().OfType<FailureSummaryWorker>());
        }

        /// <summary>正常與故障都先停止排空，再解除容器與本機檔案，重複診斷 Dispose 安全。</summary>
        public async ValueTask DisposeAsync()
        {
            await Worker.StopAsync(CancellationToken.None);
            await Diagnostics.DisposeAsync();
            await provider.DisposeAsync();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }

    /// <summary>只有當案純值與故障注入，Dispose 發生前須確定 Read 已排空。</summary>
    private sealed class Source(Fixture owner) : IFailureSummarySource, IDisposable
    {
        /// <summary>真 cancellation 等待用於停止測試，沒有自建 timer 或未觀測背景 Task。</summary>
        public async Task<IReadOnlyList<FailureSummaryCount>> ReadAsync(DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc, CancellationToken ct)
        {
            owner.SeenAmbient = owner.Ambient.Value;
            Interlocked.Increment(ref owner.Reads);
            Interlocked.Increment(ref owner.Active);
            owner.Started.TrySetResult();
            try
            {
                if (owner.Block) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return owner.Rows;
            }
            finally { Interlocked.Decrement(ref owner.Active); }
        }

        /// <summary>僅計數生命週期，不提早清空尚在讀取的集合；owner 最後刪除所有測試狀態。</summary>
        public void Dispose() { Assert.Equal(0, owner.Active); Interlocked.Increment(ref owner.DisposedSources); }
    }
}
