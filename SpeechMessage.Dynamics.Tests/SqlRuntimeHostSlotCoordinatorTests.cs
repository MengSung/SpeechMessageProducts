using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 將需要真實 SQL LocalDB 的測試標成明確的環境型測試；測試探索階段只讀取環境變數是否存在，
/// 不保存、不輸出也不解析連線秘密。未提供連線字串時由 xUnit 回報「略過」而不是把測試當成成功，
/// 因此一般單元測試仍可執行，但任何宣稱 live durable SQL 已驗證的流程都必須看到實際執行紀錄。
/// 此 attribute 沒有背景工作或可釋放資源，唯一 owner 是測試探索器，並行探索只讀取 process environment。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class LiveSqlFactAttribute : FactAttribute
{
    /// <summary>
    /// 建立 live SQL 測試標記；缺少明確連線設定時 fail closed 為可見的 skip reason，
    /// 避免以 silent return 製造假的綠燈，同時不讓日常快速測試被非必要的本機資料庫前置條件阻斷。
    /// </summary>
    public LiveSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                SqlRuntimeHostSlotCoordinatorTests.LiveConnectionStringEnvironmentVariable)))
        {
            Skip = $"需要先設定 {SqlRuntimeHostSlotCoordinatorTests.LiveConnectionStringEnvironmentVariable}，" +
                "並以顯式 provisioning script 建立 LocalDB schema；未執行 live SQL contract。";
        }
    }
}

/// <summary>
/// 驗證 SQL durable host-slot coordinator 的設定界線、schema 隔離、故障關閉與真實資料庫原子契約。
/// Provisioning script 是 schema 建立的唯一人工 owner；Gateway 與 live contract 只驗證既有 schema，不能在啟動或測試中暗自建表。
/// Live contract 只接受同一 Windows user 的固定 LocalDB 與專用資料庫，並使用唯一 namespace，避免測試彼此或正式租約互相污染。
/// </summary>
public sealed class SqlRuntimeHostSlotCoordinatorTests
{
    /// <summary>
    /// live SQL contract 唯一允許的 process-level 設定名稱；值由執行者在測試程序生命週期內擁有，
    /// 本測試只短暫讀取並交給 SqlClient，不寫入檔案、log、assertion 訊息或共享靜態快取。
    /// </summary>
    internal const string LiveConnectionStringEnvironmentVariable =
        "SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION";

