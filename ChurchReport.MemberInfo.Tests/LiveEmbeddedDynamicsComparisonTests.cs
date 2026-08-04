// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/LiveEmbeddedDynamicsComparisonTests.cs
// 用途：以同一個真實 CE WhoAmI 工作負載，量測既有 ToolUtility legacy pool 與 P4 Embedded Data8 pipeline。
//
// 真機驗證與資源生命週期契約：
// 1. 本檔案預設略過；只有明確設為 1 的環境旗標與目前測試程序的 CRM_PASSWORD 同時存在時才會連線。它不把
//    密碼、端點、帳號或原始例外寫進測試輸出、assertion 或靜態欄位。
// 2. 每一路徑先 warm-up 一次，接著以固定且有界的 21 個循序樣本量測同一個無參數 WhoAmI。legacy 必定在
//    finally 歸還已借用 service 並 Dispose pool；Embedded 的 runtime、pool、permit、client 與 logger factory
//    亦由同一 finally 依唯一 owner 釋放，避免 benchmark 自己製造 Session、Timer、Handle 或連線洩漏。
// 3. 測試只允許比較同一組帳密回傳的三個 GUID。任一組織不符、逾時、失敗或 p95 劣於 legacy 都是 fail closed；
//    它不開啟 Package01FeeReadsEnabled、不修改收費查詢路由，也不使用 Gateway、Web API、IFD 或管理通道。
// ============================================================================

using System.Diagnostics;
using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Guard;
using SpeechMessage.Dynamics.Embedded;
using ToolUtilityNameSpace.ConnectionOperations;
using Xunit;
using Xunit.Abstractions;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 真實 CE 的 P4 對照量測。這些測試的目的不是取代離線單元／生命週期測試，而是在明確啟用時證明同一組
/// ChurchReport 部署設定的 legacy 與 Embedded 取得相同 WhoAmI 身分，並產生可稽核的 p50、p95、p99。
/// 測試類別不保存 Host、ServiceProvider、credential、client 或執行結果；所有可釋放物件只存在於單一測試方法。
/// </summary>
public sealed class LiveEmbeddedDynamicsComparisonTests
{
    /// <summary>
    /// 量測樣本數固定為 21，足以讓 nearest-rank p95 與 p99 有意義，同時避免開發機 F5 驗證造成無界或過度
    /// 的 CE 流量。此為 non-destructive WhoAmI 的唯一重複次數，不適用於正式業務查詢或 Package01 讀取。
    /// </summary>
    private const int MeasurementSampleCount = 21;

    private readonly ITestOutputHelper _output;

    /// <summary>
    /// 建立測試輸出 sink。xUnit 擁有 sink 的生命週期；本類別只在測試結束前寫入已彙總的毫秒統計，絕不輸出
    /// endpoint、帳號、密碼、token、原始 CRM exception 或單次 WhoAmI GUID。
    /// </summary>
    /// <param name="output">由 xUnit 注入且在測試範圍內有效的測試輸出。</param>
    public LiveEmbeddedDynamicsComparisonTests(ITestOutputHelper output)
        => _output = output ?? throw new ArgumentNullException(nameof(output));

