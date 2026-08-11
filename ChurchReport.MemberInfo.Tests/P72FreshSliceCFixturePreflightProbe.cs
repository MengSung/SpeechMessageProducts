// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72FreshSliceCFixturePreflightProbe.cs
// 用途：提供 P7.2 Slice C fresh-fixture provision 前的獨立、完全唯讀診斷。它只將既有
//       descriptor-bound list、leader、owner 與 weekly-report 轉成固定去識別化分類，讓 parent
//       能判定外部前置條件是否實質改變，而不公開 CRM identity 或擴張任何寫入權限。
//
// 隔離與資源所有權：
// 1. probe 只借用當前 live child 所擁有的 IOrganizationService；它不 Dispose、cache、static
//    保存、跨呼叫傳遞或在背景執行 service/Entity/exception。child 的 Data8 runtime、WCF
//    channel、credential 與 logger 仍由既有 reverse-disposal finally 唯一擁有。
// 2. 所有 request scalar、SDK Entity、QueryExpression 與結果 classification 都限制於單次
//    invocation。沒有 CRM ID、名稱、endpoint、token、cookie、baseline、raw response 或例外可
//    跨使用者、profile、generation 或 process evidence boundary 保留。
// 3. 此類別只可呼叫 fixed WhoAmI 後的 direct Retrieve/RetrieveMultiple projection；沒有
//    Create、Update、Assign、Delete、Execute、Associate、Disassociate、ledger、descriptor
//    publication、feature flag、traffic switch 或 cleanup API。任何不完整投影或例外皆 fail closed。
// ============================================================================

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 表示 fresh preflight probe 的唯一固定輸入。所有 GUID 與 UTC Sunday 只能由 parent 已驗證的
/// current-user descriptor，以及同一 deployment-owned `crm91 + Data8 + CE 9.1` child 的 WhoAmI
/// 導出；這不是 ChurchReport/Gateway API，不能接受 endpoint、credential、connector、organization、
/// arbitrary query 或 caller-selected owner。此型別刻意沒有 nonce、ledger、fresh entity 或 mutation
/// selector，避免唯讀 probe 變成 fixture 控制面。
/// </summary>
/// <param name="AddListId">descriptor-bound add-members static contact list 的 exact ID。</param>
/// <param name="RemoveListId">descriptor-bound remove-members static contact list 的 exact ID。</param>
/// <param name="SmallGroupListId">descriptor-bound small-group static contact list 的 exact ID。</param>
/// <param name="ExistingTargetLeaderContactId">唯一允許作為 owner baseline 來源的 task-marked leader。</param>
/// <param name="TransferSourceListId">descriptor-bound transfer-source static contact list 的 exact ID。</param>
/// <param name="TransferTargetListId">weekly report fixed query 所使用的 static contact list exact ID。</param>
/// <param name="TransferWeekStartUtc">descriptor-bound UTC Sunday 00:00 weekly-report time key。</param>
/// <param name="Data8ServiceUserId">同 child、同 immutable deployment profile WhoAmI 驗證出的 service user ID。</param>
internal sealed record P72FreshSliceCFixturePreflightRequest(
    Guid AddListId,
    Guid RemoveListId,
    Guid SmallGroupListId,
    Guid ExistingTargetLeaderContactId,
    Guid TransferSourceListId,
    Guid TransferTargetListId,
    DateTimeOffset TransferWeekStartUtc,
    Guid Data8ServiceUserId);