    /// <summary>不允許零或無界 timeout/quarantine，避免資料庫故障造成忙迴圈或永久容量凍結。</summary>
    [Fact]
    public void Options_reject_unbounded_or_unsafe_values()
    {
        var options = new SqlRuntimeHostSlotCoordinatorOptions
        {
            ConnectionString = "Server=localhost;Database=SpeechMessageDynamicsControlPlane;Integrated Security=true;",
            CommandTimeoutSeconds = 0,
            QuarantineSeconds = 0
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// 耐久控制平面僅能使用 Windows 整合驗證；在建立任何 SQL 連線以前即拒絕 SQL 帳號模式，
    /// 使 Gateway 不會因設定漂移而保存、傳送或記錄資料庫密碼。這個測試刻意只提供非整合式
    /// 使用者名稱，不包含任何真實或測試密碼，以驗證邊界不依賴機密字串才會 fail closed。
    /// </summary>
    [Fact]
    public void Options_reject_sql_authentication_connection_strings()
    {
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = SqlRuntimeHostSlotCoordinatorOptions.RequiredDatabaseName,
            IntegratedSecurity = false,
            UserID = "non-integrated-test-identity"
        }.ConnectionString;
        var options = new SqlRuntimeHostSlotCoordinatorOptions
        {
            ConnectionString = connectionString,
            CommandTimeoutSeconds = 5,
            QuarantineSeconds = 1
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*integrated*authentication*");
    }

    /// <summary>
    /// 即使連線字串宣告整合驗證，SQL 使用者欄位仍會被 coordinator 的長生命週期設定保留；
    /// 因此必須在開啟連線之前拒絕，避免錯置的帳密欄位跨 profile、log 或診斷路徑殘留。
    /// 測試只使用無機密的佔位使用者名稱，從行為上驗證不接受任何 SQL 身分欄位。
    /// </summary>
    [Fact]
    public void Options_reject_sql_user_fields_when_integrated_security_is_enabled()
    {
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = SqlRuntimeHostSlotCoordinatorOptions.RequiredDatabaseName,
            IntegratedSecurity = true,
            UserID = "unexpected-sql-user-field"
        }.ConnectionString;
        var options = new SqlRuntimeHostSlotCoordinatorOptions
        {
            ConnectionString = connectionString,
            CommandTimeoutSeconds = 5,
            QuarantineSeconds = 1
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must not contain*credential*");
    }

    /// <summary>
    /// Coordinator 建構時必須把已驗證的非機密設定複製為 immutable snapshot；DI 中保留的
    /// options singleton 若在之後遭到錯誤修改，不能改寫既有 coordinator 的 SQL 路由、
    /// timeout 或 quarantine 行為。測試以無效關鍵字作為後續 mutation：正確實作仍會使用
    /// 原本可解析但不可連線的 loopback 設定，因此只會得到受控的 <see cref="SqlException"/>。
    /// </summary>
    [Fact]
    public async Task Coordinator_snapshots_validated_options_before_any_connection_attempt()
    {
        var options = new SqlRuntimeHostSlotCoordinatorOptions
        {
            ConnectionString = "Server=127.0.0.1,1;Database=SpeechMessageDynamicsControlPlane;Integrated Security=true;Connect Timeout=1;",
            CommandTimeoutSeconds = 1,
            QuarantineSeconds = 1
        };
        var coordinator = new SqlRuntimeHostSlotCoordinator(
            options,
            NullLogger<SqlRuntimeHostSlotCoordinator>.Instance);

        options.ConnectionString = "UnsupportedCoordinatorMutation=blocked";

        var act = () => coordinator.VerifySchemaAsync(CancellationToken.None);

        await act.Should().ThrowAsync<SqlException>();
        coordinator.ActiveDatabaseOperations.Should().Be(0);
    }

    /// <summary>schema 必須位於獨立 control-plane，不得接觸 MSCRM_CONFIG 或 OrganizationBase。</summary>
    [Fact]
    public void Schema_is_scoped_to_the_standalone_control_plane_database()
    {
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("RuntimeHostSlotLease");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("RuntimeHostFencingSequence");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("RuntimeHostAdmissionEpoch");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("ConfigurationDigest");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("AdmissionEpoch");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().Contain("SYSUTCDATETIME()");
        SqlRuntimeHostSlotCoordinator.SchemaSql.Should().NotContain("MSCRM_CONFIG");
        System.Text.RegularExpressions.Regex.IsMatch(
                SqlRuntimeHostSlotCoordinator.SchemaSql,
                @"(?<![A-Za-z0-9_])(?:\[?dbo\]?\.)?\[?OrganizationBase\]?(?![A-Za-z0-9_])",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Should().BeFalse(
                "control-plane SQL must never target the CRM OrganizationBase table; " +
                "NormalizedOrganizationBaseUri is only a local canonical-key column");
    }

    /// <summary>
    /// 耐久 SQL 協調器必須把每一個租約命名空間與唯一的實體 Dynamics Organization 綁定，
    /// 並以 SQL 的二進位字串語意維持與記憶體 <see cref="StringComparer.Ordinal"/> 相同的身分判斷。
    /// 否則兩個程序只要錯設不同的 LeaseNamespaceId，便可能各自取得同一個 Organization 的完整 host-slot 預算；
    /// 大小寫不同的命名空間也可能在資料庫預設不區分大小寫定序下互相干擾。
    /// </summary>
    [Fact]
    public void Durable_schema_requires_canonical_organization_binding_and_ordinal_string_semantics()
    {
        var repositoryRoot = FindRepositoryRoot();
        var provisionedSchema = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "eng",
            "dynamics-control-plane-schema.sql"));

        foreach (var schema in new[] { SqlRuntimeHostSlotCoordinator.SchemaSql, provisionedSchema })
        {
            schema.Should().Contain("RuntimeHostOrganizationBinding");
            schema.Should().Contain("ExpectedOrganizationId uniqueidentifier NOT NULL");
            schema.Should().Contain("NormalizedOrganizationBaseUri nvarchar(450) COLLATE Latin1_General_100_BIN2 NOT NULL");
            schema.Should().Contain("UQ_RuntimeHostOrganizationBinding_ExpectedOrganizationId");
            schema.Should().Contain("UQ_RuntimeHostOrganizationBinding_NormalizedOrganizationBaseUri");
            schema.Should().Contain("FK_RuntimeHostAdmissionEpoch_OrganizationBinding");
            schema.Should().Contain("LeaseNamespaceId nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL");
            schema.Should().Contain("HostInstanceId nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL");
            schema.Should().Contain("ConfigurationDigest char(64) COLLATE Latin1_General_100_BIN2 NOT NULL");
        }

        var acquireSql = (string?)typeof(SqlRuntimeHostSlotCoordinator)
            .GetField("AcquireSql", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetRawConstantValue();
        acquireSql.Should().NotBeNull();
        acquireSql!.Should().Contain("RuntimeHostOrganizationBinding");
        acquireSql.Should().Contain("canonical Dynamics organization");
    }

    /// <summary>
    /// live SQL contract 建立的 canonical binding 是耐久測試資料的一部分；在外鍵存在時，cleanup 必須依
    /// slot lease、admission epoch、organization binding 的相依順序刪除，否則每次 opt-in live run 都會
    /// 遺留無界測試資料，或因 FK 拒絕而遮蔽真正的 contract 結果。
    /// </summary>
    [Fact]
    public void Live_sql_cleanup_deletes_canonical_binding_after_dependent_rows()
    {
        var cleanupSql = (string?)typeof(SqlRuntimeHostSlotCoordinatorTests)
            .GetField(
                "OwnedNamespaceCleanupSql",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetRawConstantValue();

        cleanupSql.Should().NotBeNull();
        cleanupSql!.IndexOf("DELETE dbo.RuntimeHostSlotLease", StringComparison.Ordinal)
            .Should().BeGreaterThanOrEqualTo(0);
        cleanupSql.IndexOf("DELETE dbo.RuntimeHostAdmissionEpoch", StringComparison.Ordinal)
            .Should().BeGreaterThan(
                cleanupSql.IndexOf("DELETE dbo.RuntimeHostSlotLease", StringComparison.Ordinal));
        cleanupSql.IndexOf("DELETE dbo.RuntimeHostOrganizationBinding", StringComparison.Ordinal)
            .Should().BeGreaterThan(
                cleanupSql.IndexOf("DELETE dbo.RuntimeHostAdmissionEpoch", StringComparison.Ordinal));
    }

    /// <summary>
    /// SQL durable coordinator 的 acquire 請求必須攜帶已驗證的 canonical organization key。
    /// 此型別是跨程序資料庫繫結的唯一非機密物理身分；不得由 LeaseNamespaceId、環境名稱或主機名稱臨時推導，
    /// 以免設定錯誤時把同一個 Dynamics Organization 分裂成多份容量預算。
    /// </summary>
    [Fact]
    public void Durable_lease_request_carries_canonical_organization_identity()
    {
        var property = typeof(RuntimeHostSlotLeaseRequest)
            .GetProperty("CanonicalOrganizationKey");

        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(CanonicalOrganizationCapacityKey));
    }

    /// <summary>
    /// canonical base URI 是 durable store 的唯一索引鍵之一，因此設定驗證必須在建立 runtime、連線或 SQL transaction 前
    /// 拒絕超過 SQL Server 900-byte unique-index 邊界的值。這個界限同時讓跨程序 key 的記憶體與資料庫表示維持有界，
    /// 不會把任意長度的端點資料保留到長期控制平面。
    /// </summary>
    [Fact]
    public void Canonical_organization_key_rejects_base_uri_that_exceeds_durable_store_index_bound()
    {
        var oversizedRoot = new Uri(
            "https://crm.example.local/" + new string('a', 450) + "/api/data/v9.1/");

        var created = CanonicalOrganizationCapacityKey.TryCreate(
            Guid.NewGuid(),
            oversizedRoot,
            "v9.1",
            out _,
            out var error);

        created.Should().BeFalse();
        error.Should().Contain("450");
    }

    /// <summary>
    /// 舊式僅提供 LeaseNamespaceId 的 SQL acquire overload 無法安全猜測實體 Organization，
    /// 所以必須在建立連線、交易、等待或保留任何 SQL 資源之前 fail-closed。
    /// 這也避免舊呼叫端悄悄繞過 namespace-to-organization 的 durable binding。
    /// </summary>
    [Fact]
    public async Task Durable_coordinator_rejects_legacy_acquire_without_canonical_organization_identity_before_connection_ownership()
    {
        var coordinator = new SqlRuntimeHostSlotCoordinator(
            new SqlRuntimeHostSlotCoordinatorOptions
            {
                ConnectionString = "Server=127.0.0.1,1;Database=SpeechMessageDynamicsControlPlane;Integrated Security=true;Encrypt=false;Connect Timeout=1;",
                CommandTimeoutSeconds = 1,
                QuarantineSeconds = 1
            },
            NullLogger<SqlRuntimeHostSlotCoordinator>.Instance);

        var act = async () => await coordinator.TryAcquireAsync(
            new RuntimeHostSlotLeaseNamespace("legacy-without-canonical"),
            "host-1",
            maximumRuntimeHosts: 1,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*canonical organization*identity*");
        coordinator.ActiveDatabaseOperations.Should().Be(0);
    }

    /// <summary>
    /// 固定人工 provisioning 的安全契約：只可啟動 MSSQLLocalDB、只可建立專用 control-plane database、
    /// 必須重複執行安全地套用 checked-in schema，且不得授與 sysadmin、db_owner、CONTROL 等廣泛權限。
    /// 這個測試只讀取 repository 文字，不啟動 LocalDB；因此 RED 必須精準指出 script 尚未建立或契約缺漏。
    /// </summary>
    [Fact]
    public void Localdb_provisioning_script_is_explicit_idempotent_and_least_privilege()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(
            repositoryRoot,
            "docs",
            "scripts",
            "Provision-DynamicsControlPlaneLocalDb.ps1");

        File.Exists(scriptPath).Should().BeTrue(
            "LocalDB schema 只能由明確執行的人工 provisioning script 建立，Gateway 不得自行補建");

        var script = File.ReadAllText(scriptPath);
        script.Should().Contain("$InstanceName");
        script.Should().Contain("$DatabaseName");
        script.Should().Contain("$SchemaFile");
        script.Should().Contain("MSSQLLocalDB");
        script.Should().Contain("(localdb)\\MSSQLLocalDB");
        script.Should().Contain("SpeechMessageDynamicsControlPlane");
        script.Should().Contain("eng\\dynamics-control-plane-schema.sql");
        script.Should().Contain("sqllocaldb");
        script.Should().Contain("start");
        script.Should().Contain("IF DB_ID");
        script.Should().Contain("CREATE DATABASE");
        script.Should().Contain("-b");
        script.Should().Contain("-i");
        script.Should().Contain("$LASTEXITCODE");
        script.Should().Contain("同一個 Windows 使用者");
        script.Should().Contain("單機 Development");
        script.Should().Contain("不代表 Central");
        script.Should().Contain("多主機");

        System.Text.RegularExpressions.Regex.IsMatch(
                script,
                @"(?im)^\s*GRANT\s+|sysadmin|db_owner|GRANT\s+CONTROL")
            .Should().BeFalse("LocalDB 開發驗證不需要也不得擴張登入或資料庫權限");
    }

    /// <summary>
    /// 驗證 Gateway 的 Development configuration 會在標準 ASP.NET Core precedence 下，把基底的網路 SQL
    /// target 覆寫為同一 Windows 使用者擁有的固定 LocalDB instance 與專用 control-plane database。
    /// 測試只解析 checked-in JSON 與連線字串欄位，不開啟 SQL connection、不啟動 LocalDB，也不讀取環境變數或秘密；
    /// 主要 assertion 同時保護 durable coordinator 的實際可啟動性、整合式驗證、bounded pool/timeout，以及
    /// Development CRM target 仍維持不可路由的 fail-closed 位址，避免本機啟動意外碰觸正式 CE 組織。
    /// </summary>
    [Fact]
    public void Gateway_development_configuration_uses_dedicated_localdb_and_fail_closed_crm_target()
    {
        var repositoryRoot = FindRepositoryRoot();
        var gatewayRoot = Path.Combine(repositoryRoot, "SpeechMessage.Dynamics.Gateway");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(gatewayRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();

        var connectionString = configuration.GetConnectionString("DynamicsControlPlane");
        connectionString.Should().NotBeNullOrWhiteSpace(
            "非 Testing Gateway 必須在接流量前取得明確 durable coordinator 設定");

        var connection = new SqlConnectionStringBuilder(connectionString);
        connection.DataSource.Should().Be(@"(localdb)\MSSQLLocalDB");
        connection.InitialCatalog.Should().Be("SpeechMessageDynamicsControlPlane");
        connection.IntegratedSecurity.Should().BeTrue();
        connection.UserID.Should().BeNullOrEmpty();
        connection.Password.Should().BeNullOrEmpty();
        connection.Pooling.Should().BeTrue();
        connection.MaxPoolSize.Should().BeInRange(1, 32);
        connection.ConnectTimeout.Should().BeInRange(1, 5);
        connection.ApplicationName.Should().Be("SpeechMessage.Dynamics.Gateway.Development");

        configuration["DynamicsProfiles:Profiles:crm82:OrganizationWebApiBaseUri"]
            .Should().Be("https://dynamics-local.invalid/api/data/v8.2/");
    }

    /// <summary>
    /// Gateway startup 必須只驗證 operator 已 provision 的 schema；任何自動呼叫 script 或 EnsureSchemaAsync
    /// 都會把部署錯誤變成應用程式的隱性 DDL ownership，並在多程序啟動時製造不必要的併發與權限風險。
    /// 此測試只讀取 Gateway C# 原始碼，不持有 runtime、connection 或背景工作。
    /// </summary>
    [Fact]
    public void Gateway_startup_verifies_schema_without_invoking_provisioning_or_schema_creation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var gatewayRoot = Path.Combine(repositoryRoot, "SpeechMessage.Dynamics.Gateway");
        var gatewaySource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(gatewayRoot, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        gatewaySource.Should().Contain("VerifySchemaAsync");
        gatewaySource.Should().NotContain("EnsureSchemaAsync");
        gatewaySource.Should().NotContain("Provision-DynamicsControlPlaneLocalDb");
    }

    /// <summary>注入無法連線的 SQL endpoint，證明錯誤向上傳播且 ActiveDatabaseOperations 必定回到零。</summary>
    [Fact]
    public async Task Coordinator_outage_fails_closed_without_retained_connection_or_task()
    {
        var coordinator = new SqlRuntimeHostSlotCoordinator(
            new SqlRuntimeHostSlotCoordinatorOptions
            {
                ConnectionString = "Server=127.0.0.1,1;Database=SpeechMessageDynamicsControlPlane;Integrated Security=true;Encrypt=false;Connect Timeout=1;",
                CommandTimeoutSeconds = 1,
                QuarantineSeconds = 1
            },
            NullLogger<SqlRuntimeHostSlotCoordinator>.Instance);

        var act = async () => await coordinator.TryAcquireAsync(
            CreateLeaseRequest(
                CreateCanonicalOrganizationKey("outage-test"),
                new RuntimeHostSlotLeaseNamespace("outage-test"),
                "host-1",
                maximumRuntimeHosts: 1,
                leaseTtl: TimeSpan.FromSeconds(5)),
            CancellationToken.None);

        await act.Should().ThrowAsync<SqlException>();
        coordinator.ActiveDatabaseOperations.Should().Be(0);
    }

    /// <summary>
    /// 在明確 live LocalDB/SQL 上證明 epoch drift 拒絕、同 namespace 槽位上限、fencing token 單調遞增、
    /// stale renew/release 無效、不同 namespace 隔離及 quarantine 到期前不可重用。測試方法唯一擁有本次建立的
    /// 隨機 namespace 與 lease；不論 contract 成功或失敗都會嘗試刪除 durable rows，並獨立檢查 coordinator
    /// 的 operation sentinel。Cleanup 或 sentinel 失敗不得遮蔽原始 SQL/assertion 失敗，多重例外會一起回報；
    /// 所有工作皆有固定 timeout、有限 namespace 數量且不建立背景 task，避免 live 驗證本身造成無界資源保留。
    /// </summary>
    [LiveSqlFact]
    public async Task Live_sql_contract_is_atomic_fenced_quarantined_and_namespace_isolated()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            LiveConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"測試探索後遺失 {LiveConnectionStringEnvironmentVariable}；拒絕以未指定 SQL 目標繼續。");

        var connectionBuilder = new SqlConnectionStringBuilder(connectionString);
        connectionBuilder.DataSource.Equals(
                @"(localdb)\MSSQLLocalDB",
                StringComparison.OrdinalIgnoreCase)
            .Should().BeTrue("Task 5 只允許同一 Windows user 的固定 LocalDB instance");
        connectionBuilder.InitialCatalog.Should().Be("SpeechMessageDynamicsControlPlane");
        connectionBuilder.IntegratedSecurity.Should().BeTrue();

        var options = new SqlRuntimeHostSlotCoordinatorOptions
        {
            ConnectionString = connectionString,
            CommandTimeoutSeconds = 5,
            QuarantineSeconds = 1
        };
        var coordinator = new SqlRuntimeHostSlotCoordinator(
            options,
            NullLogger<SqlRuntimeHostSlotCoordinator>.Instance);
        var secondaryCoordinator = new SqlRuntimeHostSlotCoordinator(
            options,
            NullLogger<SqlRuntimeHostSlotCoordinator>.Instance);
        // Schema 的唯一建立 owner 是人工 provisioning script；live contract 僅驗證現況，缺物件時必須直接失敗。
        await coordinator.VerifySchemaAsync(CancellationToken.None);

        var ownedNamespaceIds = new HashSet<string>(StringComparer.Ordinal);
        Exception? contractFailure = null;
        try
        {
            var epochNamespace = new RuntimeHostSlotLeaseNamespace(
                "epoch-contract-" + Guid.NewGuid().ToString("N"));
            var epochCanonicalKey = CreateCanonicalOrganizationKey(
                "epoch-contract-" + Guid.NewGuid().ToString("N"));
            ownedNamespaceIds.Add(epochNamespace.LeaseNamespaceId);
            var epochLease = await coordinator.TryAcquireAsync(
                new RuntimeHostSlotLeaseRequest(
                    epochCanonicalKey,
                    epochNamespace,
                    "epoch-host",
                    MaximumRuntimeHosts: 1,
                    LeaseTtl: TimeSpan.FromSeconds(5),
                    AdmissionEpoch: 7,
                    ConfigurationDigest: new string('A', 64)),
                CancellationToken.None);
            epochLease.Should().NotBeNull();

            var drift = async () => await coordinator.TryAcquireAsync(
                new RuntimeHostSlotLeaseRequest(
                    epochCanonicalKey,
                    epochNamespace,
                    "drift-host",
                    MaximumRuntimeHosts: 1,
                    LeaseTtl: TimeSpan.FromSeconds(5),
                    AdmissionEpoch: 7,
                    ConfigurationDigest: new string('B', 64)),
                CancellationToken.None);
            await drift.Should().ThrowAsync<SqlException>()
                .Where(exception => exception.Number == 51003);
            await epochLease!.DisposeAsync();

            // 兩個 coordinator instance 沒有共用 registry、static collection 或記憶體 state；
            // B 仍被 A 已寫入且在 A release 後保留的 binding 拒絕，證明唯一權威來自 SQL durable store。
            var bindingSuffix = Guid.NewGuid().ToString("N");
            var bindingCanonicalKey = CreateCanonicalOrganizationKey("binding-contract-" + bindingSuffix);
            var bindingNamespaceA = new RuntimeHostSlotLeaseNamespace("binding-a-" + bindingSuffix);
            var bindingNamespaceB = new RuntimeHostSlotLeaseNamespace("binding-b-" + bindingSuffix);
            ownedNamespaceIds.Add(bindingNamespaceA.LeaseNamespaceId);
            ownedNamespaceIds.Add(bindingNamespaceB.LeaseNamespaceId);
            var bindingLease = await coordinator.TryAcquireAsync(
                CreateLeaseRequest(
                    bindingCanonicalKey,
                    bindingNamespaceA,
                    "binding-host-a",
                    maximumRuntimeHosts: 1,
                    leaseTtl: TimeSpan.FromSeconds(3)),
                CancellationToken.None);
            bindingLease.Should().NotBeNull();
            await bindingLease!.DisposeAsync();

            var duplicateCanonicalBinding = async () => await secondaryCoordinator.TryAcquireAsync(
                CreateLeaseRequest(
                    bindingCanonicalKey,
                    bindingNamespaceB,
                    "binding-host-b",
                    maximumRuntimeHosts: 1,
                    leaseTtl: TimeSpan.FromSeconds(3)),
                CancellationToken.None);
            await duplicateCanonicalBinding.Should().ThrowAsync<SqlException>()
                .Where(exception => exception.Number == 51005);

            var suffix = Guid.NewGuid().ToString("N");
            var ns = new RuntimeHostSlotLeaseNamespace("contract-" + suffix);
            var otherNs = new RuntimeHostSlotLeaseNamespace("contract-other-" + suffix);
            var canonicalKey = CreateCanonicalOrganizationKey("contract-" + suffix);
            var otherCanonicalKey = CreateCanonicalOrganizationKey("contract-other-" + suffix);
            ownedNamespaceIds.Add(ns.LeaseNamespaceId);
            ownedNamespaceIds.Add(otherNs.LeaseNamespaceId);
            var ttl = TimeSpan.FromSeconds(3);

            var attempts = Enumerable.Range(0, 32)
                .Select(index => coordinator.TryAcquireAsync(
                    CreateLeaseRequest(
                        canonicalKey,
                        ns,
                        "host-" + index,
                        maximumRuntimeHosts: 2,
                        leaseTtl: ttl),
                    CancellationToken.None))
                .ToArray();
            var leases = await Task.WhenAll(attempts);
            leases.Count(lease => lease is not null).Should().Be(2);

            var first = leases.First(lease => lease is not null)!;
            var firstToken = first.FencingToken;
            (await coordinator.TryRenewAsync(first, ttl, CancellationToken.None)).Should().BeTrue();
            first.FencingToken.Should().BeGreaterThan(firstToken);

            var stale = new RuntimeHostSlotLease(
                coordinator,
                first.LeaseNamespace,
                first.HostInstanceId,
                firstToken,
                first.ExpiresAtUtc,
                first.SlotOrdinal);
            (await coordinator.TryRenewAsync(stale, ttl, CancellationToken.None)).Should().BeFalse();
            await stale.DisposeAsync();
            (await coordinator.TryRenewAsync(first, ttl, CancellationToken.None)).Should().BeTrue(
                "a stale release must not delete the newer fenced lease");

            var other = await coordinator.TryAcquireAsync(
                CreateLeaseRequest(
                    otherCanonicalKey,
                    otherNs,
                    "other-host",
                    maximumRuntimeHosts: 1,
                    leaseTtl: ttl),
                CancellationToken.None);
            other.Should().NotBeNull("lease namespaces have independent bounded slots");

            foreach (var lease in leases.OfType<RuntimeHostSlotLease>())
            {
                await lease.DisposeAsync();
            }
            await other!.DisposeAsync();

            var quarantined = await coordinator.TryAcquireAsync(
                CreateLeaseRequest(
                    canonicalKey,
                    ns,
                    "replacement-before-quarantine",
                    maximumRuntimeHosts: 2,
                    leaseTtl: ttl),
                CancellationToken.None);
            quarantined.Should().BeNull();
            await Task.Delay(TimeSpan.FromMilliseconds(1200));

            var replacement = await coordinator.TryAcquireAsync(
                CreateLeaseRequest(
                    canonicalKey,
                    ns,
                    "replacement-after-quarantine",
                    maximumRuntimeHosts: 2,
                    leaseTtl: ttl),
                CancellationToken.None);
            replacement.Should().NotBeNull();
            replacement!.FencingToken.Should().BeGreaterThan(first.FencingToken);
            await replacement.DisposeAsync();
        }
        catch (Exception exception)
        {
            contractFailure = exception;
        }

        var failures = new List<Exception>(capacity: 3);
        if (contractFailure is not null)
        {
            failures.Add(contractFailure);
        }

        try
        {
            // 測試 namespace 是本方法唯一建立的 durable state；即使 assertion 或 SQL contract 失敗也要刪除，
            // 避免重複 live run 讓 LocalDB 的 lease/epoch 資料列無界累積。Schema 與 database 不屬於測試 cleanup 範圍。
            await DeleteOwnedNamespacesAsync(connectionString, ownedNamespaceIds);
        }
        catch (Exception exception)
        {
            failures.Add(new InvalidOperationException(
                "Live SQL contract 的 durable test-row cleanup 失敗；可能留下本次隨機 namespace。",
                exception));
        }

        try
        {
            coordinator.ActiveDatabaseOperations.Should().Be(0);
            secondaryCoordinator.ActiveDatabaseOperations.Should().Be(0);
        }
        catch (Exception exception)
        {
            failures.Add(new InvalidOperationException(
                "Live SQL contract 結束後仍有 coordinator database operation；可能存在 retained task/connection。",
                exception));
        }

        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(
                "Live SQL contract、durable cleanup 或 lifecycle sentinel 發生多重失敗。",
                failures);
        }
    }

