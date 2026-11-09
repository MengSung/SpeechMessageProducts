/*
檔案整體說明
目的與定位：正式平台 SQL 事件去重，僅依已驗簽 envelope 的 ChannelId 與 WebhookEventId 執行 A.8 四欄 INSERT，不使用記憶體後備。
主要功能與流程：每次建立新連線，BEGIN 後 INSERT；真 2627／2601 衝突先獨立 rollback 再讀完整鍵，只有確定已提交相同鍵才回 null。新事件回 lease，既有 queue 先 reserve 容量再 Commit 後 publish；未提交 Dispose 獨立五秒 rollback，已提交不刪除資料。
使用邊界與維護：singleton 只借用可信 factory、clock 與 diagnostics，不保存事件、request 或使用者狀態。每 lease 連線歸端點唯一 owner，正常路徑最多兩秒 queue 等待加各五秒 SQL／清理；池最多一百連線且 Open 五秒拒絕超量。提交不確定必拋原錯誤、淘汰連線且不重試／不 rollback 已可能提交結果。四欄 schema 無法原子涵蓋 queue：若 COMMIT 已生效卻失去確認，重送可判重而事件未發布，此限制不能宣稱 exactly-once。
*/
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using ToolUtilityNameSpace.Diagnostics;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Deduplication;

/// <summary>無共用可變事件集合的 SQL 去重器；只有交易連線具有目前 claim 的資料，釋放即解除所有參照。</summary>
/// <param name="factory">可信啟動組合根的連線工廠，每次必須回新建 Closed 連線；不得捕獲 request。</param>
/// <param name="clock">外借時間來源，同時提供 UTC 及有界操作期限。</param>
/// <param name="diagnostics">Host 擁有的共享診斷，清理完成後才由 Host 關閉。</param>
internal sealed class SqlWebhookEventDeduplicator(Func<DbConnection> factory, TimeProvider clock, ExceptionDiagnostics diagnostics) : IWebhookEventDeduplicator
{
    /// <summary>完成 INSERT 才移交 lease，SQL 任何不確定狀態皆失敗；初始 claim 失敗也必須回滾並釋放連線。</summary>
    public async ValueTask<IWebhookEventRegistration?> TryRegisterAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope); cancellationToken.ThrowIfCancellationRequested();
        var session = new SqlWebhookSession(factory(), clock, diagnostics);
        var lease = new SqlWebhookRegistration(session, diagnostics);
        try
        {
            await session.OpenAsync(cancellationToken).ConfigureAwait(false);
            await lease.BeginAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await session.ExecuteAsync("INSERT dbo.LineWebhookEvent(ChannelId,WebhookEventId,ReceivedUtc,IsRedelivery) VALUES (@channel,@event,@received,@redelivery)", cancellationToken,
                    SqlWebhookSession.Text("@channel", envelope.ChannelId, 20), SqlWebhookSession.Text("@event", envelope.WebhookEventId, 64),
                    SqlWebhookSession.Utc("@received", clock.GetUtcNow().UtcDateTime), new SqlParameter("@redelivery", SqlDbType.Bit) { Value = envelope.IsRedelivery }).ConfigureAwait(false);
            }
            catch (SqlException exception) when (SqlWebhookSession.IsUniqueConflict(exception) && !session.IsUncertain)
            {
                await lease.RollbackAsync().ConfigureAwait(false);
                if (!await session.ContainsAsync(envelope.ChannelId, envelope.WebhookEventId, cancellationToken).ConfigureAwait(false)) throw;
                await lease.DisposeAsync().ConfigureAwait(false);
                return null;
            }
            return lease;
        }
        catch
        {
            try { await lease.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { diagnostics.Report(cleanup, "WebhookSql.RegisterCleanup"); }
            throw;
        }
    }

    /// <summary>每日最多二十批各五百列與三十秒總預算；只刪嚴格早於固定截止點的已提交記錄。</summary>
    internal async Task CleanupAsync(CancellationToken caller)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, budget.Token);
        var session = new SqlWebhookSession(factory(), clock, diagnostics);
        Exception? primary = null;
        try
        {
            await session.OpenAsync(linked.Token).ConfigureAwait(false);
            var cutoff = clock.GetUtcNow().AddDays(-7).UtcDateTime;
            for (var batch = 0; batch < 20; batch++)
            {
                var deleted = await session.ExecuteAsync("DELETE TOP (@batch) FROM dbo.LineWebhookEvent WHERE ReceivedUtc < @cutoff", linked.Token,
                    new SqlParameter("@batch", SqlDbType.Int) { Value = 500 }, SqlWebhookSession.Utc("@cutoff", cutoff)).ConfigureAwait(false);
                if (deleted < 500) break;
            }
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) when (primary is not null) { diagnostics.Report(cleanup, "WebhookSql.CleanupRelease"); }
        }
    }
}
