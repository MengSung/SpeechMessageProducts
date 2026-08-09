// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureReconciler.cs
// 用途：將 P7.2 Slice C 已完成的唯讀 snapshot 與記憶體中的 service identity owner
//       投影成固定的、去識別化的 reconciliation 分類。
//
// 純度與隔離：
// 1. 本檔只處理既有純值 snapshot 與 Guid scalar；沒有任何外部 runtime、連線、session、cache、
//    background work 或可釋放資源，因此不會跨 request、user、profile 或 tenant 保留狀態。
// 2. 分類器不能補造缺失的歷史 baseline。即使目前形狀看起來合理，結果仍永久 no-go，並固定標記
//    baseline-unprovable 與 safeToRetry=false；呼叫端不能由此結果推導 write、restore、delete、assign、
//    cleanup 或 retry 授權。
// 3. 所有輸出字串都是本類別內的 allowlist 常數，不拷貝輸入內容、identity、例外或任何外部資料，
//    使結果可安全交給上層的 sanitized evidence 邊界。
// ============================================================================

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// Slice C 唯讀 reconciliation 的固定結果。此 record 是純值且只包含閉合分類；它不攜帶 snapshot、
/// owner identity、錯誤訊息或任何可重新取得外部資源的 handle。<see cref="Outcome"/>、<see cref="Reason"/>
/// 與生命週期旗標共同宣告：目前觀察不足以證明歷史 baseline，故不得執行或重試任何 mutation。
/// </summary>
/// <param name="Outcome">永遠是 <c>no-go</c>；目前結果不能被解讀為可執行證據。</param>
/// <param name="Reason">永遠是 <c>baseline-unprovable</c>；分類器不能恢復未保存的歷史值。</param>
/// <param name="ReadOnlyProbeExecuted">表示本分類只針對呼叫端已提供的 snapshot 完成純記憶體比較。</param>
/// <param name="SafeToRetry">永遠是 <see langword="false"/>；未知 baseline 不允許盲目重送或補償。</param>
/// <param name="OwnerBinding">service identity owner 的固定分類。</param>
/// <param name="AddMembership">add-membership baseline 的固定分類。</param>
/// <param name="RemoveMembership">remove-membership baseline 的固定分類。</param>
/// <param name="SmallGroup">small-group expected projection 的固定分類。</param>
/// <param name="ContactOwner">contact owner projection 的固定分類。</param>
/// <param name="Transfer">transfer composite shape 的固定分類。</param>
internal sealed record P72Data8ListManagementFixtureReconciliationResult(
    string Outcome,
    string Reason,
    bool ReadOnlyProbeExecuted,
    bool SafeToRetry,
    string OwnerBinding,
    string AddMembership,
    string RemoveMembership,
    string SmallGroup,
    string ContactOwner,
    string Transfer);

/// <summary>
/// 純、test-local 的 Slice C reconciliation classifier。它只比較已讀取的 bounded snapshot 與一個由
/// 同一 Data8 runtime 的 WhoAmI 得到、但已在呼叫端轉成 Guid 的 target owner；本類別不接觸任何外部
/// service 或 mutable state，也沒有 retry、cleanup 或 mutation 能力。由於歷史 baseline 並未由 snapshot
/// 保存，任何可辨識的目前形狀都只能回報分類後的 no-go，不能被誤用為 live write authorization。
/// </summary>
internal static class P72Data8ListManagementFixtureReconciler
{
    private const string NoGoOutcome = "no-go";
    private const string BaselineUnprovableReason = "baseline-unprovable";
    private const string ReadOnlyOwnerMatch = "matches-service-identity";
    private const string Unavailable = "unavailable";
    private const string AddBaselineAbsent = "baseline-absent";
    private const string AddUnexpectedPresent = "unexpected-present";
    private const string RemoveBaselinePresent = "baseline-present";
    private const string RemoveUnexpectedAbsent = "unexpected-absent";
    private const string SmallGroupNotExpected = "not-expected-baseline-unproven";
    private const string SmallGroupExpected = "expected-baseline-unproven";
    private const string ContactNonTarget = "non-target-baseline-unproven";
    private const string ContactTarget = "target-baseline-unproven";
    private const string TransferBaselineShape = "baseline-shape-unproven";
    private const string TransferUnexpectedShape = "unexpected-shape-unproven";