    /// <summary>
    /// 刪除本次 live contract 唯一擁有的隨機 namespace 資料列；固定 LocalDB/database guard 已在呼叫端完成，
    /// 每個 namespace 以參數化 transaction 依 FK 順序刪 lease、epoch、canonical binding，避免把測試字串當 SQL
    /// 或留下半套控制面狀態。正式環境的 binding 不由 runtime release 刪除；這裡只清理本測試唯一產生的隨機資料。
    /// Connection、transaction 與 command 都由此方法的 await using 唯一擁有並在成功、取消或例外時確定釋放；
    /// 清理量最多三個 namespace，選擇簡單循序交易以換取可稽核性，而不是引入背景批次或共享 connection。
    /// </summary>
    private const string OwnedNamespaceCleanupSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DELETE dbo.RuntimeHostSlotLease WHERE LeaseNamespaceId = @leaseNamespaceId;
        DELETE dbo.RuntimeHostAdmissionEpoch WHERE LeaseNamespaceId = @leaseNamespaceId;
        DELETE dbo.RuntimeHostOrganizationBinding WHERE LeaseNamespaceId = @leaseNamespaceId;
        COMMIT TRANSACTION;
        """;

    /// <summary>
    /// 建立只屬於本次測試的 canonical organization key。
    /// helper 透過正式 <see cref="CanonicalOrganizationCapacityKey.TryCreate"/> 路徑產生標準化 URI，
    /// 讓 live SQL contract 驗證的正是 production 會寫入 durable binding 的物理身分，而非自行拼湊的測試字串。
    /// 每次呼叫使用新的 GUID 與 DNS 無效的測試主機名；不會發出網路要求、保留 credential 或共享任何 Session/Token 狀態。
    /// </summary>
    private static CanonicalOrganizationCapacityKey CreateCanonicalOrganizationKey(string suffix)
    {
        var created = CanonicalOrganizationCapacityKey.TryCreate(
            Guid.NewGuid(),
            new Uri($"https://sql-coordinator-{suffix}.invalid/api/data/v9.1/"),
            "v9.1",
            out var key,
            out var error);

        created.Should().BeTrue(error);
        return key;
    }

    /// <summary>
    /// 以完整 canonical identity 建立 SQL acquire request，避免 live contract 或 outage regression
    /// 意外退回已被耐久 coordinator 拒絕的 namespace-only overload。
    /// 回傳值是短生命週期 request DTO，不持有連線、transaction、lease、計時器或任何敏感資料；
    /// 取得成功後的 RuntimeHostSlotLease 仍由呼叫端以 <c>await using</c> 或明確 DisposeAsync 負責釋放。
    /// </summary>
    private static RuntimeHostSlotLeaseRequest CreateLeaseRequest(
        CanonicalOrganizationCapacityKey canonicalOrganizationKey,
        RuntimeHostSlotLeaseNamespace leaseNamespace,
        string hostInstanceId,
        int maximumRuntimeHosts,
        TimeSpan leaseTtl,
        long admissionEpoch = 1,
        string? configurationDigest = null)
        => new(
            canonicalOrganizationKey,
            leaseNamespace,
            hostInstanceId,
            maximumRuntimeHosts,
            leaseTtl,
            admissionEpoch,
            configurationDigest ?? new string('0', 64));

    private static async Task DeleteOwnedNamespacesAsync(
        string connectionString,
        IEnumerable<string> namespaceIds)
    {
        var ownedIds = namespaceIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ownedIds.Length == 0)
        {
            return;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);

        foreach (var namespaceId in ownedIds)
        {
            await using var command = new SqlCommand(OwnedNamespaceCleanupSql, connection)
            {
                CommandTimeout = 5
            };
            command.Parameters.Add("@leaseNamespaceId", System.Data.SqlDbType.NVarChar, 128).Value =
                namespaceId;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// 從測試輸出目錄向上尋找 repository root；只接受同時含 checked-in schema 與 Gateway 專案的目錄，
    /// 避免依賴目前工作目錄而誤讀另一個 checkout。DirectoryInfo 沒有外部 handle，方法沒有需延後清理的資源；
    /// 找不到唯一可信根目錄時立即失敗，不以猜測路徑繼續讀取安全契約。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "eng", "dynamics-control-plane-schema.sql")) &&
                Directory.Exists(Path.Combine(current.FullName, "SpeechMessage.Dynamics.Gateway")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "找不到包含 Dynamics control-plane schema 與 Gateway 專案的 repository root。");
    }
}
