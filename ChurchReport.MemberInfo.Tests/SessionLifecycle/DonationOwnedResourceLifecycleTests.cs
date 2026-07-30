// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/SessionLifecycle/DonationOwnedResourceLifecycleTests.cs
// 測試責任：驗證 Donation session 資源的唯一 owner、併發冪等釋放與借用依賴隔離。
// 信任邊界：測試禁止連線 LINE 或 CRM；所有網路與 Factory 資源皆以未初始化實例或拒絕送出的 handler 隔離。
// 生命週期：正式 coordinator 必須先停止新 lease 並等待既有 caller drain，本檔只驗證 drain 後的最終 Dispose 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與 final CRLF。
// ============================================================================
using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.Models;
using ChurchReport.Tools;
using FluentAssertions;
using Line.Messaging;
using LineMessagingProcessor.Workflows;
using ToolUtilityNameSpace;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.SessionLifecycle;

/// <summary>
/// DonationPaymentManager 的既有 static configuration initializer 依賴 process-wide current directory。
/// 此 collection 禁止與其他測試平行，確保測試在極短暫切換目錄時不會污染其他 request、tenant 或測試的檔案解析邊界。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DonationOwnedResourceLifecycleCollection
{
    public const string Name = "Donation owned resource lifecycle";
}

/// <summary>
/// 驗證奉獻付款 session 物件只釋放自己建立的 LINE client 與同步閘門，且多執行緒重複呼叫不會重複清理。
/// 測試直接使用真實 production 類別，但跳過會讀取設定與建立 CRM 相依物件的建構式；反射只負責注入可觀測資源，
/// 不替 production 新增測試專用入口。LINE handler 的 <c>SendAsync</c> 永遠拒絕執行，因此任何失敗都不會外洩成真實網路呼叫。
/// </summary>
[Collection(DonationOwnedResourceLifecycleCollection.Name)]
public sealed class DonationOwnedResourceLifecycleTests : IDisposable
{
    private readonly string _originalDirectory;

    /// <summary>
    /// 在 xUnit 呼叫測試方法前先建立既有 static configuration 所需的工作目錄邊界。
    /// 測試類別位於不可平行 collection，且 <see cref="Dispose"/> 會在每個測試案例後確定還原，因此不會把 process-wide cwd 洩漏給其他測試。
    /// </summary>
    public DonationOwnedResourceLifecycleTests()
    {
        _originalDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(GetChurchReportProjectDirectory());
    }

