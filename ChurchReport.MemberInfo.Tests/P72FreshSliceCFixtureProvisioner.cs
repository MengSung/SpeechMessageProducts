// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureProvisioner.cs
// 用途：提供 P7.2 Slice C 專用的新建 fixture 控制面。它只存在於測試專案，將既有 stale
//       descriptor 視為不可變的讀取輸入，並在任何 CRM mutation 前以固定投影證明可重用的
//       operational lists、baseline owner 與 weekly report。
//
// 隔離與資源所有權：
// 1. provisioner 借用呼叫端擁有的 IOrganizationService；它絕不 Dispose、快取、static 保存或
//    將服務交給背景工作。Live child 的 fixture store/runtime 才是 WCF、channel、credential
//    與 lease 的唯一 owner，並在 child finally 中依反向建構順序釋放。
// 2. 所有 Entity、QueryExpression、lookup 與結果都只在同步方法 scope 中存在。沒有 user、
//    profile、tenant、credential、token、cookie、CRM response 或 exception detail 會跨 invocation
//    保存或輸出，因此不能讓 A/B 或跨 profile 的 session state 互相看見。
// 3. preflight 一律以 exact-ID Retrieve 或 TopCount=2 的固定條件查詢完成；它不接受 endpoint、
//    entity、欄位、FetchXML、connector、credential 或 organization 的 caller 選擇值。
// ============================================================================

using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 描述已由 PowerShell parent 驗證過本機 descriptor 與 Windows owner 後，才可交給 Slice C
/// fresh-fixture provisioner 的固定輸入。這不是產品 API：所有 GUID 都由 task-owned local
/// descriptor 與 child WhoAmI scope 導出，不能由 ChurchReport request、Gateway JSON 或瀏覽器
/// 呼叫端提供。<see cref="Nonce"/> 只用來建立本次新資料的唯一 marker，絕不進入 evidence、
/// log、TRX 或任何共享快取。
/// </summary>
/// <param name="AddListId">既有、已核准且必須保持 read-only 的 add-members static list。</param>
/// <param name="RemoveListId">新 source 必須加入的既有 remove-member static list。</param>
/// <param name="SmallGroupListId">既有小組欄位 operation 的 target static list。</param>
/// <param name="ExistingTargetLeaderContactId">用來精確導出 active baseline owner 的 task-marked leader。</param>
/// <param name="TransferSourceListId">新 source 必須加入的既有 transfer-source static list。</param>
/// <param name="TransferTargetListId">transfer target 與 weekly-report preflight 所使用的 static list。</param>
/// <param name="TransferWeekStartUtc">限定 weekly report 的 UTC Sunday 00:00 時間鍵。</param>
/// <param name="Data8ServiceUserId">同一 child 透過部署擁有 crm91/Data8 WhoAmI 驗證出的 user ID。</param>
/// <param name="Nonce">僅限本次 current-user child invocation 的不可預測 marker nonce。</param>
internal sealed record P72FreshSliceCFixtureProvisionRequest(
    Guid AddListId,
    Guid RemoveListId,
    Guid SmallGroupListId,
    Guid ExistingTargetLeaderContactId,
    Guid TransferSourceListId,
    Guid TransferTargetListId,
    DateTimeOffset TransferWeekStartUtc,
    Guid Data8ServiceUserId,
    Guid Nonce);

/// <summary>
/// 表示 provision child 每次已確認 remote mutation 後欲寫入 current-user local pending ledger
/// 的 immutable stage。此型別不會被序列化為 operator evidence；真正的 ledger writer 必須以
/// create-new/atomic-replace 寫入 parent 產生、非 reparse 的本機路徑，並在 cleanup 成功後刪除。
/// 若 mutation 具有 timeout/transport ambiguity，ledger 的唯一用途是日後 exact-ID reconciliation，
/// 而不是讓另一個使用者、profile 或 process 重用該 fixture。
/// </summary>
/// <param name="Stage">固定 allowlist stage 名稱，不允許呼叫端自訂 CRM 動作。</param>
/// <param name="Nonce">本次 invocation 的 nonce；只留在 current-user local ledger 供 ambiguity recovery 使用。</param>
/// <param name="SourceContactId">已被精確 read-back 的 fresh source contact；尚未建立時為 null。</param>
/// <param name="LeaderContactId">已被精確 read-back 的 fresh leader contact；尚未建立時為 null。</param>
/// <param name="RelationshipListId">已被精確 read-back 的 fresh relationship list；尚未建立時為 null。</param>
/// <param name="OriginalTargetLeaderContactId">在 descriptor 被 fresh leader 覆寫前，由 provision preflight 證明的 task-marked baseline leader。此值只保留在 current-user strict ledger；cleanup 以它重新驗證非服務帳號 owner，絕不從活動 descriptor、browser session 或 caller input 推導。</param>
internal sealed record P72FreshSliceCFixtureLedgerState(
    string Stage,
    Guid Nonce,
    Guid? SourceContactId,
    Guid? LeaderContactId,
    Guid? RelationshipListId,
    Guid OriginalTargetLeaderContactId);

/// <summary>
/// 定義 fresh fixture recovery ledger 的單一持久化邊界。實作不得把 state 放入 static collection、
/// production configuration、TRX 或 repository；它只能由同一 current-Windows-owner 的 child
/// request scope 使用，且每次寫入都要有明確的原子完成或 fail-closed 結果。
/// </summary>
internal interface IP72FreshSliceCFixtureLedger
{
    /// <summary>
    /// 持久化一個 immutable stage snapshot。呼叫端必須在任何下一次 remote mutation 前完成這一步；
    /// 若寫入失敗，provisioning 必須停止並保留 no-go，而不是繼續建立無法清理的 fixture graph。
    /// </summary>
    /// <param name="state">只含本次 task-owned fresh IDs 與固定 stage 的 local recovery state。</param>
    void Persist(P72FreshSliceCFixtureLedgerState state);
}

/// <summary>
/// 集中擁有 Slice C fresh fixture 所有可建立 CRM Entity 的固定 schema。這個類別只產生
/// method-local <see cref="Entity"/> 模板，不能執行 Create、選擇 endpoint、指定 connector，
/// 或保存任何 profile、credential、token、cookie、CRM response 與 mutable session state。
/// 每個 marker 皆由同一 child invocation 的 nonce 導出，供 exact-ID read-back 與 pending ledger
/// 交叉證明；它不會出現在 operator evidence、TRX、log 或共享快取中。
/// </summary>
internal static class P72FreshSliceCFixtureRequestTemplates
{
    /// <summary>CRM contact logical name 的唯一固定定義，避免控制面出現 caller-selectable entity。</summary>
    internal const string ContactEntityName = "contact";

    /// <summary>CRM list logical name 的唯一固定定義，避免 control plane 接受任意 relationship entity。</summary>
    internal const string ListEntityName = "list";

    private const string SourceLastNamePrefix = "P7.2-SC-SOURCE-";
    private const string LeaderLastNamePrefix = "P7.2-SC-LEADER-";
    private const string RelationshipListNamePrefix = "P7.2-SC-REL-";
    /// <summary>所有 fresh Slice C entity 共享的 marker 前綴；不可由 caller 覆寫。</summary>
    internal const string SliceCMarkerPrefix = "P7.2-SC-";

