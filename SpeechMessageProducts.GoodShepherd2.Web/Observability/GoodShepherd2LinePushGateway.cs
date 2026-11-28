/*
檔案整體說明
目的與定位：F11 好牧人主機連接 F3 固定產品憑證、F5 唯一推播出口與真 Dataverse adapter 的組合接縫，不新增外部發送端點或產品事件行為。
主要功能與流程：每次同步入場上限三十二操作，沒有等待佇列；借獨立 lease、membership 與 send record，將伺服器 correlation 原值交正式 Gateway。F5 完整權威查詢與測試租戶保護不變，紀錄 CAS 完成後才返回純值結果，所有 adapter 先釋放再歸還 SDK。
使用邊界與維護：全部依賴由同一產品 Host 借入；不持有跨操作的 SDK、身分、收件人或訊息。每次三十秒合作期限，單次同步 SDK 沿 manager 的既有 transport deadline 並完整等待返回，不以遺棄 Task 假裝取消。Dispose 先拒收並給三十秒 drain，必要時取消後仍等全部資源歸還；HttpClient、目錄與 manager 由外層 owner 在本轉接排空後處置。禁止修改 F5 SendAsync 或 F8 AuditEntry。
*/
using Line.Messaging;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Gateway;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using ToolUtilityNameSpace.LineRecipients;
using System.Runtime.ExceptionServices;

namespace SpeechMessageProducts.GoodShepherd2.Web.Observability;

/// <summary>固定產品主機的推播轉接；共享欄位只保存 owner 與計數，每次資料都留在單一方法區域。</summary>
internal sealed class GoodShepherd2LinePushGateway(Guid organizationId, IDataverseConnectionManager manager,
    ILineCredentialExecutor credentials, LinePushClient client, ExceptionDiagnostics diagnostics) : ILinePushGateway, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int active;
    private bool stopping;
    private Task? disposal;

    /// <summary>同一 lease 內使用真 F5 adapter，不把 caller scope 視為授權，真正故障交共用 diagnostics。</summary>
    public async Task<LinePushGatewayResult> SendAsync(PushScope scope, IReadOnlyList<string> recipientUserIds,
        IReadOnlyList<ISendMessage> messages, MessageCategory category, string correlationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            if (active >= 32) throw new InvalidOperationException("Push operation capacity is exhausted.");
            active++;
        }
        IClientLease? lease = null;
        Exception? primary = null;
        LinePushGatewayResult? result = null;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, shutdown.Token, deadline.Token);
            try
            {
                lease = manager.Acquire(operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                using var membership = new DataverseRecipientMembership(lease.Service, organizationId, GoodShepherd2Product.Name);
                using var records = new DataverseSendRecordStore(lease.Service, organizationId, GoodShepherd2Product.Name);
                var gateway = new LinePushGateway(GoodShepherd2Product.Name, credentials, client, membership, records,
                    (failure, name) => diagnostics.Report(failure, name, ct), new PushSafetyOptions());
                result = await gateway.SendAsync(scope, recipientUserIds, messages, category, correlationId, operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested && !shutdown.IsCancellationRequested)
            { throw new TimeoutException("Push operation exceeded its cooperative deadline.", exception); }
        }
        catch (Exception exception)
        {
            primary = exception;
            diagnostics.Report(exception, "GoodShepherd2.Push.Send", ct);
            if (lease is not null)
            {
                try { lease.MarkFaulted(); }
                catch (Exception cleanup) { diagnostics.Report(cleanup, "GoodShepherd2.Push.Fault"); }
            }
        }
        finally
        {
            try { lease?.Dispose(); }
            catch (Exception cleanup)
            {
                diagnostics.Report(cleanup, "GoodShepherd2.Push.Release");
                primary ??= cleanup;
            }
            finally
            {
                lock (gate)
                {
                    active--;
                    if (stopping && active == 0) drained.TrySetResult();
                }
            }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        return result!;
    }

    /// <summary>唯一關閉工作可重入；不關借用 client、目錄、manager 或 diagnostics。</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            stopping = true;
            if (active == 0) drained.TrySetResult();
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    /// <summary>先有界等待正常完成，再合作取消；所有同步 SDK 真正返回及歸還前不宣稱已 drain。</summary>
    private async Task DisposeCoreAsync()
    {
        try { await drained.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
        catch (TimeoutException) { await shutdown.CancelAsync().ConfigureAwait(false); }
        finally
        {
            await drained.Task.ConfigureAwait(false);
            shutdown.Dispose();
        }
    }
}
