// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementFreshFixtureTests.cs
// 用途：P7.2 Slice C fresh-fixture 的 opt-in CE 9.1 / Data8 provision child。
//
// 這是測試控制面，不是 ChurchReport 的產品 API。它只在 parent PowerShell 建立完整、
// current-user 綁定的短生命週期 process gate 時連到 sunnyvalechback；一般 test discovery、
// CI、瀏覽器 session 與產品 feature flag 都不會觸發此檔中的 CE mutation。
// ============================================================================

using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Connectors.Data8;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 執行 P7.2 Slice C fresh-fixture provision 的唯一 live child。它先透過 immutable
/// <c>sunnyvalechback</c> / CE 9.1 / Data8 runtime 驗證 WhoAmI，再用同一份 deployment
/// settings 建立一個僅屬於本 child 的 <see cref="OnPremiseClient"/>，交給固定 allowlist 的
/// <see cref="P72FreshSliceCFixtureProvisioner"/>。所有 CRM IDs、nonce、password 與 Entity
/// 都是方法區域變數；ledger 僅留在 current-user local path，evidence 僅輸出 bounded category。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LivePackage02Data8ListManagementFreshFixtureTests
{
    private const string ProfileAlias = "sunnyvalechback";

    /// <summary>
    /// 在 parent 已提供完整 explicit gate 時，建立一個新的、可證明來源且可逆向清理的 Slice C
    /// fixture graph。此測試不修改舊 source contact 或舊 expected relationship list；它只允許
    /// provisioner 的三個 Create、兩個 membership Add 與一個 owner Assign，並在每一個 mutation
    /// 後讀回。最終 evidence 不含任何 CRM ID，parent 只能在 child 成功退出後讀 ledger 決定是否
    /// 原子發布本機 descriptor。
    /// </summary>
    [P72Data8SliceCFreshProvisionFact]
    public async Task Provision_fresh_package02_data8_list_management_fixture_emits_sanitized_evidence()
    {
        var outcome = "no-go";
        var reason = "runtime-failure";
        var operationExecuted = false;
        var descriptorPublicationReady = false;
        FreshProvisionEnvironment? environment = null;
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;
        OnPremiseClient? service = null;

        try
        {
            environment = ReadProvisionEnvironment();
            var configuration = LivePackage02Data8ListManagementEvidenceTests.CreateDevelopmentConfiguration();
            var (profiles, catalog, organization, settings) = LivePackage02Data8ListManagementEvidenceTests.ResolveProfile(configuration);
            var credentialPassword = Environment.GetEnvironmentVariable("CRM_PASSWORD");
            credentialPassword.Should().NotBeNullOrWhiteSpace();

            loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            runtime = new EmbeddedData8Runtime(
                profiles,
                catalog,
                ProfileAlias,
                new OnPremiseData8ConnectorClientFactory(settings),
                loggerFactory.CreateLogger<EmbeddedData8Runtime>(),
                loggerFactory);

            // WhoAmI 必須由 profile-owned runtime 先驗證；environment 不能指定 service user，
            // 因此其他帳號或其他 profile 的值無法被冒充為本次 Assign 的比較基準。
            var serviceUserId = await LivePackage02Data8ListManagementEvidenceTests
                .ResolveFixtureTargetOwnerIdAsync(runtime.Executor, organization.OrganizationId)
                .ConfigureAwait(false);
            if (serviceUserId is not Guid verifiedServiceUserId)
            {
                reason = "fixture-precondition-failed";
            }
            else
            {
                service = new OnPremiseClient(organization.ServiceUri, settings.UserName, credentialPassword!);
                using var ledger = new P72FreshSliceCFixtureFileLedger(
                    environment.LedgerPath,
                    environment.LedgerRoot,
                    environment.OwnerIdentity,
                    "crm91",
                    ProfileAlias,
                    "9.1",
                    "Data8");
                var request = new P72FreshSliceCFixtureProvisionRequest(
                    environment.AddListId,
                    environment.RemoveListId,
                    environment.SmallGroupListId,
                    environment.ExistingTargetLeaderContactId,
                    environment.TransferSourceListId,
                    environment.TransferTargetListId,
                    environment.TransferWeekStartUtc,
                    verifiedServiceUserId,
                    environment.Nonce);
                var result = new P72FreshSliceCFixtureProvisioner(service).Provision(request, ledger);
                outcome = result.Outcome;
                reason = result.Reason;
                operationExecuted = result.OperationExecuted;
                descriptorPublicationReady = result.Outcome == "go" &&
                    result.Reason == "fresh-fixture-provisioned";
            }
        }
        catch (Exception)
        {
            // 不輸出 Data8/AD FS/WCF/CRM 例外內容，避免 endpoint、identity 或 credential 細節
            // 透過 test result、TRX 或 evidence 跨越 child process 邊界。
            outcome = "no-go";
            reason = "runtime-failure";
        }
        finally
        {
            DisposeService(ref service, ref outcome, ref reason);
            if (!await DisposeRuntimeAsync(runtime).ConfigureAwait(false))
            {
                outcome = "no-go";
                reason = "cleanup-failure";
                descriptorPublicationReady = false;
            }

            DisposeLogger(ref loggerFactory, ref outcome, ref reason);
            if (reason == "cleanup-failure")
            {
                descriptorPublicationReady = false;
            }
        }

        if (environment is not null)
        {
            P72FreshSliceCFixtureLiveEvidence.Write(
                environment.EvidencePath,
                environment.EvidenceRoot,
                new P72FreshSliceCFixtureLiveEvidenceValue(
                    "provision",
                    outcome,
                    reason,
                    operationExecuted,
                    descriptorPublicationReady));
        }

        outcome.Should().Be(
            "go",
            because: "only a fully proven fresh graph and deterministic Data8 resource cleanup may authorize parent descriptor publication");
    }

    /// <summary>
    /// 讀取並驗證 parent 提供的固定 fresh provision inputs。所有 GUID、日期、nonce 與 owner 都
    /// 僅用於本次 child invocation；方法不接受 endpoint、connector、profile、credential 或任意
    /// CRM entity/field，因此無法把 process environment 變成一般 CRUD 入口。
    /// </summary>
    private static FreshProvisionEnvironment ReadProvisionEnvironment()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION"),
                "replace-stale-descriptor",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The fresh-fixture confirmation is invalid.");
        }

        var owner = ReadRequiredText("P7_2_SLICE_C_FRESH_OWNER", maximumLength: 256);
        if (!string.Equals(owner, WindowsIdentity.GetCurrent().Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The fresh-fixture owner is invalid.");
        }

        return new FreshProvisionEnvironment(
            ReadRequiredText("P7_2_SLICE_C_FRESH_LEDGER_ROOT", maximumLength: 1024),
            ReadRequiredText("P7_2_SLICE_C_FRESH_LEDGER_PATH", maximumLength: 1024),
            ReadRequiredText("P7_2_SLICE_C_FRESH_EVIDENCE_PATH", maximumLength: 1024),
            owner,
            ReadGuid("P7_2_SLICE_C_FRESH_NONCE"),
            ReadGuid("P7_2_SLICE_C_FRESH_ADD_LIST_ID"),
            ReadGuid("P7_2_SLICE_C_FRESH_REMOVE_LIST_ID"),
            ReadGuid("P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID"),
            ReadGuid("P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID"),
            ReadGuid("P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID"),
            ReadGuid("P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID"),
            ReadSundayUtc("P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC"));
    }

    /// <summary>
    /// 只接受標準 D format 的非空 GUID，拒絕 loose parse、空值與 caller-friendly 的替代格式。
    /// 這讓 ledger/provisioner 的 exact-ID invariant 在 child 的第一個 remote call 前就成立。
    /// </summary>
    internal static Guid ReadGuid(string variableName)
        => Guid.TryParseExact(Environment.GetEnvironmentVariable(variableName), "D", out var value) && value != Guid.Empty
            ? value
            : throw new InvalidOperationException("The fresh-fixture input is invalid.");

    /// <summary>
    /// 讀取固定 UTC Sunday 00:00 週次，以免 local timezone 或 DST 讓 weekly-report proof 讀到
    /// 不同資料。日期不是 caller 選擇的 CRM query；它只能是 parent descriptor 已驗證的 scalar。
    /// </summary>
    internal static DateTimeOffset ReadSundayUtc(string variableName)
    {
        if (!DateTimeOffset.TryParseExact(
                Environment.GetEnvironmentVariable(variableName),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value) ||
            value.Offset != TimeSpan.Zero ||
            value.TimeOfDay != TimeSpan.Zero ||
            value.DayOfWeek != DayOfWeek.Sunday)
        {
            throw new InvalidOperationException("The fresh-fixture input is invalid.");
        }

        return value;
    }

    /// <summary>
    /// 讀取有上限且不含控制字元的 parent-owned process text。這只適用於 local path 與 Windows
    /// owner；不會用於密碼、endpoint、Organization ID 或任意 CRM payload，避免把未信任值帶入
    /// Data8 session 或 evidence。
    /// </summary>
    internal static string ReadRequiredText(string variableName, int maximumLength)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new InvalidOperationException("The fresh-fixture input is invalid.");
        }

        return value;
    }

    /// <summary>
    /// 釋放 provisioner 獨占的 Data8 service。它在 runtime 與 logger 前釋放，避免 WCF channel、
    /// credential 相關 handle 或 service state 跨 child invocation 留存；任何 Dispose 失敗都覆寫
    /// 一般結果為 <c>cleanup-failure</c>，禁止 parent 發布 descriptor。
    /// </summary>
    internal static void DisposeService(ref OnPremiseClient? service, ref string outcome, ref string reason)
    {
        if (service is null)
        {
            return;
        }

        try
        {
            service.Dispose();
        }
        catch (Exception)
        {
            outcome = "no-go";
            reason = "cleanup-failure";
        }
        finally
        {
            service = null;
        }
    }

    /// <summary>
    /// 非同步 drain profile-owned Embedded runtime。此 helper 從不將 runtime 放入 static cache 或
    /// background task；本 child 完成時所有 pool/lease/client 資源都要回到 runtime owner 的 disposal
    /// 路徑，否則回傳 <see langword="false"/> 讓呼叫端 fail closed。
    /// </summary>
    internal static async Task<bool> DisposeRuntimeAsync(EmbeddedData8Runtime? runtime)
    {
        if (runtime is null)
        {
            return true;
        }

        try
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 釋放 logger factory 及其 provider；logger 不得持有 CRM session、fixture entity 或其他 child
    /// 狀態。釋放失敗與任何資源清理失敗同樣是 release-blocking no-go。
    /// </summary>
    internal static void DisposeLogger(ref ILoggerFactory? loggerFactory, ref string outcome, ref string reason)
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
        }
        finally
        {
            loggerFactory = null;
        }
    }

    /// <summary>
    /// 封裝此 provision child 的 bounded local inputs。它只在方法 scope 傳遞，沒有 static cache、
    /// session 或跨使用者集合；constructor 的 scalar 都由 <see cref="ReadProvisionEnvironment"/>
    /// 先驗證，且不會被序列化到 evidence。
    /// </summary>
    private sealed record FreshProvisionEnvironment(
        string LedgerRoot,
        string LedgerPath,
        string EvidencePath,
        string OwnerIdentity,
        Guid Nonce,
        Guid AddListId,
        Guid RemoveListId,
        Guid SmallGroupListId,
        Guid ExistingTargetLeaderContactId,
        Guid TransferSourceListId,
        Guid TransferTargetListId,
        DateTimeOffset TransferWeekStartUtc)
    {
        /// <summary>
        /// parent 對本 child nonce 建立的 evidence root；它只由 evidence writer 使用，與可長期留存
        /// 的 recovery ledger root 分離，避免 parent 在 cleanup 時誤刪 ambiguous ledger。
        /// </summary>
        internal string EvidenceRoot => Path.GetDirectoryName(EvidencePath)
            ?? throw new InvalidOperationException("The fresh-fixture evidence path is invalid.");
    }
}