    /// <summary>fresh source contact 的唯一 provenance marker；不可套用到既有 CRM contact。</summary>
    internal const string SourceDescriptionMarker = "p7.2-contact-basic-info";

    /// <summary>fresh relationship list 必須完整持有的 deterministic area name。</summary>
    internal const string ExpectedRelationshipAreaName = "P7.2-SC-AREA-EXPECTED";
    private const string LastNameAttribute = "lastname";
    private const string DescriptionAttribute = "description";
    private const string ListNameAttribute = "listname";
    private const string ListTypeAttribute = "type";
    private const string ListCreatedFromCodeAttribute = "createdfromcode";
    private const string AreaLeaderAttribute = "new_contact_list_arealeader";
    private const string AreaNameAttribute = "new_area_name";
    private const string RaceLeaderAttribute = "new_contact_race_leager_list";
    private const int ContactListCreatedFromCodeValue = 2;

    /// <summary>
    /// 建立 fresh source contact 的最小、固定 CRM Entity 模板。此方法刻意不設定 owner、list、
    /// relationship 或任何 caller supplied field；owner assignment 與 membership 必須在後續已被
    /// test-first 指定的獨立 mutation stage 中，以完整 preflight、read-back 與 pending ledger
    /// 為前提執行。回傳的 Entity 僅屬於目前呼叫端 scope，呼叫端不得快取或跨使用者／profile 重用。
    /// </summary>
    /// <param name="nonce">parent 於同一 current-user child invocation 產生的非空唯一 nonce。</param>
    /// <returns>僅帶有 source marker 欄位的未持久化 contact Entity。</returns>
    /// <exception cref="ArgumentException">當 nonce 為空值時，拒絕產生可能與其他 run 混淆的 marker。</exception>
    internal static Entity CreateSourceContact(Guid nonce)
    {
        if (nonce == Guid.Empty)
        {
            throw new ArgumentException("The fresh-fixture nonce must be non-empty.", nameof(nonce));
        }

        return new Entity(ContactEntityName)
        {
            [LastNameAttribute] = string.Concat(SourceLastNamePrefix, nonce.ToString("N")),
            [DescriptionAttribute] = SourceDescriptionMarker
        };
    }

    /// <summary>
    /// 建立 fresh relationship leader 的最小、固定 CRM Entity 模板。CRM 會從 lastname 導出 fullname，
    /// 因此後續 exact read-back 只需要驗證 server-derived fullname 是否保有 <c>P7.2-SC-</c> 前綴。
    /// 模板刻意不含 owner、role、list 或任何既有 contact reference，避免將舊 fixture、另一位使用者
    /// 或別的 profile 的 identity 混入新 graph；回傳 Entity 仍只在目前呼叫 scope 存活。
    /// </summary>
    /// <param name="nonce">parent 於同一 current-user child invocation 產生的非空唯一 nonce。</param>
    /// <returns>僅帶有 leader task marker 的未持久化 contact Entity。</returns>
    /// <exception cref="ArgumentException">當 nonce 為空值時，拒絕產生無法 exact-ID 清理的 marker。</exception>
    internal static Entity CreateLeaderContact(Guid nonce)
    {
        if (nonce == Guid.Empty)
        {
            throw new ArgumentException("The fresh-fixture nonce must be non-empty.", nameof(nonce));
        }

        return new Entity(ContactEntityName)
        {
            [LastNameAttribute] = string.Concat(LeaderLastNamePrefix, nonce.ToString("N"))
        };
    }

    /// <summary>
    /// 建立 fresh expected-relationship list 的最小、固定 CRM Entity 模板。area leader、race leader
    /// 與 area name 必須同時完整存在，因為既有 Slice C bridge 會將它們視為單一 expected projection；
    /// 不允許建立 half-configured list 後再以 generic update 修補。<paramref name="leaderContactId"/>
    /// 僅能是剛由同一 provision child 建立並 exact read-back 的 leader ID，並不接受任何舊 descriptor
    /// relationship 或 list ID。模板本身沒有 I/O、快取、lease 或共享狀態。
    /// </summary>
    /// <param name="nonce">parent 於同一 current-user child invocation 產生的非空唯一 nonce。</param>
    /// <param name="leaderContactId">已 read-back 的 fresh leader contact ID。</param>
    /// <returns>帶有完整 fixed expected projection 的未持久化 static contact list Entity。</returns>
    /// <exception cref="ArgumentException">當 nonce 或 fresh leader ID 為空值時，拒絕建立不可證明的 graph。</exception>
    internal static Entity CreateRelationshipList(Guid nonce, Guid leaderContactId)
    {
        if (nonce == Guid.Empty)
        {
            throw new ArgumentException("The fresh-fixture nonce must be non-empty.", nameof(nonce));
        }

        if (leaderContactId == Guid.Empty)
        {
            throw new ArgumentException("The fresh leader contact ID must be non-empty.", nameof(leaderContactId));
        }

        var freshLeader = new EntityReference(ContactEntityName, leaderContactId);
        return new Entity(ListEntityName)
        {
            [ListNameAttribute] = string.Concat(RelationshipListNamePrefix, nonce.ToString("N")),
            [ListTypeAttribute] = false,
            [ListCreatedFromCodeAttribute] = new OptionSetValue(ContactListCreatedFromCodeValue),
            [AreaLeaderAttribute] = freshLeader,
            [AreaNameAttribute] = ExpectedRelationshipAreaName,
            [RaceLeaderAttribute] = freshLeader
        };
    }
}

/// <summary>
/// 回傳 provisioner 的 bounded 分類。它永遠不包含 endpoint、credential、token、cookie、CRM
/// Entity、baseline value、GUID 或原始例外；新 fixture IDs 只能留在 child 的 method-local scope
/// 與 current-user pending ledger，不能藉由此結果流到另一個 session 或產品層。
/// </summary>
/// <param name="Outcome">固定 `go` 或 `no-go` 分類。</param>
/// <param name="Reason">不洩漏遠端資料的固定 reason category。</param>
/// <param name="OperationExecuted">是否已可能發出任何 remote mutation。</param>
internal sealed record P72FreshSliceCFixtureProvisionResult(
    string Outcome,
    string Reason,
    bool OperationExecuted);

/// <summary>
/// 表示 explicit cleanup lane 的去識別化結果。只有完整 reverse-order cleanup 且每個 exact-ID
/// read-back absence proof 都成功時，<see cref="LedgerEligibleForRemoval"/> 才為真；parent 才能刪除
/// current-user pending ledger。結果不含 fresh IDs、CRM row、endpoint、credential 或原始例外。
/// </summary>
/// <param name="Outcome">固定 `go` 或 `no-go`。</param>
/// <param name="Reason">固定 cleanup outcome category。</param>
/// <param name="OperationExecuted">是否已可能發出任一 remove/delete mutation。</param>
/// <param name="LedgerEligibleForRemoval">是否可由 parent 在本機原子刪除 ledger。</param>
internal sealed record P72FreshSliceCFixtureCleanupResult(
    string Outcome,
    string Reason,
    bool OperationExecuted,
    bool LedgerEligibleForRemoval);