    /// <summary>
    /// 以同一組 ChurchReport CrmConnection 設定進行 warm-up 與有界延遲比較。故障注入由真實部署的錯誤
    /// Organization、連線、逾時或資源釋放問題自然提供；決定性斷言是 legacy/Embedded 的 User、BusinessUnit、
    /// Organization 完全一致、Embedded p95 不高於 legacy p95，並在 finally 後 Dispose 兩個 pool owner。
    /// </summary>
    [LiveDynamicsP4Fact]
    public async Task Live_legacy_and_embedded_whoami_are_identity_equivalent_and_embedded_p95_is_not_worse()
    {
        var configuration = CreateDevelopmentConfiguration();
        var options = ChurchReport.Services.DonationDynamicsAccessBootstrap.BindOptions(configuration);
        options.ConnectionMode.Should().Be(ConnectionMode.Embedded);

        ChurchReport.Services.CrmConnectionEmbeddedProfileMapper.TryCreate(
            configuration,
            options,
            out var profiles,
            out var catalog,
            out var profileMappingError).Should().BeTrue(profileMappingError);
        profiles.TryGetValue(options.ProfileAlias, out var profile).Should().BeTrue();
        catalog.TryGetValue(options.ProfileAlias, out var catalogEntry).Should().BeTrue();
        profile.Should().NotBeNull();
        catalogEntry.Should().NotBeNull();

        var password = Environment.GetEnvironmentVariable(LiveDynamicsP4FactAttribute.PasswordEnvironmentVariable)
            ?? throw new InvalidOperationException("The opted-in Dynamics password environment variable is unavailable.");
        var serviceUri = configuration["CrmConnection:ServerUrl"]
            ?? throw new InvalidOperationException("The configured Dynamics service URI is unavailable.");
        var userName = configuration["CrmConnection:Username"]
            ?? throw new InvalidOperationException("The configured Dynamics user name is unavailable.");

        CrmConnectionPool? legacyPool = null;
        ILoggerFactory? loggerFactory = null;
        ChurchReport.Services.EmbeddedData8Runtime? embeddedRuntime = null;
        try
        {
            // legacy pool 的最小值必須為 1；在量測前建立且 warm-up，可把建立 WCF client 的冷啟動成本
            // 公平地排除在兩邊的 p50/p95/p99 之外。finally 是 timer、semaphore 與 service 的唯一釋放點。
            legacyPool = new CrmConnectionPool(
                new CrmConnectionService(),
                serviceUri,
                userName,
                password,
                minPoolSize: 1,
                maxPoolSize: 1,
                connectionTimeout: TimeSpan.FromSeconds(30),
                idleTimeout: TimeSpan.FromMinutes(1));

            ChurchReport.Services.CrmConnectionEmbeddedProfileMapper.TryCreateConnectionSettings(
                configuration,
                catalogEntry!.ServiceUri,
                out var connectionSettings,
                out var connectionSettingsError).Should().BeTrue(connectionSettingsError);
            connectionSettings.Should().NotBeNull();

            loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            embeddedRuntime = new ChurchReport.Services.EmbeddedData8Runtime(
                profiles,
                catalog,
                options.ProfileAlias,
                new SpeechMessage.Dynamics.Connectors.Data8.OnPremiseData8ConnectorClientFactory(connectionSettings!),
                loggerFactory.CreateLogger<ChurchReport.Services.EmbeddedData8Runtime>(),
                loggerFactory);
            var embeddedAdapter = new EmbeddedHostAdapter(
                new RequestGuard([OperationIds.RuntimeHealthWhoAmI]),
                embeddedRuntime.Executor,
                options.ProfileAlias);

            var expectedOrganizationId = catalogEntry!.OrganizationId;
            var legacyWarmup = ExecuteLegacyWhoAmI(legacyPool);
            var embeddedWarmup = await ExecuteEmbeddedWhoAmIAsync(embeddedAdapter, options.ProfileAlias);
            AssertEquivalentIdentity(legacyWarmup, embeddedWarmup, expectedOrganizationId);

            var legacyLatencies = new List<TimeSpan>(MeasurementSampleCount);
            var embeddedLatencies = new List<TimeSpan>(MeasurementSampleCount);
            for (var index = 0; index < MeasurementSampleCount; index++)
            {
                var legacyMeasurement = Measure(ExecuteLegacyWhoAmI, legacyPool);
                var embeddedMeasurement = await MeasureAsync(
                    () => ExecuteEmbeddedWhoAmIAsync(embeddedAdapter, options.ProfileAlias));

                AssertEquivalentIdentity(
                    legacyMeasurement.Identity,
                    embeddedMeasurement.Identity,
                    expectedOrganizationId);
                legacyLatencies.Add(legacyMeasurement.Elapsed);
                embeddedLatencies.Add(embeddedMeasurement.Elapsed);
            }

            var legacyMetrics = LatencyMetrics.Create(legacyLatencies);
            var embeddedMetrics = LatencyMetrics.Create(embeddedLatencies);
            _output.WriteLine("P4 CE WhoAmI performance: legacy {0}; embedded {1}", legacyMetrics, embeddedMetrics);
            embeddedMetrics.P95.Should().BeLessThanOrEqualTo(
                legacyMetrics.P95,
                because: "P4 requires the HTTP-free Embedded p95 to be no worse than the equivalent legacy WhoAmI p95");
        }
        finally
        {
            // Embedded runtime 必須先 drain Data8 Pool，再停止 admission；其 DisposeAsync 已保證此順序。
            // legacy pool 只在所有 borrowed service 已於 ExecuteLegacyWhoAmI 的 finally 歸還後 Dispose，
            // 因而不留存它建立的 cleanup Timer、SemaphoreSlim、WCF service 或 credential graph。
            if (embeddedRuntime is not null)
            {
                await embeddedRuntime.DisposeAsync();
            }

            legacyPool?.Dispose();
            loggerFactory?.Dispose();
        }
    }

