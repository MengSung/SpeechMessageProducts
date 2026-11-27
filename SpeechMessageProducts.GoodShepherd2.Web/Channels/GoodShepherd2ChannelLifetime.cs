/*
檔案整體說明
目的與定位：F3 主機內目錄及健康傳輸的唯一實際生命週期 owner，確保停止時先取消／排空資料與 HTTP 操作，再關閉健康 HttpClient。
主要功能與流程：建構時只借來源、時鐘與共用 diagnostics，取一次設定 key 建立固定 GoodShepherd2 目錄，最後清零建構暫存陣列。健康 client 禁 Cookie 與自動轉址、有限連線與十秒傳輸期限，不在建構或啟動時呼叫 LINE。Host 的 StoppingAsync 先排空目錄，因此後續 StopAsync 的 diagnostics 及容器的 manager 釋放前，已無在途目錄 lease。
使用邊界與維護：每個 owner 屬單一產品／組織，不保存 request、Session、使用者或 SDK client。關閉共用一次 Task，不因 Host 停止 token 已取消就放棄清理；目錄最多十分鐘快照、每操作十秒與有限並行的界線由目錄自己維護。原始 Exception 交同一診斷入口，不自行發送告警或建立背景佇列；清理 finally 仍釋放 client。DI 對目錄的 alias 重入 Dispose 共用目錄原關閉工作，不形成第二個 key owner。借入來源、時鐘、manager、diagnostics 均不由本檔 Dispose。離線 shutdown／restart 測試保護此順序，非實機結果。
*/
using System.Security.Cryptography;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Channels;

namespace SpeechMessageProducts.GoodShepherd2.Web.Channels;

/// <summary>同一 Host 的唯一關閉協調者，支援 Stop 與未 Start 即 Dispose，沒有自行啟動的背景工作。</summary>
internal sealed class GoodShepherd2ChannelLifetime : IHostedLifecycleService, IAsyncDisposable, IDisposable
{
    private readonly object gate = new();
    private readonly IChannelDiagnostics diagnostics;
    private HttpClient? client;
    private Task? closing;
    /// <summary>目錄自己擁有 key copy 與 timer；公開給 DI 的 alias 只重入同一次清理。</summary>
    internal DataverseChannelDirectory Directory { get; }
    /// <summary>只借用本 owner 的安全 client，F11 才註冊每日排程；本檔不自動執行檢查。</summary>
    internal ChannelHealthChecker HealthChecker { get; }

    /// <summary>先解析借入依賴再接管目錄，確保容器反向釋放時依賴仍存活；建構失敗亦排空候選並清 key。</summary>
    public GoodShepherd2ChannelLifetime(GoodShepherd2ChannelConfiguration configuration,
        IChannelDirectorySource source, IChurchClock clock, IChannelDiagnostics diagnostics)
    {
        this.diagnostics = diagnostics;
        var key = configuration.TakeKey();
        try
        {
            Directory = new DataverseChannelDirectory(GoodShepherd2Product.Name, configuration.OrganizationId,
                source, key, clock, diagnostics);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        try
        {
            client = new HttpClient(new SocketsHttpHandler
            {
                UseCookies = false, AllowAutoRedirect = false, MaxConnectionsPerServer = 16,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(5)
            }) { Timeout = TimeSpan.FromSeconds(10) };
            HealthChecker = new ChannelHealthChecker(Directory, client);
        }
        catch
        {
            try { Directory.Dispose(); }
            finally { client?.Dispose(); client = null; }
            throw;
        }
    }

    /// <summary>不預抓資料；只確認 caller 未取消，不讓正常啟動建立資料庫或 LINE 連線。</summary>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
    /// <summary>來源及目錄維持 lazy，不附帶啟動排程或健康檢查。</summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>啟動後無額外常駐工作或訂閱。</summary>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>所有 HostedService.StopAsync 前先拒絕目錄新操作並完整排空，不能先關借入 manager／diagnostics。</summary>
    public Task StoppingAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();
    /// <summary>即使啟動中途失敗而未走 lifecycle stopping，仍共用相同的完整清理工作。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();
    /// <summary>排空後不建立重試、timer 或補償工作。</summary>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>正常停止與容器 Dispose 共用一次 Task；gate 只保護工作發布，不阻塞在 I/O 上。</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate) return new ValueTask(closing ??= CloseAsync());
    }
    /// <summary>同步容器相容入口，不從 admitted 目錄操作內呼叫，避免等待自己的操作。</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>目錄取消、SDK／HTTP／timer 全數排空及 key 清零後才 Dispose client；不接管借用依賴。</summary>
    private async Task CloseAsync()
    {
        try { await Directory.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception)
        {
            diagnostics.Report(exception, "GoodShepherd2.Channels.Stop", CancellationToken.None);
            throw;
        }
        finally { Interlocked.Exchange(ref client, null)?.Dispose(); }
    }
}