/// <summary>
/// 執行 Slice C fresh-fixture 的固定、一次性 provision sequence。它只在所有 exact-ID preflight、
/// active non-service owner 與 bounded weekly report 都通過後，依序建立 source、leader、relationship
/// list、兩筆 membership 與一次 owner assignment；每一個成功寫入都必須先 read-back，再將 immutable
/// stage 寫入 current-user pending ledger，才可開始下一次 mutation。任何 timeout、transport 例外、
/// ledger failure 或 read-back 不確定性都不重試、不自動刪除，並保留可供後續 exact-ID reconciliation 的
/// ledger state。
/// 呼叫端仍擁有 Data8 service 的 disposal，故本類別沒有 Dispose、cache、timer、thread、retry 或
/// background work，避免測試控制面引入 session、memory 或 resource leakage。
/// </summary>
internal sealed class P72FreshSliceCFixtureProvisioner
{
    private const string ListEntityName = P72FreshSliceCFixtureRequestTemplates.ListEntityName;
    private const string ContactEntityName = P72FreshSliceCFixtureRequestTemplates.ContactEntityName;
    private const string SystemUserEntityName = "systemuser";
    private const string WeeklyReportEntityName = "new_group_present_weekly_report";
    private const string ListMemberEntityName = "listmember";
    private const string PresentRecordEntityName = "new_present_record";
    private const string WeeklyReportIdAttribute = "new_group_present_weekly_reportid";
    private const string WeeklyReportListAttribute = "new_list_group_present_weekly_report";
    private const string WeeklyReportDateAttribute = "new_sunday_date";
    private const string PresentRecordWeeklyReportAttribute = "new_group_present_weekly_report_prese";
    private const string PresentRecordContactAttribute = "new_contact_new_present_record";
    private const string PresentRecordListAttribute = "new_list_new_present_record";
    private const string ContactLastNameAttribute = "lastname";
    private const string ContactDescriptionAttribute = "description";
    private const string ContactPrimaryListAttribute = "new_cell_list_contact";
    private const string ListNameAttribute = "listname";
    private const string ListTypeAttribute = "type";
    private const string ListCreatedFromCodeAttribute = "createdfromcode";
    private const string ContactFullNameAttribute = "fullname";
    private const string OwnerAttribute = "ownerid";
    private const string SystemUserDisabledAttribute = "isdisabled";
    private const string AreaLeaderAttribute = "new_contact_list_arealeader";
    private const string AreaNameAttribute = "new_area_name";
    private const string RaceLeaderAttribute = "new_contact_race_leager_list";
    private const int ContactListCreatedFromCodeValue = 2;
    private const string SliceCListMarkerPrefix = P72FreshSliceCFixtureRequestTemplates.SliceCMarkerPrefix;
    private const string FreshSourceMarkerPrefix = "P7.2-SC-SOURCE-";
    private const string FreshLeaderMarkerPrefix = "P7.2-SC-LEADER-";
    private const string FreshRelationshipListMarkerPrefix = "P7.2-SC-REL-";

    private readonly IOrganizationService _service;

