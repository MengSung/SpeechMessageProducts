using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// 以獨立 SQL control-plane 資料庫協調跨 Gateway、Embedded 與設定檔世代的 runtime-host slot。
/// SQL transaction、application lock、AdmissionEpoch、設定摘要與單調遞增 fencing token 共同形成原子邊界，
/// 確保舊主機、過期租約或不同容量設定無法在同一實體 Dynamics 組織上重複取得容量。
/// 每次資料庫作業都自行擁有並確定釋放連線、交易、命令與 reader；此型別不保存要求或認證狀態。
/// </summary>
public sealed class SqlRuntimeHostSlotCoordinator : IRuntimeHostSlotCoordinator
{
    // Schema 僅建立在專用 control-plane database。AdmissionEpoch 與 ConfigurationDigest 阻擋設定漂移，
    // rowversion 保留資料列併發版本，而全域 sequence 產生永不回退的 fencing token。
    public const string SchemaSql = """
        SET XACT_ABORT ON;
        IF OBJECT_ID(N'dbo.RuntimeHostFencingSequence', N'SO') IS NULL
            EXEC(N'CREATE SEQUENCE dbo.RuntimeHostFencingSequence AS bigint START WITH 1 INCREMENT BY 1 CACHE 1000;');

        IF OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.RuntimeHostAdmissionEpoch
            (
                LeaseNamespaceId nvarchar(128) NOT NULL,
                AdmissionEpoch bigint NOT NULL,
                ConfigurationDigest char(64) NOT NULL,
                MaximumRuntimeHosts int NOT NULL,
                LastUpdatedAtUtc datetime2(3) NOT NULL
                    CONSTRAINT DF_RuntimeHostAdmissionEpoch_LastUpdated DEFAULT (SYSUTCDATETIME()),
                RowVersion rowversion NOT NULL,
                CONSTRAINT PK_RuntimeHostAdmissionEpoch PRIMARY KEY (LeaseNamespaceId),
                CONSTRAINT CK_RuntimeHostAdmissionEpoch_Epoch CHECK (AdmissionEpoch >= 1),
                CONSTRAINT CK_RuntimeHostAdmissionEpoch_MaxHosts CHECK (MaximumRuntimeHosts >= 1)
            );
        END;

        IF OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.RuntimeHostSlotLease
            (
                LeaseNamespaceId nvarchar(128) NOT NULL,
                SlotOrdinal int NOT NULL,
                AdmissionEpoch bigint NOT NULL,
                ConfigurationDigest char(64) NOT NULL,
                HostInstanceId nvarchar(128) NULL,
                FencingToken bigint NOT NULL CONSTRAINT DF_RuntimeHostSlotLease_Fencing DEFAULT (0),
                LeaseExpiresAtUtc datetime2(3) NULL,
                QuarantineUntilUtc datetime2(3) NULL,
                LastTouchedAtUtc datetime2(3) NOT NULL
                    CONSTRAINT DF_RuntimeHostSlotLease_LastTouched DEFAULT (SYSUTCDATETIME()),
                RowVersion rowversion NOT NULL,
                CONSTRAINT PK_RuntimeHostSlotLease PRIMARY KEY (LeaseNamespaceId, SlotOrdinal),
                CONSTRAINT CK_RuntimeHostSlotLease_SlotOrdinal CHECK (SlotOrdinal >= 0)
            );
        END;

        IF COL_LENGTH(N'dbo.RuntimeHostSlotLease', N'AdmissionEpoch') IS NULL
            ALTER TABLE dbo.RuntimeHostSlotLease ADD AdmissionEpoch bigint NOT NULL
                CONSTRAINT DF_RuntimeHostSlotLease_AdmissionEpoch DEFAULT (1) WITH VALUES;
        IF COL_LENGTH(N'dbo.RuntimeHostSlotLease', N'ConfigurationDigest') IS NULL
            ALTER TABLE dbo.RuntimeHostSlotLease ADD ConfigurationDigest char(64) NOT NULL
                CONSTRAINT DF_RuntimeHostSlotLease_ConfigurationDigest DEFAULT (REPLICATE('0', 64)) WITH VALUES;
        """;

