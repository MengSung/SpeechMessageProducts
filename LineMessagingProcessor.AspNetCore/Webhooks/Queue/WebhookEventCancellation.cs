/*
檔案整體說明
目的與定位：單一 Webhook 事件的取消資源 owner，隔離 handler 自訂 cancellation callback 的故障，避免 timer 執行緒例外或中斷 Host 排空。
主要功能與流程：建立一個獨立 CTS、一個三十秒 timer 與一個 Host registration；第一個取消原因以原子操作固定。取消 callback 的真正例外交共用 diagnostics，其他 callback 仍由 CTS 執行。
使用邊界與維護：只保存本事件的 CTS 與外借診斷，不保存 envelope、HTTP、使用者或憑證。timer、registration 的執行中 callback 必須先 await 排空，再 Dispose CTS；ProcessAsync 是唯一 owner，工作及 scope 完成後必定清理。不能以事件 token 已取消為理由忽略內部逾時或 callback 失敗，正常 Host 取消則不通知。
*/
using ToolUtilityNameSpace.Diagnostics;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Queue;

/// <summary>單一事件的 CTS／timer／Host registration，第一個取消原因決定正常停止與期限失敗分類。</summary>
internal sealed class WebhookEventCancellation : IAsyncDisposable
{
    private readonly CancellationTokenSource source = new();
    private readonly ExceptionDiagnostics diagnostics;
    private readonly ITimer timer;
    private readonly CancellationTokenRegistration hostRegistration;
    private int reason;

    /// <summary>只借用時鐘及診斷，建立三十秒單次 timer；建構失敗仍清理已建立 CTS。</summary>
    internal WebhookEventCancellation(TimeProvider clock, ExceptionDiagnostics diagnostics, CancellationToken hostStop)
    {
        this.diagnostics = diagnostics;
        ITimer? candidate = null;
        try
        {
            candidate = clock.CreateTimer(static state => ((WebhookEventCancellation)state!).Cancel(1),
                this, WebhookQueueOptions.EventTimeout, Timeout.InfiniteTimeSpan);
            hostRegistration = hostStop.UnsafeRegister(static state => ((WebhookEventCancellation)state!).Cancel(2), this);
            timer = candidate;
        }
        catch
        {
            try { candidate?.Dispose(); }
            finally { source.Dispose(); }
            throw;
        }
    }

    /// <summary>只供目前 handler 借用，Process 結束後 owner Dispose，不可保存至背景子工作。</summary>
    internal CancellationToken Token => source.Token;
    /// <summary>第一個取消原因為內部期限才 true；與 Host 同時取消亦只由原子勝出者分類。</summary>
    internal bool TimedOut => Volatile.Read(ref reason) == 1;

    /// <summary>取消回呼可以故障，但不能逃出 timer 或停止流程；CTS 會跑完其他 callback 後彙總原例外。</summary>
    private void Cancel(int cause)
    {
        if (Interlocked.CompareExchange(ref reason, cause, 0) != 0) return;
        try { source.Cancel(); }
        catch (Exception exception) { diagnostics.Report(exception, "WebhookQueue.CancelEvent"); }
    }

    /// <summary>先排空兩個 callback 來源，再關 CTS，防止 Dispose 與 timer／Host cancel 競態使用已關資源。</summary>
    public async ValueTask DisposeAsync()
    {
        try { await timer.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { await hostRegistration.DisposeAsync().ConfigureAwait(false); }
            finally { source.Dispose(); }
        }
    }
}
