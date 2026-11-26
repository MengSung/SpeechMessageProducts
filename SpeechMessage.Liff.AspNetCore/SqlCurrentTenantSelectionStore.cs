/*
檔案整體說明
目的與定位：共用 LIFF 模組的平台 SQL 目前租戶選擇索引，以產品、Messaging Channel、已驗 LINE 主體的完整二進位鍵保存單一 ContactId；不執行業務授權。
主要功能與流程：建構時驗證部署 TLS 並保存連線快照；Host 明確呼叫 InitializeAsync 建立專用四欄表並驗 schema，或 ValidateAsync 驗證既有部署。Get 只在成功無列時回 null，Set 以資料庫唯一鍵、UPDLOCK／HOLDLOCK 與單次交易更新或插入，不使用 MERGE 或行程鎖。
使用邊界與維護：呼叫者在 Set 前重驗本人 Contact 與 Channel，每次使用選擇也重新授權。本檔不保存姓名、token、Session、HttpContext 或跨請求資料集合；只有部署連線字串隨 store owner 保留，索引 ID 在資料庫保留至覆寫或部署維運刪除。每次操作唯一擁有連線、命令及 T-SQL 交易，連線與每條命令各五秒、無重試；取消會 await provider 真正排空，失敗用獨立五秒回滾後只淘汰本連線所屬池，最後 Dispose。已確認提交不再檢查晚到取消；故障向上交 HTTP 共用 diagnostics，不把錯誤吞成 null。LocalDB 測試不代表遠端部署驗收。
*/
using System.Data;
using System.Runtime.ExceptionServices;
using Microsoft.Data.SqlClient;

namespace SpeechMessage.Liff.AspNetCore;