    /// <summary>
    /// 還原測試進入前的 process-wide current directory。此 cleanup 不吞例外，若目錄無法還原應讓測試失敗，
    /// 因為殘留的 cwd 會破壞後續測試與跨 request 隔離，屬於必須立即阻擋的生命週期錯誤。
    /// </summary>
    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalDirectory);
    }

    /// <summary>
    /// 對外契約必須明確提供 <see cref="IDisposable"/>，讓 cache/coordinator 能以統一介面在 eviction、logout 與 host drain 時回收。
    /// 此測試不建立任何實例，因此不會碰觸設定檔、LINE token、CRM credential 或 Factory singleton。
    /// </summary>
    [Fact]
    public void Donation_session_resource_types_expose_disposable_contract()
    {
        typeof(DonationPaymentManager).Should().BeAssignableTo<IDisposable>();
        typeof(DonationFeePaymentProcessor).Should().BeAssignableTo<IDisposable>();
    }

    /// <summary>
    /// Manager 是其 LINE client 與 <see cref="SemaphoreSlim"/> 的唯一 owner；注入 workflow 與 Factory 提供的 ToolUtility 只是借用。
    /// 以固定 64 次平行 Dispose 製造競爭，驗證只有一個 caller 執行清理、兩個 owned resource 都失效，借用物件仍保持可用。
    /// 正式流程不允許在這個階段仍有 active caller；該前置條件由 coordinator 的 lease/drain 保證，而不是由 Manager 內部等待或逾時。
    /// </summary>
    [Fact]
    public void DonationPaymentManager_concurrent_dispose_releases_only_owned_resources_once()
    {
        var manager = CreateDonationPaymentManagerWithoutExternalConstruction();
        var lineProbe = OwnedLineClientProbe.Create();
        var feeRefreshLock = new SemaphoreSlim(1, 1);
        var borrowedWorkflow = new BorrowedLineWorkflow();
        var factoryOwnedToolUtility = CreateUninitialized<ToolUtilityClass>();
        GC.SuppressFinalize(factoryOwnedToolUtility);

        SetInstanceField(manager, "m_LineMessagingClient", lineProbe.Client);
        SetInstanceField(manager, "_feeRefreshLock", feeRefreshLock);
        SetInstanceField(manager, "m_LineNotificationWorkflow", borrowedWorkflow);
        SetInstanceField(manager, "m_LineReplyWorkflow", borrowedWorkflow);
        SetInstanceField(manager, "m_ToolUtilityClass", factoryOwnedToolUtility);

        try
        {
            var disposable = manager.Should().BeAssignableTo<IDisposable>().Subject;

            Parallel.For(0, 64, _ => disposable.Dispose());

            lineProbe.DisposeCount.Should().Be(1, "Manager 對自行建立的 LINE client 只能清理一次");
            IsDisposed(feeRefreshLock).Should().BeTrue("drain 後不應保留 session-owned semaphore handle");
            borrowedWorkflow.DisposeCount.Should().Be(0, "注入 workflow 的生命週期屬於呼叫端或 DI owner");
            ReadInstanceField<bool>(factoryOwnedToolUtility, "_disposed").Should().BeFalse(
                "ToolUtilityFactory 的共享實例不能由單一 session manager 關閉");
        }
        finally
        {
            // RED 階段 production 尚未清理資源時，由測試負責回收，避免測試本身造成 handler 或 semaphore retention。
            lineProbe.DisposeIfNeeded();
            DisposeIfNeeded(feeRefreshLock);
        }
    }

    /// <summary>
    /// 收費單付款處理器只擁有建構式內自行建立的 LINE client；ToolUtility 來自 Factory，不能隨單筆付款物件一起關閉。
    /// 平行呼叫同時驗證 double/concurrent Dispose，tracking handler 精確計數可防止表面上的無例外掩蓋重複釋放。
    /// 此路徑不執行付款 workflow，也不建立 request cancellation 或 timeout；清理是同步、常數時間且不得發出 LINE/CRM I/O。
    /// </summary>
    [Fact]
    public void DonationFeePaymentProcessor_concurrent_dispose_releases_owned_line_client_once()
    {
        var processor = CreateUninitialized<DonationFeePaymentProcessor>();
        var lineProbe = OwnedLineClientProbe.Create();
        var borrowedWorkflow = new BorrowedLineWorkflow();
        var factoryOwnedToolUtility = CreateUninitialized<ToolUtilityClass>();
        GC.SuppressFinalize(factoryOwnedToolUtility);

        SetInstanceField(processor, "m_LineMessagingClient", lineProbe.Client);
        SetInstanceField(processor, "m_PushUtility", new PushUtility(lineProbe.Client, borrowedWorkflow));
        SetInstanceField(processor, "m_ReplyUtility", new ReplyUtility(lineProbe.Client, borrowedWorkflow));
        SetInstanceField(processor, "m_ToolUtilityClass", factoryOwnedToolUtility);

        try
        {
            var disposable = processor.Should().BeAssignableTo<IDisposable>().Subject;

            Parallel.For(0, 64, _ => disposable.Dispose());

            lineProbe.DisposeCount.Should().Be(1, "processor 對自行建立的 LINE client 只能清理一次");
            borrowedWorkflow.DisposeCount.Should().Be(0, "注入 workflow 的生命週期屬於呼叫端或 DI owner");
            ReadInstanceField<bool>(factoryOwnedToolUtility, "_disposed").Should().BeFalse(
                "Factory-owned CRM dependency 必須由 Factory 的 bounded owner 統一回收");
        }
        finally
        {
            lineProbe.DisposeIfNeeded();
        }
    }

    /// <summary>
    /// 建立不執行建構式的真實 production 實例，避免建構式跨越 LINE token、設定檔與 CRM Factory 信任邊界。
    /// 僅限生命週期測試使用；所有實際被 Dispose 的 owned resource 都會在測試內明確注入並於 finally 補償回收。
    /// </summary>
    private static T CreateUninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }

    /// <summary>
    /// 既有 Manager 的型別初始化會從 current directory 讀取 appsettings.json；測試只把目錄切到 repository 內既有專案位置，
    /// 讓 static initializer 完成後立刻還原，不複製、不改寫也不輸出任何 credential。真正的 instance constructor 仍完全跳過，
    /// 因此不會建立 ToolUtility、Dynamics client、LINE request 或背景資源。不可平行 collection 是此 process-wide 操作的必要隔離措施。
    /// </summary>
    private static DonationPaymentManager CreateDonationPaymentManagerWithoutExternalConstruction()
    {
        return CreateUninitialized<DonationPaymentManager>();
    }

    private static string GetChurchReportProjectDirectory()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "SpeechMessageProducts.ChurchReport"));
    }

    /// <summary>
    /// 對 private/readonly backing field 注入測試資源。搜尋基底類別可涵蓋未來將欄位上移時的相容性，找不到則立即失敗，
    /// 避免測試因欄位改名而靜默跳過 ownership 驗證。
    /// </summary>
    private static void SetInstanceField(object target, string fieldOrPropertyName, object value)
    {
        var field = FindInstanceField(target.GetType(), fieldOrPropertyName)
            ?? FindInstanceField(target.GetType(), $"<{fieldOrPropertyName}>k__BackingField");

        field.Should().NotBeNull($"{target.GetType().Name} 必須保留 {fieldOrPropertyName} ownership 欄位");
        field!.SetValue(target, value);
    }

    /// <summary>
    /// 讀取 disposal sentinel 以確認借用物件未被越權關閉；只觀察狀態，不呼叫 CRM、檔案或網路 API。
    /// </summary>
    private static T ReadInstanceField<T>(object target, string fieldName)
    {
        var field = FindInstanceField(target.GetType(), fieldName);
        field.Should().NotBeNull($"{target.GetType().Name} 必須保留 {fieldName} lifecycle sentinel");
        return (T)field!.GetValue(target)!;
    }

    private static FieldInfo? FindInstanceField(Type type, string fieldName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>
    /// 以零逾時探測 semaphore 是否仍可進入；已 Dispose 時必須同步丟出 <see cref="ObjectDisposedException"/>。
    /// 若成功取得則立即 Release，不保留 permit，也不建立背景工作或計時器。
    /// </summary>
    private static bool IsDisposed(SemaphoreSlim semaphore)
    {
        try
        {
            if (semaphore.Wait(0))
            {
                semaphore.Release();
            }

            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static void DisposeIfNeeded(SemaphoreSlim semaphore)
    {
        if (!IsDisposed(semaphore))
        {
            semaphore.Dispose();
        }
    }

    /// <summary>
    /// 同時實作通知與回覆 workflow 的借用探針。所有執行方法都拒絕呼叫，確保 lifecycle 測試不可能誤送 LINE；
    /// Dispose 計數只用來偵測 owner 越界，production 正確行為應維持零次。
    /// </summary>
    private sealed class BorrowedLineWorkflow : ILineNotificationWorkflow, ILineReplyWorkflow, IDisposable
    {
        public int DisposeCount { get; private set; }

        public Task<LineNotificationResult> SendAsync(LineNotificationRequest request)
            => throw new InvalidOperationException("生命週期測試不得送出 LINE notification。");

        public Task SendOrThrowAsync(LineNotificationRequest request)
            => throw new InvalidOperationException("生命週期測試不得送出 LINE notification。");

        public Task<LineReplyResult> ReplyAsync(LineReplyRequest request)
            => throw new InvalidOperationException("生命週期測試不得送出 LINE reply。");

        public Task ReplyOrThrowAsync(LineReplyRequest request)
            => throw new InvalidOperationException("生命週期測試不得送出 LINE reply。");

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    /// <summary>
    /// 包裝真實 <see cref="LineMessagingClient"/> 與可計數 handler。測試把 client 的 ownership flag 設為 true，
    /// 模擬 production 使用 token-only 建構式自行建立 HttpClient 的語意；handler 永不送網路，且每次 Dispose 都會被精確記錄。
    /// </summary>
    private sealed class OwnedLineClientProbe
    {
        private readonly DisposeCountingHandler _handler;

        private OwnedLineClientProbe(LineMessagingClient client, DisposeCountingHandler handler)
        {
            Client = client;
            _handler = handler;
        }

        public LineMessagingClient Client { get; }

        public int DisposeCount => _handler.DisposeCount;

        public static OwnedLineClientProbe Create()
        {
            var handler = new DisposeCountingHandler();
            var httpClient = new HttpClient(handler, disposeHandler: true);
            var client = new LineMessagingClient(httpClient, "lifecycle-test-token", "https://line.invalid/v2");

            SetInstanceField(client, "_disposeClient", true);
            return new OwnedLineClientProbe(client, handler);
        }

        public void DisposeIfNeeded()
        {
            if (DisposeCount == 0)
            {
                Client.Dispose();
            }
        }
    }

    /// <summary>
    /// 不接受任何 HTTP 請求的 handler。若 production disposal 意外觸發 I/O，測試立即失敗；正常路徑只會同步增加 Dispose 計數。
    /// Interlocked 讓計數本身在錯誤的重複併發 Dispose 情境下仍可靠，不因測試探針 race 而漏報。
    /// </summary>
    private sealed class DisposeCountingHandler : HttpMessageHandler
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("生命週期測試不得呼叫真實 LINE HTTP endpoint。");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
            }

            base.Dispose(disposing);
        }
    }
}
