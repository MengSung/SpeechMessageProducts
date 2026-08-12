// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementEvidenceTests.cs
// 用途：P7.2 Slice C 的 Lenovo／CE 9.1／Data8 實機證據測試。
//
// 此檔案刻意只提供「明確 opt-in」的測試入口。一般 test discovery 不會連線、讀取密碼、
// 變更 ChurchReport feature flag 或執行 CRM operation。執行時會先以已驗證的 task-owned
// fixture graph 完成所有基線讀取；任何圖形、版本、owner、資料型別或讀取結果無法證明時，
// 一律輸出去識別化 no-go，且在第一個 mutation 之前停止。
//
// 資源生命週期：本測試擁有 LoggerFactory、EmbeddedData8Runtime 與 fixture store。finally
// 依反向順序釋放三者，使 Data8 lease、WCF client、logger provider 與其訂閱不會跨 child
// process 留存。證據檔只在清理後寫入 parent 已建立的單次 temporary path，內容不包含密碼、token、
// cookie、endpoint、GUID、baseline、sentinel、CRM payload、原始例外或 Windows identity；parent
// 若無法確認該目錄已刪除，必須將最終 handoff 降為 No-Go。
// ============================================================================

using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;
using SpeechMessage.Dynamics.ProductClient.ListManagement;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 執行 P7.2 Slice C 五個受控 list-management operation 的實機證據測試。
/// 此類別不是 ChurchReport consumer，也不開啟任何 feature flag；它只在已核准的 Windows
/// child process 中建立 Embedded + Data8 runtime，並將 deployment-owned <c>crm91</c>
/// Credential Manager 密碼限定在該 process 的短暫環境變數。每一個 operation 都交給既有
/// <see cref="P72ListManagementFixtureBridge"/> 進行 baseline、一次 dispatch、read-back、
/// restore 與 restore read-back，因此 timeout 或傳輸模糊結果絕不盲目重送。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LivePackage02Data8ListManagementEvidenceTests
{
    private const string ProfileAlias = "sunnyvalechback";
    private const string FixtureMarker = "p7.2-list-management";
    // child evidence 是 PowerShell strict parser 的跨 process wire contract。這個 immutable-after-first-use
    // options instance 只決定固定 camelCase naming，不保存 credential、fixture、session 或任何 caller state；
    // 兩種 lane 共用它，避免 record 的 CLR PascalCase property name 讓 parent 錯把有效 evidence 當成遺失。
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] OperationIds =
    [
        "list.members.add.many",
        "list.members.remove.one",
        "listmanagement.smallgroup.update.fields",
        "contact.assign.owner",
        "newperson.contact.transfer.between.lists"
    ];

    /// <summary>
    /// 保護 reconciliation evidence 的 cleanup 優先順序。故障注入以 <paramref name="cleanupSucceeded"/>
    /// 設為 <see langword="false"/> 模擬 store、runtime 或 logger 任一唯一 owner 無法完成確定性釋放；
    /// 決定性 assertion 是最終去識別化原因必須保留 <c>cleanup-failure</c>，不得被歷史 baseline
    /// 不可證明的正常 no-go 分類覆寫。測試只處理純 scalar，不建立 Data8 runtime、Credential Manager、
    /// temporary file 或 CE 連線，因此不會造成跨 process/session 資源或資料留存。
    /// </summary>
    [Fact]
    public void Reconciliation_cleanup_failure_is_not_overwritten_by_baseline_unprovable()
    {
        var terminal = FinalizeReconciliationEvidence(
            cleanupSucceeded: false,
            readOnlyProbeExecuted: true);

        terminal.Outcome.Should().Be("no-go");
        terminal.Reason.Should().Be(
            "cleanup-failure",
            because: "a release-blocking cleanup failure must remain visible to the parent strict parser");
        terminal.ReadOnlyProbeExecuted.Should().BeFalse(
            because: "the read-only graph result cannot be trusted as completed evidence after a resource owner failed to clean up");
    }

    /// <summary>
    /// 以固定順序執行 add、remove、small-group、owner 與 transfer 五段流程。
    /// 每段流程在 bridge 內只會 dispatch 一次，之後立刻 read-back 並還原 baseline；若前段
    /// 出現 no-go，後段標記為未啟動，避免在已不可信的 graph 上擴大 mutation。所有五段的
    /// graph 前置條件會先以唯讀 store 驗證，因此正常 go path 恰好執行五次受控 dispatch。
    /// </summary>
    [P72Data8SliceCLiveFact]
    public async Task Live_package02_data8_list_management_emits_sanitized_evidence()
    {
        var outcome = "no-go";
        var reason = "runtime-failure";
        var operationExecuted = false;
        var operations = new List<SliceCOperationEvidence>(OperationIds.Length);
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;
        P72Data8ListManagementFixtureStore? store = null;

        try
        {
            var fixture = ReadFixture();
            var configuration = CreateDevelopmentConfiguration();
            var (profiles, catalog, organization, settings) = ResolveProfile(configuration);
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
            var client = new Package02ListManagementClient(
                runtime.Executor,
                loggerFactory.CreateLogger<Package02ListManagementClient>());

            // WhoAmI 必須先由同一個 runtime/immutable profile 驗證，才能建立第二個只讀 store；這使
            // Assign 與 transfer 的目標必定等於實際 Data8 credential，而非 local descriptor 任意 GUID。
            var targetOwnerId = await ResolveFixtureTargetOwnerIdAsync(
                runtime.Executor,
                organization.OrganizationId).ConfigureAwait(false);
            if (targetOwnerId is not Guid verifiedTargetOwnerId)
            {
                reason = "fixture-precondition-failed";
            }
            else
            {
                // store 是第二個、只用於 fixture read/restore 的 Data8 service owner。它絕不與
                // runtime 的 connector lease 共用可變 session；finally 會在 runtime 前釋放它。
                store = new P72Data8ListManagementFixtureStore(new OnPremiseClient(
                    organization.ServiceUri,
                    settings.UserName,
                    credentialPassword!));

                if (!TryProveFixtureGraph(store, fixture, verifiedTargetOwnerId))
                {
                    reason = "fixture-precondition-failed";
                }
                else
                {
                var nonce = Guid.NewGuid().ToString("N");
                var add = await P72ListManagementFixtureBridge.ExecuteAddMembersAsync(
                    client,
                    store,
                    fixture.AddListId,
                    [fixture.ContactId],
                    "p72-slice-c-add-" + nonce).ConfigureAwait(false);
                operations.Add(ToEvidence(OperationIds[0], add));

                if (add.Outcome == "go")
                {
                    var remove = await P72ListManagementFixtureBridge.ExecuteRemoveMemberAsync(
                        client,
                        store,
                        fixture.RemoveListId,
                        fixture.ContactId,
                        "p72-slice-c-remove-" + nonce).ConfigureAwait(false);
                    operations.Add(ToEvidence(OperationIds[1], remove));

                    if (remove.Outcome == "go")
                    {
                        var smallGroup = await P72ListManagementFixtureBridge.ExecuteSmallGroupFieldsAsync(
                            client,
                            store,
                            fixture.SmallGroupListId,
                            SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader,
                            fixture.SmallGroupTargetLeaderContactId,
                            fixture.SmallGroupExpectedRelationshipListId,
                            "p72-slice-c-small-group-" + nonce).ConfigureAwait(false);
                        operations.Add(ToEvidence(OperationIds[2], smallGroup));

                        if (smallGroup.Outcome == "go")
                        {
                            var owner = await P72ListManagementFixtureBridge.ExecuteOwnerAssignmentAsync(
                                client,
                                store,
                                fixture.ContactId,
                                verifiedTargetOwnerId,
                                "p72-slice-c-owner-" + nonce).ConfigureAwait(false);
                            operations.Add(ToEvidence(OperationIds[3], owner));

                            if (owner.Outcome == "go")
                            {
                                var transfer = await P72ListManagementFixtureBridge.ExecuteTransferAsync(
                                    client,
                                    store,
                                    new P72TransferFixture(
                                        fixture.ContactId,
                                        fixture.TransferSourceListId,
                                        fixture.TransferTargetListId,
                                        fixture.TransferWeekStartUtc,
                                        verifiedTargetOwnerId),
                                    "p72-slice-c-transfer-" + nonce).ConfigureAwait(false);
                                operations.Add(ToEvidence(OperationIds[4], transfer));
                            }
                        }
                    }
                }

                    operationExecuted = operations.Any(static operation => operation.OperationExecuted);
                    CompleteNotStartedOperations(operations);
                    if (operations.All(static operation => operation.Outcome == "go"))
                    {
                        outcome = "go";
                        reason = string.Empty;
                    }
                    else
                    {
                        reason = "live-evidence-incomplete";
                    }
                }
            }
        }
        catch (Exception)
        {
            // 任何 Data8／WCF／configuration 例外都可能包含 endpoint 或 authentication detail；
            // 證據只保留固定分類，讓 operator 依手冊在本機診斷而不把祕密寫入 TRX。
            outcome = "no-go";
            reason = "runtime-failure";
        }
        finally
        {
            DisposeStore(ref store, ref outcome, ref reason);
            var runtimeCleanup = await DisposeRuntimeAsync(runtime).ConfigureAwait(false);
            if (!runtimeCleanup)
            {
                outcome = "no-go";
                reason = "cleanup-failure";
            }

            DisposeLogger(ref loggerFactory, ref outcome, ref reason);
        }

        CompleteNotStartedOperations(operations);
        operationExecuted |= operations.Any(static operation => operation.OperationExecuted);
        var evidence = new
        {
            schemaVersion = 1,
            outcome,
            reason,
            profileAlias = ProfileAlias,
            deploymentProfileAlias = "crm91",
            ceVersion = "9.1",
            connector = "Data8",
            preflightOnly = false,
            operationExecuted,
            featureFlagChanged = false,
            operations
        };
        WriteSliceCEvidenceFile(JsonSerializer.Serialize(evidence, EvidenceJsonOptions));

        // child 的 exit code 表示 process／資源完整性，而 evidence.outcome 才表示受控 CE 操作結果。
        // 已完整寫入並完成 cleanup 的 no-go 必須正常結束，讓 parent strict parser 能保留固定分類；
        // 只有 parent 才決定最後的 non-zero handoff 與禁止重試，不能把預期 no-go 偽裝成 child crash。
        outcome.Should().BeOneOf("go", "no-go");
    }

    /// <summary>
    /// 只讀取既有 Slice C fixture graph，將目前形狀投影成去識別化的「歷史基線無法證明」結果。
    /// 此入口刻意不建立 list-management client，也不呼叫 bridge；它只以同一個 immutable Data8 profile
    /// 先取得 WhoAmI service identity，再透過 fixture store 的 bounded read projection 比較目前狀態。
    /// 即使所有讀取都成功，過去寫入前的 snapshot 已不存在，故結果永遠為 no-go 且不允許自動重試。
    /// </summary>
    /// <remarks>
    /// 本方法擁有 logger、runtime 與 store 的生命週期。finally 固定依 store、runtime、logger 的順序
    /// 釋放，避免 Data8 service、WCF channel、lease、logger provider 或訂閱跨 child process 留存。所有
    /// CRM identity、fixture scalar 與 snapshot 都只存在方法 scope，evidence 僅帶 allowlisted 分類。
    /// </remarks>
    [P72Data8SliceCReconcileFact]
    public async Task Reconcile_package02_data8_list_management_emits_sanitized_reconciliation()
    {
        var outcome = "no-go";
        var reason = "baseline-unprovable";
        var readOnlyProbeExecuted = false;
        var cleanupSucceeded = true;
        var ownerBinding = "unavailable";
        // 僅記錄 allowlist 的最近唯讀邊界，不記錄例外、GUID、endpoint、credential 或 CRM payload；大多數
        // 階段在讀取完成後更新，但可能失敗的 transfer composite 會在進入 ReadTransferGraph 前先標記，
        // 讓 parent 不會把 weekly-report/present-record 投影失敗誤報為前一個 contact-owner read。此值的
        // 生命週期僅限本次 child evidence，不能成為 retry 或 mutation 授權。
        var probeStage = "not-started";
        var operations = CreateUnavailableReconciliationOperations();
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;
        P72Data8ListManagementFixtureStore? store = null;

        try
        {
            var fixture = ReadFixture();
            var configuration = CreateDevelopmentConfiguration();
            var (profiles, catalog, organization, settings) = ResolveProfile(configuration);
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

            var targetOwnerId = await ResolveFixtureTargetOwnerIdAsync(
                runtime.Executor,
                organization.OrganizationId).ConfigureAwait(false);
            if (targetOwnerId is Guid verifiedTargetOwnerId)
            {
                // WhoAmI 成功與 fixture store 的第一個 Retrieve 是不同的信任邊界。先投影為無 GUID 的
                // ownerBinding，讓後續 bounded store read 失敗時 parent 仍可辨識 Data8 identity 已通過；
                // 此區域不寫入 CE 資料，也不延長 credential、runtime 或 owner scalar 的生命週期。
                ownerBinding = P72Data8ListManagementFixtureReconciler
                    .ClassifyVerifiedOwnerBinding(verifiedTargetOwnerId);
                probeStage = "whoami-verified";

                store = new P72Data8ListManagementFixtureStore(new OnPremiseClient(
                    organization.ServiceUri,
                    settings.UserName,
                    credentialPassword!));
                probeStage = "fixture-store-created";

                var addMembership = store.ReadMembership(fixture.AddListId, [fixture.ContactId]);
                probeStage = "add-membership-read";
                var removeMembership = store.ReadMembership(fixture.RemoveListId, [fixture.ContactId]);
                probeStage = "remove-membership-read";
                var smallGroup = store.ReadSmallGroupFields(fixture.SmallGroupListId);
                probeStage = "small-group-read";
                var smallGroupExpected = store.ResolveSmallGroupExpected(
                    fixture.SmallGroupListId,
                    SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader,
                    fixture.SmallGroupTargetLeaderContactId,
                    fixture.SmallGroupExpectedRelationshipListId);
                probeStage = "small-group-expected-read";
                var contactOwnerId = store.ReadOwnerId(fixture.ContactId);
                probeStage = "contact-owner-read";
                var transferFixture = new P72TransferFixture(
                    fixture.ContactId,
                    fixture.TransferSourceListId,
                    fixture.TransferTargetListId,
                    fixture.TransferWeekStartUtc,
                    verifiedTargetOwnerId);
                // transfer read 內含 membership、weekly report、present record、primary list 與 owner 多個固定
                // 投影；必須在呼叫前先切換 stage，否則其中任一 fail-closed 例外都會留下 contact-owner-read，
                // 誤導 operator 追查已成功的 owner 邊界。此分類不揭露失敗子階段，也不授權重試。
                probeStage = "transfer-read";
                var transfer = store.ReadTransferGraph(transferFixture);
                var reconciliation = P72Data8ListManagementFixtureReconciler.Classify(
                    addMembership,
                    removeMembership,
                    smallGroup,
                    smallGroupExpected,
                    contactOwnerId,
                    transferFixture,
                    transfer,
                    verifiedTargetOwnerId);

                readOnlyProbeExecuted = reconciliation.ReadOnlyProbeExecuted;
                ownerBinding = reconciliation.OwnerBinding;
                operations = CreateReconciliationOperations(reconciliation);
                probeStage = "classification-complete";
            }
        }
        catch (Exception)
        {
            // CE、Data8、WCF 或 descriptor 的原始例外可能含 endpoint、認證或 fixture 資料；唯讀 lane
            // 故意只保留既有 unavailable 分類，不能把失敗細節帶入 temporary evidence 或 xUnit output。
        }
        finally
        {
            DisposeStore(ref store, ref outcome, ref reason);
            if (reason == "cleanup-failure")
            {
                cleanupSucceeded = false;
            }

            if (!await DisposeRuntimeAsync(runtime).ConfigureAwait(false))
            {
                cleanupSucceeded = false;
            }

            DisposeLogger(ref loggerFactory, ref outcome, ref reason);
            if (reason == "cleanup-failure")
            {
                cleanupSucceeded = false;
            }
        }

        var terminal = FinalizeReconciliationEvidence(cleanupSucceeded, readOnlyProbeExecuted);
        outcome = terminal.Outcome;
        reason = terminal.Reason;
        readOnlyProbeExecuted = terminal.ReadOnlyProbeExecuted;

        var evidence = new
        {
            schemaVersion = 1,
            outcome,
            reason,
            profileAlias = ProfileAlias,
            deploymentProfileAlias = "crm91",
            ceVersion = "9.1",
            connector = "Data8",
            preflightOnly = false,
            readOnlyProbeExecuted,
            operationExecuted = false,
            featureFlagChanged = false,
            ownerBinding,
            probeStage,
            operations
        };
        WriteSliceCReconciliationEvidenceFile(JsonSerializer.Serialize(evidence, EvidenceJsonOptions));

        outcome.Should().Be("no-go");
        if (cleanupSucceeded)
        {
            reason.Should().Be("baseline-unprovable");
            // 無法完成某一段固定 projection 也是合法且有診斷價值的 no-go：catch 已把 raw exception
            // 隔離在 child 內，parent 應收到對應的 probeStage／unavailable state，而不是因為 xUnit
            // assertion 變成 child-process-failed。只有真的完成所有 projection 時，才允許宣告 true。
            if (readOnlyProbeExecuted)
            {
                probeStage.Should().Be(
                    "classification-complete",
                    because: "a completed read-only probe requires WhoAmI plus every fixed fixture projection and classification");
            }
        }
        else
        {
            // cleanup-failure 已寫入固定 evidence；此處不得再以 assertion 製造 child 非零結束，
            // 否則 parent 只能看到較低資訊量的 child-process-failed，會掩蓋真正的資源生命週期阻斷原因。
            reason.Should().Be("cleanup-failure");
            readOnlyProbeExecuted.Should().BeFalse(
                because: "a failed store, runtime, or logger cleanup invalidates the read-only evidence completion claim");
        }
    }

    /// <summary>
    /// 只修復 task-owned expected relationship list 的 area-leader 與 area-name 欄位。
    /// 此 lane 不呼叫 ProductClient、feature flag 或任何其他 Slice C operation；它先以同一個
    /// crm91/Data8 runtime 執行 WhoAmI，再由 fixture store 驗證 provenance，最後最多送出一次
    /// allowlisted Update 並立即 read-back。任何寫入後的不確定狀態都輸出 sanitized no-go，
    /// 不會由 child 自動重試。
    /// </summary>
    [P72Data8SliceCRepairFact]
    public async Task Repair_package02_data8_relationship_fixture_emits_sanitized_evidence()
    {
        var outcome = "no-go";
        var reason = "fixture-precondition-failed";
        var operationExecuted = false;
        var readBackConfirmed = false;
        var cleanupSucceeded = true;
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;
        P72Data8ListManagementFixtureStore? store = null;

        try
        {
            var fixture = ReadFixture();
            var configuration = CreateDevelopmentConfiguration();
            var (profiles, catalog, organization, settings) = ResolveProfile(configuration);
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

            var verifiedOwnerId = await ResolveFixtureTargetOwnerIdAsync(
                runtime.Executor,
                organization.OrganizationId).ConfigureAwait(false);
            if (verifiedOwnerId is Guid)
            {
                store = new P72Data8ListManagementFixtureStore(new OnPremiseClient(
                    organization.ServiceUri,
                    settings.UserName,
                    credentialPassword!));

                var repair = store.RepairTaskOwnedExpectedRelationshipFields(
                    fixture.ContactId,
                    fixture.SmallGroupListId,
                    fixture.SmallGroupTargetLeaderContactId,
                    fixture.SmallGroupExpectedRelationshipListId);
                outcome = repair.Outcome;
                reason = repair.Reason;
                operationExecuted = repair.OperationExecuted;
                readBackConfirmed = repair.ReadBackConfirmed;
            }
        }
        catch (Exception)
        {
            // 寫入請求可能已抵達 CE 但 child 未取得回應；這種狀態必須保持 ambiguous，禁止重試。
            reason = operationExecuted ? "repair-ambiguous" : "repair-precondition-failed";
            outcome = "no-go";
            readBackConfirmed = false;
        }
        finally
        {
            DisposeStore(ref store, ref outcome, ref reason);
            if (reason == "cleanup-failure")
            {
                cleanupSucceeded = false;
            }

            if (!await DisposeRuntimeAsync(runtime).ConfigureAwait(false))
            {
                cleanupSucceeded = false;
            }

            DisposeLogger(ref loggerFactory, ref outcome, ref reason);
            if (reason == "cleanup-failure")
            {
                cleanupSucceeded = false;
            }
        }

        if (!cleanupSucceeded)
        {
            outcome = "no-go";
            reason = "cleanup-failure";
            readBackConfirmed = false;
        }

        var evidence = new
        {
            schemaVersion = 1,
            outcome,
            reason,
            profileAlias = ProfileAlias,
            deploymentProfileAlias = "crm91",
            ceVersion = "9.1",
            connector = "Data8",
            preflightOnly = false,
            operationExecuted,
            readBackConfirmed,
            featureFlagChanged = false
        };
        WriteSliceCRepairEvidenceFile(JsonSerializer.Serialize(evidence, EvidenceJsonOptions));
    }

    /// <summary>
    /// 只執行 relationship-list repair 的唯讀 precondition probe。
    /// 此測試永遠不呼叫 Update/Execute/Create/Delete 或 ProductClient；它沿用既有 crm91/Data8
    /// WhoAmI 與 fixture store read path，將 source contact、兩個 static list、leader marker、
    /// race-leader 關聯及 area 欄位狀態投影成 bounded sanitized evidence。
    /// </summary>
    [P72Data8SliceCRepairProbeFact]
    public async Task Probe_package02_data8_relationship_fixture_emits_sanitized_evidence()
    {
        var outcome = "no-go";
        var reason = "probe-precondition-failed";
        var readOnlyProbeExecuted = false;
        var cleanupSucceeded = true;
        var probe = new P72SmallGroupFixtureRepairProbe(false, false, false, false, false, "unreadable", "unavailable");
        ILoggerFactory? loggerFactory = null;
        EmbeddedData8Runtime? runtime = null;
        P72Data8ListManagementFixtureStore? store = null;

        try
        {
            var fixture = ReadFixture();
            var configuration = CreateDevelopmentConfiguration();
            var (profiles, catalog, organization, settings) = ResolveProfile(configuration);
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

            var verifiedOwnerId = await ResolveFixtureTargetOwnerIdAsync(
                runtime.Executor,
                organization.OrganizationId).ConfigureAwait(false);
            if (verifiedOwnerId is Guid)
            {
                store = new P72Data8ListManagementFixtureStore(new OnPremiseClient(
                    organization.ServiceUri,
                    settings.UserName,
                    credentialPassword!));

                probe = store.ProbeTaskOwnedExpectedRelationshipFields(
                    fixture.ContactId,
                    fixture.SmallGroupListId,
                    fixture.SmallGroupTargetLeaderContactId,
                    fixture.SmallGroupExpectedRelationshipListId);
                // probe 永遠不是 repair 授權或成功寫入；即使所有 proof 成立，也以 no-go
                // 回報「已完成診斷」，避免 parent exit code 被誤解為可直接重試 Update。
                outcome = "no-go";
                reason = "repair-preconditions-proven";
                readOnlyProbeExecuted = true;
            }
        }
        catch (Exception)
        {
            // 唯讀診斷不輸出遠端例外；未完成 proof 時保持 no-go，且不暗示任何 repair 可重試。
            outcome = "no-go";
            reason = "probe-precondition-failed";
            readOnlyProbeExecuted = false;
        }
        finally
        {
            DisposeStore(ref store, ref outcome, ref reason);
            if (reason == "cleanup-failure")
            {
                cleanupSucceeded = false;
            }

            if (!await DisposeRuntimeAsync(runtime).ConfigureAwait(false))
            {
                cleanupSucceeded = false;
            }

            DisposeLogger(ref loggerFactory, ref outcome, ref reason);
            if (reason == "cleanup-failure")
            {
                cleanupSucceeded = false;
            }
        }

        if (!cleanupSucceeded)
        {
            outcome = "no-go";
            reason = "cleanup-failure";
            readOnlyProbeExecuted = false;
        }

        var evidence = new
        {
            schemaVersion = 1,
            outcome,
            reason,
            profileAlias = ProfileAlias,
            deploymentProfileAlias = "crm91",
            ceVersion = "9.1",
            connector = "Data8",
            preflightOnly = false,
            operationExecuted = false,
            readOnlyProbeExecuted,
            featureFlagChanged = false,
            probe = new
            {
                sourceContactMarkerValid = probe.SourceContactMarkerValid,
                smallGroupListValid = probe.SmallGroupListValid,
                expectedRelationshipListValid = probe.ExpectedRelationshipListValid,
                targetLeaderMarkerValid = probe.TargetLeaderMarkerValid,
                expectedRelationshipRaceLeaderMatches = probe.ExpectedRelationshipRaceLeaderMatches,
                expectedRelationshipFieldsState = probe.ExpectedRelationshipFieldsState,
                preconditionState = probe.PreconditionState
            }
        };
        WriteSliceCRepairProbeEvidenceFile(JsonSerializer.Serialize(evidence, EvidenceJsonOptions));
    }

    /// <summary>
    /// 決定 Slice C reconciliation 的最終去識別化 evidence 分類。未保存的歷史 baseline 本身只能得出
    /// <c>baseline-unprovable</c> 的 no-go；但 store、runtime 或 logger 任一唯一資源 owner 無法完成
    /// Dispose 時，<c>cleanup-failure</c> 是更高優先序且 release-blocking 的狀態，絕不可被 baseline
    /// 分類覆寫。此純值方法不接收例外、GUID、credential 或 resource reference，因此不會延長資料、
    /// session 或資源生命週期，也可讓離線測試以明確 fault injection 驗證 parent strict parser 會收到正確分類。
    /// </summary>
    /// <param name="cleanupSucceeded">所有本次 child-owned resource 是否均已完成確定性釋放。</param>
    /// <param name="readOnlyProbeExecuted">所有固定唯讀 projection 是否在 cleanup 前已完成。</param>
    /// <returns>固定 no-go outcome、最高優先序 reason 與可安全宣告的 read-only probe 完成旗標。</returns>
    internal static (string Outcome, string Reason, bool ReadOnlyProbeExecuted) FinalizeReconciliationEvidence(
        bool cleanupSucceeded,
        bool readOnlyProbeExecuted)
    {
        // cleanup failure 代表 child 無法證明 WCF/runtime/logger 已回到基線；即使 CRM projection 已完成，
        // 仍不可把它保留為可供 operator 使用的 read-only evidence，否則 parent 會隱藏資源洩漏風險。
        return cleanupSucceeded
            ? ("no-go", "baseline-unprovable", readOnlyProbeExecuted)
            : ("no-go", "cleanup-failure", false);
    }

    /// <summary>
    /// 在任何 bridge dispatch 前讀取五段流程所需的固定 graph，並確認每一段都會有可判斷的
    /// baseline 與不同的 expected state。這個方法只呼叫 store 的 read-only projection；任一
    /// read 失敗、結果模糊或 precondition 不合即回傳 <see langword="false"/>，避免 mutation。
    /// </summary>
    /// <param name="store">唯一的 Data8 fixture graph 讀取 owner。</param>
    /// <param name="fixture">已由 script 與 child process 共同驗證的 task-owned descriptor。</param>
    /// <returns>完整 graph 已被證明時為 <see langword="true"/>；否則為 <see langword="false"/>。</returns>
    private static bool TryProveFixtureGraph(
        P72Data8ListManagementFixtureStore store,
        SliceCFixture fixture,
        Guid targetOwnerId)
    {
        try
        {
            // provenance 是 execute lane 的第一個 CRM 邊界：在任何 baseline read 或 bridge dispatch 前，
            // 先由同一個 store 以固定 Retrieve 證明 descriptor 指向 task-owned、static、contact-only 的
            // 遠端 graph。失敗時 caller 會回傳 fixture-precondition-failed；這個順序禁止本機 GUID、
            // Windows identity 或歷史 evidence 取代 CE 端 ownership proof，也保證不會先發出 mutation。
            if (!store.TryValidateTaskOwnedSliceCFixtureGraph(
                    fixture.ContactId,
                    fixture.AddListId,
                    fixture.RemoveListId,
                    fixture.SmallGroupListId,
                    fixture.SmallGroupTargetLeaderContactId,
                    fixture.SmallGroupExpectedRelationshipListId,
                    fixture.TransferSourceListId,
                    fixture.TransferTargetListId))
            {
                return false;
            }

            var addBaseline = store.ReadMembership(fixture.AddListId, [fixture.ContactId]);
            var removeBaseline = store.ReadMembership(fixture.RemoveListId, [fixture.ContactId]);
            var smallGroupBaseline = store.ReadSmallGroupFields(fixture.SmallGroupListId);
            var smallGroupExpected = store.ResolveSmallGroupExpected(
                fixture.SmallGroupListId,
                SmallGroupFixedFieldsUpdateMode.ChangeAreaLeader,
                fixture.SmallGroupTargetLeaderContactId,
                fixture.SmallGroupExpectedRelationshipListId);
            var baselineOwnerId = store.ReadOwnerId(fixture.ContactId);
            var transferFixture = new P72TransferFixture(
                fixture.ContactId,
                fixture.TransferSourceListId,
                fixture.TransferTargetListId,
                fixture.TransferWeekStartUtc,
                targetOwnerId);
            var transferBaseline = store.ReadTransferGraph(transferFixture);

            return !addBaseline.PresentMemberIds.Contains(fixture.ContactId) &&
                   removeBaseline.PresentMemberIds.Contains(fixture.ContactId) &&
                   smallGroupBaseline != smallGroupExpected &&
                   baselineOwnerId != targetOwnerId &&
                   transferBaseline.SourceMembershipPresent &&
                   !transferBaseline.TargetMembershipPresent &&
                   transferBaseline.PresentRecordId is null &&
                   !transferBaseline.PresentRecordMatches &&
                   transferBaseline.PrimaryListId != fixture.TransferTargetListId &&
                   transferBaseline.OwnerId != targetOwnerId;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 將 bridge 的已去識別化結果投影成 temporary evidence 的固定 operation schema。
    /// 這個轉換不傳遞任何 fixture identity、baseline 或 exception，只保留 bridge 已 allowlist
    /// 的 outcome、reason、reconciliation 與 cleanup 分類，供 PowerShell 的嚴格 parser 驗證。
    /// </summary>
    private static SliceCOperationEvidence ToEvidence(
        string operationId,
        P72ListManagementFixtureBridgeResult result)
        => new(
            operationId,
            result.Outcome,
            result.Reason,
            result.OperationExecuted,
            result.ReconciliationState,
            result.CleanupState);

    /// <summary>
    /// 將 pure reconciler 的五個閉合分類對齊既有 operation ID 順序。每筆皆明確標記為 not-run：本 lane
    /// 只讀取 CE，不曾發出 dispatch、也不持有任何可供補償或重送的 mutation state。清理狀態固定為
    /// not-applicable，避免把「沒有需要還原的變更」誤解為已執行過 rollback。
    /// </summary>
    /// <param name="reconciliation">已完成純值分類的去識別化結果。</param>
    /// <returns>剛好五筆、只含 allowlisted scalar 的 not-run evidence。</returns>
    private static List<SliceCOperationEvidence> CreateReconciliationOperations(
        P72Data8ListManagementFixtureReconciliationResult reconciliation)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);
        return
        [
            new SliceCOperationEvidence(OperationIds[0], "not-run", "baseline-unprovable", false, reconciliation.AddMembership, "not-applicable"),
            new SliceCOperationEvidence(OperationIds[1], "not-run", "baseline-unprovable", false, reconciliation.RemoveMembership, "not-applicable"),
            new SliceCOperationEvidence(OperationIds[2], "not-run", "baseline-unprovable", false, reconciliation.SmallGroup, "not-applicable"),
            new SliceCOperationEvidence(OperationIds[3], "not-run", "baseline-unprovable", false, reconciliation.ContactOwner, "not-applicable"),
            new SliceCOperationEvidence(OperationIds[4], "not-run", "baseline-unprovable", false, reconciliation.Transfer, "not-applicable")
        ];
    }

    /// <summary>
    /// 建立 probe 尚未完成時的封閉 fallback。這個方法不接受 exception、GUID 或 caller supplied value，
    /// 因而任何 runtime 或 descriptor failure 都不能經由 evidence 外洩，亦不能被誤判為可重試的 baseline。
    /// </summary>
    /// <returns>五筆 unavailable/not-run 分類，且沒有任何 mutation 訊號。</returns>
    private static List<SliceCOperationEvidence> CreateUnavailableReconciliationOperations()
        =>
        [
            new SliceCOperationEvidence(OperationIds[0], "not-run", "baseline-unprovable", false, "unavailable", "not-applicable"),
            new SliceCOperationEvidence(OperationIds[1], "not-run", "baseline-unprovable", false, "unavailable", "not-applicable"),
            new SliceCOperationEvidence(OperationIds[2], "not-run", "baseline-unprovable", false, "unavailable", "not-applicable"),
            new SliceCOperationEvidence(OperationIds[3], "not-run", "baseline-unprovable", false, "unavailable", "not-applicable"),
            new SliceCOperationEvidence(OperationIds[4], "not-run", "baseline-unprovable", false, "unavailable", "not-applicable")
        ];

    /// <summary>
    /// 以同一個已建立的 Data8 runtime 取得 fixture 唯一可用的 CRM target owner。
    /// 此方法不接受 descriptor 或 Windows identity 作為 CRM principal；它只送出固定 WhoAmI operation，
    /// 並逐一檢查 profile 固定的 CE 9.1、operation discriminator、WhoAmI branch 與三個非空 GUID。任何
    /// transport failure、組織不符或不完整 projection 都回傳 <see langword="null"/>，讓 caller 在建立
    /// raw fixture store 或發出 mutation 前 fail closed。結果只在目前 live test invocation 的 local scope
    /// 存活，不寫入 evidence、static field、cache 或跨請求/session state。
    /// </summary>
    /// <param name="executor">目前 immutable <c>crm91</c> profile 所擁有的 Data8 executor。</param>
    /// <param name="expectedOrganizationId">configuration catalog 已確認的 CE 9.1 Organization GUID。</param>
    /// <returns>可安全作為 owner target 的 service UserId；無法完整證明時為 <see langword="null"/>。</returns>
    internal static async Task<Guid?> ResolveFixtureTargetOwnerIdAsync(
        IDynamicsOperationExecutor executor,
        Guid expectedOrganizationId)
    {
        ArgumentNullException.ThrowIfNull(executor);
        if (expectedOrganizationId == Guid.Empty)
        {
            return null;
        }

        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = ProfileAlias,
            CapabilityOperationId = global::SpeechMessage.Dynamics.Abstractions.Operations.OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "p7.2-list-management-fixture-owner"
        }).ConfigureAwait(false);
        var data = result.Data;
        if (!result.Succeeded ||
            data is null ||
            !string.Equals(
                data.OperationId,
                global::SpeechMessage.Dynamics.Abstractions.Operations.OperationIds.RuntimeHealthWhoAmI,
                StringComparison.Ordinal) ||
            !string.Equals(data.CeVersion, "9.1", StringComparison.Ordinal) ||
            data.ResponseKind != OperationResponseKind.WhoAmI ||
            data.WhoAmI is not { } whoAmI)
        {
            return null;
        }

        var userId = whoAmI.UserId;
        var businessUnitId = whoAmI.BusinessUnitId;
        var organizationId = whoAmI.OrganizationId;
        if (userId is not Guid verifiedUserId ||
            businessUnitId is not Guid verifiedBusinessUnitId ||
            organizationId is not Guid verifiedOrganizationId ||
            verifiedUserId == Guid.Empty ||
            verifiedBusinessUnitId == Guid.Empty ||
            verifiedOrganizationId == Guid.Empty ||
            verifiedOrganizationId != expectedOrganizationId)
        {
            return null;
        }

        return verifiedUserId;
    }

    /// <summary>
    /// 將清理完成後的去識別化 evidence 寫入 handoff parent 已建立且唯一擁有的暫存檔。
    /// 路徑必須在 OS temporary root 下的 nonce Slice C 目錄，名稱固定、父目錄不得是 reparse point，且
    /// 檔案必須尚未存在；<see cref="FileMode.CreateNew"/> 讓 child 無法覆寫先前資料。內容限定為一行
    /// UTF-8 no-BOM JSON 加 final CRLF、最大 32 KiB，避免 TRX stdout 的重複序列化，也避免 evidence
    /// 變成可跨 test/profile 留存的資料通道。parent 在 child 結束後讀取並負責整個目錄的 finally cleanup。
    /// </summary>
    /// <param name="evidenceJson">已由本 test 建立的固定 schema、去識別化 JSON。</param>
    private static void WriteSliceCEvidenceFile(string evidenceJson)
        => WriteSliceCEvidenceFile(
            evidenceJson,
            "P7_2_SLICE_C_EVIDENCE_PATH",
            "P72Data8ListManagementEvidence.json");

    /// <summary>
    /// 將 reconciliation child 的唯一 evidence 寫入 parent 指派的另一個固定檔名。它與 write lane
    /// 共用相同 nonce directory 驗證與 CreateNew 邊界，但永遠不共用環境變數或檔名，避免讀取結果被
    /// 既有 mutation evidence 誤解析。parent 在輸出 final JSON 前仍是此目錄唯一 cleanup owner。
    /// </summary>
    /// <param name="evidenceJson">只含 closed reconciliation schema 的去識別化 JSON。</param>
    private static void WriteSliceCReconciliationEvidenceFile(string evidenceJson)
        => WriteSliceCEvidenceFile(
            evidenceJson,
            "P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH",
            "P72Data8ListManagementReconciliationEvidence.json");

    /// <summary>
    /// 將 fixture repair child 的 sanitized evidence 寫入 parent 指定的 nonce temporary file。
    /// repair lane 與 execute/reconcile lane 共用相同的 bounded UTF-8/CRLF/回收契約，避免
    /// credential、GUID、CRM payload 或端點從 child stdout 泄漏。
    /// </summary>
    /// <param name="evidenceJson">已通過 child schema 投影的 bounded JSON。</param>
    private static void WriteSliceCRepairEvidenceFile(string evidenceJson)
        => WriteSliceCEvidenceFile(
            evidenceJson,
            "P7_2_SLICE_C_REPAIR_EVIDENCE_PATH",
            "P72Data8ListManagementRepairEvidence.json");

    /// <summary>
    /// 將唯讀 repair probe child evidence 寫入 parent 指定的 bounded temporary file。
    /// probe 不使用 stdout 傳送 CRM projection，並沿用既有 UTF-8/CRLF/finally cleanup 契約。
    /// </summary>
    /// <param name="evidenceJson">已去識別化且通過固定 schema 的 JSON。</param>
    private static void WriteSliceCRepairProbeEvidenceFile(string evidenceJson)
        => WriteSliceCEvidenceFile(
            evidenceJson,
            "P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH",
            "P72Data8ListManagementRepairProbeEvidence.json");

    /// <summary>
    /// 實作兩種 child evidence 共用的單次 temporary-file 邊界。environment variable 與固定檔名皆由
    /// 本檔 hard-code 呼叫點提供；此方法不接受外部 caller 的 path 設計，因此 child 不可能選擇 repository、
    /// profile 或另一個測試的保存位置。FileMode.CreateNew 和最終 CRLF 避免覆寫或拼接先前 evidence。
    /// </summary>
    /// <param name="evidenceJson">已完成序列化的 bounded JSON。</param>
    /// <param name="environmentVariableName">父行程唯一允許的 temporary-path environment variable。</param>
    /// <param name="expectedFileName">此 lane 唯一允許的 temporary evidence file name。</param>
    private static void WriteSliceCEvidenceFile(
        string evidenceJson,
        string environmentVariableName,
        string expectedFileName)
    {
        var configuredPath = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(configuredPath) ||
            configuredPath.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            string.IsNullOrWhiteSpace(evidenceJson) ||
            evidenceJson.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            string.IsNullOrWhiteSpace(environmentVariableName) ||
            string.IsNullOrWhiteSpace(expectedFileName) ||
            Encoding.UTF8.GetByteCount(evidenceJson) > 32766)
        {
            throw new InvalidOperationException("The Slice C evidence path is unavailable.");
        }

        var fullPath = Path.GetFullPath(configuredPath);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var parent = Directory.GetParent(fullPath);
        if (!fullPath.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(fullPath), expectedFileName, StringComparison.Ordinal) ||
            parent is null ||
            !parent.Name.StartsWith("speechmessage-p7-2-slice-c-", StringComparison.Ordinal) ||
            !parent.Exists ||
            (parent.Attributes & FileAttributes.ReparsePoint) != 0 ||
            File.Exists(fullPath))
        {
            throw new InvalidOperationException("The Slice C evidence path is invalid.");
        }

        using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false, true));
        writer.Write(evidenceJson);
        writer.Write("\r\n");
    }

    /// <summary>
    /// 補齊因前段 no-go 而安全停止的 operation。固定五筆輸出讓 parser 可區分「未執行」與
    /// 「執行後失敗」，且不會誤稱五段都已送出；每筆未啟動項目都沒有 dispatch 或 CRM mutation。
    /// </summary>
    private static void CompleteNotStartedOperations(List<SliceCOperationEvidence> operations)
    {
        foreach (var operationId in OperationIds.Skip(operations.Count))
        {
            operations.Add(new SliceCOperationEvidence(
                operationId,
                "not-run",
                "prior-operation-no-go",
                false,
                "not-started",
                "not-started"));
        }
    }

    /// <summary>
    /// 解析 child-process 環境中由 PowerShell 放入的純 scalar fixture 值。
    /// 環境變數只在單一 child process 存活期間存在，與 parent 的 Credential Manager pointer、
    /// browser session、token 或其他 request state 不共用；所有 GUID 與日期均再次 fail-closed。
    /// </summary>
    private static SliceCFixture ReadFixture()
    {
        var owner = Environment.GetEnvironmentVariable("P7_2_SLICE_C_FIXTURE_OWNER");
        if (!string.Equals(Environment.GetEnvironmentVariable("P7_2_SLICE_C_FIXTURE_MARKER"), FixtureMarker, StringComparison.Ordinal) ||
            !IsSafeOwner(owner) ||
            !string.Equals(owner, WindowsIdentity.GetCurrent().Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The P7.2 Slice C fixture descriptor is invalid.");
        }

        var fixture = new SliceCFixture(
            ReadGuid("P7_2_SLICE_C_CONTACT_ID"),
            ReadGuid("P7_2_SLICE_C_ADD_LIST_ID"),
            ReadGuid("P7_2_SLICE_C_REMOVE_LIST_ID"),
            ReadGuid("P7_2_SLICE_C_SMALL_GROUP_LIST_ID"),
            ReadGuid("P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID"),
            ReadGuid("P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID"),
            ReadGuid("P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID"),
            ReadGuid("P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID"),
            ReadSundayUtc("P7_2_SLICE_C_TRANSFER_WEEK_START_UTC"));

        var listIds = new[]
        {
            fixture.AddListId,
            fixture.RemoveListId,
            fixture.SmallGroupListId,
            fixture.SmallGroupExpectedRelationshipListId,
            fixture.TransferSourceListId,
            fixture.TransferTargetListId
        };
        if (listIds.Distinct().Count() != listIds.Length)
        {
            throw new InvalidOperationException("The P7.2 Slice C fixture graph is invalid.");
        }

        // 專用 relationship list 只能提供 expected projection，不能重用成欲寫入的 small-group target list；
        // 否則 baseline 會被誤當成外部關係證據，所以在建立 CRM runtime 前 fail closed。
        if (fixture.SmallGroupExpectedRelationshipListId == fixture.SmallGroupListId)
        {
            throw new InvalidOperationException("The P7.2 Slice C fixture graph is invalid.");
        }

        return fixture;
    }

    /// <summary>
    /// 解析固定 D 格式的非空 GUID，不讓 CultureInfo、隱藏 trim 或部分字串匹配改變 fixture
    /// identity。錯誤訊息保持固定，避免把 caller supplied scalar 回寫到 TRX 或 console。
    /// </summary>
    private static Guid ReadGuid(string variableName)
        => Guid.TryParseExact(Environment.GetEnvironmentVariable(variableName), "D", out var value) && value != Guid.Empty
            ? value
            : throw new InvalidOperationException("The P7.2 Slice C fixture descriptor is invalid.");

    /// <summary>
    /// 解析 transfer 唯一允許的 UTC Sunday 00:00 key，避免 local timezone 或 DST 導致 weekly
    /// report query 指向不同週次。輸入必須為 round-trip ISO-8601 scalar，否則不執行 mutation。
    /// </summary>
    private static DateTimeOffset ReadSundayUtc(string variableName)
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
            throw new InvalidOperationException("The P7.2 Slice C fixture descriptor is invalid.");
        }

        return value;
    }

    /// <summary>
    /// 驗證 owner scalar 可安全與目前 Windows identity 比對；禁止換行與 NUL，避免透過 test
    /// output 或環境傳遞插入額外資料。identity 本身永遠不會序列化到 evidence。
    /// </summary>
    private static bool IsSafeOwner(string? owner)
        => !string.IsNullOrWhiteSpace(owner) &&
           owner.Length <= 256 &&
           owner.IndexOfAny(['\0', '\r', '\n']) < 0;

    /// <summary>
    /// 從既有 ChurchReport development configuration 建立不帶 reload watcher 的 snapshot。
    /// 禁用 reload 避免 evidence 期間保留 file watcher、subscription 或跨測試 mutable state；
    /// connector/profile/endpoint 仍完全由部署設定擁有，fixture 不能覆寫它們。
    /// </summary>
    internal static IConfiguration CreateDevelopmentConfiguration()
    {
        var root = FindRepositoryRoot();
        return new ConfigurationBuilder()
            .SetBasePath(Path.Combine(root, "SpeechMessageProducts.ChurchReport"))
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();
    }

    /// <summary>
    /// 由 deployment-owned configuration 驗證 Embedded、Data8、CE 9.1 profile，而非讓 test
    /// 透過 environment 指定 connector、endpoint、organization 或 CE version。任何 mapper
    /// 不一致都在建立 CRM client 前失敗，確保 CE 8.2 與 Official Worker 不會被選取或 fallback。
    /// </summary>
    internal static (
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

    /// <summary>
    /// 尋找目前 worktree root，而不接受 caller supplied repository path 或 endpoint。此方法只走
    /// AppContext 父目錄，找到兩個固定 project directory 才成功，避免在意外工作目錄讀取資料。
    /// </summary>
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

    /// <summary>
    /// 釋放 fixture store 及其唯一 Data8 service owner。即使 Dispose 失敗也不拋出原始例外，
    /// 因為它可能含 WCF endpoint 或 authentication detail；外層 evidence 以 cleanup-failure
    /// 記錄，並禁止把結果當作可用的 live evidence。
    /// </summary>
    private static void DisposeStore(
        ref P72Data8ListManagementFixtureStore? store,
        ref string outcome,
        ref string reason)
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
        }
        finally
        {
            store = null;
        }
    }

    /// <summary>
    /// 非同步釋放 generation-owned Embedded runtime，等待 pool drain 與 connector cleanup 完成。
    /// 不使用 fire-and-forget，以免 child process 結束後仍保有 lease、channel、permit 或 timer；
    /// 呼叫端只取得成功布林值，避免任何 raw cleanup detail 進入 evidence。
    /// </summary>
    private static async Task<bool> DisposeRuntimeAsync(EmbeddedData8Runtime? runtime)
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
    /// 釋放 logger factory 與其 provider/subscription。factory 只在此 child process 建立，沒有
    /// static cache 或跨 request session；無法釋放時將 evidence 降為 no-go，避免誤報無資源洩漏。
    /// </summary>
    private static void DisposeLogger(ref ILoggerFactory? loggerFactory, ref string outcome, ref string reason)
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
    /// 封閉的 Slice C fixture projection。所有 GUID 僅存在 child process 記憶體並傳給已驗證的
    /// bridge/store；record 不會記錄至 xUnit output，亦不會放進 cache、session 或 static state。
    /// </summary>
    /// <remarks>
    /// <c>SmallGroupExpectedRelationshipListId</c> 是 task-owned relationship list 的唯一識別；它只用於
    /// 建立 small-group expected projection，且必須與 <c>SmallGroupListId</c> 不同，避免跨 fixture 或使用者
    /// list 的資料進入此次 evidence。record 僅由 child process 持有，finally 後不寫入輸出或共享狀態。
    /// </remarks>
    private sealed record SliceCFixture(
        Guid ContactId,
        Guid AddListId,
        Guid RemoveListId,
        Guid SmallGroupListId,
        Guid SmallGroupTargetLeaderContactId,
        Guid SmallGroupExpectedRelationshipListId,
        Guid TransferSourceListId,
        Guid TransferTargetListId,
        DateTimeOffset TransferWeekStartUtc);

    /// <summary>
    /// temporary evidence 中每個 operation 的去識別化摘要。固定分類可讓 parent script 嚴格驗證一次
    /// dispatch、reconciliation 與 cleanup 結果，卻不攜帶 CRM ID、使用者、baseline 或例外。
    /// </summary>
    private sealed record SliceCOperationEvidence(
        string OperationId,
        string Outcome,
        string Reason,
        bool OperationExecuted,
        string ReconciliationState,
        string CleanupState);
}

