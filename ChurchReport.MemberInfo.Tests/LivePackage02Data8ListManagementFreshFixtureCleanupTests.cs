// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementFreshFixtureCleanupTests.cs
// 目的：P7.2 Slice C fresh-fixture 的明確 cleanup child。
//
// 這個 child 只接受 parent 產生的 current-user ledger 與固定 operational-list
// projection；不搜尋 CRM、不讀取瀏覽器 session，也不把任何 CRM ID 或例外寫入
// evidence。每個 child invocation 都擁有自己的 Data8 runtime、CRM client、logger
// 與 ledger writer，並在 finally 依反向建構順序釋放，避免跨 profile/session 或
// process/resource state 留在下一次 invocation。
// ============================================================================

using System.Runtime.Versioning;
using System.Security.Principal;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 執行 P7.2 Slice C fresh graph 的反向清理流程。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LivePackage02Data8ListManagementFreshFixtureCleanupTests
{
    private const string ProfileAlias = "sunnyvalechback";

    /// <summary>
    /// 僅依 strict ledger 的 exact IDs 呼叫 cleanup；任何 ledger、WhoAmI、CRM
    /// provenance 或 deterministic cleanup 失敗都輸出去識別化 no-go。
    /// </summary>
    [P72Data8SliceCFreshCleanupFact]
    public async Task Cleanup_fresh_package02_data8_list_management_fixture_emits_sanitized_evidence()
    {
        var outcome = "no-go";
        var reason = "runtime-failure";
        var operationExecuted = false;
        var descriptorPublicationReady = false;
        FreshCleanupEnvironment? environment = null;
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;
        OnPremiseClient? service = null;

        try
        {
            environment = ReadCleanupEnvironment();
            var ledgerState = P72FreshSliceCFixtureFileLedger.Read(
                environment.LedgerPath,
                environment.LedgerRoot,
                environment.OwnerIdentity,
                "crm91",
                ProfileAlias,
                "9.1",
                "Data8");

            var configuration = LivePackage02Data8ListManagementEvidenceTests.CreateDevelopmentConfiguration();
            var (profiles, catalog, organization, settings) =
                LivePackage02Data8ListManagementEvidenceTests.ResolveProfile(configuration);
            var credentialPassword = Environment.GetEnvironmentVariable("CRM_PASSWORD");
            if (string.IsNullOrWhiteSpace(credentialPassword))
            {
                throw new InvalidOperationException("The cleanup credential is unavailable.");
            }

            loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            runtime = new EmbeddedData8Runtime(
                profiles,
                catalog,
                ProfileAlias,
                new OnPremiseData8ConnectorClientFactory(settings),
                loggerFactory.CreateLogger<EmbeddedData8Runtime>(),
                loggerFactory);

            var serviceUserId = await LivePackage02Data8ListManagementEvidenceTests
                .ResolveFixtureTargetOwnerIdAsync(runtime.Executor, organization.OrganizationId)
                .ConfigureAwait(false);
            if (serviceUserId is not Guid verifiedServiceUserId)
            {
                reason = "fixture-precondition-failed";
            }
            else
            {
                service = new OnPremiseClient(organization.ServiceUri, settings.UserName, credentialPassword);
                // descriptor 已在 provision 後替換為 fresh leader；cleanup 的 baseline owner 只能從
                // current-user strict ledger 取得，避免 session、descriptor 或環境變數跨 profile 影響清理。
                var request = new P72FreshSliceCFixtureProvisionRequest(
                    environment.AddListId,
                    environment.RemoveListId,
                    environment.SmallGroupListId,
                    ledgerState.OriginalTargetLeaderContactId,
                    environment.TransferSourceListId,
                    environment.TransferTargetListId,
                    environment.TransferWeekStartUtc,
                    verifiedServiceUserId,
                    ledgerState.Nonce);
                using var ledger = new P72FreshSliceCFixtureFileLedger(
                    environment.LedgerPath,
                    environment.LedgerRoot,
                    environment.OwnerIdentity,
                    "crm91",
                    ProfileAlias,
                    "9.1",
                    "Data8");

                var result = new P72FreshSliceCFixtureProvisioner(service).Cleanup(request, ledgerState, ledger);
                outcome = result.Outcome;
                reason = result.Reason;
                operationExecuted = result.OperationExecuted;
            }
        }
        catch (Exception)
        {
            // 不將 SDK/WCF/AD FS 例外內容帶出 child boundary；parent 會先檢查 exit code，
            // 因此任何未完成的 evidence 都不會被信任，也不會觸發 descriptor/ledger 刪除。
            outcome = "no-go";
            reason = operationExecuted ? "cleanup-ambiguous" : "runtime-failure";
            descriptorPublicationReady = false;
        }
        finally
        {
            LivePackage02Data8ListManagementFreshFixtureTests.DisposeService(
                ref service,
                ref outcome,
                ref reason);
            if (!await LivePackage02Data8ListManagementFreshFixtureTests
                    .DisposeRuntimeAsync(runtime)
                    .ConfigureAwait(false))
            {
                outcome = "no-go";
                reason = "cleanup-failure";
                descriptorPublicationReady = false;
            }

            LivePackage02Data8ListManagementFreshFixtureTests.DisposeLogger(
                ref loggerFactory,
                ref outcome,
                ref reason);
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
                    "cleanup",
                    outcome,
                    reason,
                    operationExecuted,
                    descriptorPublicationReady));
        }

        outcome.Should().Be(
            "go",
            because: "only exact-ID reverse cleanup with complete read-back may authorize ledger removal");
    }

    /// <summary>
    /// 讀取 parent 產生的固定 cleanup inputs。ledger nonce 會以 ledger 內容為準，
    /// 而非信任 caller 另外傳入的 nonce；其餘欄位只允許 deployment-owned static IDs。
    /// </summary>
    private static FreshCleanupEnvironment ReadCleanupEnvironment()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION"),
                "cleanup-fresh-fixture",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The cleanup confirmation is invalid.");
        }

        var owner = LivePackage02Data8ListManagementFreshFixtureTests
            .ReadRequiredText("P7_2_SLICE_C_FRESH_OWNER", 256);
        if (!string.Equals(owner, WindowsIdentity.GetCurrent().Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The cleanup owner is invalid.");
        }

        return new FreshCleanupEnvironment(
            LivePackage02Data8ListManagementFreshFixtureTests.ReadRequiredText("P7_2_SLICE_C_FRESH_LEDGER_ROOT", 1024),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadRequiredText("P7_2_SLICE_C_FRESH_LEDGER_PATH", 1024),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadRequiredText("P7_2_SLICE_C_FRESH_EVIDENCE_PATH", 1024),
            owner,
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_ADD_LIST_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_REMOVE_LIST_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadSundayUtc("P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC"));
    }

    /// <summary>
    /// cleanup child 的 bounded input projection；不保存密碼、endpoint、CRM payload 或
    /// browser/session state，且 evidence path 僅存在於 parent-owned temporary root。
    /// </summary>
    private sealed record FreshCleanupEnvironment(
        string LedgerRoot,
        string LedgerPath,
        string EvidencePath,
        string OwnerIdentity,
        Guid AddListId,
        Guid RemoveListId,
        Guid SmallGroupListId,
        Guid TransferSourceListId,
        Guid TransferTargetListId,
        DateTimeOffset TransferWeekStartUtc)
    {
        internal string EvidenceRoot => Path.GetDirectoryName(EvidencePath)
            ?? throw new InvalidOperationException("The cleanup evidence path is invalid.");
    }
}
