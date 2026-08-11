// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementFreshPreflightProbeTests.cs
// 用途：P7.2 Slice C fresh-fixture provision 前的唯一 opt-in CE 9.1/Data8 read-only child。
//
// 這是 test-fixture diagnostic control plane，不是 ChurchReport 產品 API。它只在 parent
// PowerShell 建立完整、current-user-bound process gate 時使用固定 crm91/Data8 deployment
// profile 執行 WhoAmI、Retrieve 和 RetrieveMultiple；沒有 Create、Update、Assign、Delete、
// Associate、Disassociate、ledger、descriptor publication、cleanup、feature flag 或流量切換。
// ============================================================================

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
/// 執行 Slice C fresh preflight 的 bounded live diagnostic。所有 CRM IDs、service user ID、password、
/// Entity、runtime、logger 與 exception 都只存活於這個 child test 的 local scope；最終跨 process
/// 輸出只能是 <see cref="P72FreshSliceCFixturePreflightProbeLiveEvidenceValue"/> 的固定分類。Child
/// 不會把 no-go assertion 成 test failure，因為 parent 必須能讀取一份已完整 cleanup 的 no-go evidence
/// 來判斷外部 prerequisite，而不是把 nonzero test exit 誤判為可能已寫入。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LivePackage02Data8ListManagementFreshPreflightProbeTests
{
    private const string ProfileAlias = "sunnyvalechback";

    /// <summary>
    /// 在 explicit parent gate 下，以 deployment-owned crm91/Data8/CE 9.1 WhoAmI 確認 service user，
    /// 再用同一 profile settings 的 request-scoped direct service 執行 fresh preflight probe。此測試的
    /// success 定義是 deterministic resource cleanup 與 strict deidentified evidence 已寫入；remote
    /// categories 為 no-go 仍是合法診斷結果，不能觸發 mutation/retry。Data8 runtime、WCF/service、
    /// logger 依反向建構順序釋放，任一 cleanup failure 覆寫為 no-go/cleanup-failure。
    /// </summary>
    [P72Data8SliceCFreshPreflightProbeFact]
    public async Task Probe_fresh_package02_data8_list_management_preconditions_emits_sanitized_evidence()
    {
        FreshPreflightProbeEnvironment? environment = null;
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;
        OnPremiseClient? service = null;
        var evidence = UnavailableEvidence("probe-unavailable", requestShape: "valid");

        try
        {
            environment = ReadFreshPreflightProbeEnvironment();
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

            // 唯一允許的 identity read 由 immutable deployment profile 的 executor 完成；environment
            // 沒有 service-user input，故 child 不能將另一個 user/profile 冒充為 owner comparison base。
            var serviceUserId = await LivePackage02Data8ListManagementEvidenceTests
                .ResolveFixtureTargetOwnerIdAsync(runtime.Executor, organization.OrganizationId)
                .ConfigureAwait(false);
            if (serviceUserId is not Guid verifiedServiceUserId)
            {
                evidence = UnavailableEvidence("probe-unavailable", requestShape: "valid");
            }
            else
            {
                service = new OnPremiseClient(organization.ServiceUri, settings.UserName, credentialPassword!);
                var result = new P72FreshSliceCFixturePreflightProbe(service).Probe(
                    new P72FreshSliceCFixturePreflightRequest(
                        environment.AddListId,
                        environment.RemoveListId,
                        environment.SmallGroupListId,
                        environment.ExistingTargetLeaderContactId,
                        environment.TransferSourceListId,
                        environment.TransferTargetListId,
                        environment.TransferWeekStartUtc,
                        verifiedServiceUserId));
                evidence = new P72FreshSliceCFixturePreflightProbeLiveEvidenceValue(
                    result.Outcome,
                    result.Reason,
                    result.ReadOnlyProbeExecuted,
                    result.RequestShape,
                    result.OperationalLists,
                    result.LeaderMarker,
                    result.OwnerKind,
                    result.OwnerState,
                    result.OwnerRelation,
                    result.WeeklyReport);
            }
        }
        catch (Exception)
        {
            // 任何 setup/WhoAmI/direct-read exception 都可能含 endpoint、AD FS/WCF 或 credential detail；
            // evidence 僅保留固定 unavailable category，且不會重試、建立 ledger 或嘗試補救資料。
            evidence = environment is null
                ? UnavailableEvidence("probe-input-invalid", requestShape: "invalid")
                : UnavailableEvidence("probe-unavailable", requestShape: "valid");
        }
        finally
        {
            var cleanupOutcome = evidence.Outcome;
            var cleanupReason = evidence.Reason;
            LivePackage02Data8ListManagementFreshFixtureTests.DisposeService(
                ref service,
                ref cleanupOutcome,
                ref cleanupReason);
            if (cleanupReason == "cleanup-failure")
            {
                evidence = UnavailableEvidence("cleanup-failure", requestShape: "valid");
            }

            if (!await LivePackage02Data8ListManagementFreshFixtureTests.DisposeRuntimeAsync(runtime).ConfigureAwait(false))
            {
                evidence = UnavailableEvidence("cleanup-failure", requestShape: "valid");
            }

            cleanupOutcome = evidence.Outcome;
            cleanupReason = evidence.Reason;
            LivePackage02Data8ListManagementFreshFixtureTests.DisposeLogger(
                ref loggerFactory,
                ref cleanupOutcome,
                ref cleanupReason);
            if (cleanupReason == "cleanup-failure")
            {
                evidence = UnavailableEvidence("cleanup-failure", requestShape: "valid");
            }
        }

        if (environment is not null)
        {
            P72FreshSliceCFixtureLiveEvidence.WritePreflightProbe(
                environment.EvidencePath,
                environment.EvidenceRoot,
                evidence);
        }

        // This fact intentionally succeeds for both go and no-go classifications after evidence publication:
        // xUnit's exit code is a process-integrity signal at the parent boundary, whereas the evidence outcome
        // alone represents the bounded remote precondition. A no-go must not be transformed into a nonzero
        // child exit because the parent would rightly discard it as potentially incomplete child cleanup.
        evidence.Outcome.Should().BeOneOf("go", "no-go");
    }

    /// <summary>
    /// 讀取 parent 唯一能提供的 fresh read-only inputs。所有 values 都是 bounded descriptor scalars；
    /// method 不接受 endpoint、connector、credential reference、organization、ledger、nonce、descriptor
    /// publication switch 或 arbitrary CRM field。缺失/錯誤資料在 Data8 runtime 與任何 remote I/O 前
    /// 拋出固定本機 exception，由 caller 映射成 deidentified input no-go。
    /// </summary>
    /// <returns>僅可供此 child invocation 使用的 immutable probe environment。</returns>
    private static FreshPreflightProbeEnvironment ReadFreshPreflightProbeEnvironment()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE"),
                "1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The fresh preflight probe mode is invalid.");
        }

        var owner = LivePackage02Data8ListManagementFreshFixtureTests.ReadRequiredText(
            "P7_2_SLICE_C_FRESH_OWNER",
            maximumLength: 256);
        if (!string.Equals(owner, WindowsIdentity.GetCurrent().Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The fresh preflight probe owner is invalid.");
        }

        return new FreshPreflightProbeEnvironment(
            LivePackage02Data8ListManagementFreshFixtureTests.ReadRequiredText(
                "P7_2_SLICE_C_FRESH_PREFLIGHT_EVIDENCE_PATH",
                maximumLength: 1024),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_ADD_LIST_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_REMOVE_LIST_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadGuid("P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID"),
            LivePackage02Data8ListManagementFreshFixtureTests.ReadSundayUtc("P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC"));
    }

    /// <summary>
    /// 建立未完成 remote proof、但仍完全去識別化的 failure evidence。這個 helper 不保留 exception、
    /// request、profile、credential 或 SDK object；all unavailable categories 明確避免 parent 將 partial
    /// result 當成 mutation authorization。它只供 catch/finally 的 deterministic fail-closed mapping。
    /// </summary>
    /// <param name="reason">writer allowlist 內的 fixed unavailable/input/cleanup reason。</param>
    /// <param name="requestShape">local shape 已證明或未證明的 fixed category。</param>
    /// <returns>零 mutation、零 read-completion bit 的 immutable evidence value。</returns>
    private static P72FreshSliceCFixturePreflightProbeLiveEvidenceValue UnavailableEvidence(
        string reason,
        string requestShape)
        => new(
            "no-go",
            reason,
            ReadOnlyProbeExecuted: false,
            RequestShape: requestShape,
            OperationalLists: "unavailable",
            LeaderMarker: "unavailable",
            OwnerKind: "unavailable",
            OwnerState: "unavailable",
            OwnerRelation: "unavailable",
            WeeklyReport: "unavailable");

    /// <summary>
    /// 封裝 fresh preflight child 的 bounded process input。record 只在此 child scope 傳遞 descriptor
    /// scalars，沒有 ledger、nonce、source contact、cache 或 session reference；evidence root 從 parent
    /// fixed path 導出，writer 會額外驗證它存在、不是 reparse point 且 filename 精確，避免 child 自選
    /// 輸出位置或將資料跨 profile/session 留存。
    /// </summary>
    private sealed record FreshPreflightProbeEnvironment(
        string EvidencePath,
        Guid AddListId,
        Guid RemoveListId,
        Guid SmallGroupListId,
        Guid ExistingTargetLeaderContactId,
        Guid TransferSourceListId,
        Guid TransferTargetListId,
        DateTimeOffset TransferWeekStartUtc)
    {
        /// <summary>取得由 parent temporary evidence path 導出的唯一 root；缺失 parent path 時 fail closed。</summary>
        internal string EvidenceRoot => Path.GetDirectoryName(EvidencePath)
            ?? throw new InvalidOperationException("The fresh preflight evidence path is invalid.");
    }
}
