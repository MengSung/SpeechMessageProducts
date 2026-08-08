// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/LivePackage02Data8ContactProfileEvidenceTests.cs
// 用途：P7.2 Slice B1/B2 operator-only live evidence。
//       B1 執行三欄位 LINE profile sentinel update 與 restore；B2 執行
//       Data8 aggregate read 與同一輸入類別的 legacy parity read。兩者都只輸出
//       去識別化 JSON，且不啟用 ChurchReport feature flag。
// ============================================================================

using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Connectors.Data8;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using Xunit;
using Xunit.Abstractions;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>P7.2 Slice B1/B2 的真機證據測試；只有明確 opt-in 才會執行。</summary>
[SupportedOSPlatform("windows")]
public sealed class LivePackage02Data8ContactProfileEvidenceTests
{
    private const string ProfileAlias = "sunnyvalechback";

    private readonly ITestOutputHelper _output;

    /// <summary>建立 xUnit evidence sink。</summary>
    public LivePackage02Data8ContactProfileEvidenceTests(ITestOutputHelper output)
        => _output = output ?? throw new ArgumentNullException(nameof(output));

    /// <summary>
    /// 執行 B1 的 baseline、sentinel、read-back、restore 與 restore read-back。
    /// 任一 cleanup 或 reconciliation 不明確時測試保持 no-go。
    /// </summary>
    [P72Data8B1LiveFact]
    public async Task Live_package02_data8_contact_line_profile_emits_sanitized_evidence()
    {
        var outcome = "no-go";
        var reason = "runtime-failure";
        var operationExecuted = false;
        var sentinelState = "unknown";
        var cleanupState = "manual-reconciliation-required";
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;
        P72Data8ContactLineProfileFixtureStore? store = null;

        try
        {
            var fixture = ReadB1Fixture();
            var configuration = CreateDevelopmentConfiguration();
            var (profiles, catalog, organization, settings) = ResolveProfile(configuration);
            loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            runtime = new EmbeddedData8Runtime(
                profiles,
                catalog,
                ProfileAlias,
                new OnPremiseData8ConnectorClientFactory(settings),
                loggerFactory.CreateLogger<EmbeddedData8Runtime>(),
                loggerFactory);
            var client = new Package02ContactProfileClient(
                runtime.Executor,
                loggerFactory.CreateLogger<Package02ContactProfileClient>());
            var credentialPassword = Environment.GetEnvironmentVariable("CRM_PASSWORD");
            credentialPassword.Should().NotBeNullOrWhiteSpace();
            store = new P72Data8ContactLineProfileFixtureStore(new OnPremiseClient(
                organization.ServiceUri,
                settings.UserName,
                credentialPassword!));

            var nonce = Guid.NewGuid().ToString("N");
            var result = await P72ContactProfileFixtureBridge.ExecuteAsync(
                client,
                store,
                fixture.ContactId,
                "p72-line-profile-" + nonce,
                new P72ContactLineProfileSnapshot(
                    "https://example.invalid/p72-" + nonce + ".png",
                    "p72-line-status-" + nonce[..12],
                    "p72-line-name-" + nonce[..12])).ConfigureAwait(false);
            outcome = result.Outcome;
            reason = result.Reason;
            operationExecuted = result.OperationExecuted;
            sentinelState = result.SentinelState;
            cleanupState = result.CleanupState;
        }
        catch (Exception)
        {
            outcome = "no-go";
            reason = "runtime-failure";
        }
        finally
        {
            DisposeStore(ref store, ref outcome, ref reason, ref cleanupState);
            var runtimeCleanup = await DisposeRuntimeAsync(runtime).ConfigureAwait(false);
            if (!runtimeCleanup.Succeeded)
            {
                outcome = "no-go";
                reason = runtimeCleanup.Reason;
                cleanupState = runtimeCleanup.CleanupState;
            }
            DisposeLogger(ref loggerFactory, ref outcome, ref reason, ref cleanupState);
        }

        var evidence = new
        {
            schemaVersion = 1,
            outcome,
            reason,
            operationId = "memberinfo.contact.update.line.profile",
            profileAlias = ProfileAlias,
            deploymentProfileAlias = "crm91",
            ceVersion = "9.1",
            connector = "Data8",
            preflightOnly = false,
            operationExecuted,
            sentinelState,
            cleanupState,
            featureFlagChanged = false
        };
        _output.WriteLine("P7_2_B1_EVIDENCE_JSON=" + JsonSerializer.Serialize(evidence));
        outcome.Should().Be("go", because: "B1 write, reconciliation, restore and disposal must complete");
    }