/// <summary>
/// 表示 fresh preflight 的 bounded、去識別化分類結果。所有 string property 都受
/// <see cref="P72FreshSliceCFixturePreflightProbe"/> 的 private allowlist 約束；它不可能攜帶
/// CRM ID、name、endpoint、credential、token、cookie、baseline、raw Entity 或原始例外，因而可
/// 安全交給 live child evidence writer。這個結果只說明「前置條件是否被證明」，絕不構成 provision
/// retry、descriptor publication、cleanup 或下一個 Slice 的寫入授權。
/// </summary>
/// <param name="Outcome">固定為 <c>go</c> 或 <c>no-go</c>。</param>
/// <param name="Reason">固定完成或 fail-closed reason category。</param>
/// <param name="ReadOnlyProbeExecuted">所有必要 CRM read 完成且可安全分類時為 true。</param>
/// <param name="RequestShape">request/descriptor scalar shape 的固定狀態。</param>
/// <param name="OperationalLists">五個 reused operational lists 的 aggregate task-owned/static 狀態。</param>
/// <param name="LeaderMarker">descriptor-bound leader 的 marker/identity projection 狀態。</param>
/// <param name="OwnerKind">leader owner lookup 的 logical kind 狀態。</param>
/// <param name="OwnerState">owner active/disabled projection 狀態。</param>
/// <param name="OwnerRelation">active owner 與 Data8 WhoAmI user 是否不同的固定狀態。</param>
/// <param name="WeeklyReport">fixed transfer-target/UTC-Sunday weekly-report cardinality 狀態。</param>
internal sealed record P72FreshSliceCFixturePreflightProbeResult(
    string Outcome,
    string Reason,
    bool ReadOnlyProbeExecuted,
    string RequestShape,
    string OperationalLists,
    string LeaderMarker,
    string OwnerKind,
    string OwnerState,
    string OwnerRelation,
    string WeeklyReport);

/// <summary>
/// 執行 Slice C fresh fixture 的固定 read-only preflight。此類別專門服務於先前 nonzero provision
/// child 被 parent 安全遮罩為 <c>fixture-precondition-failed</c> 的診斷需求：它以 exact-ID
/// projections 將 list、leader/owner、weekly report 的失敗 stage 降至 bounded categories，卻不
/// 執行或允許任何 CRM mutation。每次呼叫重新讀取所有 evidence，沒有 static cache、retry、timer、
/// thread 或 background task；因此 profile/owner 切換後不可能重用前一次 read 的 mutable state。
/// </summary>
internal sealed class P72FreshSliceCFixturePreflightProbe
{
    private const string ListEntityName = P72FreshSliceCFixtureRequestTemplates.ListEntityName;
    private const string ContactEntityName = P72FreshSliceCFixtureRequestTemplates.ContactEntityName;
    private const string SystemUserEntityName = "systemuser";
    private const string WeeklyReportEntityName = "new_group_present_weekly_report";
    private const string ListNameAttribute = "listname";
    private const string ListTypeAttribute = "type";
    private const string ListCreatedFromCodeAttribute = "createdfromcode";
    private const string ContactFullNameAttribute = "fullname";
    private const string OwnerAttribute = "ownerid";
    private const string SystemUserDisabledAttribute = "isdisabled";
    private const string WeeklyReportIdAttribute = "new_group_present_weekly_reportid";
    private const string WeeklyReportListAttribute = "new_list_group_present_weekly_report";
    private const string WeeklyReportDateAttribute = "new_sunday_date";
    private const string ExactlyOneActiveWeeklyReport = "exactly-one-active";
    private const string ZeroActiveWeeklyReports = "zero-active";
    private const string DuplicateActiveWeeklyReports = "duplicate-active";
    private const string WeeklyReportUnavailable = "unavailable";
    private const int ContactListCreatedFromCodeValue = 2;

    private readonly IOrganizationService _service;