/// <summary>
/// 為 Slice C 實機 evidence 提供顯式且 fail-closed 的 xUnit opt-in gate。
/// 預設測試探索與一般 CI 一律 Skip；必須由 PowerShell runner 在同一 child process 設定所有
/// scalar 才可能執行。attribute 不解析或記錄這些值，因此不會把 credential、fixture graph 或
/// operator identity 留在 xUnit metadata、session 或長壽命 static collection。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P72Data8SliceCLiveFactAttribute : FactAttribute
{
    private static readonly string[] RequiredVariables =
    [
        "SPEECHMESSAGE_P7_2_SLICE_C_LIVE",
        "CRM_PASSWORD",
        "P7_2_SLICE_C_FIXTURE_OWNER",
        "P7_2_SLICE_C_FIXTURE_MARKER",
        "P7_2_SLICE_C_CONTACT_ID",
        "P7_2_SLICE_C_ADD_LIST_ID",
        "P7_2_SLICE_C_REMOVE_LIST_ID",
        "P7_2_SLICE_C_SMALL_GROUP_LIST_ID",
        "P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID",
        "P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_WEEK_START_UTC",
        "P7_2_SLICE_C_EVIDENCE_PATH"
    ];

    /// <summary>
    /// 只在 runner 提供完整的短生命週期環境時開啟 live test。
    /// <c>SPEECHMESSAGE_P7_2_SLICE_C_LIVE</c> 必須精確為 <c>1</c>；缺少任一值時採用
    /// Skip 而非猜測 fixture 或嘗試 CE 8.2／Official Worker fallback，保護一般開發與 CI。
    /// </summary>
    public P72Data8SliceCLiveFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_LIVE"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE"), "1", StringComparison.Ordinal) ||
            !RequiredVariables.All(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "P7.2 Slice C live evidence requires an approved task-owned CE 9.1 Data8 fixture graph.";
        }
    }
}

