/*
檔案整體說明
目的與定位：F3 產品主機的持久診斷輪詢 owner，將已提交的產品不符事件交給共用接收器，而非在 Plugin 沙箱建立通知 client。
主要功能與流程：設定完整時由組合根登錄，啟動後每分鐘處理一次有界批次；停止先取消計時器與在途 SDK，再等待接收器歸還租約，確保 diagnostics 及連線 manager 仍存活。
使用邊界與維護：唯一 owner 只保存固定部署接收器、共用診斷與計時器，不保存 request、Session、租戶或秘密。每次批次自己的逾時與正式 RowVersion 由接收器管理；本檔不額外重試同批次，也不把已記錄的故障重複通知。Dispose 必須排空背景 Task 並釋放 timer／取消來源，測試只以合成時間及 SDK 驗證，不代表實機告警已送達。
*/
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Channels;

/// <summary>只有一個有界輪詢工作的 Host 生命週期 owner，停止前不釋放借用依賴。</summary>
internal sealed class GoodShepherd2ChannelDiagnosticPump : IHostedLifecycleService, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly SlotRequestDiagnosticReceiver receiver;
    private readonly ExceptionDiagnostics diagnostics;
    private readonly TimeProvider timeProvider;
    private CancellationTokenSource? stop;
    private PeriodicTimer? timer;
    private Task? worker;
    private Task? closing;
    /// <summary>借入固定部署接收器、診斷與時鐘；建構不建立計時器或 SDK lease。</summary>
    public GoodShepherd2ChannelDiagnosticPump(SlotRequestDiagnosticReceiver receiver, ExceptionDiagnostics diagnostics, TimeProvider timeProvider)
    {
        this.receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }
    /// <summary>尚未啟動背景工作。</summary>
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>開始週期等待，不立即呼叫 SDK。</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing != null, this);
            if (worker != null) throw new InvalidOperationException("Diagnostic pump already started.");
            var candidateStop = new CancellationTokenSource();
            PeriodicTimer? candidateTimer = null;
            try
            {
                candidateTimer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
                stop = candidateStop; timer = candidateTimer;
                // 背景工作不得繼承啟動呼叫端的 AsyncLocal 身分或 request context。
                if (ExecutionContext.IsFlowSuppressed()) worker = RunAsync(candidateTimer, candidateStop.Token);
                else
                {
                    using (ExecutionContext.SuppressFlow()) worker = RunAsync(candidateTimer, candidateStop.Token);
                }
            }
            catch
            {
                candidateTimer?.Dispose(); candidateStop.Dispose();
                timer = null; stop = null;
                throw;
            }
        }
        return Task.CompletedTask;
    }
    /// <summary>沒有其他啟動副作用。</summary>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>先排空再讓其他 owner 停止。</summary>
    public Task StoppingAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();
    /// <summary>共用同一關閉工作。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();
    /// <summary>已完成全部清理。</summary>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>關閉唯一 timer／取消來源及工作。</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate) return new ValueTask(closing ??= CloseAsync());
    }

    /// <summary>至多一個批次在途，故障由接收器記錄；下一分鐘可處理新批次，不製造遺留重試工作。</summary>
    private async Task RunAsync(PeriodicTimer ownedTimer, CancellationToken cancellationToken)
    {
        try
        {
            while (await ownedTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try { await receiver.DrainOnceAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception) { /* 接收器已用原 Exception 上報並歸還租約，不能再製造第二份告警。 */ }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            diagnostics.Report(exception, "GoodShepherd2.Channels.DiagnosticPump", cancellationToken);
            throw;
        }
    }

    /// <summary>即使 Host 停止 token 已取消仍完整排空，先拒絕下一 tick，再等待 SDK 歸還，最後釋放取消來源。</summary>
    private async Task CloseAsync()
    {
        try
        {
            stop?.Cancel(); timer?.Dispose();
            if (worker != null) await worker.ConfigureAwait(false);
        }
        finally
        {
            timer?.Dispose(); timer = null;
            stop?.Dispose(); stop = null;
            worker = null;
        }
    }
}
