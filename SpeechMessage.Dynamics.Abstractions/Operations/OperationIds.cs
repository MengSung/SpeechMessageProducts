// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs
// 目的：集中管理 Package 0 / Package 1 的 capabilityOperationId 字串常數。
//
// 保母教學：
// - capabilityOperationId 必須符合 ^[a-z0-9]+(\.[a-z0-9]+)+$
// - 不要在業務程式隨手拼字串，避免拼錯或出現連字號。
// - 這些 ID 必須與 phase0-organization-call-matrix.json 的 normalizedCallSites 對齊。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// Package 0（runtime）與 Package 1（fee reads）的正式操作 ID。
/// </summary>
public static class OperationIds
{
    // -------- Package 0：runtime 基礎能力，僅包含健康檢查、設定檔與連線池控制作業 --------

    /// <summary>對應 ORG-CALL-00003：WhoAmI 健康檢查。</summary>
    public const string RuntimeHealthWhoAmI = "runtime.health.whoami";

    /// <summary>對應 ORG-CALL-00004：profile 連線/資源健康檢查。</summary>
    public const string RuntimePoolValidateConnection = "runtime.pool.validate.connection";

    /// <summary>對應 ORG-CALL-00040：option-set metadata 讀取。</summary>
    public const string MetadataOptionSetByAttribute = "metadata.optionset.retrieve.by.attribute";

    // -------- Package 1：費用唯讀能力；正式流量在 Phase 4 證據完成前維持 feature flag 關閉 --------

    /// <summary>對應 ORG-CALL-00005：依 contact 讀取 dedication fee。</summary>
    public const string FeeDedicationRetrieveByContact = "fee.dedication.retrieve.by.contact";

    /// <summary>對應 ORG-CALL-00006：依 contact + 日期區間讀取 dedication fee。</summary>
    public const string FeeDedicationRetrieveByContactDateRange = "fee.dedication.retrieve.by.contact.date.range";

    /// <summary>對應 ORG-CALL-00064：依 dedication booking + paid period 讀取 fee。</summary>
    public const string FeesRetrieveByDedicationPeriod = "fees.retrieve.by.dedication.period";

    /// <summary>對應 ORG-CALL-00066：fee editor 依 disciple lesson 載入。</summary>
    public const string FeesEditorLoadByDiscipleLesson = "fees.editor.load.by.disciplelesson";

    /// <summary>對應 ORG-CALL-00061：依 contact 讀取 stor lessons（fee 畫面支援）。</summary>
    public const string LessonsStorRetrieveByContact = "lessons.stor.retrieve.by.contact";

    /// <summary>對應 ORG-CALL-00062：依 disciple lesson 讀取 stor lessons。</summary>
    public const string LessonsStorRetrieveByDiscipleLesson = "lessons.stor.retrieve.by.disciplelesson";

    // -------- Package 2：會友具名寫入能力；P7.4 啟用產品流量前維持 fail-closed --------

    /// <summary>
    /// 對應 ORG-CALL-00030：只更新會友基本資料的具名能力。
    /// 此常數只識別 server-owned allowlist，不能攜帶或推導 CRM endpoint、Entity 名稱、欄位字典、credential、
    /// Profile 或 CE version；實際 routing 仍完全由 deployment-owned immutable profile 決定。
    /// </summary>
    public const string MemberInfoContactUpdateBasicInfo = "memberinfo.contact.update.basic.info";

    /// <summary>
    /// 對應 ORG-CALL-00023：將 ChurchReport 已取得並正規化的 LINE profile 寫入 contact 三個固定欄位。
    /// LINE token、LINE user ID、profile API response 與圖片探測結果不得進入此 operation；它們仍由產品流程擁有。
    /// </summary>
    public const string MemberInfoContactUpdateLineProfile = "memberinfo.contact.update.line.profile";

    /// <summary>
    /// 對應 ORG-CALL-00024：以 server-owned 小組／在籍規則計算未分組 contact 的非空 commitment raw value 筆數。
    /// 此 ID 不允許 caller 提供 FetchXML、QueryExpression、entity name、欄位名或 grouped contact GUID graph。
    /// </summary>
    public const string MemberInfoContactCountUngroupedCommitment =
        "memberinfo.contact.count.ungrouped.commitment";

    // -------- Package 2：名單與小組固定操作；每個 ID 都有獨立的 fixture／reconciliation owner --------

    /// <summary>
    /// 對應 P7.2 Slice C：一次將最多 1,000 位 distinct contact 加入一個固定 static list。
    /// member set 只可由 bounded <c>guid-array</c> 表達；它不是 generic association 或任意 Entity command。
    /// </summary>
    public const string ListMembersAddMany = "list.members.add.many";

    /// <summary>
    /// 對應 P7.2 Slice C：從一個固定 static list 移除一位指定 contact。
    /// 這個 ID 不接受 listmember Entity、任意條件或 caller-selected relationship。
    /// </summary>
    public const string ListMembersRemoveOne = "list.members.remove.one";

    /// <summary>
    /// 對應 P7.2 Slice C：只以 <c>change-race-leader</c> 或 <c>change-area-leader</c> 模式更新小組固定六欄。
    /// 呼叫端不能傳入欄位 map；area leader 與 area name 只由 connector 的固定關聯規則解析。
    /// </summary>
    public const string ListManagementSmallGroupUpdateFields = "listmanagement.smallgroup.update.fields";

    /// <summary>
    /// 對應 P7.2 Slice C：將一位 contact 指派給一位 allowlisted systemuser。
    /// 這個 ID 不會成為任意 entity AssignRequest 的代理。
    /// </summary>
    public const string ContactAssignOwner = "contact.assign.owner";

    /// <summary>
    /// 對應 P7.2 Slice C：以 server-owned 順序完成 contact 的 static-list transfer、週報 present record、
    /// primary-list lookup 與選擇性 owner assignment。它不是可任意拼裝 CRM transaction 的 generic workflow。
    /// </summary>
    public const string NewPersonContactTransferBetweenLists = "newperson.contact.transfer.between.lists";
}