/// <summary>
/// 為 Slice C 唯讀 reconciliation 提供與 write lane 完全分離的顯式 opt-in gate。此 attribute 只承認
/// parent runner 設定的 reconciliation flag 與固定 temporary evidence path；若 execute flag 同時存在，
/// 或任一共同 fixture scalar 缺失，測試一律 Skip。這是第二道防線，確保 child process 的環境殘留不會
/// 讓只讀 command 意外命中既有寫入測試。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P72Data8SliceCReconcileFactAttribute : FactAttribute
{
    private static readonly string[] RequiredVariables =
    [
        "SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE",
        "CRM_PASSWORD",
        "P7_2_SLICE_C_FIXTURE_OWNER",
        "P7_2_SLICE_C_FIXTURE_MARKER",
        "P7_2_SLICE_C_CONTACT_ID",
        "P7_2_SLICE_C_ADD_LIST_ID",
        "P7_2_SLICE_C_REMOVE_LIST_ID",
        "P7_2_SLICE_C_SMALL_GROUP_LIST_ID",
        "P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID",
        "P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_WEEK_START_UTC",
        "P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH"
    ];

    /// <summary>
    /// 只有 reconciliation runner 同時提供完整短生命週期 scalar 時才允許 xUnit 執行。attribute 不讀取、
    /// 記錄或序列化任何值；它僅檢查 presence 與兩個 mode flag 的互斥，使一般 discovery、CI 及 execute
    /// lane 都不會建立 runtime、接觸 Credential Manager 或保留 session state。
    /// </summary>
    public P72Data8SliceCReconcileFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_LIVE"), "1", StringComparison.Ordinal) ||
            !RequiredVariables.All(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "P7.2 Slice C reconciliation requires an approved task-owned CE 9.1 Data8 fixture graph.";
        }
    }
}

