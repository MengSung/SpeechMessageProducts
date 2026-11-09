/*
檔案整體說明
目的與定位：Hosting 唯一擁有的每日 SQL 保留期清理工作，啟動後等待一天才清，避免把啟動健康檢查誤當建表或資料變更。
主要功能與流程：單一迴圈依 TimeProvider 等一天，再呼叫有界批次清理；錯誤交共用 diagnostics 後繼續下一日，不重疊或建立無界重試。停止取消 timer／SQL，永遠等真正 worker drain，不因 Host token 已取消而提早回傳。
使用邊界與維護：只借用無事件狀態的 dedupe、clock 與 diagnostics，無 HttpContext、Session、租戶快取或憑證。Host DI 唯一 Dispose，關閉順序為停止工作、排空命令與連線、釋放 CTS；同步 Dispose 也委派同一排空，不能留下 fire-and-forget。每輪最多一萬列、三十秒取消預算，provider drain 的完成不能被宣稱為五秒內硬終止。
*/
using Microsoft.Extensions.Hosting;
using ToolUtilityNameSpace.Diagnostics;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Deduplication;

/// <summary>單一 hosted 工作 owner，Start／Stop／Dispose 共用同一受控狀態，不重建已關閉 worker。</summary>
/// <param name="resolve">只在啟動驗證時解析 singleton，不提前讓 hosted collection 建立失敗。</param>
/// <param name="clock">外借每日排程與期限來源，不建立主機外的全域 timer。</param>
/// <param name="diagnostics">外借共享告警，工作停止後 Host 才能關閉。</param>
internal sealed class SqlWebhookCleanupService(Func<SqlWebhookEventDeduplicator> resolve, TimeProvider clock, ExceptionDiagnostics diagnostics) : IHostedLifecycleService, IDisposable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource stopping = new();
    private Task? worker;
    private Task? stop;
    private Task? disposal;
    private bool closed;
    private SqlWebhookEventDeduplicator? dedupe;

    /// <summary>在全部 hosted instance 建立後才解析及驗證部署快照；啟動拒絕仍可讓 Host 正常 Stop／Dispose。</summary>
    public Task StartingAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); dedupe ??= resolve(); return Task.CompletedTask; }

    /// <summary>只建立一次固定 worker，開始時不執行 SQL；啟動 token 取消直接拒絕。</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(closed, this);
            dedupe ??= resolve();
            worker ??= RunAsync(stopping.Token); return Task.CompletedTask;
        }
    }

    /// <summary>每輪失敗都安全上報但不終止日程；正常停止取消不通知。</summary>
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromDays(1), clock, token).ConfigureAwait(false);
                try { await dedupe!.CleanupAsync(token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception exception) { diagnostics.Report(exception, "WebhookSql.DailyCleanup"); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* 唯一停止訊號，timer 已由 Task.Delay 確定解除。 */ }
    }

    /// <summary>忽略 Host 已取消的等待期限，仍取消並 await 真 worker，保證停止回傳後零在途 SQL。</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (gate) { closed = true; return stop ??= StopCoreAsync(); }
    }

    /// <summary>取消 callbacks 完成後繼續等完整 SQL 清理，不拋棄運作中的 Task。</summary>
    private async Task StopCoreAsync()
    { await stopping.CancelAsync().ConfigureAwait(false); if (worker is not null) await worker.ConfigureAwait(false); }

    /// <summary>DI 非同步關閉的唯一釋放工作；重入共用 task，不重複 Dispose CTS。</summary>
    public ValueTask DisposeAsync()
    { lock (gate) { return new ValueTask(disposal ??= DisposeCoreAsync()); } }

    /// <summary>先 drain 才釋放 CTS，finally 即使 worker 意外失敗亦不留 registration。</summary>
    private async Task DisposeCoreAsync()
    { try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } finally { stopping.Dispose(); } }

    /// <summary>同步 Host Dispose 仍必須完整 drain；所有非同步續接皆不捕獲同步環境，避免假同步清理。</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>啟動後不新增 SQL、timer 或訂閱，唯一迴圈已由 Start 管理。</summary>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>Stop 階段負責完整取消及排空，不在前置階段另外建立停止工作。</summary>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>Stop 已等待工作歸零，這裡沒有額外資源或通知。</summary>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
