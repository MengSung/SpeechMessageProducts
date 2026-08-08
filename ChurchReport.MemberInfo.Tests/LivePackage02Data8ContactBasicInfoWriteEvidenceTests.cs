// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/LivePackage02Data8ContactBasicInfoWriteEvidenceTests.cs
// 用途：以明確 opt-in 的單次測試，取得 P7.2 contact basic-info Data8 寫入、
//       read-back reconciliation 與 baseline restore 的去識別化 CE 9.1 證據。
//
// 信任、隔離與生命週期：
// 1. 一般 test discovery 預設略過；只有 task-owned descriptor 已由 PowerShell preflight
//    驗證，且 child process 同時收到 opt-in、contact、owner、marker 與短生命期密碼時才執行。
// 2. 測試只呼叫固定 typed ProductClient 與固定欄位 fixture store；不接受 endpoint、
//    OrganizationId、ConnectorKind、CE version、Entity、QueryBase、FetchXML 或 raw SDK request。
// 3. 寫入最多 dispatch 一次。transport ambiguity 只做 read-back，不重送；只有完整 sentinel
//    才能還原 baseline，未知狀態必須要求人工 reconciliation。
// 4. fixture store、Embedded runtime 與 logger factory 都在 evidence marker 前反向釋放；
//    測試不建立 timer、背景工作、static cache、Session 或跨測試 credential/profile state。
// 5. marker 只含固定分類與布林值，不輸出 GUID、owner、欄位值、帳號、密碼、token、cookie、
//    endpoint、路徑、CRM payload 或原始例外。
// ============================================================================