    /// <summary>
    /// 建立只借用 request-scoped CE service 的 probe。service 的唯一 lifecycle owner 是 live child；
    /// 本類別不可自行 Dispose，否則可能破壞 Data8 runtime 的 reverse disposal 並讓同 child 的
    /// logger/WCF cleanup 看到已關閉 service。constructor 不做 remote I/O，也不保存 profile、
    /// credential、identity 或 caller state。
    /// </summary>
    /// <param name="service">已繫結 immutable crm91/Data8/CE 9.1 child scope 的 direct CRM service。</param>
    internal P72FreshSliceCFixturePreflightProbe(IOrganizationService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>
    /// 以固定順序證明 fresh provision 的所有遠端前置條件。先拒絕 local shape，再依序讀取五個
    /// static lists、leader/owner 與 TopCount=2 weekly report；任一 unproven projection 立即停止
    /// 後續 remote read 並回傳 fixed no-go category。這不是 retry loop：exception、timeout、
    /// permission 或 cleanup uncertainty 都回傳 <c>probe-unavailable</c>，不會重送 request 或
    /// 以部分結果授權任何寫入。
    /// </summary>
    /// <param name="request">parent/WhoAmI 已導出的固定 descriptor scalar。</param>
    /// <returns>僅包含 bounded category 的 go/no-go evidence projection。</returns>
    internal P72FreshSliceCFixturePreflightProbeResult Probe(P72FreshSliceCFixturePreflightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsRequestShapeValid(request))
        {
            return new P72FreshSliceCFixturePreflightProbeResult(
                "no-go",
                "probe-input-invalid",
                ReadOnlyProbeExecuted: false,
                RequestShape: "invalid",
                OperationalLists: "unavailable",
                LeaderMarker: "unavailable",
                OwnerKind: "unavailable",
                OwnerState: "unavailable",
                OwnerRelation: "unavailable",
                WeeklyReport: "unavailable");
        }

        try
        {
            if (!AreOperationalListsTaskOwned(request))
            {
                return NotProven(
                    operationalLists: "invalid",
                    leaderMarker: "unavailable",
                    ownerKind: "unavailable",
                    ownerState: "unavailable",
                    ownerRelation: "unavailable",
                    weeklyReport: "unavailable");
            }

            var leader = _service.Retrieve(
                ContactEntityName,
                request.ExistingTargetLeaderContactId,
                new ColumnSet(ContactFullNameAttribute, OwnerAttribute));
            if (!IsTaskMarkedLeader(leader, request.ExistingTargetLeaderContactId))
            {
                return NotProven(
                    operationalLists: "valid",
                    leaderMarker: "invalid",
                    ownerKind: "unavailable",
                    ownerState: "unavailable",
                    ownerRelation: "unavailable",
                    weeklyReport: "unavailable");
            }

            if (leader.GetAttributeValue<EntityReference>(OwnerAttribute) is not { LogicalName: SystemUserEntityName, Id: var ownerId } ||
                ownerId == Guid.Empty)
            {
                return NotProven(
                    operationalLists: "valid",
                    leaderMarker: "valid",
                    ownerKind: "other-or-missing",
                    ownerState: "unavailable",
                    ownerRelation: "unavailable",
                    weeklyReport: "unavailable");
            }

            var owner = _service.Retrieve(
                SystemUserEntityName,
                ownerId,
                new ColumnSet(SystemUserDisabledAttribute));
            if (!IsActiveSystemUser(owner, ownerId))
            {
                return NotProven(
                    operationalLists: "valid",
                    leaderMarker: "valid",
                    ownerKind: "systemuser",
                    ownerState: "inactive-or-missing",
                    ownerRelation: "unavailable",
                    weeklyReport: "unavailable");
            }

            if (ownerId == request.Data8ServiceUserId)
            {
                return NotProven(
                    operationalLists: "valid",
                    leaderMarker: "valid",
                    ownerKind: "systemuser",
                    ownerState: "active",
                    ownerRelation: "same-as-data8",
                    weeklyReport: "unavailable");
            }

            var weeklyReport = ClassifyActiveWeeklyReport(
                request.TransferTargetListId,
                request.TransferWeekStartUtc);
            if (!string.Equals(weeklyReport, ExactlyOneActiveWeeklyReport, StringComparison.Ordinal) &&
                !string.Equals(weeklyReport, ZeroActiveWeeklyReports, StringComparison.Ordinal))
            {
                return NotProven(
                    operationalLists: "valid",
                    leaderMarker: "valid",
                    ownerKind: "systemuser",
                    ownerState: "active",
                    ownerRelation: "different-from-data8",
                    weeklyReport: weeklyReport);
            }

            return new P72FreshSliceCFixturePreflightProbeResult(
                "go",
                "fresh-preconditions-proven",
                ReadOnlyProbeExecuted: true,
                RequestShape: "valid",
                OperationalLists: "valid",
                LeaderMarker: "valid",
                OwnerKind: "systemuser",
                OwnerState: "active",
                OwnerRelation: "different-from-data8",
                WeeklyReport: weeklyReport);
        }
        catch (Exception)
        {
            // CRM/WCF exception 可能包含端點、authentication 或 raw server detail；probe 不可把它交給
            // evidence boundary，也不可因為是唯讀而重試。固定 unavailable projection 使 parent
            // 無法把 partial state 當作新的 fresh-fixture 寫入授權。
            return new P72FreshSliceCFixturePreflightProbeResult(
                "no-go",
                "probe-unavailable",
                ReadOnlyProbeExecuted: false,
                RequestShape: "valid",
                OperationalLists: "unavailable",
                LeaderMarker: "unavailable",
                OwnerKind: "unavailable",
                OwnerState: "unavailable",
                OwnerRelation: "unavailable",
                WeeklyReport: WeeklyReportUnavailable);
        }
    }