/// <summary>
/// Slice C relationship-list repair 的獨立 opt-in gate。
/// repair 不與 execute 或 reconciliation 共用 mode flag；只有 runner 明確設定 repair flag、
/// credential、fixture identities 與 bounded evidence path 時才啟用，避免一般測試探索意外修改 CRM。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P72Data8SliceCRepairFactAttribute : FactAttribute
{
    private static readonly string[] RequiredVariables =
    [
        "SPEECHMESSAGE_P7_2_SLICE_C_REPAIR",
        "CRM_PASSWORD",
        "P7_2_SLICE_C_FIXTURE_OWNER",
        "P7_2_SLICE_C_FIXTURE_MARKER",
        "P7_2_SLICE_C_CONTACT_ID",
        "P7_2_SLICE_C_ADD_LIST_ID",
        "P7_2_SLICE_C_REMOVE_LIST_ID",
        "P7_2_SLICE_C_SMALL_GROUP_LIST_ID",
        "P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID",
        "P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_WEEK_START_UTC",
        "P7_2_SLICE_C_REPAIR_EVIDENCE_PATH"
    ];

    /// <summary>
    /// 檢查 repair child 是否由受控 runner 啟動。缺少任一 bounded input 或同時啟用其他 lane
    /// 時直接 Skip，確保一般 `dotnet test` 不會建立 Data8 session 或觸發 CRM Update。
    /// </summary>
    public P72Data8SliceCRepairFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_REPAIR"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_LIVE"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE"), "1", StringComparison.Ordinal) ||
            !RequiredVariables.All(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "P7.2 Slice C repair requires an explicit task-owned CE 9.1 Data8 repair lane.";
        }
    }
}