    /// <summary>
    /// 執行 legacy pool 的唯一 non-destructive operation。service 只在此同步方法內存在；不論 WhoAmI 成功、
    /// 失敗或 projection 例外，finally 都將它歸還給 legacy pool，避免 benchmark 造成 active connection 泄漏。
    /// </summary>
    /// <param name="pool">測試方法唯一擁有且尚未 Dispose 的 legacy connection pool。</param>
    /// <returns>僅含三個 GUID 的安全身分純值，不含 CRM service、credential 或端點。</returns>
    private static DynamicsIdentity ExecuteLegacyWhoAmI(CrmConnectionPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        Microsoft.Xrm.Sdk.IOrganizationService? service = null;
        try
        {
            service = pool.AcquireConnection();
            var response = service.Execute(new WhoAmIRequest()) as WhoAmIResponse
                ?? throw new InvalidOperationException("The legacy Dynamics WhoAmI response is invalid.");
            return DynamicsIdentity.From(response.UserId, response.BusinessUnitId, response.OrganizationId);
        }
        finally
        {
            if (service is not null)
            {
                pool.ReleaseConnection(service);
            }
        }
    }

    /// <summary>
    /// 經 P4 Embedded Adapter 執行唯一 allowlisted operation。Adapter 會套用 RequestGuard，後續 executor 固定
    /// 經 ProfileResolver、Admission、Router 與 Data8 Pool；此 helper 不得直接觸及 client、permit 或 endpoint。
    /// </summary>
    /// <param name="adapter">固定 profile 的 stateless Embedded adapter。</param>
    /// <param name="profileAlias">只從 Development 設定取得的產品 alias。</param>
    /// <returns>已通過 executor 組織比對的三個 WhoAmI GUID。</returns>
    private static async Task<DynamicsIdentity> ExecuteEmbeddedWhoAmIAsync(
        EmbeddedHostAdapter adapter,
        string profileAlias)
    {
        var result = await adapter.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = profileAlias,
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "churchreport-p4-live-comparison"
        }).ConfigureAwait(false);

        result.Succeeded.Should().BeTrue(result.ErrorCode);
        result.Data?.WhoAmI.Should().NotBeNull();
        var identity = result.Data!.WhoAmI!;
        return DynamicsIdentity.From(
            identity.UserId.GetValueOrDefault(),
            identity.BusinessUnitId.GetValueOrDefault(),
            identity.OrganizationId.GetValueOrDefault());
    }

    /// <summary>
    /// 以 Stopwatch 的單調計時器測量同步工作；委派只存活到此方法傳回，不加入 queue、timer 或背景工作。
    /// </summary>
    private static TimedIdentity Measure(Func<CrmConnectionPool, DynamicsIdentity> operation, CrmConnectionPool pool)
    {
        var stopwatch = Stopwatch.StartNew();
        var identity = operation(pool);
        stopwatch.Stop();
        return new TimedIdentity(identity, stopwatch.Elapsed);
    }

    /// <summary>
    /// 以 Stopwatch 的單調計時器測量單次非同步 Embedded work；awaiter 完成前沒有額外 Task cache 或 CTS owner，
    /// Data8 lease 的 cleanup 仍完全由 executor 的 await using 路徑負責。
    /// </summary>
    private static async Task<TimedIdentity> MeasureAsync(Func<Task<DynamicsIdentity>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        var identity = await operation().ConfigureAwait(false);
        stopwatch.Stop();
        return new TimedIdentity(identity, stopwatch.Elapsed);
    }

    /// <summary>
    /// 比較同一帳號預期取得的三個 identity GUID。Organization 額外與 catalog 的 immutable expected value 比對，
    /// 因此即使 legacy 與 Embedded 恰好同時連到錯誤組織，也會在任何效能結論前 fail closed。
    /// </summary>
    private static void AssertEquivalentIdentity(
        DynamicsIdentity legacy,
        DynamicsIdentity embedded,
        Guid expectedOrganizationId)
    {
        legacy.OrganizationId.Should().Be(expectedOrganizationId);
        embedded.OrganizationId.Should().Be(expectedOrganizationId);
        embedded.UserId.Should().Be(legacy.UserId);
        embedded.BusinessUnitId.Should().Be(legacy.BusinessUnitId);
    }

    /// <summary>
    /// 載入 ChurchReport 的 JSONC 組態與 Development overlay。ConfigurationBuilder 只在測試方法開始建立一次，
    /// 不啟用 reload watcher，因此不會保留檔案 handle、timer 或跨測試 mutable 設定狀態。
    /// </summary>
    private static IConfiguration CreateDevelopmentConfiguration()
    {
        var applicationRoot = Path.Combine(FindRepositoryRoot(), "SpeechMessageProducts.ChurchReport");
        return new ConfigurationBuilder()
            .SetBasePath(applicationRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();
    }

    /// <summary>
    /// 從測試輸出目錄向上定位現有 worktree，避免把開發者資料夾、Session 或機密路徑寫進程式。DirectoryInfo
    /// 只在方法範圍使用，不建立 FileSystemWatcher 或保留檔案 handle。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(System.AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "SpeechMessageProducts.ChurchReport")) &&
                Directory.Exists(Path.Combine(current.FullName, "SpeechMessage.Dynamics.Embedded")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("The current worktree root was not found.");
    }

    /// <summary>
    /// 封裝不含 CRM 物件的 WhoAmI 三個 GUID。它是值型別且只存在於單一 test invocation，不能承載 session、
    /// credential、endpoint、connector 或 request mutable state。
    /// </summary>
    private readonly record struct DynamicsIdentity(Guid UserId, Guid BusinessUnitId, Guid OrganizationId)
    {
        /// <summary>驗證三個 GUID 都不是空值，避免不完整服務回應被量測流程誤當成功。</summary>
        public static DynamicsIdentity From(Guid userId, Guid businessUnitId, Guid organizationId)
        {
            if (userId == Guid.Empty || businessUnitId == Guid.Empty || organizationId == Guid.Empty)
            {
                throw new InvalidOperationException("The Dynamics WhoAmI response is incomplete.");
            }

            return new DynamicsIdentity(userId, businessUnitId, organizationId);
        }
    }

    /// <summary>
    /// 將單次 identity 與其單調時鐘耗時綁定；不保存 operation、service、client 或任何可釋放資源。
    /// </summary>
    private readonly record struct TimedIdentity(DynamicsIdentity Identity, TimeSpan Elapsed);

    /// <summary>
    /// 量測輸出只包含固定樣本數與延遲統計。nearest-rank 實作只在建立時配置至多 21 個 ticks 的短生命週期陣列，
    /// 不使用 static aggregator、Timer 或 telemetry buffer，因此不會把先前測試的資料留給下一個執行。
    /// </summary>
    private readonly record struct LatencyMetrics(TimeSpan P50, TimeSpan P95, TimeSpan P99)
    {
        /// <summary>從非空且有界的樣本集合計算 p50、p95、p99。</summary>
        public static LatencyMetrics Create(IReadOnlyList<TimeSpan> samples)
            => new(GetPercentile(samples, 0.50), GetPercentile(samples, 0.95), GetPercentile(samples, 0.99));

        /// <summary>以不含文化相依或秘密值的格式輸出三個毫秒統計。</summary>
        public override string ToString()
            => string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "p50={0:F3}ms p95={1:F3}ms p99={2:F3}ms",
                P50.TotalMilliseconds,
                P95.TotalMilliseconds,
                P99.TotalMilliseconds);

        /// <summary>計算 nearest-rank percentile，輸入只可為本測試固定的小型樣本清單。</summary>
        private static TimeSpan GetPercentile(IReadOnlyList<TimeSpan> samples, double percentile)
        {
            samples.Should().HaveCount(MeasurementSampleCount);
            percentile.Should().BeInRange(0.0, 1.0);
            var orderedTicks = samples.Select(sample => sample.Ticks).Order().ToArray();
            var rank = Math.Max(1, (int)Math.Ceiling(percentile * orderedTicks.Length));
            return TimeSpan.FromTicks(orderedTicks[rank - 1]);
        }
    }
}