    /// <summary>
    /// 建立一個只借用呼叫端 service 的 preflight owner。<paramref name="service"/> 必須由同一
    /// Live child 的 fixture store/runtime 擁有及 Dispose；本類別不能自行關閉它，否則會破壞既有
    /// reverse-disposal order，並可能讓後續 cleanup 在已關閉的 WCF channel 上錯誤執行。
    /// </summary>
    /// <param name="service">已綁定 immutable crm91/Data8 profile generation 的 request-scoped service。</param>
    internal P72FreshSliceCFixtureProvisioner(IOrganizationService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>
    /// 以固定 allowlist 建立並證明完整 fresh fixture graph。任何 stale source、stale expected
    /// relationship list、既有 owner 或 descriptor scalar 都不會傳給 mutation API；只有五個已證明的
    /// operational lists 與既有 task-marked leader 可作為讀取 preflight。結果不含 CRM ID、endpoint、
    /// credential、token 或 raw exception，因此不能成為跨使用者、跨 profile 或跨 process 的 state carrier。
    /// </summary>
    /// <param name="request">由 parent/child 驗證後提供的固定 descriptor 與 WhoAmI input。</param>
    /// <param name="ledger">本次 current-user recovery ledger；preflight 失敗時不得寫入。</param>
    /// <returns>不含 CRM identity 的 bounded success/no-go 分類。</returns>
    internal P72FreshSliceCFixtureProvisionResult Provision(
        P72FreshSliceCFixtureProvisionRequest request,
        IP72FreshSliceCFixtureLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ledger);
        var operationExecuted = false;
        Guid? sourceContactId = null;
        Guid? leaderContactId = null;
        Guid? relationshipListId = null;

        try
        {
            if (!IsRequestShapeValid(request) ||
                !AreOperationalListsTaskOwned(request) ||
                !TryResolveActiveBaselineOwner(request.ExistingTargetLeaderContactId, out var baselineOwnerId))
            {
                return NoGo("fixture-precondition-failed");
            }

            if (baselineOwnerId == request.Data8ServiceUserId)
            {
                // owner evidence 需要從 baseline owner 變更到 WhoAmI service user 再還原；若兩者相同，
                // Create 後即使執行 Assign 也沒有可證明的行為差異。此時必須在所有寫入前停止。
                return NoGo("baseline-owner-unavailable");
            }

            if (!TryResolveExactlyOneActiveWeeklyReport(
                    request.TransferTargetListId,
                    request.TransferWeekStartUtc,
                    out var weeklyReportId))
            {
                return NoGo("fixture-precondition-failed");
            }

            // 在第一次 Create 前保存 pending state；若 transport 在回傳 ID 前中斷，ledger 仍保有 nonce
            // 與 stage，可讓後續人工／唯讀 reconciliation 辨識本次 run，而不誤用 stale fixture。
            PersistLedger(ledger, "preflight-proven", request, request.ExistingTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            operationExecuted = true;
            sourceContactId = _service.Create(P72FreshSliceCFixtureRequestTemplates.CreateSourceContact(request.Nonce));
            if (!IsFreshSourceContact(sourceContactId.Value))
            {
                return NoGo("fresh-source-readback-failed", true);
            }

            PersistLedger(ledger, "source-contact-created", request, request.ExistingTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            leaderContactId = _service.Create(P72FreshSliceCFixtureRequestTemplates.CreateLeaderContact(request.Nonce));
            if (!IsFreshLeaderContact(leaderContactId.Value))
            {
                return NoGo("fresh-leader-readback-failed", true);
            }

            PersistLedger(ledger, "leader-contact-created", request, request.ExistingTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            relationshipListId = _service.Create(P72FreshSliceCFixtureRequestTemplates.CreateRelationshipList(request.Nonce, leaderContactId.Value));
            if (!IsFreshRelationshipList(relationshipListId.Value, leaderContactId.Value))
            {
                return NoGo("fresh-relationship-readback-failed", true);
            }

            PersistLedger(ledger, "relationship-list-created", request, request.ExistingTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            _service.Execute(new AddListMembersListRequest
            {
                ListId = request.RemoveListId,
                MemberIds = [sourceContactId.Value]
            });
            if (!HasExactMembership(request.RemoveListId, sourceContactId.Value))
            {
                return NoGo("remove-membership-readback-failed", true);
            }

            PersistLedger(ledger, "remove-membership-added", request, request.ExistingTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            _service.Execute(new AddListMembersListRequest
            {
                ListId = request.TransferSourceListId,
                MemberIds = [sourceContactId.Value]
            });
            if (!HasExactMembership(request.TransferSourceListId, sourceContactId.Value))
            {
                return NoGo("transfer-source-membership-readback-failed", true);
            }

            PersistLedger(ledger, "transfer-source-membership-added", request, request.ExistingTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            _service.Execute(new AssignRequest
            {
                Target = new EntityReference(ContactEntityName, sourceContactId.Value),
                Assignee = new EntityReference(SystemUserEntityName, baselineOwnerId)
            });
            if (!HasExactOwner(sourceContactId.Value, baselineOwnerId))
            {
                return NoGo("baseline-owner-readback-failed", true);
            }

            PersistLedger(ledger, "baseline-owner-assigned", request, request.ExistingTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            if (!IsFreshGraphProven(
                    request,
                    sourceContactId.Value,
                    leaderContactId.Value,
                    relationshipListId.Value,
                    weeklyReportId,
                    baselineOwnerId))
            {
                return NoGo("fresh-graph-unproven", true);
            }

            PersistLedger(ledger, "fresh-graph-proven", request, request.ExistingTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);
            return new P72FreshSliceCFixtureProvisionResult("go", "fresh-fixture-provisioned", true);
        }
        catch (Exception)
        {
            // CRM/WCF/projection exception 可能含 endpoint 或 authentication 細節，且 mutation 開始後可能是
            // timeout-after-dispatch。任何此類例外一律不重試；已存在的 pending ledger 是唯一 recovery input。
            return operationExecuted
                ? NoGo("provisioning-ambiguous", true)
                : NoGo("fixture-precondition-failed");
        }
    }

    /// <summary>
    /// 執行獨立、explicit 的 fresh-fixture cleanup。它只接受同一 nonce 且 stage 已達
    /// `fresh-graph-proven` 的 ledger；先重做 source/leader/relationship provenance 與 membership
    /// proof，再依建立相反順序移除 transfer-source、remove membership，刪除 relationship list、source、
    /// leader。每個 mutation 後都用固定 exact-ID query 證明已不存在，任何不確定性都停止、保留 ledger、
    /// 不重試，且不會碰觸 stale descriptor rows。這是 test-only control plane，不會由 ChurchReport、
    /// Gateway JSON、browser 或 request-time connector routing 呼叫。
    /// </summary>
    /// <param name="request">原始 provision request；其 list IDs 與 nonce 必須與 ledger 完全一致。</param>
    /// <param name="ledgerState">current-user local ledger 的 final graph snapshot。</param>
    /// <param name="ledger">同一 owner 綁定的 pending ledger writer；每一個 cleanup stage 都原子持久化。</param>
    /// <returns>只有全部 remote absence proofs 通過時才回傳可刪除 ledger 的 go。</returns>
    internal P72FreshSliceCFixtureCleanupResult Cleanup(
        P72FreshSliceCFixtureProvisionRequest request,
        P72FreshSliceCFixtureLedgerState ledgerState,
        IP72FreshSliceCFixtureLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ledgerState);
        ArgumentNullException.ThrowIfNull(ledger);
        var operationExecuted = false;

        try
        {
            if (ledgerState.Stage != "fresh-graph-proven" ||
                ledgerState.Nonce != request.Nonce ||
                ledgerState.SourceContactId is not Guid sourceContactId ||
                ledgerState.LeaderContactId is not Guid leaderContactId ||
                ledgerState.RelationshipListId is not Guid relationshipListId ||
                ledgerState.OriginalTargetLeaderContactId == Guid.Empty ||
                sourceContactId == Guid.Empty ||
                leaderContactId == Guid.Empty ||
                relationshipListId == Guid.Empty ||
                ledgerState.OriginalTargetLeaderContactId == leaderContactId ||
                !IsFreshSourceContact(sourceContactId) ||
                !IsFreshLeaderContact(leaderContactId) ||
                !IsFreshRelationshipList(relationshipListId, leaderContactId) ||
                // cleanup 的 baseline provenance 只能讀取 strict ledger。request 仍可能攜帶已發布
                // descriptor 的 fresh leader，但它永遠不是 owner scope 的授權來源。
                !TryResolveActiveBaselineOwner(ledgerState.OriginalTargetLeaderContactId, out var baselineOwnerId) ||
                baselineOwnerId == request.Data8ServiceUserId ||
                !HasExactOwner(sourceContactId, baselineOwnerId) ||
                !HasExactMembership(request.RemoveListId, sourceContactId) ||
                !HasExactMembership(request.TransferSourceListId, sourceContactId) ||
                HasExactMembership(request.AddListId, sourceContactId) ||
                HasExactMembership(request.TransferTargetListId, sourceContactId))
            {
                return CleanupNoGo("cleanup-precondition-failed");
            }

            PersistLedger(ledger, "cleanup-preflight-proven", request, ledgerState.OriginalTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            // 將 cleanup lane 標記為已開始，再送出第一個遠端要求。若 transport 在
            // dispatch 後拋出例外，catch 必須回報 cleanup-ambiguous，而不是把未確認
            // 的遠端狀態誤判成尚未執行，否則父控制面可能錯誤重試或刪除 ledger。
            operationExecuted = true;
            _service.Execute(new RemoveMemberListRequest
            {
                ListId = request.TransferSourceListId,
                EntityId = sourceContactId
            });
            if (HasExactMembership(request.TransferSourceListId, sourceContactId))
            {
                return CleanupNoGo("cleanup-membership-readback-failed", true);
            }

            PersistLedger(ledger, "cleanup-transfer-source-membership-removed", request, ledgerState.OriginalTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            _service.Execute(new RemoveMemberListRequest
            {
                ListId = request.RemoveListId,
                EntityId = sourceContactId
            });
            if (HasExactMembership(request.RemoveListId, sourceContactId))
            {
                return CleanupNoGo("cleanup-membership-readback-failed", true);
            }

            PersistLedger(ledger, "cleanup-remove-membership-removed", request, ledgerState.OriginalTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            _service.Delete(ListEntityName, relationshipListId);
            if (!IsEntityAbsent(ListEntityName, "listid", relationshipListId))
            {
                return CleanupNoGo("cleanup-relationship-readback-failed", true);
            }

            PersistLedger(ledger, "cleanup-relationship-list-deleted", request, ledgerState.OriginalTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            _service.Delete(ContactEntityName, sourceContactId);
            if (!IsEntityAbsent(ContactEntityName, "contactid", sourceContactId))
            {
                return CleanupNoGo("cleanup-source-readback-failed", true);
            }

            PersistLedger(ledger, "cleanup-source-contact-deleted", request, ledgerState.OriginalTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);

            _service.Delete(ContactEntityName, leaderContactId);
            if (!IsEntityAbsent(ContactEntityName, "contactid", leaderContactId))
            {
                return CleanupNoGo("cleanup-leader-readback-failed", true);
            }

            PersistLedger(ledger, "cleanup-leader-contact-deleted", request, ledgerState.OriginalTargetLeaderContactId, sourceContactId, leaderContactId, relationshipListId);
            return new P72FreshSliceCFixtureCleanupResult("go", "fresh-fixture-cleaned", true, true);
        }
        catch (Exception)
        {
            // Remove/Delete transport uncertainty is never retried. Parent must retain the ledger and use a
            // separate exact-ID reconciliation lane; cleanup cannot silently report success or delete stale rows.
            return CleanupNoGo(operationExecuted ? "cleanup-ambiguous" : "cleanup-precondition-failed", operationExecuted);
        }
    }

    /// <summary>
    /// 驗證所有 GUID、nonce 與 UTC week key 的 local shape，先於第一個 CRM Retrieve 執行。此步只
    /// 處理 method-local scalar，不會配置 connector、保留 session 或根據 caller 值選擇任何 transport。
    /// </summary>
    /// <param name="request">待驗證的 fixed fixture input。</param>
    /// <returns>shape 完整且五個 operational lists 彼此不同時為 <see langword="true"/>。</returns>
    private static bool IsRequestShapeValid(P72FreshSliceCFixtureProvisionRequest request)
    {
        var listIds = new[]
        {
            request.AddListId,
            request.RemoveListId,
            request.SmallGroupListId,
            request.TransferSourceListId,
            request.TransferTargetListId
        };
        return request.ExistingTargetLeaderContactId != Guid.Empty &&
               request.Data8ServiceUserId != Guid.Empty &&
               request.Nonce != Guid.Empty &&
               listIds.All(static id => id != Guid.Empty) &&
               listIds.Distinct().Count() == listIds.Length &&
               request.TransferWeekStartUtc.Offset == TimeSpan.Zero &&
               request.TransferWeekStartUtc.TimeOfDay == TimeSpan.Zero &&
               request.TransferWeekStartUtc.DayOfWeek == DayOfWeek.Sunday;
    }

    /// <summary>
    /// 以五次 exact-ID Retrieve 證明可重用 operational list 均為 task-marked static contact list。
    /// 順序與結果不會被快取；每個 invocation 都重新讀取，使 profile、generation 或使用者切換後
    /// 無法重用前次 CRM state。這是一次性 operator 成本，不會進入產品正常請求熱路徑。
    /// </summary>
    /// <param name="request">只包含五個 descriptor-bound operational list IDs 的 input。</param>
    /// <returns>五個 lists 均通過固定投影時為 <see langword="true"/>。</returns>
    private bool AreOperationalListsTaskOwned(P72FreshSliceCFixtureProvisionRequest request)
        => IsTaskOwnedStaticList(request.AddListId) &&
           IsTaskOwnedStaticList(request.RemoveListId) &&
           IsTaskOwnedStaticList(request.SmallGroupListId) &&
           IsTaskOwnedStaticList(request.TransferSourceListId) &&
           IsTaskOwnedStaticList(request.TransferTargetListId);

    /// <summary>
    /// 直接投影既有 task-marked leader 的 owner，並確認該 owner 是 active system user。這個 leader
    /// 是唯一允許的 baseline-owner source，故不會掃描 systemuser、接受 caller-provided owner，或從
    /// stale source contact 推測權限。失敗時輸出 false，不讓任何 subsequent mutation 開始。
    /// </summary>
    /// <param name="leaderContactId">descriptor-bound task-marked leader contact ID。</param>
    /// <param name="baselineOwnerId">成功時為 active system user ID；失敗時為 empty GUID。</param>
    /// <returns>leader marker、owner reference 與 active user 都可精確證明時為 <see langword="true"/>。</returns>
    private bool TryResolveActiveBaselineOwner(Guid leaderContactId, out Guid baselineOwnerId)
    {
        baselineOwnerId = Guid.Empty;
        var leader = _service.Retrieve(
            ContactEntityName,
            leaderContactId,
            new ColumnSet(ContactFullNameAttribute, OwnerAttribute));
        if (leader is null ||
            !string.Equals(leader.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
            leader.Id != leaderContactId ||
            !leader.Attributes.TryGetValue(ContactFullNameAttribute, out var nameValue) ||
            nameValue is not string fullName ||
            !fullName.StartsWith(SliceCListMarkerPrefix, StringComparison.Ordinal) ||
            !leader.Attributes.TryGetValue(OwnerAttribute, out var ownerValue) ||
            ownerValue is not EntityReference { LogicalName: SystemUserEntityName, Id: var ownerId } ||
            ownerId == Guid.Empty)
        {
            return false;
        }

        var owner = _service.Retrieve(
            SystemUserEntityName,
            ownerId,
            new ColumnSet(SystemUserDisabledAttribute));
        if (owner is null ||
            !string.Equals(owner.LogicalName, SystemUserEntityName, StringComparison.Ordinal) ||
            owner.Id != ownerId ||
            !owner.Attributes.TryGetValue(SystemUserDisabledAttribute, out var disabledValue) ||
            disabledValue is not bool isDisabled ||
            isDisabled)
        {
            return false;
        }

        baselineOwnerId = ownerId;
        return true;
    }

    /// <summary>
    /// 以 exact target-list/date 條件與 TopCount=2 解析唯一 active weekly report。report ID 只在目前
    /// invocation 的 method-local scalar 中保留，供 final present-record proof 使用；它不會進入 result、
    /// evidence、log 或跨 profile 快取。零筆、多筆、paging、錯 logical name 或空 ID 均 fail closed。
    /// </summary>
    /// <param name="targetListId">已證明為 task-owned static transfer target list 的 ID。</param>
    /// <param name="weekStartUtc">已驗證的 UTC Sunday 00:00 time key。</param>
    /// <param name="weeklyReportId">成功時的 exact report ID；失敗時為 empty GUID。</param>
    /// <returns>恰好一筆 active weekly report 且投影 identity 合法時為 <see langword="true"/>。</returns>
    private bool TryResolveExactlyOneActiveWeeklyReport(
        Guid targetListId,
        DateTimeOffset weekStartUtc,
        out Guid weeklyReportId)
    {
        weeklyReportId = Guid.Empty;
        var query = new QueryExpression(WeeklyReportEntityName)
        {
            ColumnSet = new ColumnSet(WeeklyReportIdAttribute),
            NoLock = true,
            TopCount = 2
        };
        query.Criteria.AddCondition(WeeklyReportListAttribute, ConditionOperator.Equal, targetListId);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition(WeeklyReportDateAttribute, ConditionOperator.Equal, weekStartUtc.UtcDateTime);
        var rows = _service.RetrieveMultiple(query);
        if (rows is null ||
            rows.MoreRecords ||
            rows.Entities.Count != 1 ||
            !string.Equals(rows.Entities[0].LogicalName, WeeklyReportEntityName, StringComparison.Ordinal) ||
            rows.Entities[0].Id == Guid.Empty)
        {
            return false;
        }

        weeklyReportId = rows.Entities[0].Id;
        return true;
    }

    /// <summary>
    /// 讀回剛建立的 source contact，確認它是此 control plane 的 fresh marker，而不是 stale descriptor
    /// contact。這個 check 只接受 server-returned exact ID，沒有名稱搜尋、query fallback 或重新 Create；
    /// 因此 transport 不確定性不會被誤當成可安全重試。
    /// </summary>
    /// <param name="sourceContactId">Create 成功回傳的 fresh source contact ID。</param>
    /// <returns>source identity、lastname prefix 與 description marker 都相符時為 <see langword="true"/>。</returns>
    private bool IsFreshSourceContact(Guid sourceContactId)
    {
        if (sourceContactId == Guid.Empty)
        {
            return false;
        }

        var source = _service.Retrieve(
            ContactEntityName,
            sourceContactId,
            new ColumnSet(ContactLastNameAttribute, ContactDescriptionAttribute));
        return source is not null &&
               string.Equals(source.LogicalName, ContactEntityName, StringComparison.Ordinal) &&
               source.Id == sourceContactId &&
               source.Attributes.TryGetValue(ContactLastNameAttribute, out var lastNameValue) &&
               lastNameValue is string lastName &&
               lastName.StartsWith(FreshSourceMarkerPrefix, StringComparison.Ordinal) &&
               source.Attributes.TryGetValue(ContactDescriptionAttribute, out var descriptionValue) &&
               descriptionValue is string description &&
               string.Equals(description, P72FreshSliceCFixtureRequestTemplates.SourceDescriptionMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// 讀回剛建立的 leader contact，驗證 CRM server-derived fullname 仍保有本次 fresh leader marker。
    /// 只接受 Create 回傳的 exact ID，避免舊 relationship leader、使用者輸入姓名或全域 contact 搜尋被
    /// 當成 fixture graph 的安全基準。
    /// </summary>
    /// <param name="leaderContactId">Create 成功回傳的 fresh leader contact ID。</param>
    /// <returns>exact contact identity 與 server-derived fullname marker 相符時為 <see langword="true"/>。</returns>
    private bool IsFreshLeaderContact(Guid leaderContactId)
    {
        if (leaderContactId == Guid.Empty)
        {
            return false;
        }

        var leader = _service.Retrieve(
            ContactEntityName,
            leaderContactId,
            new ColumnSet(ContactFullNameAttribute));
        return leader is not null &&
               string.Equals(leader.LogicalName, ContactEntityName, StringComparison.Ordinal) &&
               leader.Id == leaderContactId &&
               leader.Attributes.TryGetValue(ContactFullNameAttribute, out var fullNameValue) &&
               fullNameValue is string fullName &&
               fullName.StartsWith(FreshLeaderMarkerPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// 讀回剛建立的 expected-relationship list，確認 static/contact-list schema 與 area/race leader
    /// projection 同時完整指向 fresh leader。這取代對 stale relationship list 的 repair；任一欄缺失、
    /// 型別不符或只填一半都不會進入 descriptor publication 或 operation dispatch。
    /// </summary>
    /// <param name="relationshipListId">Create 成功回傳的 fresh relationship list ID。</param>
    /// <param name="leaderContactId">已 read-back 的 fresh leader ID。</param>
    /// <returns>完整固定 relationship projection 已被 exact read-back 時為 <see langword="true"/>。</returns>
    private bool IsFreshRelationshipList(Guid relationshipListId, Guid leaderContactId)
    {
        if (relationshipListId == Guid.Empty || leaderContactId == Guid.Empty)
        {
            return false;
        }

        var relationshipList = _service.Retrieve(
            ListEntityName,
            relationshipListId,
            new ColumnSet(
                ListNameAttribute,
                ListTypeAttribute,
                ListCreatedFromCodeAttribute,
                AreaLeaderAttribute,
                AreaNameAttribute,
                RaceLeaderAttribute));
        return relationshipList is not null &&
               string.Equals(relationshipList.LogicalName, ListEntityName, StringComparison.Ordinal) &&
               relationshipList.Id == relationshipListId &&
               relationshipList.Attributes.TryGetValue(ListNameAttribute, out var nameValue) &&
               nameValue is string name &&
               name.StartsWith(FreshRelationshipListMarkerPrefix, StringComparison.Ordinal) &&
               relationshipList.Attributes.TryGetValue(ListTypeAttribute, out var typeValue) &&
               typeValue is bool isDynamic &&
               !isDynamic &&
               relationshipList.Attributes.TryGetValue(ListCreatedFromCodeAttribute, out var createdFromCodeValue) &&
               createdFromCodeValue is OptionSetValue { Value: ContactListCreatedFromCodeValue } &&
               HasExactContactReference(relationshipList, AreaLeaderAttribute, leaderContactId) &&
               relationshipList.Attributes.TryGetValue(AreaNameAttribute, out var areaNameValue) &&
               areaNameValue is string areaName &&
               string.Equals(areaName, P72FreshSliceCFixtureRequestTemplates.ExpectedRelationshipAreaName, StringComparison.Ordinal) &&
               HasExactContactReference(relationshipList, RaceLeaderAttribute, leaderContactId);
    }

    /// <summary>
    /// 以 exact list/contact equality query 讀回單一 membership。回傳 false 代表不存在或投影不可信；
    /// 因為呼叫端會將兩者皆視為 no-go，任一多筆、paging、錯 lookup 或跨 list row 都不可能被誤判為
    /// 成功。這個 one-row read 是一次性 fixture cost，不會進入產品熱路徑。
    /// </summary>
    /// <param name="listId">已預先證明 task-owned 的 operational list ID。</param>
    /// <param name="contactId">此 invocation 建立並讀回的 fresh source ID。</param>
    /// <returns>剛好一筆且 lookup identity 完全相符時為 <see langword="true"/>。</returns>
    private bool HasExactMembership(Guid listId, Guid contactId)
    {
        if (listId == Guid.Empty || contactId == Guid.Empty)
        {
            return false;
        }

        var query = new QueryExpression(ListMemberEntityName)
        {
            ColumnSet = new ColumnSet("listid", "entityid"),
            NoLock = true,
            TopCount = 2
        };
        query.Criteria.AddCondition("listid", ConditionOperator.Equal, listId);
        query.Criteria.AddCondition("entityid", ConditionOperator.Equal, contactId);
        var rows = _service.RetrieveMultiple(query);
        return rows is not null &&
               !rows.MoreRecords &&
               rows.Entities.Count == 1 &&
               string.Equals(rows.Entities[0].LogicalName, ListMemberEntityName, StringComparison.Ordinal) &&
               rows.Entities[0].Id != Guid.Empty &&
               HasExactReference(rows.Entities[0], "listid", ListEntityName, listId) &&
               HasExactReference(rows.Entities[0], "entityid", ContactEntityName, contactId);
    }

    /// <summary>
    /// 讀回 fresh source owner，只接受 exact systemuser lookup。team、null、錯 logical name 或新 service
    /// user 皆會 fail closed；這確保對 baseline owner 的唯一 Assign 是可觀察且不會把 service identity
    /// 當作跨 profile 的共享 owner。
    /// </summary>
    /// <param name="sourceContactId">fresh source contact ID。</param>
    /// <param name="expectedOwnerId">preflight 從 task-marked existing leader 導出的 active owner ID。</param>
    /// <returns>owner lookup 完全相符時為 <see langword="true"/>。</returns>
    private bool HasExactOwner(Guid sourceContactId, Guid expectedOwnerId)
    {
        if (sourceContactId == Guid.Empty || expectedOwnerId == Guid.Empty)
        {
            return false;
        }

        var source = _service.Retrieve(ContactEntityName, sourceContactId, new ColumnSet(OwnerAttribute));
        return source is not null &&
               string.Equals(source.LogicalName, ContactEntityName, StringComparison.Ordinal) &&
               source.Id == sourceContactId &&
               HasExactReference(source, OwnerAttribute, SystemUserEntityName, expectedOwnerId);
    }

    /// <summary>
    /// 完整重讀 fresh graph 的五個 Slice C 執行基線。final proof 不能信任先前 read-back 或 local descriptor：
    /// 它再次確認 source/leader/relationship provenance、四個 membership 期望、owner、primary-list 以及
    /// matching present-record 缺席，才允許 child 對 parent 回傳 `go`。所有讀取都以 exact IDs/TopCount=2
    /// 執行，沒有 cache、全組織搜尋或跨 invocation retained Entity。
    /// </summary>
    /// <param name="request">固定 operational list/date/profile-bound input。</param>
    /// <param name="sourceContactId">fresh source contact ID。</param>
    /// <param name="leaderContactId">fresh leader contact ID。</param>
    /// <param name="relationshipListId">fresh expected-relationship list ID。</param>
    /// <param name="weeklyReportId">preflight 唯一解析的 transfer weekly report ID。</param>
    /// <param name="baselineOwnerId">active non-service baseline owner ID。</param>
    /// <returns>所有 final graph invariant 都保持成立時為 <see langword="true"/>。</returns>
    private bool IsFreshGraphProven(
        P72FreshSliceCFixtureProvisionRequest request,
        Guid sourceContactId,
        Guid leaderContactId,
        Guid relationshipListId,
        Guid weeklyReportId,
        Guid baselineOwnerId)
    {
        return IsFreshSourceContact(sourceContactId) &&
               IsFreshLeaderContact(leaderContactId) &&
               IsFreshRelationshipList(relationshipListId, leaderContactId) &&
               !HasExactMembership(request.AddListId, sourceContactId) &&
               HasExactMembership(request.RemoveListId, sourceContactId) &&
               HasExactMembership(request.TransferSourceListId, sourceContactId) &&
               !HasExactMembership(request.TransferTargetListId, sourceContactId) &&
               HasExactOwner(sourceContactId, baselineOwnerId) &&
               HasNonTransferPrimaryList(sourceContactId, request.TransferTargetListId) &&
               HasNoMatchingPresentRecord(weeklyReportId, sourceContactId, request.TransferTargetListId, request.TransferWeekStartUtc);
    }

    /// <summary>
    /// 驗證 source 的 primary-list lookup 不是 transfer target。null 是合法的 fresh-source 初始值；若
    /// 有值則必須是 list reference 且 ID 不等於 target。這避免未來 transfer evidence 把 source 初始
    /// 狀態誤認成已完成 target assignment。
    /// </summary>
    /// <param name="sourceContactId">fresh source contact ID。</param>
    /// <param name="transferTargetListId">既有 task-owned transfer target list ID。</param>
    /// <returns>primary list 為 null 或明確非 target list 時為 <see langword="true"/>。</returns>
    private bool HasNonTransferPrimaryList(Guid sourceContactId, Guid transferTargetListId)
    {
        var source = _service.Retrieve(ContactEntityName, sourceContactId, new ColumnSet(ContactPrimaryListAttribute));
        if (source is null ||
            !string.Equals(source.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
            source.Id != sourceContactId ||
            !source.Attributes.TryGetValue(ContactPrimaryListAttribute, out var primaryListValue) ||
            primaryListValue is null)
        {
            return source is not null &&
                   string.Equals(source.LogicalName, ContactEntityName, StringComparison.Ordinal) &&
                   source.Id == sourceContactId;
        }

        return primaryListValue is EntityReference { LogicalName: ListEntityName, Id: var primaryListId } &&
               primaryListId != Guid.Empty &&
               primaryListId != transferTargetListId;
    }

    /// <summary>
    /// 以 weekly report、source、target list 與 UTC Sunday 四個 fixed filters 確認沒有 matching present
    /// record。這不是泛用 attendance 查詢；結果只允許零列，任何資料列、paging 或 transport failure 都
    /// 使 provision result 保持 no-go，避免預先存在的 attendance state 被本次 fixture 接管。
    /// </summary>
    /// <param name="weeklyReportId">preflight 唯一解析的 weekly report ID。</param>
    /// <param name="sourceContactId">fresh source contact ID。</param>
    /// <param name="transferTargetListId">既有 transfer target list ID。</param>
    /// <param name="weekStartUtc">固定 UTC Sunday date key。</param>
    /// <returns>response 完整且沒有 matching record 時為 <see langword="true"/>。</returns>
    private bool HasNoMatchingPresentRecord(
        Guid weeklyReportId,
        Guid sourceContactId,
        Guid transferTargetListId,
        DateTimeOffset weekStartUtc)
    {
        if (weeklyReportId == Guid.Empty || sourceContactId == Guid.Empty || transferTargetListId == Guid.Empty)
        {
            return false;
        }

        var query = new QueryExpression(PresentRecordEntityName)
        {
            ColumnSet = new ColumnSet("new_present_recordid"),
            NoLock = true,
            TopCount = 2
        };
        query.Criteria.AddCondition(PresentRecordWeeklyReportAttribute, ConditionOperator.Equal, weeklyReportId);
        query.Criteria.AddCondition(PresentRecordContactAttribute, ConditionOperator.Equal, sourceContactId);
        query.Criteria.AddCondition(PresentRecordListAttribute, ConditionOperator.Equal, transferTargetListId);
        query.Criteria.AddCondition(WeeklyReportDateAttribute, ConditionOperator.Equal, weekStartUtc.UtcDateTime);
        var rows = _service.RetrieveMultiple(query);
        return rows is not null && !rows.MoreRecords && rows.Entities.Count == 0;
    }

    /// <summary>
    /// 將已 read-back 的 local-only recovery state 交給 caller-owned ledger。stage 字串只由此類別的
    /// private allowlist 提供，nonce/IDs 只留在 current-user storage；若 writer 拋出，外層會以
    /// `provisioning-ambiguous` 停止，絕不在沒有 recovery evidence 的情況下繼續下一個 CRM mutation。
    /// </summary>
    /// <param name="ledger">parent 建立且綁定 current Windows owner 的 pending ledger。</param>
    /// <param name="stage">固定 mutation/read-back stage。</param>
    /// <param name="request">本次 nonce 的來源。</param>
    /// <param name="sourceContactId">目前已證明的 fresh source ID。</param>
    /// <param name="leaderContactId">目前已證明的 fresh leader ID。</param>
    /// <param name="relationshipListId">目前已證明的 fresh relationship-list ID。</param>
    /// <param name="originalBaselineLeaderId">供應階段從已證明的 descriptor 基準取得、清理階段從嚴格帳本取得的不可變原始領隊識別碼。不得以重新發布後的 descriptor 值取代它，否則後續清理會失去跨階段的擁有者證明。</param>
    private static void PersistLedger(
        IP72FreshSliceCFixtureLedger ledger,
        string stage,
        P72FreshSliceCFixtureProvisionRequest request,
        Guid originalBaselineLeaderId,
        Guid? sourceContactId,
        Guid? leaderContactId,
        Guid? relationshipListId)
        => ledger.Persist(new P72FreshSliceCFixtureLedgerState(
            stage,
            request.Nonce,
            sourceContactId,
            leaderContactId,
            relationshipListId,
            originalBaselineLeaderId));

    /// <summary>檢查指定 list Entity 欄位是否精確為 fresh leader contact reference。</summary>
    private static bool HasExactContactReference(Entity entity, string attributeName, Guid expectedContactId)
        => HasExactReference(entity, attributeName, ContactEntityName, expectedContactId);

    /// <summary>檢查任一 Entity lookup 是否具有固定 logical name 與 exact ID；不接受空或異型 reference。</summary>
    private static bool HasExactReference(Entity entity, string attributeName, string expectedLogicalName, Guid expectedId)
        => entity.Attributes.TryGetValue(attributeName, out var value) &&
           value is EntityReference { LogicalName: var logicalName, Id: var id } &&
           string.Equals(logicalName, expectedLogicalName, StringComparison.Ordinal) &&
           id == expectedId;

    /// <summary>
    /// 以 fixed primary-key query 證明一個已 ledger-proven fresh entity 已不存在。此方法不把 Retrieve
    /// exception 當成 deletion success，因為 network/authorization failure 與 not-found 無法安全混同；
    /// 只有完整、零列、非 paging 的 QueryExpression response 才能讓 cleanup 繼續下一個 Delete。
    /// </summary>
    /// <param name="entityName">只允許 contact 或 list。</param>
    /// <param name="idAttribute">只允許與 entityName 對應的固定 primary-key attribute。</param>
    /// <param name="id">先前由 current-user ledger 精確證明的 fresh ID。</param>
    /// <returns>遠端精確查詢確認零列時為 <see langword="true"/>。</returns>
    private bool IsEntityAbsent(string entityName, string idAttribute, Guid id)
    {
        if (id == Guid.Empty ||
            (entityName != ListEntityName && entityName != ContactEntityName) ||
            (entityName == ListEntityName && idAttribute != "listid") ||
            (entityName == ContactEntityName && idAttribute != "contactid"))
        {
            return false;
        }

        var query = new QueryExpression(entityName)
        {
            ColumnSet = new ColumnSet(idAttribute),
            NoLock = true,
            TopCount = 2
        };
        query.Criteria.AddCondition(idAttribute, ConditionOperator.Equal, id);
        var rows = _service.RetrieveMultiple(query);
        return rows is not null && !rows.MoreRecords && rows.Entities.Count == 0;
    }

    /// <summary>
    /// 驗證 descriptor-bound list 為 CE 9.1 static contact list 以及 P7.2 Slice C marker。此 method
    /// 不接受 list name 或 list type 作為參數，將 schema、marker 與 ID selection 固定在 test-only
    /// control plane，避免 generic CRUD route 或跨 fixture 的 list identity reuse。
    /// </summary>
    /// <param name="listId">由已驗證 local descriptor 提供的 exact list ID。</param>
    /// <returns>遠端投影完整且符合 static contact list 契約時為 <see langword="true"/>。</returns>
    private bool IsTaskOwnedStaticList(Guid listId)
    {
        var list = _service.Retrieve(
            ListEntityName,
            listId,
            new ColumnSet(ListNameAttribute, ListTypeAttribute, ListCreatedFromCodeAttribute));
        return list is not null &&
               string.Equals(list.LogicalName, ListEntityName, StringComparison.Ordinal) &&
               list.Id == listId &&
               list.Attributes.TryGetValue(ListNameAttribute, out var nameValue) &&
               nameValue is string name &&
               name.StartsWith(SliceCListMarkerPrefix, StringComparison.Ordinal) &&
               list.Attributes.TryGetValue(ListTypeAttribute, out var typeValue) &&
               typeValue is bool isDynamic &&
               !isDynamic &&
               list.Attributes.TryGetValue(ListCreatedFromCodeAttribute, out var createdFromCodeValue) &&
               createdFromCodeValue is OptionSetValue { Value: ContactListCreatedFromCodeValue };
    }

    /// <summary>
    /// 建立尚未開始 remote mutation 的固定 no-go 結果。此分支只用於 local shape、remote preflight 或
    /// baseline-owner 拒絕；上層可安全修正前置條件後另起一次新的 invocation，不能重用任何 ledger 或
    /// CRM session state。
    /// </summary>
    /// <param name="reason">已由呼叫點選定的 bounded、無敏感資訊 reason category。</param>
    /// <returns>固定 false execution flag 的 no-go 結果。</returns>
    private static P72FreshSliceCFixtureProvisionResult NoGo(string reason)
        => NoGo(reason, false);

    /// <summary>
    /// 建立不含內部物件或例外的 fixed no-go 結果。當 <paramref name="operationExecuted"/> 為真時，
    /// caller 必須把它視為可能部分成功或 read-back 未知的 non-retryable state：保留 pending ledger、
    /// 不自動 Delete，也不退回 stale descriptor。這個 result 不會洩漏任何 CRM ID 或使用者資料。
    /// </summary>
    /// <param name="reason">固定 sanitized failure category。</param>
    /// <param name="operationExecuted">是否已可能發出任一 remote mutation。</param>
    /// <returns>帶有正確 ambiguous-state 標誌的 bounded no-go result。</returns>
    private static P72FreshSliceCFixtureProvisionResult NoGo(string reason, bool operationExecuted)
        => new("no-go", reason, operationExecuted);

    /// <summary>
    /// 建立 cleanup lane 的 sanitized no-go result。無論 mutation 是否已開始，ledger 均不可由 child
    /// 刪除；只有 parent 在收到 <c>go</c> 且完成自己的本機 atomic cleanup 後才可移除它。這可避免
    /// timeout、read-back failure 或多 profile process 交錯時把可恢復的 exact-ID state 遺失。
    /// </summary>
    /// <param name="reason">固定、不含 CRM detail 的 cleanup failure category。</param>
    /// <param name="operationExecuted">是否已可能發出 remove/delete mutation。</param>
    /// <returns>永遠要求保留 ledger 的 bounded no-go result。</returns>
    private static P72FreshSliceCFixtureCleanupResult CleanupNoGo(string reason, bool operationExecuted = false)
        => new("no-go", reason, operationExecuted, false);
}
