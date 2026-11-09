/*
檔案整體說明
目的與定位：實作一次 HTTP → queue 交接的 SQL lease 狀態機，序列化提交與釋放且保留原始不確定結果。
主要功能與流程：BEGIN 成功標記 pending，只有 queue reserve 後呼叫 COMMIT。Commit 與 Dispose 的工作由同一局部 gate 發布，每個操作只執行一次；Dispose 等正在提交真正完成，再依 pending／committed／uncertain 決定獨立 rollback 或直接釋放。
使用邊界與維護：不保存 envelope、user 或 token，只短期持有單一 session。rollback 不用 HTTP 已取消 token；每條命令五秒發取消並 await drain。提交未取得成功確認而失敗為 uncertain，不重試、不 rollback、不刪資料；成功確認後晚取消或命令清理故障不得撤回發布；安全診斷固定 operation 識別此限制。釋放失敗也不覆蓋主要 SQL 錯誤，端點仍是 lease 唯一 owner。
*/
using ToolUtilityNameSpace.Diagnostics;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Deduplication;

/// <summary>只在單筆交接期間存活的交易 owner；鎖只保護狀態，不在鎖內阻塞等待 I/O。</summary>
/// <param name="session">移交給 lease 的唯一連線 owner，Dispose 必須完整釋放。</param>
/// <param name="diagnostics">外借共享告警，不能由目前 lease 關閉或保存事件本文。</param>
internal sealed class SqlWebhookRegistration(SqlWebhookSession session, ExceptionDiagnostics diagnostics) : IWebhookEventRegistration
{
    private readonly object gate = new();
    private bool pending;
    private bool closing;
    private bool committed;
    private Task? commitTask;
    private Task? disposeTask;

    /// <summary>明確 BEGIN 命令可取消且有 CommandTimeout，開始失敗時視為不確定連線而非有效 lease。</summary>
    internal async Task BeginAsync(CancellationToken caller)
    {
        await session.ExecuteAsync("BEGIN TRANSACTION", caller).ConfigureAwait(false);
        pending = true;
    }

    /// <summary>提交前 caller 已取消可保留 pending 交獨立 rollback；提交開始後多次呼叫共用同一工作，禁止重複 COMMIT。</summary>
    public ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (committed) return ValueTask.CompletedTask;
            if (commitTask is not null) return new ValueTask(commitTask);
            cancellationToken.ThrowIfCancellationRequested();
            commitTask = CommitCoreAsync(cancellationToken);
            return new ValueTask(commitTask);
        }
    }

    /// <summary>只有未取得 provider 成功確認的故障才是不確定；已確認提交維持成功，讓 queue 必須發布。原故障交共享入口，不猜測回滾。</summary>
    private async Task CommitCoreAsync(CancellationToken caller)
    {
        try
        {
            await session.CommitAsync(caller).ConfigureAwait(false);
            committed = true;
        }
        catch (Exception exception)
        {
            session.Discard(); diagnostics.Report(exception, "WebhookSql.CommitUncertain", caller);
            throw;
        }
        finally { pending = false; }
    }

    /// <summary>只供 claim 建立失敗／衝突及未提交 Dispose 使用，忽略 caller token、獨立五秒並精確清空 pending。</summary>
    internal async Task RollbackAsync()
    {
        if (!pending) return;
        pending = false;
        try { await session.ExecuteAsync("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION", CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception)
        { session.Discard(); diagnostics.Report(exception, "WebhookSql.Rollback"); throw; }
    }

    /// <summary>先發布 closing 再等已有 commit；重入共用一次清理工作，成功釋放後不再觸碰 connection。</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposeTask is not null) return new ValueTask(disposeTask);
            closing = true; disposeTask = DisposeCoreAsync(commitTask);
            return new ValueTask(disposeTask);
        }
    }

    /// <summary>提交錯誤已交原 caller，這裡只 drain；rollback 與 connection Dispose 均嘗試且保留第一個清理故障。</summary>
    private async Task DisposeCoreAsync(Task? committing)
    {
        if (committing is not null)
        { try { await committing.ConfigureAwait(false); } catch (Exception) { /* 原提交錯誤已記錄並交回 Commit caller；清理仍須繼續。 */ } }
        Exception? primary = null;
        try { await RollbackAsync().ConfigureAwait(false); }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) when (primary is not null) { diagnostics.Report(cleanup, "WebhookSql.Release"); }
        }
    }
}