/// <summary>
/// Slice C relationship-list repair precondition probe 的獨立 opt-in gate。
/// 它只允許 read-only Data8 proof，且與 execute、reconcile、repair 三個 lane 互斥；普通
/// test discovery 不會建立 CRM session，也不會觸發任何 mutation。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P72Data8SliceCRepairProbeFactAttribute : FactAttribute
{
    private static readonly string[] RequiredVariables =
    [
        "SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE",
        "CRM_PASSWORD",
        "P7_2_SLICE_C_FIXTURE_OWNER",
        "P7_2_SLICE_C_FIXTURE_MARKER",
        "P7_2_SLICE_C_CONTACT_ID",
        "P7_2_SLICE_C_ADD_LIST_ID",
        "P7_2_SLICE_C_REMOVE_LIST_ID",
        "P7_2_SLICE_C_SMALL_GROUP_LIST_ID",
        "P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID",
        "P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID",
        "P7_2_SLICE_C_TRANSFER_WEEK_START_UTC",
        "P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH"
    ];

    /// <summary>
    /// 只有 runner 明確設定 probe mode 且所有 bounded inputs 都存在時才啟用測試。
    /// 任一 mutation lane 同時存在都直接 Skip，避免 read-only 診斷意外與寫入共用 process。
    /// </summary>
    public P72Data8SliceCRepairProbeFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_LIVE"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_REPAIR"), "1", StringComparison.Ordinal) ||
            !RequiredVariables.All(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "P7.2 Slice C repair probe requires an explicit task-owned CE 9.1 Data8 read-only lane.";
        }
    }
}