    // 取得槽位必須在同一 serializable transaction 內完成：sp_getapplock 序列化同一 namespace，
    // XACT_ABORT 保證 SQL 錯誤時整筆回滾；過期槽位先進入 quarantine，避免仍在網路中的舊工作與替代主機重疊。
    private const string AcquireSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        DECLARE @lockResult int;
        DECLARE @lockResource nvarchar(255) = CONCAT(N'SpeechMessageDynamicsLease:', @leaseNamespaceId);
        EXEC @lockResult = sys.sp_getapplock
            @Resource = @lockResource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = @lockTimeoutMilliseconds;
        IF @lockResult < 0 THROW 51000, 'Unable to acquire the lease namespace lock.', 1;

        DECLARE @now datetime2(3) = SYSUTCDATETIME();
        IF NOT EXISTS
        (
            SELECT 1 FROM dbo.RuntimeHostAdmissionEpoch WITH (UPDLOCK, HOLDLOCK)
            WHERE LeaseNamespaceId = @leaseNamespaceId
        )
        BEGIN
            INSERT dbo.RuntimeHostAdmissionEpoch
                (LeaseNamespaceId, AdmissionEpoch, ConfigurationDigest, MaximumRuntimeHosts, LastUpdatedAtUtc)
            VALUES
                (@leaseNamespaceId, @admissionEpoch, @configurationDigest, @maximumRuntimeHosts, @now);
        END
        ELSE IF NOT EXISTS
        (
            SELECT 1 FROM dbo.RuntimeHostAdmissionEpoch WITH (UPDLOCK, HOLDLOCK)
            WHERE LeaseNamespaceId = @leaseNamespaceId
              AND AdmissionEpoch = @admissionEpoch
              AND ConfigurationDigest = @configurationDigest
              AND MaximumRuntimeHosts = @maximumRuntimeHosts
        )
        BEGIN
            THROW 51003, 'AdmissionEpoch or configuration digest mismatch.', 1;
        END;