    /// <summary>
    /// 將五個 Slice C read-back projection 與 in-memory WhoAmI target owner 轉成一個封閉結果。參數都
    /// 被視為呼叫端已完成的唯讀 probe 輸出；本方法只在目前同步 scope 比較純值，不保存參考、不呼叫
    /// 外部元件，且無任何例外／取消／timeout 路徑需要清理。null 或缺失 projection 會被分類為
    /// <c>unavailable</c>，不會以預設值猜測 baseline。
    /// </summary>
    /// <param name="addMembership">add-many list 的 bounded membership snapshot；空集合表示 baseline 缺席。</param>
    /// <param name="removeMembership">remove-one list 的 bounded membership snapshot；非空表示 baseline 存在。</param>
    /// <param name="smallGroup">目前讀到的 six-field small-group projection。</param>
    /// <param name="smallGroupExpected">由 task-owned relationship proof 得到的 six-field expected projection。</param>
    /// <param name="contactOwnerId">目前 contact owner 的純 Guid projection。</param>
    /// <param name="transferFixture">固定 transfer descriptor，只用來驗證 target/source identity 與日期 shape。</param>
    /// <param name="transfer">目前 transfer composite 的 bounded graph snapshot。</param>
    /// <param name="whoAmITargetOwnerId">呼叫端從 WhoAmI read-only 結果投影出的 target owner Guid。</param>
    /// <returns>固定 no-go、baseline-unprovable、不可重試且不含輸入資料的 sanitized result。</returns>
    internal static P72Data8ListManagementFixtureReconciliationResult Classify(
        P72MembershipSnapshot? addMembership,
        P72MembershipSnapshot? removeMembership,
        P72SmallGroupFixedFieldsSnapshot? smallGroup,
        P72SmallGroupFixedFieldsSnapshot? smallGroupExpected,
        Guid? contactOwnerId,
        P72TransferFixture? transferFixture,
        P72TransferGraphSnapshot? transfer,
        Guid? whoAmITargetOwnerId)
        => new(
            Outcome: NoGoOutcome,
            Reason: BaselineUnprovableReason,
            ReadOnlyProbeExecuted: true,
            SafeToRetry: false,
            OwnerBinding: ClassifyVerifiedOwnerBinding(whoAmITargetOwnerId),
            AddMembership: ClassifyAddMembership(addMembership),
            RemoveMembership: ClassifyRemoveMembership(removeMembership),
            SmallGroup: ClassifySmallGroup(smallGroup, smallGroupExpected),
            ContactOwner: ClassifyContactOwner(contactOwnerId, whoAmITargetOwnerId),
            Transfer: ClassifyTransfer(transferFixture, transfer, whoAmITargetOwnerId));

    /// <summary>只接受非空 WhoAmI owner scalar；空值表示 probe 沒有可用的 service identity。</summary>
    /// <remarks>
    /// 這個投影不保留、記錄或序列化 GUID；呼叫端可在建立 fixture store 前立即保存回傳分類，使後續
    /// <c>Retrieve</c> 或 <c>RetrieveMultiple</c> 失敗不會抹除「WhoAmI 已成功」的診斷邊界。空值或
    /// 空 GUID 一律 fail-closed 為 <c>unavailable</c>，不得暗示可寫入、可重試或跨 profile 取得 owner。
    /// </remarks>
    /// <param name="whoAmITargetOwnerId">僅限本次 immutable <c>crm91</c> Data8 runtime 驗證後、生命週期由目前呼叫端持有的 owner scalar。</param>
    /// <returns>不含身分資訊的 <c>matches-service-identity</c> 或 <c>unavailable</c> 分類。</returns>
    internal static string ClassifyVerifiedOwnerBinding(Guid? whoAmITargetOwnerId)
        => IsNonEmpty(whoAmITargetOwnerId) ? ReadOnlyOwnerMatch : Unavailable;

    /// <summary>add-many 的 baseline 只能由空集合表示；任何已存在 member 都是 unexpected。</summary>
    private static string ClassifyAddMembership(P72MembershipSnapshot? snapshot)
        => snapshot is null
            ? Unavailable
            : snapshot.PresentMemberIds.Count == 0
                ? AddBaselineAbsent
                : AddUnexpectedPresent;

    /// <summary>remove-one 的 baseline 只能由非空集合表示；缺席即代表 unexpected。</summary>
    private static string ClassifyRemoveMembership(P72MembershipSnapshot? snapshot)
        => snapshot is null
            ? Unavailable
            : snapshot.PresentMemberIds.Count > 0
                ? RemoveBaselinePresent
                : RemoveUnexpectedAbsent;

