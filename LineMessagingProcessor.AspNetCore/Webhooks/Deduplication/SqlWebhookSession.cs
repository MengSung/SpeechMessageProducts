/*
檔案整體說明
目的與定位：單次平台 SQL 操作的唯一連線 owner，集中五秒 deadline、具型別參數、原例外及不確定連線淘汰。
主要功能與流程：每個 Open／SQL 命令建立 TimeProvider deadline 與 caller linked CTS；命令本身亦固定五秒，完成後先釋放命令再釋放 CTS。COMMIT 成功確認後保留該結果，晚到取消不能撤回，命令清理故障只安全通知並淘汰連線。交易使用可取消的 T-SQL BEGIN／COMMIT／ROLLBACK，而非 DbTransaction 基類可能同步執行的 async 後備。
使用邊界與維護：連線只屬目前 claim 或清理批次，不保留 envelope、HttpContext 或 credential。逾時、取消及未確定的傳輸失敗標記 discard，Dispose 前只清此連線所屬池，不使用 ClearAllPools；所有非同步操作都 await 到 provider drain，不用 WaitAsync 遺棄在途 I/O。五秒為發出取消及 provider 命令期限，取消確認／transport 關閉的真實 drain 不以假完成掩蓋。
*/
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using ToolUtilityNameSpace.Diagnostics;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Deduplication;

/// <summary>不可跨操作重用的連線與 deadline 執行器；lease 或清理工作唯一 Dispose。</summary>
/// <param name="connection">本操作新建且尚未開啟的連線，建構成功即移交唯一釋放責任。</param>
/// <param name="clock">外借期限來源，不由 session Dispose。</param>
/// <param name="diagnostics">外借共享告警入口，只傳真例外與固定 operation。</param>
internal sealed class SqlWebhookSession(DbConnection connection, TimeProvider clock, ExceptionDiagnostics diagnostics) : IAsyncDisposable
{
    private bool discard;
    private bool disposed;
    /// <summary>取消、傳輸或釋放故障後不准再將唯一鍵錯誤降格成成功判重。</summary>
    internal bool IsUncertain => discard;

    /// <summary>五秒涵蓋連線池等待及實際 Open，錯誤保留原物件並禁止重用此連線。</summary>
    internal async Task OpenAsync(CancellationToken caller)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (connection.State != ConnectionState.Closed) throw new InvalidOperationException("SQL factory must return a new closed connection.");
            await connection.OpenAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
        }
        catch { discard = true; throw; }
    }

    /// <summary>固定 SQL 與 detached 參數在單一命令內執行，期限獨立於前一步，不遺棄 cancellation 後工作。</summary>
    internal Task<int> ExecuteAsync(string sql, CancellationToken caller, params DbParameter[] parameters) =>
        RunCommandAsync(sql, static (command, token) => command.ExecuteNonQueryAsync(token), caller, false, parameters);

    /// <summary>COMMIT 由 provider 成功回傳即為已確認提交；晚到取消或命令清理失敗不得撤回 queue 的發布義務。</summary>
    internal Task CommitAsync(CancellationToken caller) =>
        RunCommandAsync("COMMIT TRANSACTION", static (command, token) => command.ExecuteNonQueryAsync(token), caller, true);

    /// <summary>每條命令唯一 owner，finally 的 Dispose 故障單獨通知且不覆蓋執行原例外。</summary>
    private async Task<T> RunCommandAsync<T>(string sql, Func<DbCommand, CancellationToken, Task<T>> execute,
        CancellationToken caller, bool preserveAcknowledgedResult, params DbParameter[] parameters)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
        DbCommand? command = null;
        Exception? primary = null;
        try
        {
            command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 5;
            foreach (var parameter in parameters) command.Parameters.Add(parameter);
            linked.Token.ThrowIfCancellationRequested();
            var result = await execute(command, linked.Token).ConfigureAwait(false);
            // 只有成功回傳的 COMMIT 已不可撤回；provider 拋出的取消／傳輸故障仍走 uncertain，不假造確認。
            if (!preserveAcknowledgedResult) linked.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (SqlException exception) when (IsUniqueConflict(exception))
        { primary = exception; if (linked.IsCancellationRequested) discard = true; throw; }
        catch (Exception exception) { primary = exception; discard = true; throw; }
        finally
        {
            if (command is not null)
            {
                try { await command.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup)
                {
                    discard = true; diagnostics.Report(cleanup, "WebhookSql.CommandRelease");
                    if (primary is null && !preserveAcknowledgedResult) throw;
                }
            }
        }
    }

    /// <summary>只有真 SqlException 的唯一鍵錯誤可作候選；呼叫端仍須查明相同複合鍵已存在。</summary>
    internal static bool IsUniqueConflict(SqlException exception) => exception.Number is 2627 or 2601;

    /// <summary>主鍵衝突後以兩欄完整鍵讀已提交結果；二進位比對防止資料庫定序把不同 ASCII ID 誤當相同。</summary>
    internal async Task<bool> ContainsAsync(string channel, string eventId, CancellationToken caller)
    {
        var result = await RunCommandAsync("SELECT COUNT_BIG(*) FROM dbo.LineWebhookEvent WHERE ChannelId=@channel AND WebhookEventId=@event AND ChannelId COLLATE Latin1_General_100_BIN2=@channel AND WebhookEventId COLLATE Latin1_General_100_BIN2=@event",
            static (command, token) => command.ExecuteScalarAsync(token), caller, false, Text("@channel", channel, 20), Text("@event", eventId, 64)).ConfigureAwait(false);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>建立有界 varchar 參數，不使用 AddWithValue 或字串拼接輸入。</summary>
    internal static SqlParameter Text(string name, string value, int size) => new(name, SqlDbType.VarChar, size) { Value = value };

    /// <summary>datetime2(3) UTC 參數，cleanup 與 INSERT 同一精度契約。</summary>
    internal static SqlParameter Utc(string name, DateTime value) => new(name, SqlDbType.DateTime2) { Scale = 3, Value = value };

    /// <summary>提交結果不確定時無論 provider 狀態為何均淘汰；不得以 pool reset 猜測傳輸結果。</summary>
    internal void Discard() => discard = true;

    /// <summary>先使不確定連線不可再借出再 Dispose；清池失敗不阻止關閉，兩者故障皆交共享入口。</summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (discard && connection is SqlConnection sql) SqlConnection.ClearPool(sql);
        }
        catch (Exception exception) { diagnostics.Report(exception, "WebhookSql.DiscardPool"); }
        await connection.DisposeAsync().ConfigureAwait(false);
    }
}