/// <summary>
/// 標示顯式 opt-in 的真實 CE P4 測試。探索階段只讀取兩個環境變數是否存在，不讀取、不保存且不輸出其值；
/// 未啟用時以可見 skip 表示「沒有真機證據」，而不是以 silent return 產生假綠燈。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class LiveDynamicsP4FactAttribute : FactAttribute
{
    /// <summary>明確允許此測試連到 CE 的非機密旗標名稱；只有值完全為 <c>1</c> 才執行。</summary>
    internal const string EnableEnvironmentVariable = "SPEECHMESSAGE_DYNAMICS_P4_LIVE";

    /// <summary>測試程序唯一讀取的 credential 環境變數名稱；值不會被輸出或放入 skip reason。</summary>
    internal const string PasswordEnvironmentVariable = "CRM_PASSWORD";

    /// <summary>
    /// 建立 live CE 測試標記。少任一前置條件即略過，避免一般 CI／離線開發誤連線；測試實際執行後仍會以
    /// identity 與 p95 assertions 失敗關閉，絕不把略過視為已驗證。
    /// </summary>
    public LiveDynamicsP4FactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(EnableEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            Skip = $"需要將 {EnableEnvironmentVariable}=1 與 {PasswordEnvironmentVariable} 設在目前測試程序；未執行真實 CE P4 對照量測。";
        }
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PasswordEnvironmentVariable)))
        {
            Skip = $"需要在目前測試程序設定 {PasswordEnvironmentVariable}；未執行真實 CE P4 對照量測。";
        }
    }
}