    /// <summary>
    /// 將目前 small-group projection 與 server-owned expected projection 做純 record equality 比較。
    /// 相等仍只表示目前狀態符合 expected，並不代表歷史 baseline 可恢復，因此使用 unproven 分類。
    /// </summary>
    private static string ClassifySmallGroup(
        P72SmallGroupFixedFieldsSnapshot? snapshot,
        P72SmallGroupFixedFieldsSnapshot? expected)
        => snapshot is null || expected is null
            ? Unavailable
            : snapshot == expected
                ? SmallGroupExpected
                : SmallGroupNotExpected;

    /// <summary>
    /// 比較目前 contact owner 與唯一 service identity target。任一 identity 缺失即 unavailable；相等
    /// 只表示目前 owner 已是 target，仍不能證明 mutation 前的歷史 owner baseline。
    /// </summary>
    private static string ClassifyContactOwner(Guid? contactOwnerId, Guid? whoAmITargetOwnerId)
        => !IsNonEmpty(contactOwnerId) || !IsNonEmpty(whoAmITargetOwnerId)
            ? Unavailable
            : contactOwnerId == whoAmITargetOwnerId
                ? ContactTarget
                : ContactNonTarget;

    /// <summary>
    /// 以 transfer fixture 的固定 identity/date、其暫存 verified target owner 與目前 graph 比較 baseline
    /// shape。fixture owner 必須是非空且精確等於同次 WhoAmI scalar，否則不能建立比較邊界而回報
    /// unavailable；邊界完整但 graph 任一 component 不符時回報 unexpected-shape-unproven。即使全部
    /// component 符合 baseline shape，結果仍是 baseline-shape-unproven，因為本 classifier 沒有歷史
    /// snapshot 的時間證據。
    /// </summary>
    private static string ClassifyTransfer(
        P72TransferFixture? fixture,
        P72TransferGraphSnapshot? snapshot,
        Guid? whoAmITargetOwnerId)
    {
        if (fixture is null ||
            snapshot is null ||
            !IsNonEmpty(whoAmITargetOwnerId) ||
            fixture.TargetOwnerId is not Guid fixtureTargetOwnerId ||
            fixtureTargetOwnerId != whoAmITargetOwnerId ||
            !HasUsableFixtureShape(fixture))
        {
            return Unavailable;
        }

        var sourceMembershipIsBaseline = !fixture.SourceListId.HasValue || snapshot.SourceMembershipPresent;
        var targetMembershipIsAbsent = !snapshot.TargetMembershipPresent;
        var presentRecordIsAbsent = snapshot.PresentRecordId is null && !snapshot.PresentRecordMatches;
        var primaryListIsNotTarget = snapshot.PrimaryListId != fixture.TargetListId;
        var ownerIsNotTarget = snapshot.OwnerId is Guid ownerId && ownerId != whoAmITargetOwnerId;

        return sourceMembershipIsBaseline &&
               targetMembershipIsAbsent &&
               presentRecordIsAbsent &&
               primaryListIsNotTarget &&
               ownerIsNotTarget
            ? TransferBaselineShape
            : TransferUnexpectedShape;
    }

    /// <summary>
    /// 驗證純 fixture descriptor 的比較邊界。這裡只檢查 contact/list identity 與 UTC Sunday；暫存
    /// target owner 與 WhoAmI 的精確綁定由呼叫端 <see cref="ClassifyTransfer"/> 執行，任一不符都回
    /// unavailable，避免以不完整 descriptor 猜測 graph。
    /// </summary>
    private static bool HasUsableFixtureShape(P72TransferFixture fixture)
    {
        if (!IsNonEmpty(fixture.ContactId) || !IsNonEmpty(fixture.TargetListId))
        {
            return false;
        }

        if (fixture.SourceListId is Guid sourceListId &&
            (!IsNonEmpty(sourceListId) || sourceListId == fixture.TargetListId))
        {
            return false;
        }

        var utc = fixture.WeekStartDate.ToUniversalTime();
        return fixture.WeekStartDate.Offset == TimeSpan.Zero &&
               utc.TimeOfDay == TimeSpan.Zero &&
               utc.DayOfWeek == DayOfWeek.Sunday;
    }

    /// <summary>Guid 空值是缺失 identity 的唯一表示；不將字串格式或輸入內容帶入結果。</summary>
    private static bool IsNonEmpty(Guid? value)
        => value is Guid guid && guid != Guid.Empty;
}
