/*
檔案整體說明
目的與定位：主機的 Hosting／Localization 組合轉接，只使用既有共用 owner，不實作 LINE 業務或三項後續服務。
主要功能與流程：語系註冊先執行既有 ICU 及發布政策守門，再保存不可變 options；診斷註冊依獨立輸出開關建立共用 sender，ILogger 的 Error／Critical 與 endpoint catch 共用同一 diagnostics 入口。
使用邊界與維護：部署組態只由 Host 擁有，不接受 HTTP 切換 profile；sender 保存已驗部署快照。停止順序為 HTTP 關閉後 drain diagnostics，再 Dispose sender；服務容器釋放時可重入本清理，但不能建立第二個 consumer、保留 request 或自行傳送 LINE。測試四組輸出及本機安全日誌，不宣稱供應商送達。
*/
using SpeechMessage.Hosting;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.LineWebhook;

/// <summary>固定主機的語系啟動轉接；不建立翻譯後備快取或使用者文化全域狀態。</summary>
public static class LineWebhookRegistration
{
    /// <summary>借用受信任組態驗 ICU 及語系政策，發布 detached options；文字 catalog 的業務接入留後續事件處理包。</summary>
    /// <param name="services">Host Build 前唯一寫入的集合。</param>
    /// <param name="configuration">Host 擁有的部署設定，不保存到 options。</param>
    /// <param name="isProduction">由 Host Environment 判定，不能來自 request。</param>
    /// <returns>同一集合，無效政策立即拒絕。</returns>
    public static IServiceCollection AddSpeechMessageLocalization(
        this IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(services);
        StartupGuards.ValidateIcuMode();
        services.AddSingleton(StartupGuards.ValidateLocalizationConfiguration(configuration, isProduction));
        return services;
    }
}

/// <summary>診斷通知的 Host lifecycle 組合 owner，僅管理共用 sender 與共用 diagnostics 的先後釋放。</summary>
internal sealed class LineWebhookDiagnosticsRegistration(
    ExceptionDiagnostics diagnostics, ExceptionOutputOptions options, IConfiguration configuration) : IHostedService, IAsyncDisposable
{
    /// <summary>只在 SendLine 啟用時建立；沒有 request、使用者 profile 或跨 Host static owner。</summary>
    private LineExceptionSender? sender;
    /// <summary>Host 單一停止流程的冪等旗標；停止後不可重啟同一 owner。</summary>
    private bool stopped;

    /// <summary>依部署輸出快照接上唯一共用 sender；不啟用時完全不建立 client 或 consumer。</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.SendLine)
        {
            sender = new LineExceptionSender(configuration);
            diagnostics.StartNotifications(sender.SendAsync);
        }
        return Task.CompletedTask;
    }

    /// <summary>正常停止與啟動失敗皆使用同一 drain 路徑，不用已取消的停止 token 跳過必要清理。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    /// <summary>先排空共用最多六十四筆通知，再關閉 sender；finally 確保 drain 失敗也不漏 client。</summary>
    public async ValueTask DisposeAsync()
    {
        if (stopped) return;
        stopped = true;
        try { await diagnostics.DisposeAsync().ConfigureAwait(false); }
        finally { sender?.Dispose(); sender = null; }
    }
}