        DECLARE @slot int = 0;
        WHILE @slot < @maximumRuntimeHosts
        BEGIN
            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.RuntimeHostSlotLease WITH (UPDLOCK, HOLDLOCK)
                WHERE LeaseNamespaceId = @leaseNamespaceId AND SlotOrdinal = @slot
            )
            BEGIN
                INSERT dbo.RuntimeHostSlotLease
                    (LeaseNamespaceId, SlotOrdinal, AdmissionEpoch, ConfigurationDigest)
                VALUES
                    (@leaseNamespaceId, @slot, @admissionEpoch, @configurationDigest);
            END;
            SET @slot += 1;
        END;

        UPDATE dbo.RuntimeHostSlotLease WITH (UPDLOCK, ROWLOCK)
        SET HostInstanceId = NULL,
            LeaseExpiresAtUtc = NULL,
            QuarantineUntilUtc = DATEADD(second, @quarantineSeconds, @now),
            LastTouchedAtUtc = @now
        WHERE LeaseNamespaceId = @leaseNamespaceId
          AND SlotOrdinal < @maximumRuntimeHosts
          AND HostInstanceId IS NOT NULL
          AND LeaseExpiresAtUtc <= @now;

        DECLARE @selectedSlot int;
        SELECT TOP (1) @selectedSlot = SlotOrdinal
        FROM dbo.RuntimeHostSlotLease WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE LeaseNamespaceId = @leaseNamespaceId
              AND SlotOrdinal < @maximumRuntimeHosts
              AND HostInstanceId = @hostInstanceId
              AND AdmissionEpoch = @admissionEpoch
              AND ConfigurationDigest = @configurationDigest
              AND LeaseExpiresAtUtc > @now
        ORDER BY SlotOrdinal;

        IF @selectedSlot IS NULL
        BEGIN
            SELECT TOP (1) @selectedSlot = SlotOrdinal
            FROM dbo.RuntimeHostSlotLease WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE LeaseNamespaceId = @leaseNamespaceId
              AND SlotOrdinal < @maximumRuntimeHosts
              AND HostInstanceId IS NULL
              AND (QuarantineUntilUtc IS NULL OR QuarantineUntilUtc <= @now)
            ORDER BY SlotOrdinal;
        END;

        IF @selectedSlot IS NOT NULL
        BEGIN
            DECLARE @token bigint = NEXT VALUE FOR dbo.RuntimeHostFencingSequence;
            DECLARE @expires datetime2(3) = DATEADD(millisecond, @leaseTtlMilliseconds, @now);
            UPDATE dbo.RuntimeHostSlotLease
            SET HostInstanceId = @hostInstanceId,
                AdmissionEpoch = @admissionEpoch,
                ConfigurationDigest = @configurationDigest,
                FencingToken = @token,
                LeaseExpiresAtUtc = @expires,
                QuarantineUntilUtc = NULL,
                LastTouchedAtUtc = @now
            WHERE LeaseNamespaceId = @leaseNamespaceId AND SlotOrdinal = @selectedSlot;
            SELECT @selectedSlot AS SlotOrdinal, @token AS FencingToken, @expires AS ExpiresAtUtc;
        END;
        """;

    // 續租以 expected fencing token、host、epoch 與設定摘要做 compare-and-swap；任何一項不符即回傳失敗，
    // 呼叫端必須將租約視為失效，不得以本機時間或快取狀態延長所有權。
    private const string RenewSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        DECLARE @now datetime2(3) = SYSUTCDATETIME();
        DECLARE @token bigint = NEXT VALUE FOR dbo.RuntimeHostFencingSequence;
        DECLARE @expires datetime2(3) = DATEADD(millisecond, @leaseTtlMilliseconds, @now);
        UPDATE dbo.RuntimeHostSlotLease WITH (UPDLOCK, ROWLOCK)
        SET FencingToken = @token,
            LeaseExpiresAtUtc = @expires,
            LastTouchedAtUtc = @now
        OUTPUT INSERTED.FencingToken, INSERTED.LeaseExpiresAtUtc
        WHERE LeaseNamespaceId = @leaseNamespaceId
          AND SlotOrdinal = @slotOrdinal
          AND HostInstanceId = @hostInstanceId
          AND AdmissionEpoch = @admissionEpoch
          AND ConfigurationDigest = @configurationDigest
          AND FencingToken = @expectedFencingToken
          AND LeaseExpiresAtUtc > @now
          AND QuarantineUntilUtc IS NULL
          AND EXISTS
          (
              SELECT 1 FROM dbo.RuntimeHostAdmissionEpoch
              WHERE LeaseNamespaceId = @leaseNamespaceId
                AND AdmissionEpoch = @admissionEpoch
                AND ConfigurationDigest = @configurationDigest
          );
        """;

    // 釋放只接受目前 fencing token，並先設定 quarantine 而非立即重用槽位，防止延遲中的舊 outbound 工作撞上新持有者。
    private const string ReleaseSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        DECLARE @now datetime2(3) = SYSUTCDATETIME();
        UPDATE dbo.RuntimeHostSlotLease WITH (UPDLOCK, ROWLOCK)
        SET HostInstanceId = NULL,
            LeaseExpiresAtUtc = NULL,
            QuarantineUntilUtc = DATEADD(second, @quarantineSeconds, @now),
            LastTouchedAtUtc = @now
        WHERE LeaseNamespaceId = @leaseNamespaceId
          AND SlotOrdinal = @slotOrdinal
          AND HostInstanceId = @hostInstanceId
          AND AdmissionEpoch = @admissionEpoch
          AND ConfigurationDigest = @configurationDigest
          AND FencingToken = @expectedFencingToken;
        """;

    private readonly SqlRuntimeHostSlotCoordinatorOptions _options;
    private readonly ILogger<SqlRuntimeHostSlotCoordinator> _logger;
    private int _activeDatabaseOperations;

    public SqlRuntimeHostSlotCoordinator(
        SqlRuntimeHostSlotCoordinatorOptions options,
        ILogger<SqlRuntimeHostSlotCoordinator> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsDurable => true;
    public bool SupportsAdmissionEpoch => true;
    public int ActiveDatabaseOperations => Volatile.Read(ref _activeDatabaseOperations);

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        // 僅供明確的佈署/測試 provisioning 使用；正式 readiness 走 VerifySchemaAsync，避免應用程式暗中修改錯誤資料庫。
        await ExecuteOperationAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = CreateCommand(connection, SchemaSql);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    public async Task VerifySchemaAsync(CancellationToken cancellationToken)
    {
        // readiness 必須同時確認固定資料庫名稱、資料表、sequence 與 fencing 欄位；缺任何一項即 fail-closed。
        const string verifySql = """
            SET NOCOUNT ON;
            IF DB_NAME() <> N'SpeechMessageDynamicsControlPlane'
                THROW 51001, 'Unexpected Dynamics control-plane database.', 1;
            IF OBJECT_ID(N'dbo.RuntimeHostSlotLease', N'U') IS NULL
               OR OBJECT_ID(N'dbo.RuntimeHostAdmissionEpoch', N'U') IS NULL
               OR OBJECT_ID(N'dbo.RuntimeHostFencingSequence', N'SO') IS NULL
               OR COL_LENGTH(N'dbo.RuntimeHostSlotLease', N'AdmissionEpoch') IS NULL
               OR COL_LENGTH(N'dbo.RuntimeHostSlotLease', N'ConfigurationDigest') IS NULL
                THROW 51002, 'Dynamics control-plane schema is not provisioned.', 1;
            SELECT 1;
            """;

        await ExecuteOperationAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = CreateCommand(connection, verifySql);
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    public async Task<RuntimeHostSlotLease?> TryAcquireAsync(
        RuntimeHostSlotLeaseNamespace leaseNamespace,
        string hostInstanceId,
        int maximumRuntimeHosts,
        TimeSpan leaseTtl,
        CancellationToken cancellationToken)
        => await TryAcquireAsync(
            new RuntimeHostSlotLeaseRequest(
                leaseNamespace,
                hostInstanceId,
                maximumRuntimeHosts,
                leaseTtl,
                AdmissionEpoch: 1,
                ConfigurationDigest: new string('0', 64)),
            cancellationToken).ConfigureAwait(false);

    public async Task<RuntimeHostSlotLease?> TryAcquireAsync(
        RuntimeHostSlotLeaseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateLeaseArguments(
            request.LeaseNamespace,
            request.HostInstanceId,
            request.MaximumRuntimeHosts,
            request.LeaseTtl,
            request.AdmissionEpoch,
            request.ConfigurationDigest);
        return await ExecuteOperationAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            // Serializable transaction 與 namespace application lock 讓「找空位、標記舊租約、寫入新租約」成為不可分割操作。
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await using var command = CreateCommand(connection, AcquireSql, transaction);
                AddCommonParameters(command, request.LeaseNamespace, request.HostInstanceId);
                AddEpochParameters(command, request.AdmissionEpoch, request.ConfigurationDigest);
                command.Parameters.Add("@maximumRuntimeHosts", SqlDbType.Int).Value = request.MaximumRuntimeHosts;
                command.Parameters.Add("@leaseTtlMilliseconds", SqlDbType.Int).Value =
                    checked((int)request.LeaseTtl.TotalMilliseconds);
                command.Parameters.Add("@quarantineSeconds", SqlDbType.Int).Value = _options.QuarantineSeconds;
                command.Parameters.Add("@lockTimeoutMilliseconds", SqlDbType.Int).Value =
                    checked(_options.CommandTimeoutSeconds * 1000);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                RuntimeHostSlotLease? lease = null;
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var slotOrdinal = reader.GetInt32(0);
                    var fencingToken = reader.GetInt64(1);
                    var expiresAtUtc = new DateTimeOffset(
                        DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc));
                    lease = new RuntimeHostSlotLease(
                        this,
                        request.LeaseNamespace,
                        request.HostInstanceId,
                        fencingToken,
                        expiresAtUtc,
                        slotOrdinal,
                        request.AdmissionEpoch,
                        request.ConfigurationDigest);
                }

                await reader.DisposeAsync().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return lease;
            }
            catch
            {
                // 即使呼叫端已取消，也使用 CancellationToken.None 完成 rollback，避免把未決交易留給 connection pool 清理。
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }).ConfigureAwait(false);
    }

    public async Task<bool> TryRenewAsync(
        RuntimeHostSlotLease lease,
        TimeSpan leaseTtl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.SlotOrdinal < 0 || leaseTtl <= TimeSpan.Zero || leaseTtl > TimeSpan.FromHours(1))
        {
            return false;
        }

        return await ExecuteOperationAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = CreateCommand(connection, RenewSql);
            AddLeaseParameters(command, lease);
            command.Parameters.Add("@leaseTtlMilliseconds", SqlDbType.Int).Value =
                checked((int)leaseTtl.TotalMilliseconds);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var fencingToken = reader.GetInt64(0);
            var expiresAtUtc = new DateTimeOffset(
                DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
            lease.Update(fencingToken, expiresAtUtc);
            return true;
        }).ConfigureAwait(false);
    }

    public async ValueTask ReleaseAsync(
        RuntimeHostSlotLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.SlotOrdinal < 0)
        {
            return;
        }

        await ExecuteOperationAsync(async () =>
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = CreateCommand(connection, ReleaseSql);
            AddLeaseParameters(command, lease);
            command.Parameters.Add("@quarantineSeconds", SqlDbType.Int).Value = _options.QuarantineSeconds;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    private async Task<T> ExecuteOperationAsync<T>(Func<Task<T>> operation)
    {
        // ActiveDatabaseOperations 是資源生命週期哨兵；任何成功、SQL 失敗、逾時或取消路徑都必須在 finally 回到基線。
        Interlocked.Increment(ref _activeDatabaseOperations);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SqlException or TimeoutException)
        {
            _logger.LogWarning(
                ex,
                "Dynamics durable host-slot coordinator operation failed closed.");
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _activeDatabaseOperations);
        }
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            // OpenAsync 失敗時 SqlConnection 尚未交給 await using 擁有，因此必須在此方法內立即 DisposeAsync。
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private SqlCommand CreateCommand(
        SqlConnection connection,
        string commandText,
        SqlTransaction? transaction = null)
        => new(commandText, connection, transaction)
        {
            CommandType = CommandType.Text,
            CommandTimeout = _options.CommandTimeoutSeconds
        };

    private static void AddCommonParameters(
        SqlCommand command,
        RuntimeHostSlotLeaseNamespace leaseNamespace,
        string hostInstanceId)
    {
        command.Parameters.Add("@leaseNamespaceId", SqlDbType.NVarChar, 128).Value =
            leaseNamespace.LeaseNamespaceId;
        command.Parameters.Add("@hostInstanceId", SqlDbType.NVarChar, 128).Value = hostInstanceId;
    }

    private static void AddLeaseParameters(SqlCommand command, RuntimeHostSlotLease lease)
    {
        AddCommonParameters(command, lease.LeaseNamespace, lease.HostInstanceId);
        command.Parameters.Add("@slotOrdinal", SqlDbType.Int).Value = lease.SlotOrdinal;
        command.Parameters.Add("@expectedFencingToken", SqlDbType.BigInt).Value = lease.FencingToken;
        AddEpochParameters(command, lease.AdmissionEpoch, lease.ConfigurationDigest);
    }

    private static void AddEpochParameters(
        SqlCommand command,
        long admissionEpoch,
        string configurationDigest)
    {
        command.Parameters.Add("@admissionEpoch", SqlDbType.BigInt).Value = admissionEpoch;
        command.Parameters.Add("@configurationDigest", SqlDbType.Char, 64).Value = configurationDigest;
    }

    private static void ValidateLeaseArguments(
        RuntimeHostSlotLeaseNamespace leaseNamespace,
        string hostInstanceId,
        int maximumRuntimeHosts,
        TimeSpan leaseTtl,
        long admissionEpoch = 1,
        string? configurationDigest = null)
    {
        if (string.IsNullOrWhiteSpace(leaseNamespace.LeaseNamespaceId) ||
            leaseNamespace.LeaseNamespaceId.Length > 128)
        {
            throw new ArgumentException("Lease namespace must contain 1-128 characters.", nameof(leaseNamespace));
        }

        if (string.IsNullOrWhiteSpace(hostInstanceId) || hostInstanceId.Length > 128)
        {
            throw new ArgumentException("Host instance ID must contain 1-128 characters.", nameof(hostInstanceId));
        }

        if (maximumRuntimeHosts is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRuntimeHosts));
        }

        if (leaseTtl <= TimeSpan.Zero || leaseTtl > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseTtl));
        }

        if (admissionEpoch < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(admissionEpoch));
        }

        if (configurationDigest is null ||
            configurationDigest.Length != 64 ||
            configurationDigest.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Configuration digest must be exactly 64 hexadecimal characters.",
                nameof(configurationDigest));
        }
    }
}