using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text.Json;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Connectors.Data8;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using Xunit;
using Xunit.Abstractions;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// P7.2 Slice A 的 operator-only live evidence lane。所有可連線資源均由單一測試方法
/// 建立與釋放；即使 write、read-back、restore 或 dispose 失敗，也只輸出固定 no-go
/// 分類，不讓 credential、fixture identity 或 CRM 資料進入 TRX evidence。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LivePackage02Data8ContactBasicInfoWriteEvidenceTests
{
    private const string ProfileAlias = "sunnyvalechback";
    private const string OperationId = "memberinfo.contact.update.basic.info";
    private const string FixtureMarker = "p7.2-contact-basic-info";
    private readonly ITestOutputHelper _output;

    /// <summary>建立 xUnit 擁有的 evidence sink；sink 只接收方法末端的 sanitized marker。</summary>
    public LivePackage02Data8ContactBasicInfoWriteEvidenceTests(ITestOutputHelper output)
        => _output = output ?? throw new ArgumentNullException(nameof(output));

    /// <summary>
    /// 執行一次 baseline read → typed sentinel update → read-back → baseline restore → restore
    /// read-back。方法沒有 retry loop；bridge 回傳後先釋放 store/runtime/logger，最後才輸出
    /// marker。外層例外若可能發生在 dispatch 後，一律採保守的 manual reconciliation 分類。
    /// </summary>
    [P72Data8LiveFact]
    public async Task Live_package02_data8_contact_basic_info_write_emits_sanitized_evidence()
    {
        var outcome = "no-go";
        var reason = "runtime-failure";
        var operationExecuted = false;
        var sentinelState = "unknown";
        var cleanupState = "manual-reconciliation-required";
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;
        IP72ContactBasicInfoFixtureStore? store = null;

        try
        {
            var fixture = ReadFixture();
            var configuration = CreateDevelopmentConfiguration();
            var options = new ProductDynamicsOptions
            {
                ConnectionMode = ConnectionMode.Embedded,
                ProfileAlias = ProfileAlias
            };

            CrmConnectionEmbeddedProfileMapper.TryCreate(
                configuration,
                options,
                out var profiles,
                out var catalog,
                out var profileError)
                .Should().BeTrue(profileError);
            profiles.TryGetValue(ProfileAlias, out var profile).Should().BeTrue();
            catalog.TryGetValue(ProfileAlias, out var organization).Should().BeTrue();
            profile.Should().NotBeNull();
            organization.Should().NotBeNull();
            profile!.CeVersion.Should().Be(CeVersion.Ce91);

            CrmConnectionEmbeddedProfileMapper.TryCreateConnectionSettings(
                configuration,
                organization!.ServiceUri,
                out var settings,
                out var settingsError)
                .Should().BeTrue(settingsError);
            settings.Should().NotBeNull();

            var credentialPassword = Environment.GetEnvironmentVariable("CRM_PASSWORD");
            credentialPassword.Should().NotBeNullOrWhiteSpace();
            loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            runtime = new EmbeddedData8Runtime(
                profiles,
                catalog,
                ProfileAlias,
                new OnPremiseData8ConnectorClientFactory(settings!),
                loggerFactory.CreateLogger<EmbeddedData8Runtime>(),
                loggerFactory);
            var client = new Package02ContactBasicInfoUpdateClient(
                runtime.Executor,
                loggerFactory.CreateLogger<Package02ContactBasicInfoUpdateClient>());

            // Fixture read/restore 必須有獨立 service ownership，不能借用 Pool 內 client 或讓同一 WCF
            // session 同時受 lease 與 fixture store 擁有。child process 結束前會先 Dispose 此 store。
            store = new P72Data8ContactBasicInfoFixtureStore(new OnPremiseClient(
                settings!.ServiceUri,
                settings.UserName,
                credentialPassword!));

            var nonce = Guid.NewGuid().ToString("N");
            var idempotencyKey = "p72-contact-basic-info-" + nonce;
            var sentinelPhone = "p72-phone-" + nonce[..12];
            var sentinelAddress = "p72-address-" + nonce[..16];

            // 一旦進入 bridge，若外層遇到非預期例外就不能證明未 dispatch；先採保守 true，
            // 正常回傳時再以 bridge 的決定性結果覆寫。
            operationExecuted = true;
            var bridgeResult = await P72ContactBasicInfoFixtureBridge.ExecuteAsync(
                client,
                store,
                fixture.ContactId,
                idempotencyKey,
                sentinelPhone,
                sentinelAddress).ConfigureAwait(false);

            outcome = bridgeResult.Outcome;
            reason = bridgeResult.Reason;
            operationExecuted = bridgeResult.OperationExecuted;
            sentinelState = bridgeResult.SentinelState;
            cleanupState = bridgeResult.CleanupState;
        }
        catch (Exception)
        {
            // Provider／WCF 例外可能含 endpoint、使用者名稱或 CRM 細節；evidence 只能保留固定分類。
            outcome = "no-go";
            reason = "runtime-failure";
        }
        finally
        {
            if (store is not null)
            {
                try
                {
                    store.Dispose();
                }
                catch (Exception)
                {
                    outcome = "no-go";
                    reason = "cleanup-failure";
                    cleanupState = "manual-reconciliation-required";
                }
            }

            if (runtime is not null)
            {
                try
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    outcome = "no-go";
                    reason = "cleanup-failure";
                    cleanupState = "manual-reconciliation-required";
                }
            }

            try
            {
                loggerFactory?.Dispose();
            }
            catch (Exception)
            {
                outcome = "no-go";
                reason = "cleanup-failure";
                cleanupState = "manual-reconciliation-required";
            }
        }

        _output.WriteLine(
            "P7_2_EVIDENCE_JSON=" +
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                outcome,
                reason,
                operationId = OperationId,
                profileAlias = ProfileAlias,
                deploymentProfileAlias = "crm91",
                ceVersion = "9.1",
                connector = "Data8",
                preflightOnly = false,
                operationExecuted,
                sentinelState,
                cleanupState,
                featureFlagChanged = false
            }));

        outcome.Should().Be("go", because: "the bounded write, reconciliation, baseline restore, and disposal must all complete");
    }

    /// <summary>
    /// 讀取 child-process-only fixture scalar，並再次比對 marker 與目前 Windows identity。
    /// PowerShell preflight 雖已驗證 descriptor，測試程序仍須自行 fail closed，避免環境在 process
    /// 啟動前遭替換。owner 與 contact 只保存在方法區域，不會寫入 evidence。
    /// </summary>
    private static LiveFixture ReadFixture()
    {
        var marker = Environment.GetEnvironmentVariable("P7_2_FIXTURE_MARKER");
        var owner = Environment.GetEnvironmentVariable("P7_2_FIXTURE_OWNER");
        var currentIdentity = WindowsIdentity.GetCurrent().Name;
        if (!string.Equals(marker, FixtureMarker, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(owner) ||
            owner.Length > 256 ||
            owner.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            !string.Equals(owner, currentIdentity, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(Environment.GetEnvironmentVariable("P7_2_CONTACT_ID"), out var contactId) ||
            contactId == Guid.Empty)
        {
            throw new InvalidOperationException("The P7.2 fixture descriptor is invalid.");
        }

        return new LiveFixture(contactId);
    }

    /// <summary>
    /// 建立不啟用 reload watcher 的固定 ChurchReport 開發設定；設定內容只由 repository 與
    /// process-local CRM_PASSWORD 組成，測試不把 endpoint 或 credential 放入 request。
    /// </summary>
    private static IConfiguration CreateDevelopmentConfiguration()
    {
        var root = FindRepositoryRoot();
        return new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root, "SpeechMessageProducts.ChurchReport"))
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();
    }

    /// <summary>從測試輸出目錄向上找 worktree；絕不輸出或保存解析後的絕對路徑。</summary>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
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

    /// <summary>方法區域內唯一需要的 task-owned contact identity。</summary>
    private sealed record LiveFixture(Guid ContactId);
}

/// <summary>
/// P7.2 真機寫入的 opt-in gate。探索階段只檢查必要環境變數是否存在，不解析、輸出或
/// 保存任何秘密與 fixture 值；缺少任一條件時，一般 test run 必須明確略過。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P72Data8LiveFactAttribute : FactAttribute
{
    private const string EnableVariable = "SPEECHMESSAGE_P7_2_LIVE";
    private static readonly string[] RequiredVariables =
    [
        EnableVariable,
        "CRM_PASSWORD",
        "P7_2_CONTACT_ID",
        "P7_2_FIXTURE_OWNER",
        "P7_2_FIXTURE_MARKER"
    ];

    /// <summary>只有完整且明確的 child process opt-in 才允許 xUnit 執行一次 live test。</summary>
    public P72Data8LiveFactAttribute()
    {
        if (!RequiredVariables.All(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))) ||
            !string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            Skip = "P7.2 contact basic-info live evidence requires an approved task-owned fixture.";
        }
    }
}