/// <summary>無請求可變狀態的 SQL 選擇索引；部署快照由 Host 的 store owner 持有，所有 SQL 資源只屬單次呼叫。</summary>
public sealed class SqlCurrentTenantSelectionStore : ICurrentTenantSelectionStore
{
    private readonly string connectionString;
    private const int TimeoutSeconds = 5;
    private const string CreateSql = """
        IF OBJECT_ID(N'dbo.line_current_selection') IS NULL
        BEGIN
            CREATE TABLE dbo.line_current_selection
            (
                product nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
                channel_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
                line_user_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
                contact_id uniqueidentifier NOT NULL,
                CONSTRAINT PK_line_current_selection PRIMARY KEY CLUSTERED(product, channel_key, line_user_id)
            );
        END;
        """;
    private const string ValidateSql = """
        DECLARE @table int = OBJECT_ID(N'dbo.line_current_selection', N'U');
        IF @table IS NULL
            THROW 51060, 'LIFF selection schema is incompatible.', 1;
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id=@table
            AND name COLLATE Latin1_General_100_BIN2=N'line_current_selection'
            AND temporal_type=0 AND is_memory_optimized=0)
            THROW 51060, 'LIFF selection schema is incompatible.', 1;
        IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=@table)<>4
            THROW 51060, 'LIFF selection schema is incompatible.', 1;
        IF (SELECT COUNT(*) FROM sys.columns AS c
            JOIN (VALUES (1,N'product',231,256), (2,N'channel_key',231,256),
                         (3,N'line_user_id',231,256), (4,N'contact_id',36,16)) AS e(ordinal,name,type,length)
              ON c.column_id=e.ordinal AND c.name COLLATE Latin1_General_100_BIN2=e.name COLLATE Latin1_General_100_BIN2
             AND c.system_type_id=e.type AND c.user_type_id=c.system_type_id AND c.max_length=e.length
            WHERE c.object_id=@table AND c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0
              AND c.is_rowguidcol=0 AND c.is_sparse=0 AND c.generated_always_type=0
              AND c.encryption_type IS NULL AND c.default_object_id=0 AND c.rule_object_id=0
              AND (e.ordinal=4 OR c.collation_name COLLATE Latin1_General_100_BIN2=N'Latin1_General_100_BIN2'))<>4
            THROW 51060, 'LIFF selection schema is incompatible.', 1;
        IF (SELECT COUNT(*) FROM sys.indexes WHERE object_id=@table)<>1
            THROW 51060, 'LIFF selection schema is incompatible.', 1;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes AS i WHERE i.object_id=@table
            AND i.is_primary_key=1 AND i.is_unique=1 AND i.type=1 AND i.is_disabled=0
            AND i.has_filter=0 AND i.ignore_dup_key=0
            AND (SELECT COUNT(*) FROM sys.index_columns AS k WHERE k.object_id=i.object_id AND k.index_id=i.index_id)=3
            AND (SELECT COUNT(*) FROM sys.index_columns AS k WHERE k.object_id=i.object_id AND k.index_id=i.index_id
                 AND k.key_ordinal=k.column_id AND k.key_ordinal BETWEEN 1 AND 3
                 AND k.is_descending_key=0 AND k.is_included_column=0)=3)
            THROW 51060, 'LIFF selection schema is incompatible.', 1;
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=@table)
            OR EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=@table)
            OR EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id=@table)
            THROW 51060, 'LIFF selection schema is incompatible.', 1;
        """;
    private const string GetSql = "SELECT contact_id FROM dbo.line_current_selection WHERE product=@product AND channel_key=@channel AND line_user_id=@user;";
    private const string SetSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        UPDATE dbo.line_current_selection WITH (UPDLOCK, HOLDLOCK)
            SET contact_id=@contact
            WHERE product=@product AND channel_key=@channel AND line_user_id=@user;
        IF @@ROWCOUNT=0
            INSERT dbo.line_current_selection(product,channel_key,line_user_id,contact_id)
            VALUES (@product,@channel,@user,@contact);
        COMMIT TRANSACTION;
        """;

    /// <summary>借用部署連線字串為 owner 快照，拒絕非加密、憑證信任繞過或系統資料庫；不在建構時進行 I/O。</summary>
    /// <param name="connectionString">由 Host 提供且不得輸出的部署秘密；必須指定伺服器與專用非系統資料庫。</param>
    /// <exception cref="ArgumentException">部署設定無效或違反 TLS／專用資料庫政策；固定訊息不包含連線內容。</exception>
    public SqlCurrentTenantSelectionStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { throw new ArgumentException("Deployment SQL connection settings are invalid.", nameof(connectionString)); }
        if (builder.Encrypt.Equals(SqlConnectionEncryptOption.Optional) || builder.TrustServerCertificate
            || string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || new[] { "master", "model", "msdb", "tempdb" }.Contains(builder.InitialCatalog, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Deployment SQL requires validated TLS and a dedicated database.", nameof(connectionString));
        }
        builder.ConnectTimeout = TimeoutSeconds;
        builder.ConnectRetryCount = 0;
        builder.Enlist = false;
        builder.MultipleActiveResultSets = false;
        builder.MinPoolSize = 0;
        builder.MaxPoolSize = Math.Min(builder.MaxPoolSize, 64);
        this.connectionString = builder.ConnectionString;
    }

    /// <summary>由部署／Host 啟動一次顯式建立四欄專用表並驗完整 schema；不修改不相容的既有表，也不在每次請求執行。</summary>
    /// <param name="ct">啟動取消來源，取消後等待 SQL 排空並以獨立期限回滾。</param>
    /// <returns>確認初始化交易已提交的工作；失敗由最終 Host 診斷入口處理。</returns>
    public async Task InitializeAsync(CancellationToken ct)
    {
        await ExecuteAsync("SET XACT_ABORT ON; BEGIN TRANSACTION;\n" + CreateSql + "\n" + ValidateSql + "\nCOMMIT TRANSACTION;", null, false, true, ct).ConfigureAwait(false);
    }

    /// <summary>只驗證已部署資料表的精確欄位、二進位定序、唯一主鍵與專用 schema，不建表、不自行修補或接受不明擴充。</summary>
    /// <param name="ct">部署驗證取消來源，所有 I/O 都必須真正排空。</param>
    /// <returns>schema 相容時完成，缺表或任何不相容均拋 SQL 51060。</returns>
    public async Task ValidateAsync(CancellationToken ct)
    {
        await ExecuteAsync(ValidateSql, null, false, false, ct).ConfigureAwait(false);
    }

    /// <summary>按完整鍵讀取 detached ContactId；只在 SQL 成功且沒有資料列時回 null，空 ID 資料損壞則拒絕使用。</summary>
    /// <param name="product">已驗產品識別，一至 128 UTF-16 單位，不能含空白、控制或代理字元。</param>
    /// <param name="channelKey">已驗 Messaging Channel 鍵；不修剪、不轉換大小寫。</param>
    /// <param name="lineUserId">已驗 LINE 主體；不修剪、不轉換大小寫。</param>
    /// <param name="ct">caller 取消來源，只有預先取消能保證零 I/O。</param>
    /// <returns>單一純 ID 或成功無列；傳輸、逾時與取消不會變成 null。</returns>
    public async Task<Guid?> GetAsync(string product, string channelKey, string lineUserId, CancellationToken ct)
    {
        ValidateScope(product, channelKey, lineUserId);
        var value = await ExecuteAsync(GetSql, parameters => AddScope(parameters, product, channelKey, lineUserId), true, false, ct).ConfigureAwait(false);
        if (value is null) return null;
        if (value is Guid contact && contact != Guid.Empty) return contact;
        throw new InvalidDataException("LIFF selection contains an invalid contact identifier.");
    }

    /// <summary>在 SQL 範圍鎖與唯一鍵保護下原子更新或插入；主線必須在此呼叫之前重新授權本人 Contact 與 Channel。</summary>
    /// <param name="product">已驗產品識別，完整比對。</param>
    /// <param name="channelKey">已驗 Messaging Channel 鍵，大小寫有別。</param>
    /// <param name="lineUserId">已驗且屬於目標 Contact 的 LINE 主體，大小寫有別。</param>
    /// <param name="contactId">非空且由產品重新授權的資料庫 ID，本 store 不執行授權。</param>
    /// <param name="ct">caller 取消來源；provider 確認 COMMIT 之後不檢查晚到取消。</param>
    /// <returns>SQL 確認整個交易提交後完成的工作，失敗向上傳播且排空資源。</returns>
    public async Task SetAsync(string product, string channelKey, string lineUserId, Guid contactId, CancellationToken ct)
    {
        ValidateScope(product, channelKey, lineUserId);
        if (contactId == Guid.Empty) throw new ArgumentException("A nonempty contact identifier is required.", nameof(contactId));
        await ExecuteAsync(SetSql, parameters =>
        {
            AddScope(parameters, product, channelKey, lineUserId);
            parameters.Add(new SqlParameter("@contact", SqlDbType.UniqueIdentifier) { Value = contactId });
        }, false, true, ct).ConfigureAwait(false);
    }

    /// <summary>三段鍵驗證在取消與所有 I/O 前完成；禁止空白避免 SQL 即使在 BIN2 下仍有尾端補空白等價問題。</summary>
    private static void ValidateScope(string product, string channelKey, string lineUserId)
    {
        ValidateComponent(product, nameof(product));
        ValidateComponent(channelKey, nameof(channelKey));
        ValidateComponent(lineUserId, nameof(lineUserId));
    }

    /// <summary>只接受不需正規化的有界原值，拒絕代理字元以防無效 Unicode 在 provider 編碼時改寫成相同鍵。</summary>
    private static void ValidateComponent(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || char.IsSurrogate(character)))
        {
            throw new ArgumentException("Selection identifiers must be bounded and contain no whitespace or control characters.", name);
        }
    }

    /// <summary>三欄明確指定 nvarchar(128)，不使用 AddWithValue，不拼接 caller 值；三欄共 768 bytes，低於主鍵 900 bytes 上限。</summary>
    private static void AddScope(SqlParameterCollection parameters, string product, string channelKey, string lineUserId)
    {
        parameters.Add(new SqlParameter("@product", SqlDbType.NVarChar, 128) { Value = product });
        parameters.Add(new SqlParameter("@channel", SqlDbType.NVarChar, 128) { Value = channelKey });
        parameters.Add(new SqlParameter("@user", SqlDbType.NVarChar, 128) { Value = lineUserId });
    }

    /// <summary>唯一操作 owner；五秒為發出取消及 provider 命令期限，所有非同步 I/O 都 await 到真實完成，不以 WaitAsync 遺棄 transport。</summary>
    private async Task<object?> ExecuteAsync(string sql, Action<SqlParameterCollection>? parameters, bool read, bool transactional, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        var connection = new SqlConnection(connectionString);
        Exception? failure = null;
        object? result = null;
        try
        {
            using (var opening = CancellationTokenSource.CreateLinkedTokenSource(caller))
            {
                opening.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                await connection.OpenAsync(opening.Token).ConfigureAwait(false);
            }
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = TimeoutSeconds;
            parameters?.Invoke(command.Parameters);
            using var executing = CancellationTokenSource.CreateLinkedTokenSource(caller);
            executing.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
            if (read) result = await command.ExecuteScalarAsync(executing.Token).ConfigureAwait(false);
            else await command.ExecuteNonQueryAsync(executing.Token).ConfigureAwait(false);
            // provider 已確認整個批次（包括 COMMIT）完成；不可再用晚到 caller／deadline 取消撤回成功。
        }
        catch (Exception exception) { failure = exception; }
        if (failure is not null)
        {
            if (transactional && connection.State == ConnectionState.Open)
            {
                try
                {
                    // caller 取消不能放棄交易；新的有界清理 token 不繼承已取消的 caller。
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                    await using var rollback = connection.CreateCommand();
                    rollback.CommandTimeout = TimeoutSeconds;
                    rollback.CommandText = "IF XACT_STATE()<>0 ROLLBACK TRANSACTION;";
                    await rollback.ExecuteNonQueryAsync(cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception cleanup) { failure = new AggregateException(failure, cleanup); }
            }
            try { SqlConnection.ClearPool(connection); }
            catch (Exception cleanup) { failure = new AggregateException(failure, cleanup); }
        }
        try { await connection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception cleanup)
        {
            failure = failure is null ? cleanup : new AggregateException(failure, cleanup);
            // 即使只有 Dispose 失敗，也不能把狀態不明的實體連線留給下一個主體重用。
            try { SqlConnection.ClearPool(connection); }
            catch (Exception discard) { failure = new AggregateException(failure, discard); }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
}