    /// <summary>
    /// 執行 B2 唯讀 aggregate 與 legacy parity；不接受或傳遞任意 FetchXML、entity
    /// name、field name、grouped IDs 或 credential。
    /// </summary>
    [P72Data8B2LiveFact]
    public async Task Live_package02_data8_ungrouped_commitment_emits_sanitized_evidence()
    {
        var outcome = "no-go";
        var reason = "runtime-failure";
        var operationExecuted = false;
        var parityState = "unknown";
        var rowCount = 0;
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;
        P72Data8UngroupedCommitmentParityStore? store = null;

        try
        {
            ReadB2Fixture();
            var configuration = CreateDevelopmentConfiguration();
            var (profiles, catalog, organization, settings) = ResolveProfile(configuration);
            loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            runtime = new EmbeddedData8Runtime(
                profiles,
                catalog,
                ProfileAlias,
                new OnPremiseData8ConnectorClientFactory(settings),
                loggerFactory.CreateLogger<EmbeddedData8Runtime>(),
                loggerFactory);
            var client = new Package02ContactProfileClient(
                runtime.Executor,
                loggerFactory.CreateLogger<Package02ContactProfileClient>());
            var credentialPassword = Environment.GetEnvironmentVariable("CRM_PASSWORD");
            credentialPassword.Should().NotBeNullOrWhiteSpace();
            store = new P72Data8UngroupedCommitmentParityStore(new OnPremiseClient(
                organization.ServiceUri,
                settings.UserName,
                credentialPassword!));

            var result = await P72UngroupedCommitmentFixtureBridge.ExecuteAsync(
                client,
                store,
                ProfileAlias,
                "p7.2-ungrouped-commitment").ConfigureAwait(false);
            outcome = result.Outcome;
            reason = result.Reason;
            operationExecuted = result.OperationExecuted;
            parityState = result.ParityState;
            rowCount = result.RowCount;
            if (string.Equals(reason, "data8-read-failed", StringComparison.Ordinal))
            {
                try
                {
                    _ = store.ReadLegacyCounts(search: null);
                    reason = "data8-read-failed-legacy-probe-succeeded";
                }
                catch (Exception)
                {
                    reason = "data8-read-failed-legacy-probe-failed";
                }
            }
        }
        catch (Exception)
        {
            outcome = "no-go";
            reason = "runtime-failure";
        }
        finally
        {
            DisposeStore(ref store, ref outcome, ref reason, ref parityState);
            var runtimeCleanup = await DisposeRuntimeAsync(runtime).ConfigureAwait(false);
            if (!runtimeCleanup.Succeeded)
            {
                outcome = "no-go";
                reason = runtimeCleanup.Reason;
                parityState = runtimeCleanup.CleanupState;
            }
            DisposeLogger(ref loggerFactory, ref outcome, ref reason, ref parityState);
        }

        var evidence = new
        {
            schemaVersion = 1,
            outcome,
            reason,
            operationId = "memberinfo.contact.count.ungrouped.commitment",
            profileAlias = ProfileAlias,
            deploymentProfileAlias = "crm91",
            ceVersion = "9.1",
            connector = "Data8",
            preflightOnly = false,
            operationExecuted,
            parityState,
            rowCount,
            featureFlagChanged = false
        };
        var evidenceJson = JsonSerializer.Serialize(evidence);
        WriteB2EvidenceFile(evidenceJson);
        outcome.Should().Be(
            "go",
            because: $"B2 Data8 aggregate, legacy parity and disposal must complete; sanitized reason={reason}; parity={parityState}");
    }