    /// <summary>
    /// 驗證 request 的所有 GUID、distinct list set 與 UTC Sunday key。此步只處理 method-local
    /// scalar，且在第一個 CRM call 前完成；因此損壞 descriptor 不會讀取其他 profile/fixture 的資料、
    /// 取得 service mutation lease 或將無效 state 保留到任何 session/ledger。
    /// </summary>
    /// <param name="request">待檢查的 fixed preflight scalar。</param>
    /// <returns>所有 local invariants 成立時為 true。</returns>
    private static bool IsRequestShapeValid(P72FreshSliceCFixturePreflightRequest request)
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
               listIds.All(static id => id != Guid.Empty) &&
               listIds.Distinct().Count() == listIds.Length &&
               request.TransferWeekStartUtc.Offset == TimeSpan.Zero &&
               request.TransferWeekStartUtc.TimeOfDay == TimeSpan.Zero &&
               request.TransferWeekStartUtc.DayOfWeek == DayOfWeek.Sunday;
    }

    /// <summary>
    /// 以五次 fixed exact-ID Retrieve 證明 reused operational list 都是 task-marked static contact
    /// list。此方法不接受 list name/query/field from caller，且不會 cache projection；每一 invocation
    /// 都重新驗證，使不同 Data8 profile generation 或 Windows user session 不會重用先前結果。
    /// </summary>
    /// <param name="request">只含五個 descriptor-bound list ID 的 validated input。</param>
    /// <returns>五個 fixed projection 皆有效時為 true。</returns>
    private bool AreOperationalListsTaskOwned(P72FreshSliceCFixturePreflightRequest request)
        => IsTaskOwnedStaticList(request.AddListId) &&
           IsTaskOwnedStaticList(request.RemoveListId) &&
           IsTaskOwnedStaticList(request.SmallGroupListId) &&
           IsTaskOwnedStaticList(request.TransferSourceListId) &&
           IsTaskOwnedStaticList(request.TransferTargetListId);

    /// <summary>
    /// 讀取單一 descriptor-bound list 的最小 projection，確認 logical name、exact ID、task marker、
    /// static type 與 contact origin。read failure 交由外層統一轉成 unavailable；不以名稱搜尋、
    /// collection enumeration 或 generic query 嘗試找替代 list，確保未證明資料永遠不會成為 fixture input。
    /// </summary>
    /// <param name="listId">已完成 local shape validation 的 exact list ID。</param>
    /// <returns>這一筆 list 同時滿足 fixed provenance projection 時為 true。</returns>
    private bool IsTaskOwnedStaticList(Guid listId)
    {
        var list = _service.Retrieve(
            ListEntityName,
            listId,
            new ColumnSet(ListNameAttribute, ListTypeAttribute, ListCreatedFromCodeAttribute));
        return list is not null &&
               string.Equals(list.LogicalName, ListEntityName, StringComparison.Ordinal) &&
               list.Id == listId &&
               list.GetAttributeValue<string>(ListNameAttribute)?.StartsWith(
                   P72FreshSliceCFixtureRequestTemplates.SliceCMarkerPrefix,
                   StringComparison.Ordinal) == true &&
               list.GetAttributeValue<bool?>(ListTypeAttribute) == false &&
               list.GetAttributeValue<OptionSetValue>(ListCreatedFromCodeAttribute)?.Value == ContactListCreatedFromCodeValue;
    }

    /// <summary>
    /// 驗證 descriptor-bound leader 的 fixed identity projection。只有 exact contact ID、contact logical
    /// name、P7.2-SC fullname marker 與存在 owner lookup 時才可繼續讀 owner；任何不符都不會讀取或
    /// 猜選其他 CRM user，避免全組織 scanning 與 owner authority leakage。
    /// </summary>
    /// <param name="leader">direct Retrieve 所得、僅含 fullname/ownerid 的 method-local entity。</param>
    /// <param name="expectedLeaderId">descriptor-bound leader exact ID。</param>
    /// <returns>leader marker 與 minimal owner reference 都可被證明時為 true。</returns>
    private static bool IsTaskMarkedLeader(Entity leader, Guid expectedLeaderId)
        => leader is not null &&
           string.Equals(leader.LogicalName, ContactEntityName, StringComparison.Ordinal) &&
           leader.Id == expectedLeaderId &&
           leader.GetAttributeValue<string>(ContactFullNameAttribute)?.StartsWith(
               P72FreshSliceCFixtureRequestTemplates.SliceCMarkerPrefix,
               StringComparison.Ordinal) == true &&
           leader.Attributes.ContainsKey(OwnerAttribute);

    /// <summary>
    /// 驗證 owner direct projection 是同一 exact ID 的 enabled systemuser。此方法不輸出 identity、
    /// full name、role 或其他 owner detail；它只回傳 active/inactive 事實，使 parent evidence 不可
    /// 被用來列舉或關聯 CRM 使用者資料。
    /// </summary>
    /// <param name="owner">direct Retrieve 所得、僅含 isdisabled 的 method-local entity。</param>
    /// <param name="expectedOwnerId">從 task-marked leader ownerid 精確導出的 user ID。</param>
    /// <returns>owner 是 enabled systemuser 時為 true。</returns>
    private static bool IsActiveSystemUser(Entity owner, Guid expectedOwnerId)
        => owner is not null &&
           string.Equals(owner.LogicalName, SystemUserEntityName, StringComparison.Ordinal) &&
           owner.Id == expectedOwnerId &&
           owner.GetAttributeValue<bool?>(SystemUserDisabledAttribute) == false;

    /// <summary>
    /// 以 exact transfer-target list、active state、UTC Sunday date 與 TopCount=2 將 weekly report
    /// cardinality 轉為固定去識別化分類。這不是同日全 Organization 的計數：其他 list 的週報在 CRM
    /// filter 中已排除。report ID 僅在 method-local collection 內檢查合法性，絕不寫入
    /// result/evidence/cache；輸出永遠不含 count、ID、名稱、日期、raw Entity 或例外。
    /// </summary>
    /// <param name="targetListId">已證明 task-owned static 的 transfer-target list exact ID。</param>
    /// <param name="weekStartUtc">已驗證 UTC Sunday 00:00 的 fixed time key。</param>
    /// <returns>
    /// 完整零列時為 <c>zero-active</c>；完整且剛好一筆合法列時為 <c>exactly-one-active</c>；
    /// 已驗證有多列或 paging 時為 <c>duplicate-active</c>；任何不完整或不合理投影為
    /// <c>unavailable</c>。後三者都只能使外層 fail closed。
    /// </returns>
    private string ClassifyActiveWeeklyReport(Guid targetListId, DateTimeOffset weekStartUtc)
    {
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
        if (rows is null)
        {
            return WeeklyReportUnavailable;
        }

        if (!rows.MoreRecords && rows.Entities.Count == 0)
        {
            return ZeroActiveWeeklyReports;
        }

        if (rows.Entities.Count == 0 ||
            rows.Entities.Any(static entity =>
                !string.Equals(entity.LogicalName, WeeklyReportEntityName, StringComparison.Ordinal) ||
                entity.Id == Guid.Empty))
        {
            return WeeklyReportUnavailable;
        }

        if (!rows.MoreRecords && rows.Entities.Count == 1)
        {
            return ExactlyOneActiveWeeklyReport;
        }

        // TopCount=2 使這條分支最多保留兩個 method-local identity projection；MoreRecords 或第二列
        // 都已足以證明「此固定 list/date 交集不是唯一」，無須取得 total count 或掃描其他小組資料。
        return DuplicateActiveWeeklyReports;
    }

    /// <summary>
    /// 產生已完成 read-only projection、但其中一個 fixed precondition 未證明的統一結果。所有
    /// classification 都由本類別傳入常數，沒有 CRM 值、例外或 caller text；此 helper 不做 I/O、
    /// 不保留 collection，也不改變 service state。
    /// </summary>
    /// <returns>operation 不可開始的固定 no-go result。</returns>
    private static P72FreshSliceCFixturePreflightProbeResult NotProven(
        string operationalLists,
        string leaderMarker,
        string ownerKind,
        string ownerState,
        string ownerRelation,
        string weeklyReport)
        => new(
            "no-go",
            "fresh-preconditions-not-proven",
            ReadOnlyProbeExecuted: true,
            RequestShape: "valid",
            OperationalLists: operationalLists,
            LeaderMarker: leaderMarker,
            OwnerKind: ownerKind,
            OwnerState: ownerState,
            OwnerRelation: ownerRelation,
            WeeklyReport: weeklyReport);
}