    /// <summary>解析固定 crm91/Data8/CE 9.1 deployment profile。</summary>
    private static (
        IReadOnlyDictionary<string, DynamicsProfileOptions> Profiles,
        IReadOnlyDictionary<string, OrganizationCatalogEntry> Catalog,
        OrganizationCatalogEntry Organization,
        Data8OnPremiseConnectionSettings Settings)
        ResolveProfile(IConfiguration configuration)
    {
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
            out var profileError).Should().BeTrue(profileError);
        profiles.TryGetValue(ProfileAlias, out var profile).Should().BeTrue();
        catalog.TryGetValue(ProfileAlias, out var organization).Should().BeTrue();
        profile.Should().NotBeNull();
        organization.Should().NotBeNull();
        profile!.CeVersion.Should().Be(CeVersion.Ce91);
        CrmConnectionEmbeddedProfileMapper.TryCreateConnectionSettings(
            configuration,
            organization!.ServiceUri,
            out var settings,
            out var settingsError).Should().BeTrue(settingsError);
        settings.Should().NotBeNull();
        return (profiles, catalog, organization, settings!);
    }

    /// <summary>讀取 B1 fixture scalar，確認 owner 與 marker 屬於目前 Windows identity。</summary>
    private static B1Fixture ReadB1Fixture()
    {
        var marker = Environment.GetEnvironmentVariable("P7_2_B1_FIXTURE_MARKER");
        var owner = Environment.GetEnvironmentVariable("P7_2_B1_FIXTURE_OWNER");
        var currentIdentity = WindowsIdentity.GetCurrent().Name;
        if (!string.Equals(marker, "p7.2-contact-line-profile", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(owner) ||
            owner.Length > 256 ||
            owner.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            !string.Equals(owner, currentIdentity, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(Environment.GetEnvironmentVariable("P7_2_B1_CONTACT_ID"), out var contactId) ||
            contactId == Guid.Empty)
        {
            throw new InvalidOperationException("The P7.2 B1 fixture descriptor is invalid.");
        }

        return new B1Fixture(contactId);
    }

    /// <summary>讀取 B2 fixture owner/marker；B2 不需要任何 contact GUID。</summary>
    private static void ReadB2Fixture()
    {
        var marker = Environment.GetEnvironmentVariable("P7_2_B2_FIXTURE_MARKER");
        var owner = Environment.GetEnvironmentVariable("P7_2_B2_FIXTURE_OWNER");
        var currentIdentity = WindowsIdentity.GetCurrent().Name;
        if (!string.Equals(marker, "p7.2-ungrouped-commitment", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(owner) ||
            owner.Length > 256 ||
            owner.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            !string.Equals(owner, currentIdentity, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The P7.2 B2 fixture descriptor is invalid.");
        }
    }

    /// <summary>建立與 Slice A 相同的固定 Embedded 開發設定。</summary>
    private static IConfiguration CreateDevelopmentConfiguration()
    {
        var root = FindRepositoryRoot();
        return new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root, "SpeechMessageProducts.ChurchReport"))
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();
    }

    /// <summary>向上搜尋同時含 ChurchReport 與 Embedded 專案的 worktree root。</summary>
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

    /// <summary>在所有路徑嘗試 cleanup，且不讓 dispose 例外洩漏到 evidence。</summary>
    private static void DisposeStore<T>(ref T? store, ref string outcome, ref string reason, ref string cleanupState)
        where T : class, IDisposable
    {
        if (store is null)
        {
            return;
        }

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
        finally
        {
            store = null;
        }
    }

    /// <summary>釋放 Embedded runtime 與其 generation-owned connector lease。</summary>
    private static async Task<CleanupResult> DisposeRuntimeAsync(EmbeddedData8Runtime? runtime)
    {
        if (runtime is null)
        {
            return new CleanupResult(true, "none", "unchanged");
        }

        try
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            return new CleanupResult(true, "none", "disposed");
        }
        catch (Exception)
        {
            return new CleanupResult(false, "cleanup-failure", "manual-reconciliation-required");
        }
    }

    /// <summary>
    /// 將 B2 的去識別化 evidence 寫入 handoff 擁有的唯一暫存檔。路徑必須位於 OS temp、
    /// 父目錄不得是 reparse point，且檔案不得預先存在；handoff 在 finally 移除整個目錄，
    /// 因此不會跨測試、帳號或 profile 保留可變狀態。
    /// </summary>
    private static void WriteB2EvidenceFile(string evidenceJson)
    {
        var configuredPath = Environment.GetEnvironmentVariable("P7_2_B2_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(configuredPath) ||
            configuredPath.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new InvalidOperationException("The B2 evidence path is unavailable.");
        }

        var fullPath = Path.GetFullPath(configuredPath);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var parent = Directory.GetParent(fullPath);
        if (!fullPath.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(fullPath), "P72Data8B2Evidence.json", StringComparison.Ordinal) ||
            parent is null ||
            !parent.Name.StartsWith("speechmessage-p7-2-profile-", StringComparison.Ordinal) ||
            !parent.Exists ||
            (parent.Attributes & FileAttributes.ReparsePoint) != 0 ||
            File.Exists(fullPath))
        {
            throw new InvalidOperationException("The B2 evidence path is invalid.");
        }

        File.WriteAllText(fullPath, evidenceJson + "\r\n", new UTF8Encoding(false, true));
    }

    /// <summary>釋放 logger factory，避免背景 provider 或 subscription 被保留。</summary>
    private static void DisposeLogger(
        ref ILoggerFactory? loggerFactory,
        ref string outcome,
        ref string reason,
        ref string cleanupState)
    {
        if (loggerFactory is null)
        {
            return;
        }

        try
        {
            loggerFactory.Dispose();
        }
        catch (Exception)
        {
            outcome = "no-go";
            reason = "cleanup-failure";
            cleanupState = "manual-reconciliation-required";
        }
        finally
        {
            loggerFactory = null;
        }
    }

    private sealed record B1Fixture(Guid ContactId);

    private sealed record CleanupResult(bool Succeeded, string Reason, string CleanupState);

}

/// <summary>B1 live test 的明確 opt-in gate。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P72Data8B1LiveFactAttribute : FactAttribute
{
    /// <summary>只有 operator handoff 完整設定才執行真機 write。</summary>
    public P72Data8B1LiveFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_B1_LIVE"), "1", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CRM_PASSWORD")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("P7_2_B1_CONTACT_ID")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("P7_2_B1_FIXTURE_OWNER")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("P7_2_B1_FIXTURE_MARKER")))
        {
            Skip = "P7.2 B1 live evidence requires an approved task-owned fixture.";
        }
    }
}

/// <summary>B2 live test 的明確 opt-in gate。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P72Data8B2LiveFactAttribute : FactAttribute
{
    /// <summary>只有 operator handoff 完整設定才執行真機 read/parity。</summary>
    public P72Data8B2LiveFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_B2_LIVE"), "1", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CRM_PASSWORD")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("P7_2_B2_FIXTURE_OWNER")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("P7_2_B2_FIXTURE_MARKER")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("P7_2_B2_EVIDENCE_PATH")))
        {
            Skip = "P7.2 B2 live evidence requires an approved task-owned fixture.";
        }
    }
}
