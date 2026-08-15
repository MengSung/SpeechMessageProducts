// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs
// 用途：定義 Gateway/Embedded 到產品之間唯一可序列化的封閉 Dynamics 回應 envelope 與 Package 1 安全 wire records。
//
// 安全與生命週期邊界：
// 1. 此型別只保存已投影的 operation ID、CE 版本與產品欄位；禁止 JsonElement、object、OData annotation、
//    CRM host、API root、continuation、credential、token、session 或任何 upstream extension data。
// 2. 每個 envelope 由建立它的 request scope 擁有。集合在建構時複製為陣列，讓 caller 的可變集合不能在
//    queue、audit 或 Gateway 序列化期間改寫資料；本型別不持有 stream、HttpResponseMessage、timer 或 handle。
// 3. discriminator 與 branch 的一對一驗證在建構時失敗關閉。未支援作業沒有 branch，避免 metadata/raw payload
//    因「暫時方便」而穿越信任邊界；後續 connector 必須在自己的 scope 內 dispose page 資源。
// ============================================================================

using System.Text.Json.Serialization;

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 已登錄作業可回傳給產品的封閉資料種類。列舉值同時是 matrix、template revision 與 JSON discriminator 的
/// 固定合約；新增值必須先新增安全投影、有限 page/byte 政策、matrix 記錄與跨層測試，不能把未知 OData
/// 形狀當成預設分支。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperationResponseKind
{
    /// <summary>
    /// 作業沒有產品可見回應投影。呼叫端必須把它視為 fail-closed，而不是回傳 raw metadata 或任意 JSON。
    /// </summary>
    Unsupported = 0,

    /// <summary>
    /// 已投影的 WhoAmI GUID 結果；不包含 CRM endpoint、token、session 或原始 OData context。
    /// </summary>
    WhoAmI = 1,

    /// <summary>
    /// Package 1 fee-read 的安全 fee record 集合。
    /// </summary>
    Package01FeeRecords = 2,

    /// <summary>
    /// Package 1 stor-lesson/read-editor 的安全 stor lesson record 集合。
    /// </summary>
    Package01StorLessonRecords = 3,

    /// <summary>
    /// P7.2 會友基本資料更新的封閉回應種類。
    /// 目前 registry 先以此 discriminator 保留寫入契約；在 Data8 template、read-back projection 與 ProductClient
    /// branch 尚未全部完成前，executor 仍會在取得 connector lease 前拒絕該 operation，不能把 enum 的存在誤解為
    /// 已允許 CRM 寫入或 feature flag 已啟用。
    /// </summary>
    ContactBasicInfoUpdate = 4,

    /// <summary>
    /// P7.2 contact LINE profile 三欄固定寫入的封閉結果；LINE token、profile payload、URL 與 contact identity
    /// 不得出現在結果。正式 branch 只有在 Data8 read-back 完成後才可建構。
    /// </summary>
    ContactLineProfileUpdate = 5,

    /// <summary>
    /// P7.2 未分組 commitment aggregate 的 bounded raw OptionSet value/count 集合；不包含 FetchXML、
    /// QueryExpression、Entity、AliasedValue、metadata label 或 grouped contact identity。
    /// </summary>
    UngroupedCommitmentCounts = 6,

    /// <summary>
    /// P7.2 Slice C static-list member add/remove 的封閉結果；它不含 list、contact 或 listmember identity，
    /// 只能描述固定 action 是否由 read-back 證實。
    /// </summary>
    StaticListMembershipMutation = 7,

    /// <summary>
    /// P7.2 Slice C 小組六欄固定寫入的封閉結果；任何部分欄位、未知 timeout 或任意 field-map 都不能建構此 branch。
    /// </summary>
    SmallGroupFixedFieldsMutation = 8,

    /// <summary>
    /// P7.2 Slice C contact 指派固定 systemuser owner 的封閉結果；它不回傳 owner/contact identity 或 CRM Assign response。
    /// </summary>
    ContactOwnerAssignment = 9,

    /// <summary>
    /// P7.2 Slice C contact list-transfer composite 的封閉結果；只有完整 membership、weekly record、lookup 與 owner
    /// reconciliation 都已確認時才可回傳成功 branch。
    /// </summary>
    ContactListTransfer = 10,

    /// <summary>
    /// P7.3 contact entityimage 的 copied bytes 投影。它不含 stream、decoder、CRM Entity、contact identity
    /// 或 cache key；connector 已在 request scope 完成格式與大小驗證，產品只能讀取 defensive copy。
    /// </summary>
    ContactImage = 11,

    /// <summary>
    /// P7.3 兩個 contact image write operation 的最小 read-back-confirmed 結果。未知 timeout、部分寫入或
    /// cleanup uncertainty 不得使用此 discriminator，必須維持 fail-closed error。
    /// </summary>
    ContactImageUpdate = 12,

    /// <summary>
    /// P7.3 固定 metadata OptionSet 的 ordered pure value/label projection。它不保留 AttributeMetadata、
    /// LocalizedLabel、cache segment、profile generation 或任何 SDK graph。
    /// </summary>
    OptionSetOptions = 13,

    /// <summary>
    /// P7.3 依 UTC Sunday 讀取的 bounded meeting statistic projection。它不含 FetchXML、cookie、page token
    /// 或 raw Entity，任何 page failure 都不會產生 partial branch。
    /// </summary>
    MeetingStatistics = 14,

    /// <summary>Package 01 奉獻預約的 bounded read projection。</summary>
    Package01DedicationBookingRecords = 15,

    /// <summary>
    /// P7.4 認證聯絡人唯讀的安全投影。這個 discriminator 只容許 contact locator、顯示名稱、active 狀態與
    /// 不含資料的安全分類；任何秘密、原始 Entity、例外、token、cookie 或 transport response 都不能成為 branch。
    /// </summary>
    AuthenticationContactReadRecords = 16,

    /// <summary>
    /// P7.1 App-named 名單目錄的 bounded pure-scalar projection。它只允許 list ID、名稱、created-from option、
    /// UTC last-used 時間與 purpose；不含 CRM Entity、FetchXML、formatted-value 字典、cookie、profile、endpoint、
    /// credential、cache key 或原始 transport response。此 discriminator 僅建立封閉 wire contract，不表示
    /// Data8、產品 consumer、CE 驗證或 deployment gate 已啟用；任何頁面/lease 資源仍由未來 connector request
    /// scope 的唯一 owner 在完成、取消、逾時或 fault 時釋放。
    /// </summary>
    AppNamedListCatalogRecords = 17,

    /// <summary>
    /// P7.1 App-named 小組名單目錄的 bounded pure-scalar projection。它與一般 app-named catalog 使用不同的
    /// discriminator 與 wire record，額外只允許兩個 nullable leader contact GUID；不含 CRM Entity、EntityReference
    /// 名稱、FetchXML、formatted-value 字典、cookie、profile、endpoint、credential、cache key 或 transport response。
    /// 此值只建立 local typed contract，不代表 Data8、產品 consumer、CE 驗證或 deployment gate 已啟用；未來 page、
    /// lease 與 transport 資源仍由 connector request scope 的唯一 owner 在完成、取消、逾時或 fault 時釋放。
    /// </summary>
    SmallGroupAppNamedListCatalogRecords = 18,

    /// <summary>
    /// ORG-CALL-00026 已授權 contact 的個人出席紀錄純量列。此分支只含可顯示的日期、旗標及
    /// 有界文字，不含 CRM Entity、lookup、profile、session、credential、lease、stream 或取消狀態；
    /// 因此 connector 完成並釋放外部資源後，產品層仍只能取得 request-local 值快照。
    /// </summary>
    MemberInfoPresentRecordReadRecords = 19,

    /// <summary> P7.4 聯絡人顯示聯集：影像、LINE 重新導向或預設頭像。 </summary>
    ContactImageDisplay = 20,

    /// <summary>
    /// ORG-CALL-00057 已授權 contact 的 App-named 名單成員關係純量列。此分支只允許 list GUID 與 nullable
    /// list name，不含 CRM Entity、EntityCollection、listmember、QueryExpression、排序、paging cookie、profile、
    /// credential、session、cache 或原始 transport response。factory/constructor 會採取獨立唯讀快照並拒絕空白或
    /// 重複 ID、超過 32 列、無效 UTF-8 或超過 32 KiB 的資料；此值只建立 local typed contract，不代表任何
    /// consumer、Data8 connector、CE 證據或 deployment gate 已啟用。
    /// </summary>
    AppNamedMembershipRecords = 21,

    /// <summary>
    /// P7 MemberInfo 伺服器擁有的 subject assignment evidence。它只包含 subject GUID、封閉 access mode 與最多
    /// 512 個 unique list GUID；不含 job title、CRM Entity、lookup name、query、profile、credential、Session、
    /// cookie、cache、connector 或原始例外。此 branch 僅提供 disabled local data plane，不代表 consumer、CE、
    /// traffic、P7.5 或 P8 已完成。
    /// </summary>
    MemberInfoAssignmentEvidence = 22,

    /// <summary>
    /// ORG-CALL-00031／00032 的單一 MemberInfo 小組 descriptor／membership immutable snapshot。此 branch 只允許
    /// subject、封閉 access mode 與已複製的純量列，不攜帶 CRM SDK、query、cookie、profile、credential、Session 或
    /// 原始例外；它是 CE 9.1 local-only 的 disabled data-plane contract，不代表 consumer、traffic 或 CE 證據已啟用。
    /// </summary>
    MemberInfoSmallGroupSnapshot = 23
}

/// <summary>
/// Gateway/Embedded 成功回應的封閉 concrete union。所有 branch 由 immutable discriminator 決定，且 constructor
/// 強制只存在一個相符 branch；這讓產品可以在不保留 upstream document、頁面串流或 continuation 的情況下，
/// 驗證回應種類並映射到自己的 DTO。
/// </summary>
public sealed class OperationResponseData
{
    private const int MaximumUngroupedCommitmentCountRecords = 4096;
    private const int MaximumOptionSetOptionRecords = 1024;
    private const int MaximumMeetingStatisticRecords = 4096;
    // 與兩個固定 authentication QueryExpression 的 TopCount = 2 完全一致。這是跨層第二道
    // retained-data 上限：即使未來 connector、測試替身或 transport 回傳錯誤的集合，envelope 也
    // 不會 materialize 第三筆 contact 到 ProductClient 邊界，並保留 zero / one / duplicate 的唯一語意。
    private const int MaximumAuthenticationContactReadRecords = 2;
    private const int MaximumMemberInfoPresentRecordReadRecords = 4096;
    private const int MaximumOptionSetLabelCharacters = 512;
    private const int MaximumMeetingNameCharacters = 512;
    private const int MaximumMemberInfoPresentRecordTextCharacters = 512;
    private const int MaximumMemberInfoPresentRecordTextBytes =
        MaximumMemberInfoPresentRecordTextCharacters * 4;
    private const int MaximumMemberInfoPresentRecordResponseBytes = 256 * 1024;
    private const int MemberInfoPresentRecordFixedRowBytes = 96;
    private const int MaximumAppNamedMembershipRecords = 32;
    private const int MaximumAppNamedMembershipResponseBytes = 32 * 1024;
    private const int AppNamedMembershipFixedRowBytes = 32;
    private const int MaximumMemberInfoAuthorizationAssignmentListIds = 512;
    private const int MaximumMemberInfoSmallGroupDescriptorRecords = 512;
    private const int MaximumMemberInfoSmallGroupMembershipRecords = 4096;
    private const int MaximumMemberInfoSmallGroupTextCharacters = 512;
    private const int MaximumMemberInfoSmallGroupTextBytes =
        MaximumMemberInfoSmallGroupTextCharacters * 4;
    private const int MaximumMemberInfoSmallGroupResponseBytes = 1024 * 1024;
    private const int MemberInfoSmallGroupMembershipFixedRowBytes = 32;
    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// 建立封閉回應。JSON 反序列化也必須經過此驗證，故未知/錯配 branch 無法被悄悄保存在長生命週期的
    /// Gateway 結果、queue 或 audit 物件中。非 null 集合會複製，唯一 owner 成為本 envelope。
    /// </summary>
    [JsonConstructor]
    public OperationResponseData(
        string operationId,
        string ceVersion,
        OperationResponseKind responseKind,
        WhoAmIResponseData? whoAmI = null,
        IReadOnlyList<Package01FeeRecord>? feeRecords = null,
        IReadOnlyList<Package01StorLessonRecord>? storLessonRecords = null,
        ContactBasicInfoUpdateResponseData? contactBasicInfoUpdate = null,
        ContactLineProfileUpdateResponseData? contactLineProfileUpdate = null,
        IReadOnlyList<UngroupedCommitmentCountRecord>? ungroupedCommitmentCounts = null,
        StaticListMembershipMutationResponseData? staticListMembershipMutation = null,
        SmallGroupFixedFieldsMutationResponseData? smallGroupFixedFieldsMutation = null,
        ContactOwnerAssignmentResponseData? contactOwnerAssignment = null,
        ContactListTransferResponseData? contactListTransfer = null,
        ContactImageResponseData? contactImage = null,
        ContactImageDisplayResponseData? contactImageDisplay = null,
        ContactImageUpdateResponseData? contactImageUpdate = null,
        IReadOnlyList<OptionSetOptionRecord>? optionSetOptions = null,
        IReadOnlyList<MeetingStatisticRecord>? meetingStatistics = null,
        IReadOnlyList<Package01DedicationBookingRecord>? dedicationBookingRecords = null,
        IReadOnlyList<AppNamedListCatalogRecord>? appNamedListCatalogRecords = null,
        IReadOnlyList<SmallGroupAppNamedListCatalogRecord>? smallGroupAppNamedListCatalogRecords = null,
        IReadOnlyList<AuthenticationContactReadRecord>? authenticationContactReadRecords = null,
        IReadOnlyList<MemberInfoPresentRecordReadRecord>? memberInfoPresentRecordReadRecords = null,
        IReadOnlyList<AppNamedMembershipRecord>? appNamedMembershipRecords = null,
        MemberInfoAuthorizationAssignmentEvidenceResponseData? memberInfoAuthorizationAssignmentEvidence = null,
        MemberInfoSmallGroupSnapshotResponseData? memberInfoSmallGroupSnapshot = null,
        AuthenticationContactReadSafetyClassification authenticationContactReadSafetyClassification =
            AuthenticationContactReadSafetyClassification.Safe)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("operationId is required.", nameof(operationId));
        }

        if (string.IsNullOrWhiteSpace(ceVersion))
        {
            throw new ArgumentException("ceVersion is required.", nameof(ceVersion));
        }

        ValidateSingleSafeBranch(
            responseKind,
            whoAmI,
            feeRecords,
            storLessonRecords,
            contactBasicInfoUpdate,
            contactLineProfileUpdate,
            ungroupedCommitmentCounts,
            staticListMembershipMutation,
            smallGroupFixedFieldsMutation,
            contactOwnerAssignment,
            contactListTransfer,
            contactImage,
            contactImageDisplay,
            contactImageUpdate,
            optionSetOptions,
            meetingStatistics,
            dedicationBookingRecords,
            appNamedListCatalogRecords,
            smallGroupAppNamedListCatalogRecords,
            authenticationContactReadRecords,
            memberInfoPresentRecordReadRecords,
            appNamedMembershipRecords,
            memberInfoAuthorizationAssignmentEvidence,
            memberInfoSmallGroupSnapshot,
            authenticationContactReadSafetyClassification);

        OperationId = operationId;
        CeVersion = ceVersion;
        ResponseKind = responseKind;
        WhoAmI = whoAmI;
        FeeRecords = feeRecords?.ToArray();
        StorLessonRecords = storLessonRecords?.ToArray();
        ContactBasicInfoUpdate = contactBasicInfoUpdate;
        ContactLineProfileUpdate = contactLineProfileUpdate;
        UngroupedCommitmentCounts = ungroupedCommitmentCounts?.ToArray();
        StaticListMembershipMutation = staticListMembershipMutation;
        SmallGroupFixedFieldsMutation = smallGroupFixedFieldsMutation;
        ContactOwnerAssignment = contactOwnerAssignment;
        ContactListTransfer = contactListTransfer;
        ContactImage = contactImage;
        ContactImageDisplay = contactImageDisplay;
        ContactImageUpdate = contactImageUpdate;
        OptionSetOptions = optionSetOptions?.ToArray();
        MeetingStatistics = meetingStatistics?.ToArray();
        DedicationBookingRecords = dedicationBookingRecords?.ToArray();
        AppNamedListCatalogRecords = appNamedListCatalogRecords?.ToArray();
        SmallGroupAppNamedListCatalogRecords = smallGroupAppNamedListCatalogRecords?.ToArray();
        AuthenticationContactReadRecords = authenticationContactReadRecords?.ToArray();
        MemberInfoPresentRecordReadRecords = memberInfoPresentRecordReadRecords?.ToArray();
        AppNamedMembershipRecords = appNamedMembershipRecords is null
            ? null
            : Array.AsReadOnly(appNamedMembershipRecords.ToArray());
        MemberInfoAuthorizationAssignmentEvidence = memberInfoAuthorizationAssignmentEvidence;
        MemberInfoSmallGroupSnapshot = memberInfoSmallGroupSnapshot is null
            ? null
            : new MemberInfoSmallGroupSnapshotResponseData(
                memberInfoSmallGroupSnapshot.SubjectContactId,
                memberInfoSmallGroupSnapshot.AccessMode,
                memberInfoSmallGroupSnapshot.Descriptors,
                memberInfoSmallGroupSnapshot.Memberships);
        AuthenticationContactReadSafetyClassification = authenticationContactReadSafetyClassification;
    }

    /// <summary>
    /// 已登錄 capability ID；它只識別 allowlisted 作業，不能是 CRM URL、template、profile alias 或 caller input。
    /// </summary>
    [JsonPropertyName("operationId")]
    public string OperationId { get; }

    /// <summary>
    /// 已設定且驗證過的 CE API 版本，例如 v8.2/v9.1；不攜帶組織主機或 ApprovedWebApiRoot。
    /// </summary>
    [JsonPropertyName("ceVersion")]
    public string CeVersion { get; }

    /// <summary>
    /// 封閉 branch discriminator。產品與 Gateway 必須先驗證它，再讀取對應 branch，避免不同 operation 的資料
    /// 被錯誤重用或讓未知 JSON 延長到產品 scope。
    /// </summary>
    [JsonPropertyName("responseKind")]
    public OperationResponseKind ResponseKind { get; }

    /// <summary>
    /// WhoAmI 的安全 GUID 投影；非 WhoAmI 回應一律在 JSON 省略此欄位，避免 branch 混用。
    /// </summary>
    [JsonPropertyName("whoAmI")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WhoAmIResponseData? WhoAmI { get; }

    /// <summary>
    /// Package 1 fee records 的安全投影；集合由 constructor 複製並且在非 fee branch 時從 JSON 省略。
    /// 它不保存 OData value wrapper、formatted annotation 或 nextLink，因此沒有可追蹤的上游頁面資源。
    /// </summary>
    [JsonPropertyName("feeRecords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Package01FeeRecord>? FeeRecords { get; }

    /// <summary>
    /// Package 1 stor-lesson records 的安全投影；集合由 constructor 複製並且在非 stor branch 時從 JSON 省略。
    /// </summary>
    [JsonPropertyName("storLessonRecords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Package01StorLessonRecord>? StorLessonRecords { get; }

    /// <summary>
    /// P7.2 會友基本資料寫入的唯一安全結果投影。它只含 changed/no-change 與固定的 read-back correlation
    /// category；不含 contact ID、電話、地址、OptionSet、baseline、CRM logical name、URL、token、cookie、
    /// 例外或原始 response。此 immutable 值沒有資源所有權，connector 的 lease、service、request、response、
    /// buffer 與 cancellation registration 仍必須在 executor request scope 內釋放，不能由結果物件延長生命週期。
    /// </summary>
    [JsonPropertyName("contactBasicInfoUpdate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactBasicInfoUpdateResponseData? ContactBasicInfoUpdate { get; }

    /// <summary>
    /// P7.2 LINE profile 三欄寫入的唯一安全結果。它只描述已確認 mutation 與 read-back category；不含
    /// contact ID、LINE user ID、token、picture URL、status、display name、CRM response 或 fixture baseline。
    /// </summary>
    [JsonPropertyName("contactLineProfileUpdate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactLineProfileUpdateResponseData? ContactLineProfileUpdate { get; }

    /// <summary>
    /// P7.2 未分組 commitment aggregate 的 bounded value/count 純值集合。constructor 立即複製集合，避免
    /// caller 在 Gateway 序列化期間改寫；它不保存 Entity、AliasedValue、FetchXML、metadata 或 grouped contact。
    /// </summary>
    [JsonPropertyName("ungroupedCommitmentCounts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UngroupedCommitmentCountRecord>? UngroupedCommitmentCounts { get; }

    /// <summary>
    /// P7.2 Slice C static-list member action 的最小安全結果。它沒有 list/contact/member identity、加入數量、
    /// baseline、endpoint、credential 或 CRM response；所有 SDK graph 已在 connector lease scope 釋放。
    /// </summary>
    [JsonPropertyName("staticListMembershipMutation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StaticListMembershipMutationResponseData? StaticListMembershipMutation { get; }

    /// <summary>
    /// P7.2 Slice C 小組固定欄位操作的最小安全結果。回應不攜帶六個欄位或 leader identity，避免把 fixture
    /// baseline 或跨 profile 資料保留到 ProductClient／Gateway 序列化範圍。
    /// </summary>
    [JsonPropertyName("smallGroupFixedFieldsMutation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SmallGroupFixedFieldsMutationResponseData? SmallGroupFixedFieldsMutation { get; }

    /// <summary>
    /// P7.2 Slice C contact owner assignment 的最小安全結果。它只陳述 read-back 是否確認，不回傳 contact 或
    /// systemuser GUID、名稱、CRM request 或 profile 資訊。
    /// </summary>
    [JsonPropertyName("contactOwnerAssignment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactOwnerAssignmentResponseData? ContactOwnerAssignment { get; }

    /// <summary>
    /// P7.2 Slice C transfer composite 的最小安全結果。成功只表示完整固定圖譜已 reconcile；不回傳 membership、
    /// present record、weekly report、owner、contact 或 cleanup baseline identity。
    /// </summary>
    [JsonPropertyName("contactListTransfer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactListTransferResponseData? ContactListTransfer { get; }

    /// <summary>
    /// P7.3 contact image 的封閉 projection。該物件自身會在 getter 複製 bytes，故 serializing 或產品端修改不會
    /// 回寫 connector/envelope；image 不可進 shared cache，stream/decoder/buffer 已於 connector scope 釋放。
    /// </summary>
    [JsonPropertyName("contactImage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactImageResponseData? ContactImage { get; }

    /// <summary> P7.4 顯示聯集；建構時只允許一個已驗證分支，且不保留上游可變資料。 </summary>
    [JsonPropertyName("contactImageDisplay")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactImageDisplayResponseData? ContactImageDisplay { get; }

    /// <summary>
    /// P7.3 image write 的最小 read-back-confirmed 結果。它不包含 contact、image、hash、baseline 或 CRM response，
    /// 因此不會讓 fixture/session identity 跨 ProductClient 或 Gateway response 留存。
    /// </summary>
    [JsonPropertyName("contactImageUpdate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactImageUpdateResponseData? ContactImageUpdate { get; }

    /// <summary>
    /// P7.3 metadata options 的 ordered pure value projection。constructor 立即 materialize collection；它不持有
    /// metadata cache、profile/generation key、SDK object 或 locale context，這些資源/邊界由 connector owner 管理。
    /// </summary>
    [JsonPropertyName("optionSetOptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OptionSetOptionRecord>? OptionSetOptions { get; }

    /// <summary>
    /// P7.3 weekly meeting statistics 的 bounded DTO collection。constructor 複製資料且不保存 paging cookie、
    /// upstream page 或 FetchXML；任何 connector page failure 必須在建立本 branch 前失敗關閉。
    /// </summary>
    [JsonPropertyName("meetingStatistics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MeetingStatisticRecord>? MeetingStatistics { get; }

    /// <summary>
    /// P7.4 認證聯絡人查詢的 bounded、immutable records。constructor 會複製集合，並且只允許安全分類為
    /// <see cref="AuthenticationContactReadSafetyClassification.Safe"/> 時含有資料；因此另一個 request、序列化器
    /// 或 connector 不可能在 envelope 建立後加入 contact、秘密或可變 CRM graph。
    /// </summary>
    [JsonPropertyName("authenticationContactReadRecords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AuthenticationContactReadRecord>? AuthenticationContactReadRecords { get; }

    /// <summary>
    /// ORG-CALL-00026 的 immutable 出席紀錄列快照。建構子會在目前 request 立即複製集合，讓
    /// connector、測試替身或上游可變 list 在 response 發佈後無法插入其他 contact 的列；row 僅有
    /// allowlisted scalar，且不擁有需 dispose 的資源。字數、UTF-8 可編碼性、唯一非空 ID 與總列數
    /// 都在同一個封閉 union 驗證，任何不合格資料皆不會形成 partial response。
    /// </summary>
    [JsonPropertyName("memberInfoPresentRecordReadRecords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MemberInfoPresentRecordReadRecord>? MemberInfoPresentRecordReadRecords { get; }

    /// <summary>
    /// ORG-CALL-00057 的 immutable App-named membership rows。constructor 立即複製輸入集合並封裝為唯讀清單，
    /// 因此 connector、測試替身或序列化前 caller 都不能在 envelope 發布後替換另一個 request 的列。row 僅包含
    /// allowlisted list GUID 與 nullable 名稱，不保存 CRM Entity、listmember、QueryExpression、cookie、profile、
    /// session、cache、stream、handle 或 cancellation registration；Data8 page、lease 與 transport 的唯一 owner
    /// 必須在建立 envelope 前釋放它們，取消、逾時或 fault 也不得發布 partial rows。
    /// </summary>
    [JsonPropertyName("appNamedMembershipRecords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AppNamedMembershipRecord>? AppNamedMembershipRecords { get; }

    /// <summary>
    /// P7 MemberInfo 的唯一 assignment evidence branch。它是已驗證 subject 對應的 request-local 純值快照；
    /// constructor 會在 branch validation 後接受此 immutable record，且 record 本身再防禦性複製 list GUID。
    /// 此 property 不能作為 profile、credential、connector、owner 或下一次 CRM 讀取的 caller-controlled authority。
    /// </summary>
    [JsonPropertyName("memberInfoAuthorizationAssignmentEvidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MemberInfoAuthorizationAssignmentEvidenceResponseData? MemberInfoAuthorizationAssignmentEvidence { get; }

    /// <summary>
    /// ORG-CALL-00031／00032 的唯一小組快照 branch。它只保存 subject、封閉 access mode、descriptor 純量列與
    /// membership 純量列；constructor 會在 branch validation 後再建立獨立 defensive copy。這個 property 不得被
    /// 用作 profile、credential、connector、owner、Session 或下一次 CRM query 的 caller authority，外部 page、lease、
    /// transport 與 cancellation registration 仍由 Data8 request owner 在建立 envelope 前確定釋放。
    /// </summary>
    [JsonPropertyName("memberInfoSmallGroupSnapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MemberInfoSmallGroupSnapshotResponseData? MemberInfoSmallGroupSnapshot { get; }

    /// <summary>
    /// P7.4 專用的非敏感安全分類。<see cref="AuthenticationContactReadSafetyClassification.SecretPresent"/> 只說明
    /// connector 偵測到禁止跨越邊界的資料，絕不攜帶其名稱、值、雜湊、來源 Entity 或原始 response；該分類時
    /// records 必須為空，產品端只能回傳固定 fail-closed 狀態。
    /// </summary>
    [JsonPropertyName("authenticationContactReadSafetyClassification")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AuthenticationContactReadSafetyClassification AuthenticationContactReadSafetyClassification { get; }

    /// <summary>
    /// Package 01 奉獻預約的 immutable wire rows。建構時會複製輸入集合，避免上游 CRM
    /// collection 在 response 發布後被修改而跨請求或跨使用者洩漏資料。
    /// </summary>
    [JsonPropertyName("dedicationBookingRecords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Package01DedicationBookingRecord>? DedicationBookingRecords { get; }

    /// <summary>
    /// P7.1 App-named 名單目錄的 immutable wire rows。constructor 在本次呼叫立即複製輸入集合，因此 upstream
    /// connector、測試替身或序列化前的 caller 無法在 envelope 發布後置換、加入或移除另一個 request 的資料。
    /// rows 僅含 allowlisted scalar，不保存 CRM Entity、paging cookie、profile、session、cache、stream、handle
    /// 或 cancellation registration；這個 envelope 不擁有外部資源，connector 必須在建立它前自行釋放資源。
    /// </summary>
    [JsonPropertyName("appNamedListCatalogRecords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AppNamedListCatalogRecord>? AppNamedListCatalogRecords { get; }

    /// <summary>
    /// P7.1 App-named 小組名單目錄的 immutable wire rows。constructor 在本次呼叫立即複製輸入集合，因此 upstream
    /// connector、測試替身或序列化前 caller 無法在 envelope 發布後置換、加入或移除另一個 request 的資料。rows 僅含
    /// allowlisted scalar 和 nullable leader GUID，不保存 CRM Entity、EntityReference 名稱、paging cookie、profile、
    /// session、cache、stream、handle 或 cancellation registration；connector 必須在建立 envelope 前釋放其外部資源。
    /// </summary>
    [JsonPropertyName("smallGroupAppNamedListCatalogRecords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SmallGroupAppNamedListCatalogRecord>? SmallGroupAppNamedListCatalogRecords { get; }

    /// <summary>
    /// 建立 WhoAmI branch。呼叫端在 connector request scope 完成原始 JSON 投影並 dispose 上游 response 後才可
    /// 呼叫；本方法只接受已投影的 GUID data，不接受 raw JSON 或 transport 物件。
    /// </summary>
    public static OperationResponseData ForWhoAmI(
        string operationId,
        string ceVersion,
        WhoAmIResponseData whoAmI)
    {
        ArgumentNullException.ThrowIfNull(whoAmI);
        return new OperationResponseData(operationId, ceVersion, OperationResponseKind.WhoAmI, whoAmI: whoAmI);
    }

    /// <summary>
    /// 建立 Package 1 fee branch。列舉結果會立即 materialize 成陣列，避免 caller 在非同步序列化、稽核或
    /// queue handoff 期間修改集合；不建立或保留 CRM stream、continuation 或認證狀態。
    /// </summary>
    public static OperationResponseData ForPackage01FeeRecords(
        string operationId,
        string ceVersion,
        IEnumerable<Package01FeeRecord> feeRecords)
    {
        ArgumentNullException.ThrowIfNull(feeRecords);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.Package01FeeRecords,
            feeRecords: feeRecords.ToArray());
    }

    /// <summary>
    /// 建立 Package 1 stor-lesson branch。列舉結果會立即 materialize 成陣列，確保 response envelope 是 request
    /// scope 可獨立轉移的值，而非外部可變 collection 或未 dispose 的 upstream page。
    /// </summary>
    public static OperationResponseData ForPackage01StorLessonRecords(
        string operationId,
        string ceVersion,
        IEnumerable<Package01StorLessonRecord> storLessonRecords)
    {
        ArgumentNullException.ThrowIfNull(storLessonRecords);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.Package01StorLessonRecords,
            storLessonRecords: storLessonRecords.ToArray());
    }

    /// <summary>
    /// 建立奉獻預約專用 response branch，並在 envelope 建構時 materialize 輸入集合。
    /// </summary>
    public static OperationResponseData ForPackage01DedicationBookingRecords(
        string operationId,
        string ceVersion,
        IEnumerable<Package01DedicationBookingRecord> dedicationBookingRecords)
    {
        ArgumentNullException.ThrowIfNull(dedicationBookingRecords);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.Package01DedicationBookingRecords,
            dedicationBookingRecords: dedicationBookingRecords.ToArray());
    }

    /// <summary>
    /// 建立 P7.1 App-named 名單目錄的唯一成功 branch。<paramref name="appNamedListCatalogRecords"/> 會在目前
    /// request scope 立刻 materialize，之後 envelope 也會再複製一次，使上游可變 collection 的生命週期不會
    /// 越過 Gateway/ProductClient 邊界。呼叫端必須先完成固定 template、分頁、列數與 byte 預算驗證，並釋放
    /// Data8 page、lease 與 transport 資源；取消、逾時、fault 或任一頁失敗時不得使用本 factory 發布 partial rows。
    /// </summary>
    /// <param name="operationId">固定的 server-owned ORG-CALL-00014 capability ID。</param>
    /// <param name="ceVersion">由 deployment-owned profile 已選定的 CE API 版本。</param>
    /// <param name="appNamedListCatalogRecords">本次 operation 投影出的純值、request-local 名單列。</param>
    /// <returns>只具有 catalog branch 的 immutable response envelope。</returns>
    public static OperationResponseData ForAppNamedListCatalogRecords(
        string operationId,
        string ceVersion,
        IEnumerable<AppNamedListCatalogRecord> appNamedListCatalogRecords)
    {
        ArgumentNullException.ThrowIfNull(appNamedListCatalogRecords);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.AppNamedListCatalogRecords,
            appNamedListCatalogRecords: appNamedListCatalogRecords.ToArray());
    }

    /// <summary>
    /// 建立 P7.1 App-named 小組名單目錄的唯一成功 branch。<paramref name="smallGroupAppNamedListCatalogRecords"/>
    /// 會在目前 request scope 立刻 materialize，之後 envelope 也會再 defensive-copy，使上游可變 collection 的
    /// 生命週期不會越過 Gateway/ProductClient 邊界。呼叫端必須先完成固定 template、leader lookup scalar、分頁、
    /// 列數與 byte 預算驗證並釋放 Data8 page、lease 與 transport；取消、逾時、fault 或任一頁失敗時不得發布 partial rows。
    /// </summary>
    /// <param name="operationId">固定的 server-owned ORG-CALL-00065 capability ID。</param>
    /// <param name="ceVersion">由 deployment-owned profile 已選定的 CE API 版本。</param>
    /// <param name="smallGroupAppNamedListCatalogRecords">本次 operation 投影出的純值、request-local 小組名單列。</param>
    /// <returns>只具有 small-group catalog branch 的 immutable response envelope。</returns>
    public static OperationResponseData ForSmallGroupAppNamedListCatalogRecords(
        string operationId,
        string ceVersion,
        IEnumerable<SmallGroupAppNamedListCatalogRecord> smallGroupAppNamedListCatalogRecords)
    {
        ArgumentNullException.ThrowIfNull(smallGroupAppNamedListCatalogRecords);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.SmallGroupAppNamedListCatalogRecords,
            smallGroupAppNamedListCatalogRecords: smallGroupAppNamedListCatalogRecords.ToArray());
    }

    /// <summary>
    /// 建立 ORG-CALL-00057 唯一成功 branch。<paramref name="appNamedMembershipRecords"/> 會在目前 request scope
    /// 先 materialize，constructor 再建立獨立唯讀快照，故 caller 的可變 collection、CRM page 或另一個 request
    /// 無法在 response 發布後插入或替換資料。呼叫端必須先完成固定 App-named/active/relationship filter、排序、
    /// 單頁、32 列與 32 KiB 驗證，並釋放 Data8 page、lease 與 transport；取消、逾時、fault、MoreRecords、重複
    /// 或 malformed ID 時不得以本 factory 發布 partial rows。
    /// </summary>
    /// <param name="operationId">固定的 server-owned ORG-CALL-00057 capability ID。</param>
    /// <param name="ceVersion">由 deployment-owned profile 已選定的 CE API 版本。</param>
    /// <param name="appNamedMembershipRecords">本次 operation 投影出的 list GUID 與 nullable 名稱純量列。</param>
    /// <returns>只具有 App-named membership branch 的 immutable response envelope。</returns>
    public static OperationResponseData ForAppNamedMembershipRecords(
        string operationId,
        string ceVersion,
        IEnumerable<AppNamedMembershipRecord> appNamedMembershipRecords)
    {
        ArgumentNullException.ThrowIfNull(appNamedMembershipRecords);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.AppNamedMembershipRecords,
            appNamedMembershipRecords: appNamedMembershipRecords.ToArray());
    }

    /// <summary>
    /// 建立 P7.4 認證聯絡人唯讀 branch。輸入列舉會在目前 request scope 立刻 materialize，讓未來 Data8 connector
    /// 可於釋放 EntityCollection、lease 與 transport response 後只留下 allowlisted pure values。安全分類為
    /// <see cref="AuthenticationContactReadSafetyClassification.SecretPresent"/> 時，<paramref name="records"/> 必須為空；
    /// factory 因而能傳達 fail-closed 原因而不投影或保存任何秘密。
    /// </summary>
    /// <param name="operationId">兩個 server-owned authentication capability 之一。</param>
    /// <param name="ceVersion">已由 deployment profile 選定的 CE API 版本。</param>
    /// <param name="records">只包含安全 contact scalar 的 request-local 投影。</param>
    /// <param name="safetyClassification">不含秘密內容的封閉安全分類。</param>
    /// <returns>具有唯一 authentication branch 的 immutable response envelope。</returns>
    public static OperationResponseData ForAuthenticationContactReadRecords(
        string operationId,
        string ceVersion,
        IEnumerable<AuthenticationContactReadRecord> records,
        AuthenticationContactReadSafetyClassification safetyClassification =
            AuthenticationContactReadSafetyClassification.Safe)
    {
        ArgumentNullException.ThrowIfNull(records);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.AuthenticationContactReadRecords,
            authenticationContactReadRecords: records.ToArray(),
            authenticationContactReadSafetyClassification: safetyClassification);
    }

    /// <summary>
    /// 建立 P7.2 會友基本資料更新的封閉成功 branch。呼叫端只能選擇有限 enum，不能附帶 contact、欄位值、
    /// CRM response 或自訂 correlation ID；<paramref name="disposition"/> 與
    /// <paramref name="correlationCategory"/> 的合法配對由這個 class 的單一驗證點確認。
    /// <see cref="ContactBasicInfoUpdateDisposition.NoChange"/> 表示 executor 在取得 connector lease 前發現沒有
    /// 可更新的 allowlisted 欄位；<see cref="ContactBasicInfoUpdateDisposition.Changed"/> 則只能在 Data8 寫入後
    /// read-back 完全確認時回傳。timeout 或任何未知結果不得建構成功 envelope，必須留在 fail-closed error path。
    /// </summary>
    public static OperationResponseData ForContactBasicInfoUpdate(
        string operationId,
        string ceVersion,
        ContactBasicInfoUpdateDisposition disposition,
        ContactBasicInfoUpdateCorrelationCategory correlationCategory)
        => new(
            operationId,
            ceVersion,
            OperationResponseKind.ContactBasicInfoUpdate,
            contactBasicInfoUpdate: new ContactBasicInfoUpdateResponseData
            {
                Disposition = disposition,
                CorrelationCategory = correlationCategory
            });

    /// <summary>
    /// 建立 P7.2 LINE profile write 的封閉成功 branch。Changed 只能與 ReadBackConfirmed 配對；timeout、
    /// partial update 或未知結果不得建構此 envelope，必須由 executor 回傳 sanitized failure 並交由 fixture reconcile。
    /// </summary>
    public static OperationResponseData ForContactLineProfileUpdate(
        string operationId,
        string ceVersion,
        ContactLineProfileUpdateDisposition disposition,
        ContactLineProfileUpdateCorrelationCategory correlationCategory)
        => new(
            operationId,
            ceVersion,
            OperationResponseKind.ContactLineProfileUpdate,
            contactLineProfileUpdate: new ContactLineProfileUpdateResponseData
            {
                Disposition = disposition,
                CorrelationCategory = correlationCategory
            });

    /// <summary>
    /// 建立 P7.2 ungrouped commitment aggregate 的封閉 branch。輸入先 materialize 為 request-owned array，
    /// constructor 再驗證數量、唯一 raw value 與非負 count；任何 invalid row 都 fail closed，不回傳 partial data。
    /// </summary>
    public static OperationResponseData ForUngroupedCommitmentCounts(
        string operationId,
        string ceVersion,
        IEnumerable<UngroupedCommitmentCountRecord> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.UngroupedCommitmentCounts,
            ungroupedCommitmentCounts: counts.ToArray());
    }

    /// <summary>
    /// 建立 static-list member add/remove 的封閉結果。NoChange 只可表示 pre-read 已證實集合處於目標狀態；
    /// Changed 則必須已完成 fixed action 與 membership read-back。timeout、部分集合或未知結果不得建構成功回應。
    /// </summary>
    public static OperationResponseData ForStaticListMembershipMutation(
        string operationId,
        string ceVersion,
        P72ControlledMutationDisposition disposition,
        P72ControlledMutationCorrelationCategory correlationCategory)
        => new(
            operationId,
            ceVersion,
            OperationResponseKind.StaticListMembershipMutation,
            staticListMembershipMutation: new StaticListMembershipMutationResponseData
            {
                Disposition = disposition,
                CorrelationCategory = correlationCategory
            });

    /// <summary>
    /// 建立小組六欄 fixed-mode write 的封閉結果。NoChange 代表完整 six-field projection 已相同；Changed
    /// 只能在所有 set/clear 欄位 read-back 確認後回傳。
    /// </summary>
    public static OperationResponseData ForSmallGroupFixedFieldsMutation(
        string operationId,
        string ceVersion,
        P72ControlledMutationDisposition disposition,
        P72ControlledMutationCorrelationCategory correlationCategory)
        => new(
            operationId,
            ceVersion,
            OperationResponseKind.SmallGroupFixedFieldsMutation,
            smallGroupFixedFieldsMutation: new SmallGroupFixedFieldsMutationResponseData
            {
                Disposition = disposition,
                CorrelationCategory = correlationCategory
            });

    /// <summary>
    /// 建立 contact owner assignment 的封閉結果。NoChange 只在 pre-read owner 已符合時成立；Changed 必須由 Assign
    /// 後 ownerid read-back 證實，絕不把 transport completion 當作 assignment success。
    /// </summary>
    public static OperationResponseData ForContactOwnerAssignment(
        string operationId,
        string ceVersion,
        P72ControlledMutationDisposition disposition,
        P72ControlledMutationCorrelationCategory correlationCategory)
        => new(
            operationId,
            ceVersion,
            OperationResponseKind.ContactOwnerAssignment,
            contactOwnerAssignment: new ContactOwnerAssignmentResponseData
            {
                Disposition = disposition,
                CorrelationCategory = correlationCategory
            });

    /// <summary>
    /// 建立 contact list-transfer composite 的封閉結果。NoChange 必須先確認所有 target graph 都已完成；Changed
    /// 則僅在 membership、present record、primary lookup 與 optional owner 全部 read-back 後成立。
    /// </summary>
    public static OperationResponseData ForContactListTransfer(
        string operationId,
        string ceVersion,
        P72ControlledMutationDisposition disposition,
        P72ControlledMutationCorrelationCategory correlationCategory)
        => new(
            operationId,
            ceVersion,
            OperationResponseKind.ContactListTransfer,
            contactListTransfer: new ContactListTransferResponseData
            {
                Disposition = disposition,
                CorrelationCategory = correlationCategory
            });

    /// <summary>
    /// 建立 P7.3 image read branch。影像 bytes 由 <paramref name="contactImage"/> constructor 與自己的 getter
    /// defensive copy；這個 factory 不接受 raw stream 或 SDK object，也不承擔 decoder/buffer 的 disposal。
    /// </summary>
    public static OperationResponseData ForContactImage(
        string operationId,
        string ceVersion,
        ContactImageResponseData contactImage)
    {
        ArgumentNullException.ThrowIfNull(contactImage);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.ContactImage,
            contactImage: contactImage);
    }

    /// <summary>
    /// 建立 P7.4 聯絡人顯示聯集。display branch 已在其 factory 中完成資料驗證與防禦性複製；
    /// envelope 只允許此單一 branch，避免影像、重新導向與頭像資料混合發布。
    /// </summary>
    public static OperationResponseData ForContactImageDisplay(
        string operationId,
        string ceVersion,
        ContactImageDisplayResponseData contactImageDisplay)
    {
        ArgumentNullException.ThrowIfNull(contactImageDisplay);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.ContactImageDisplay,
            contactImageDisplay: contactImageDisplay);
    }

    /// <summary>
    /// 建立 P7.3 image update 的封閉成功 branch。只有 Changed/ReadBackConfirmed 的合法配對能通過 union 驗證；
    /// timeout、ambiguous transport、read-back mismatch 或 cleanup uncertainty 必須由 executor 回傳失敗，不能呼叫此 factory。
    /// </summary>
    public static OperationResponseData ForContactImageUpdate(
        string operationId,
        string ceVersion,
        ContactImageUpdateDisposition disposition,
        ContactImageUpdateCorrelationCategory correlationCategory)
        => new(
            operationId,
            ceVersion,
            OperationResponseKind.ContactImageUpdate,
            contactImageUpdate: new ContactImageUpdateResponseData
            {
                Disposition = disposition,
                CorrelationCategory = correlationCategory
            });

    /// <summary>
    /// 建立 P7.3 metadata OptionSet branch。輸入立即 materialize，constructor 再驗證 value/order/label 的 bounded
    /// 純值規則；AttributeMetadata、LocalizedLabel、exception、profile/generation cache key 均不可成為 response。
    /// </summary>
    public static OperationResponseData ForOptionSetOptions(
        string operationId,
        string ceVersion,
        IEnumerable<OptionSetOptionRecord> optionSetOptions)
    {
        ArgumentNullException.ThrowIfNull(optionSetOptions);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.OptionSetOptions,
            optionSetOptions: optionSetOptions.ToArray());
    }

    /// <summary>
    /// 建立 P7.3 weekly meeting statistics branch。輸入先 materialize，讓 upstream page collection/cookie 不會被
    /// envelope 保留；任何 page failure、超限或 schema mismatch 都必須在 connector scope 內阻止呼叫此 factory。
    /// </summary>
    public static OperationResponseData ForMeetingStatistics(
        string operationId,
        string ceVersion,
        IEnumerable<MeetingStatisticRecord> meetingStatistics)
    {
        ArgumentNullException.ThrowIfNull(meetingStatistics);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.MeetingStatistics,
            meetingStatistics: meetingStatistics.ToArray());
    }

    /// <summary>
    /// 建立 ORG-CALL-00026 的唯一成功 response branch。列舉在 factory 與 constructor 均會 materialize
    /// 成 request-local 陣列；呼叫端必須先完成 authorization 以及固定單頁 query 的 schema/MoreRecords
    /// 驗證。此方法不建立或持有 CRM client、lease、profile、cache、stream、timer 或背景工作，外部資源
    /// 的唯一 owner 仍是 connector/host，取消、fault 與 timeout 時不得呼叫本方法發佈 partial rows。
    /// </summary>
    /// <param name="operationId">僅能對應 server-owned ORG-CALL-00026 的 capability ID。</param>
    /// <param name="ceVersion">已由 deployment routing 確認的 CE version；此 abstraction 不做版本選擇。</param>
    /// <param name="memberInfoPresentRecordReadRecords">已完整驗證的純量列集合。</param>
    /// <returns>只含此分支且具有獨立集合快照的封閉 response envelope。</returns>
    public static OperationResponseData ForMemberInfoPresentRecordReadRecords(
        string operationId,
        string ceVersion,
        IEnumerable<MemberInfoPresentRecordReadRecord> memberInfoPresentRecordReadRecords)
    {
        ArgumentNullException.ThrowIfNull(memberInfoPresentRecordReadRecords);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.MemberInfoPresentRecordReadRecords,
            memberInfoPresentRecordReadRecords: memberInfoPresentRecordReadRecords.ToArray());
    }

    /// <summary>
    /// 建立 P7 MemberInfo assignment evidence 的唯一成功 branch。呼叫端必須已在 Data8 lease scope 完成固定 subject
    /// contact retrieve、Church-wide precedence、六個 assignment lookup、TopCount 513 overflow 偵測與 row validation；
    /// cancellation、fault、paging、重複或不完整資料不得呼叫此 factory。此方法不配置 cache、connector、lease、
    /// Session、timer 或背景工作，外部資源的 release 仍由 executor 的單一 owner 負責。
    /// </summary>
    /// <param name="operationId">固定 server-owned assignment evidence capability ID。</param>
    /// <param name="ceVersion">由 deployment-owned profile 確定的 CE version。</param>
    /// <param name="memberInfoAuthorizationAssignmentEvidence">已驗證且 immutable 的目前 subject evidence。</param>
    /// <returns>只含 assignment evidence branch 的封閉 response envelope。</returns>
    public static OperationResponseData ForMemberInfoAuthorizationAssignmentEvidence(
        string operationId,
        string ceVersion,
        MemberInfoAuthorizationAssignmentEvidenceResponseData memberInfoAuthorizationAssignmentEvidence)
    {
        ArgumentNullException.ThrowIfNull(memberInfoAuthorizationAssignmentEvidence);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.MemberInfoAssignmentEvidence,
            memberInfoAuthorizationAssignmentEvidence: memberInfoAuthorizationAssignmentEvidence);
    }

    /// <summary>
    /// 建立 ORG-CALL-00031／00032 的唯一 composed snapshot branch。呼叫端必須已在 CE 9.1 Data8 request scope 完成
    /// scope、descriptor、membership、metadata、paging、duplicate、row、UTF-8 與 byte budget 驗證；factory 只接受
    /// 三個 server-owned scalar 對應出的 immutable snapshot，並由 envelope 再複製 records。取消、逾時、fault 或
    /// partial page 不得呼叫此方法，外部 transport／lease／permit 的 deterministic cleanup 仍由 executor 擁有。
    /// </summary>
    /// <param name="operationId">固定的 MemberInfo 小組快照 capability ID。</param>
    /// <param name="ceVersion">部署 profile 已選定且由 executor 驗證的 CE 版本。</param>
    /// <param name="memberInfoSmallGroupSnapshot">本次 request 的純量 descriptor／membership snapshot。</param>
    /// <returns>只有 MemberInfo small-group snapshot branch 的 immutable response envelope。</returns>
    public static OperationResponseData ForMemberInfoSmallGroupSnapshot(
        string operationId,
        string ceVersion,
        MemberInfoSmallGroupSnapshotResponseData memberInfoSmallGroupSnapshot)
    {
        ArgumentNullException.ThrowIfNull(memberInfoSmallGroupSnapshot);
        return new OperationResponseData(
            operationId,
            ceVersion,
            OperationResponseKind.MemberInfoSmallGroupSnapshot,
            memberInfoSmallGroupSnapshot: memberInfoSmallGroupSnapshot);
    }

    /// <summary>
    /// 建立明確的 unsupported envelope。connector/Gateway 應把它轉成受控失敗，而不是把未投影 metadata、
    /// OData annotation 或 endpoint detail 回傳給產品；此值不擁有背景資源或可清理 handle。
    /// </summary>
    public static OperationResponseData Unsupported(string operationId, string ceVersion)
        => new(operationId, ceVersion, OperationResponseKind.Unsupported);

    private static void ValidateSingleSafeBranch(
        OperationResponseKind responseKind,
        WhoAmIResponseData? whoAmI,
        IReadOnlyList<Package01FeeRecord>? feeRecords,
        IReadOnlyList<Package01StorLessonRecord>? storLessonRecords,
        ContactBasicInfoUpdateResponseData? contactBasicInfoUpdate,
        ContactLineProfileUpdateResponseData? contactLineProfileUpdate,
        IReadOnlyList<UngroupedCommitmentCountRecord>? ungroupedCommitmentCounts,
        StaticListMembershipMutationResponseData? staticListMembershipMutation,
        SmallGroupFixedFieldsMutationResponseData? smallGroupFixedFieldsMutation,
        ContactOwnerAssignmentResponseData? contactOwnerAssignment,
        ContactListTransferResponseData? contactListTransfer,
        ContactImageResponseData? contactImage,
        ContactImageDisplayResponseData? contactImageDisplay,
        ContactImageUpdateResponseData? contactImageUpdate,
        IReadOnlyList<OptionSetOptionRecord>? optionSetOptions,
        IReadOnlyList<MeetingStatisticRecord>? meetingStatistics,
        IReadOnlyList<Package01DedicationBookingRecord>? dedicationBookingRecords,
        IReadOnlyList<AppNamedListCatalogRecord>? appNamedListCatalogRecords,
        IReadOnlyList<SmallGroupAppNamedListCatalogRecord>? smallGroupAppNamedListCatalogRecords,
        IReadOnlyList<AuthenticationContactReadRecord>? authenticationContactReadRecords,
        IReadOnlyList<MemberInfoPresentRecordReadRecord>? memberInfoPresentRecordReadRecords,
        IReadOnlyList<AppNamedMembershipRecord>? appNamedMembershipRecords,
        MemberInfoAuthorizationAssignmentEvidenceResponseData? memberInfoAuthorizationAssignmentEvidence,
        MemberInfoSmallGroupSnapshotResponseData? memberInfoSmallGroupSnapshot,
        AuthenticationContactReadSafetyClassification authenticationContactReadSafetyClassification)
    {
        // 先計算所有非 null branch，再比對 discriminator；這在反序列化入口也生效，避免使用者或上游資料透過
        // 多 branch 讓資料跨 capability 混合。失敗時不保留任何集合或外部資源。
        var branchCount = (whoAmI is null ? 0 : 1) +
                          (feeRecords is null ? 0 : 1) +
                          (storLessonRecords is null ? 0 : 1) +
                          (contactBasicInfoUpdate is null ? 0 : 1) +
                          (contactLineProfileUpdate is null ? 0 : 1) +
                          (ungroupedCommitmentCounts is null ? 0 : 1) +
                          (staticListMembershipMutation is null ? 0 : 1) +
                          (smallGroupFixedFieldsMutation is null ? 0 : 1) +
                          (contactOwnerAssignment is null ? 0 : 1) +
                          (contactListTransfer is null ? 0 : 1) +
                          (contactImage is null ? 0 : 1) +
                          (contactImageDisplay is null ? 0 : 1) +
                          (contactImageUpdate is null ? 0 : 1) +
                           (optionSetOptions is null ? 0 : 1) +
                           (meetingStatistics is null ? 0 : 1) +
                           (dedicationBookingRecords is null ? 0 : 1) +
                           (appNamedListCatalogRecords is null ? 0 : 1) +
                           (smallGroupAppNamedListCatalogRecords is null ? 0 : 1) +
                           (authenticationContactReadRecords is null ? 0 : 1) +
                          (memberInfoPresentRecordReadRecords is null ? 0 : 1) +
                          (appNamedMembershipRecords is null ? 0 : 1) +
                          (memberInfoAuthorizationAssignmentEvidence is null ? 0 : 1) +
                          (memberInfoSmallGroupSnapshot is null ? 0 : 1);
        var isValid = responseKind switch
        {
            OperationResponseKind.Unsupported => branchCount == 0,
            OperationResponseKind.WhoAmI => branchCount == 1 && whoAmI is not null,
            OperationResponseKind.Package01FeeRecords => branchCount == 1 && feeRecords is not null,
            OperationResponseKind.Package01StorLessonRecords => branchCount == 1 && storLessonRecords is not null,
            OperationResponseKind.ContactBasicInfoUpdate =>
                branchCount == 1 &&
                contactBasicInfoUpdate is not null &&
                IsValidContactBasicInfoUpdate(contactBasicInfoUpdate),
            OperationResponseKind.ContactLineProfileUpdate =>
                branchCount == 1 &&
                contactLineProfileUpdate is not null &&
                IsValidContactLineProfileUpdate(contactLineProfileUpdate),
            OperationResponseKind.UngroupedCommitmentCounts =>
                branchCount == 1 &&
                ungroupedCommitmentCounts is not null &&
                IsValidUngroupedCommitmentCounts(ungroupedCommitmentCounts),
            OperationResponseKind.StaticListMembershipMutation =>
                branchCount == 1 &&
                staticListMembershipMutation is not null &&
                IsValidP72ControlledMutation(staticListMembershipMutation),
            OperationResponseKind.SmallGroupFixedFieldsMutation =>
                branchCount == 1 &&
                smallGroupFixedFieldsMutation is not null &&
                IsValidP72ControlledMutation(smallGroupFixedFieldsMutation),
            OperationResponseKind.ContactOwnerAssignment =>
                branchCount == 1 &&
                contactOwnerAssignment is not null &&
                IsValidP72ControlledMutation(contactOwnerAssignment),
            OperationResponseKind.ContactListTransfer =>
                branchCount == 1 &&
                contactListTransfer is not null &&
                IsValidP72ControlledMutation(contactListTransfer),
            OperationResponseKind.ContactImage =>
                branchCount == 1 &&
                contactImage is not null &&
                IsValidContactImage(contactImage),
            OperationResponseKind.ContactImageDisplay =>
                branchCount == 1 &&
                contactImageDisplay is not null &&
                IsValidContactImageDisplay(contactImageDisplay),
            OperationResponseKind.ContactImageUpdate =>
                branchCount == 1 &&
                contactImageUpdate is not null &&
                IsValidContactImageUpdate(contactImageUpdate),
            OperationResponseKind.OptionSetOptions =>
                branchCount == 1 &&
                optionSetOptions is not null &&
                IsValidOptionSetOptions(optionSetOptions),
            OperationResponseKind.MeetingStatistics =>
                branchCount == 1 &&
                meetingStatistics is not null &&
                IsValidMeetingStatistics(meetingStatistics),
            OperationResponseKind.Package01DedicationBookingRecords => branchCount == 1 && dedicationBookingRecords is not null,
            OperationResponseKind.AppNamedListCatalogRecords => branchCount == 1 && appNamedListCatalogRecords is not null,
            OperationResponseKind.SmallGroupAppNamedListCatalogRecords => branchCount == 1 && smallGroupAppNamedListCatalogRecords is not null,
            OperationResponseKind.AuthenticationContactReadRecords =>
                branchCount == 1 &&
                authenticationContactReadRecords is not null &&
                IsValidAuthenticationContactReadRecords(
                    authenticationContactReadRecords,
                    authenticationContactReadSafetyClassification),
            OperationResponseKind.MemberInfoPresentRecordReadRecords =>
                branchCount == 1 &&
                memberInfoPresentRecordReadRecords is not null &&
                IsValidMemberInfoPresentRecordReadRecords(memberInfoPresentRecordReadRecords),
            OperationResponseKind.AppNamedMembershipRecords => branchCount == 1 && appNamedMembershipRecords is not null &&
                IsValidAppNamedMembershipRecords(appNamedMembershipRecords),
            OperationResponseKind.MemberInfoAssignmentEvidence =>
                branchCount == 1 &&
                memberInfoAuthorizationAssignmentEvidence is not null &&
                IsValidMemberInfoAuthorizationAssignmentEvidence(memberInfoAuthorizationAssignmentEvidence),
            OperationResponseKind.MemberInfoSmallGroupSnapshot =>
                branchCount == 1 &&
                memberInfoSmallGroupSnapshot is not null &&
                IsValidMemberInfoSmallGroupSnapshot(memberInfoSmallGroupSnapshot),
            _ => false
        };

        if (!isValid)
        {
            throw new ArgumentException(
                "responseKind must select exactly one matching safe response branch.",
                nameof(responseKind));
        }
    }

    /// <summary>
    /// 驗證 P7.4 authentication branch 的 bounded 純值列與安全分類配對。資料列上限使錯誤上游回應不能無界保留
    /// contact DTO；秘密分類只允許空集合，讓任何偵測到的敏感欄位在 connector scope 釋放後僅留下固定分類。
    /// 本方法不記錄、快取或複製原始 SDK/transport 資料。
    /// </summary>
    private static bool IsValidAuthenticationContactReadRecords(
        IReadOnlyList<AuthenticationContactReadRecord> records,
        AuthenticationContactReadSafetyClassification safetyClassification)
    {
        if (!Enum.IsDefined(safetyClassification) || records.Count > MaximumAuthenticationContactReadRecords)
        {
            return false;
        }

        if (safetyClassification == AuthenticationContactReadSafetyClassification.SecretPresent)
        {
            return records.Count == 0;
        }

        return records.All(record =>
            record is not null &&
            record.ContactId != Guid.Empty &&
            IsBoundedAuthenticationContactText(record.AccountLocator) &&
            IsBoundedAuthenticationContactText(record.DisplayName));
    }

    /// <summary>
    /// 驗證 ORG-CALL-00026 row 的封閉純量合約。HashSet 只存在於 constructor 呼叫期間，完成後不會
    /// 保留 contact 或 response 狀態；因此它既能防止同一 response 混入重複 identity，也不會成為
    /// 跨 request cache。日期保持原始 <see cref="DateTime"/> 語意，不做 UTC 或時區轉換；缺值及
    /// 年份小於等於一由上游投影成 null，避免本 abstraction 改寫既有 Sunday-date 行為。
    /// </summary>
    /// <param name="records">欲建立 response 的 request-local 純量列。</param>
    /// <returns>全部列皆符合數量、ID 與文字界限時為 <see langword="true"/>。</returns>
    private static bool IsValidMemberInfoPresentRecordReadRecords(
        IReadOnlyList<MemberInfoPresentRecordReadRecord> records)
    {
        if (records.Count > MaximumMemberInfoPresentRecordReadRecords)
        {
            return false;
        }

        var ids = new HashSet<Guid>();
        var totalBytes = 0;
        foreach (var record in records)
        {
            if (record is null ||
                record.PresentRecordId == Guid.Empty ||
                !ids.Add(record.PresentRecordId) ||
                !IsValidMemberInfoPresentRecordDate(record.SundayDate) ||
                !TryAddMemberInfoPresentRecordBytes(ref totalBytes, record))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 ORG-CALL-00057 row 的唯一純量合約。HashSet 與 byte counter 只存在於 constructor 驗證期間，完成後
    /// 不保留 list identity、名稱或 response 狀態；因而可拒絕空白／重複 GUID、超過 32 列、無效 UTF-8 與超過
    /// 32 KiB 的資料，同時避免把 request-local membership 資料變成跨 request cache。
    /// </summary>
    /// <param name="records">欲建立 response 的 request-local list membership 純量列。</param>
    /// <returns>全部列皆符合數量、ID、唯一性與位元組預算時為 <see langword="true"/>。</returns>
    private static bool IsValidAppNamedMembershipRecords(IReadOnlyList<AppNamedMembershipRecord> records)
    {
        if (records.Count > MaximumAppNamedMembershipRecords)
        {
            return false;
        }

        var ids = new HashSet<Guid>();
        var totalBytes = 0;
        foreach (var record in records)
        {
            if (record is null ||
                record.ListId == Guid.Empty ||
                !ids.Add(record.ListId) ||
                !TryAddAppNamedMembershipRecordBytes(ref totalBytes, record))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 P7 assignment evidence 的 subject、access mode 與 bounded list snapshot。HashSet 只在 constructor
    /// validation 期間存活，不會把 user identity 或 authorization list 留在 static/cache/session；Church-wide 必須
    /// 沒有 list，assigned-list 則必須由 0 至 512 個非空且唯一 GUID 組成。任何歧義都拒絕 envelope。
    /// </summary>
    /// <param name="evidence">由固定 Data8 operation 投影出的 immutable evidence。</param>
    /// <returns>evidence 符合封閉 subject authorization contract 時為 <see langword="true"/>。</returns>
    private static bool IsValidMemberInfoAuthorizationAssignmentEvidence(
        MemberInfoAuthorizationAssignmentEvidenceResponseData evidence)
    {
        if (evidence.SubjectContactId == Guid.Empty ||
            !Enum.IsDefined(evidence.AccessMode) ||
            evidence.AssignedListIds.Count > MaximumMemberInfoAuthorizationAssignmentListIds)
        {
            return false;
        }

        if (evidence.AccessMode == MemberInfoAuthorizationAssignmentAccessMode.ChurchWide)
        {
            return evidence.AssignedListIds.Count == 0;
        }

        var unique = new HashSet<Guid>();
        return evidence.AssignedListIds.All(id => id != Guid.Empty && unique.Add(id));
    }

    /// <summary>
    /// 驗證 ORG-CALL-00031／00032 的 subject、mode、descriptor 與 membership closed union。descriptor identity 必須
    /// 唯一且不超過 512；membership identity 以 list/contact pair 唯一、最多 4,096，且每一個 list ID 必須來自同一
    /// response 的 descriptor set。HashSet 與 byte counter 只在目前 constructor frame 存活，不進入 static、cache、
    /// Session 或下一個 request；任何不確定資料均在 publish 前 fail closed。
    /// </summary>
    /// <param name="snapshot">目前 Data8 request scope 投影出的純量小組快照。</param>
    /// <returns>整個快照符合 bounded immutable contract 時為 <see langword="true"/>。</returns>
    private static bool IsValidMemberInfoSmallGroupSnapshot(
        MemberInfoSmallGroupSnapshotResponseData snapshot)
    {
        if (snapshot.SubjectContactId == Guid.Empty ||
            !Enum.IsDefined(snapshot.AccessMode) ||
            snapshot.Descriptors.Count > MaximumMemberInfoSmallGroupDescriptorRecords ||
            snapshot.Memberships.Count > MaximumMemberInfoSmallGroupMembershipRecords)
        {
            return false;
        }

        var descriptorIds = new HashSet<Guid>();
        var memberIds = new HashSet<(Guid ListId, Guid ContactId)>();
        var totalBytes = 0;
        foreach (var descriptor in snapshot.Descriptors)
        {
            if (descriptor is null ||
                descriptor.ListId == Guid.Empty ||
                !descriptorIds.Add(descriptor.ListId) ||
                (descriptor.RaceLeaderContactId is Guid raceLeaderId && raceLeaderId == Guid.Empty) ||
                !IsValidMemberInfoSmallGroupText(descriptor.ListName) ||
                !IsValidMemberInfoSmallGroupText(descriptor.AreaName) ||
                !IsValidMemberInfoSmallGroupText(descriptor.RaceLeaderName) ||
                !IsValidMemberInfoSmallGroupText(descriptor.GroupLeaderName) ||
                !IsValidMemberInfoSmallGroupText(descriptor.GroupTime) ||
                !IsValidMemberInfoSmallGroupText(descriptor.GroupPlace) ||
                !TryAddMemberInfoSmallGroupDescriptorBytes(ref totalBytes, descriptor))
            {
                return false;
            }
        }

        foreach (var membership in snapshot.Memberships)
        {
            if (membership is null ||
                membership.ListId == Guid.Empty ||
                membership.ContactId == Guid.Empty ||
                !descriptorIds.Contains(membership.ListId) ||
                !memberIds.Add((membership.ListId, membership.ContactId)) ||
                !TryAddMemberInfoSmallGroupMembershipBytes(ref totalBytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 descriptor／membership 使用的每一個文字 scalar。長度與 strict UTF-8 byte count 同時受限；無效
    /// surrogate、超過 512 個 UTF-16 scalar 或無法以 UTF-8 編碼的文字都立即拒絕，避免序列化器替換字元、建立
    /// 不可預期的 response 大小，或把另一個 request 的錯誤內容保留下來。
    /// </summary>
    /// <param name="value">已由 fixed projection 取得的 nullable display scalar。</param>
    /// <returns>值為 null 或符合字元與 UTF-8 byte bound 時為 <see langword="true"/>。</returns>
    private static bool IsValidMemberInfoSmallGroupText(string? value)
    {
        if (value is null || value.Length > MaximumMemberInfoSmallGroupTextCharacters)
        {
            return value is null;
        }

        try
        {
            return StrictUtf8.GetByteCount(value) <= MaximumMemberInfoSmallGroupTextBytes;
        }
        catch (System.Text.EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// 將 descriptor 的固定 GUID 成本與六個 optional display scalar 累加到整體 snapshot byte budget。計數器
    /// 僅屬於目前 union validation；checked overflow、無效 surrogate 或超過 1 MiB 都 fail closed，不能發布 partial
    /// descriptors 或把暫存 buffer 交給長生命週期 caller。
    /// </summary>
    /// <param name="totalBytes">本次 response 的 request-local 累積 byte 計數。</param>
    /// <param name="descriptor">要加入累積預算的 descriptor scalar。</param>
    /// <returns>加入後仍符合整體 response budget 時為 <see langword="true"/>。</returns>
    private static bool TryAddMemberInfoSmallGroupDescriptorBytes(
        ref int totalBytes,
        MemberInfoSmallGroupDescriptorRecord descriptor)
    {
        if (!TryAddMemberInfoSmallGroupBytes(ref totalBytes, 32))
        {
            return false;
        }

        foreach (var value in new[]
        {
            descriptor.ListName,
            descriptor.AreaName,
            descriptor.RaceLeaderName,
            descriptor.GroupLeaderName,
            descriptor.GroupTime,
            descriptor.GroupPlace
        })
        {
            if (value is null)
            {
                continue;
            }

            try
            {
                if (!TryAddMemberInfoSmallGroupBytes(ref totalBytes, StrictUtf8.GetByteCount(value)))
                {
                    return false;
                }
            }
            catch (System.Text.EncoderFallbackException)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 將固定 membership pair 成本加入 snapshot response budget。membership 沒有名稱、EntityReference、query 或
    /// metadata，只保留 list/contact GUID；這個 helper 不保存 pair 或建立任何可釋放的資源。
    /// </summary>
    /// <param name="totalBytes">本次 response 的 request-local 累積 byte 計數。</param>
    /// <returns>加入一列後仍在 1 MiB 上限內時為 <see langword="true"/>。</returns>
    private static bool TryAddMemberInfoSmallGroupMembershipBytes(ref int totalBytes)
        => TryAddMemberInfoSmallGroupBytes(ref totalBytes, MemberInfoSmallGroupMembershipFixedRowBytes);

    /// <summary>
    /// 以 checked 算術維持整個 composed snapshot 的 bounded byte budget。overflow、負值或累積值超過固定上限時
    /// 回傳 false，使 caller 在建立 envelope 前停止，不會保留 partial response 或跨 request 共用 buffer。
    /// </summary>
    /// <param name="totalBytes">目前 request-local 累積值。</param>
    /// <param name="additionalBytes">欲加入的已嚴格驗證 byte 數。</param>
    /// <returns>累積值仍在 1 MiB 上限內時為 <see langword="true"/>。</returns>
    private static bool TryAddMemberInfoSmallGroupBytes(ref int totalBytes, int additionalBytes)
    {
        if (additionalBytes < 0)
        {
            return false;
        }

        try
        {
            totalBytes = checked(totalBytes + additionalBytes);
            return totalBytes <= MaximumMemberInfoSmallGroupResponseBytes;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// 將固定 list GUID 結構成本與 nullable list name 的嚴格 UTF-8 byte 數加入 ORG-CALL-00057 封閉預算。
    /// 此計數器只由目前 constructor 持有；checked overflow、無效 surrogate 或超限均立即失敗關閉，不會產生
    /// partial envelope、shared buffer 或可跨 profile 重用的名稱集合。
    /// </summary>
    /// <param name="totalBytes">目前 request-local envelope 的累積估算位元組數。</param>
    /// <param name="record">待驗證的 list membership 純量列。</param>
    /// <returns>row 可在不超過 32 KiB response hard limit 時加入 envelope。</returns>
    private static bool TryAddAppNamedMembershipRecordBytes(
        ref int totalBytes,
        AppNamedMembershipRecord record)
    {
        if (!TryAddAppNamedMembershipBytes(ref totalBytes, AppNamedMembershipFixedRowBytes))
        {
            return false;
        }

        if (record.ListName is null)
        {
            return true;
        }

        try
        {
            return TryAddAppNamedMembershipBytes(ref totalBytes, StrictUtf8.GetByteCount(record.ListName));
        }
        catch (System.Text.EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// 以 checked 算術累加 ORG-CALL-00057 的固定 32 KiB response budget。數值只在單次 union 驗證內存活，
    /// 不會成為 static/session/cache state；overflow 或超限一律回傳 false，使呼叫端不能發布部分 membership rows。
    /// </summary>
    /// <param name="totalBytes">目前 request-local 累積數。</param>
    /// <param name="additionalBytes">欲加入的已驗證固定或 UTF-8 純量位元組數。</param>
    /// <returns>累積後仍在 ORG-CALL-00057 安全上限內時為 <see langword="true"/>。</returns>
    private static bool TryAddAppNamedMembershipBytes(ref int totalBytes, int additionalBytes)
    {
        try
        {
            totalBytes = checked(totalBytes + additionalBytes);
            return totalBytes <= MaximumAppNamedMembershipResponseBytes;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// 驗證 Sunday-date 保持既有語意。connector 已把缺值或年份小於等於一投影為 null；union 再次拒絕
    /// 年份小於等於一的非 null 值，以免錯誤 transport/JSON payload 重建無效日期。沒有任何時區轉換，
    /// 因此不會把另一使用者的 locale、session 或時區狀態帶入 response。
    /// </summary>
    /// <param name="value">已投影的可選 Sunday 日期。</param>
    /// <returns>值為 null 或是有效 legacy-compatible 日期時為 <see langword="true"/>。</returns>
    private static bool IsValidMemberInfoPresentRecordDate(DateTime? value)
        => value is null || value.Value.Year > 1;

    /// <summary>
    /// 將一筆 row 的固定 JSON 結構成本及兩段文字計入 256 KiB response hard limit。計數器與 HashSet
    /// 只在 constructor 驗證期間存活，成功後不保存 byte counter 或 identity state；checked overflow、
    /// 無效 UTF-8、單欄超限或總額超限皆立即失敗關閉，避免 partial response 保留在 request 之外。
    /// </summary>
    /// <param name="totalBytes">目前 request-local envelope 的累積估算位元組數。</param>
    /// <param name="record">待驗證的純量 row。</param>
    /// <returns>row 可在不超過安全上限時加入 response。</returns>
    private static bool TryAddMemberInfoPresentRecordBytes(
        ref int totalBytes,
        MemberInfoPresentRecordReadRecord record)
    {
        if (!TryAddBoundedMemberInfoPresentRecordBytes(ref totalBytes, MemberInfoPresentRecordFixedRowBytes) ||
            !TryAddBoundedMemberInfoPresentRecordTextBytes(ref totalBytes, record.ContactFullName) ||
            !TryAddBoundedMemberInfoPresentRecordTextBytes(ref totalBytes, record.PrayItem))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 驗證可選顯示文字的字元與 UTF-8 byte 上限，並把實際 byte 數加入目前 response 的固定預算。
    /// null 合法代表 legacy source 未提供顯示值；helper 不配置共享 cache，也不記錄文字內容，故不會
    /// 延長任何 contact 資料生命週期。
    /// </summary>
    /// <param name="value">可為 null 的純量顯示文字。</param>
    /// <returns>文字可安全納入封閉 response 時為 <see langword="true"/>。</returns>
    private static bool TryAddBoundedMemberInfoPresentRecordTextBytes(ref int totalBytes, string? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value.Length > MaximumMemberInfoPresentRecordTextCharacters)
        {
            return false;
        }

        try
        {
            var textBytes = StrictUtf8.GetByteCount(value);
            return textBytes <= MaximumMemberInfoPresentRecordTextBytes &&
                   TryAddBoundedMemberInfoPresentRecordBytes(ref totalBytes, textBytes);
        }
        catch (System.Text.EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// 以 checked 算術累加封閉 response 預算。此數值沒有 static owner 且不會離開 constructor；任何
    /// overflow 或超過 registry 256 KiB hard limit 都回傳 false，讓呼叫端不建立 envelope。
    /// </summary>
    /// <param name="totalBytes">目前 request-local 累積數。</param>
    /// <param name="additionalBytes">欲加入的已驗證固定或 UTF-8 文字位元組數。</param>
    /// <returns>累積後仍在有界 response 預算內時為 <see langword="true"/>。</returns>
    private static bool TryAddBoundedMemberInfoPresentRecordBytes(ref int totalBytes, int additionalBytes)
    {
        try
        {
            totalBytes = checked(totalBytes + additionalBytes);
            return totalBytes <= MaximumMemberInfoPresentRecordResponseBytes;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// 驗證 authentication contact 的 locator/display 是 bounded、非空且可嚴格編碼的文字。此限制只約束
    /// immutable wire 值，沒有建立 shared cache、租用 buffer 或跨 request retained state。
    /// </summary>
    private static bool IsBoundedAuthenticationContactText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
        {
            return false;
        }

        try
        {
            _ = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (System.Text.EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// 驗證 P7.2 寫入結果的兩個 enum 必須是已知且可安全解釋的配對。這裡是 JSON constructor 與 public factory
    /// 共用的防線，避免反序列化或未來呼叫端用未定義的 enum 數值偽造「已確認」結果；此方法不配置或保留任何
    /// client、lease、stream、timer、cache 或 session 狀態。
    /// </summary>
    private static bool IsValidContactBasicInfoUpdate(ContactBasicInfoUpdateResponseData response)
    {
        if (!Enum.IsDefined(response.Disposition) || !Enum.IsDefined(response.CorrelationCategory))
        {
            return false;
        }

        return response.Disposition switch
        {
            ContactBasicInfoUpdateDisposition.NoChange =>
                response.CorrelationCategory == ContactBasicInfoUpdateCorrelationCategory.NoDispatch,
            ContactBasicInfoUpdateDisposition.Changed =>
                response.CorrelationCategory == ContactBasicInfoUpdateCorrelationCategory.ReadBackConfirmed,
            _ => false
        };
    }

    /// <summary>
    /// 驗證 LINE profile 成功結果的 enum 與配對，防止 unknown JSON enum 或 NoChange 偽裝成已完成的 write。
    /// 此方法只讀純值，不保留 request、URL、profile 或 connector 資源。
    /// </summary>
    private static bool IsValidContactLineProfileUpdate(ContactLineProfileUpdateResponseData response)
        => Enum.IsDefined(response.Disposition) &&
           Enum.IsDefined(response.CorrelationCategory) &&
           response.Disposition == ContactLineProfileUpdateDisposition.Changed &&
           response.CorrelationCategory == ContactLineProfileUpdateCorrelationCategory.ReadBackConfirmed;

    /// <summary>
    /// 驗證 aggregate row 數量有限、raw value 唯一且 count 非負。HashSet 只活在 constructor scope，
    /// 最多 4096 個整數，驗證完成後立即可回收，不形成跨 request／tenant cache。
    /// </summary>
    private static bool IsValidUngroupedCommitmentCounts(IReadOnlyList<UngroupedCommitmentCountRecord> counts)
    {
        if (counts.Count > MaximumUngroupedCommitmentCountRecords)
        {
            return false;
        }

        var values = new HashSet<int>();
        foreach (var row in counts)
        {
            if (row is null || row.Count < 0 || !values.Add(row.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 Slice C 四個封閉 response branch 共用的成功狀態機。NoChange 必須配 NoDispatch，Changed 必須配
    /// ReadBackConfirmed；其他 enum 數值、timeout、partial graph 或 cleanup ambiguity 永遠不能偽裝為成功。
    /// </summary>
    private static bool IsValidP72ControlledMutation(IP72ControlledMutationResponse response)
        => Enum.IsDefined(response.Disposition) &&
           Enum.IsDefined(response.CorrelationCategory) &&
           response.Disposition switch
           {
               P72ControlledMutationDisposition.NoChange =>
                   response.CorrelationCategory == P72ControlledMutationCorrelationCategory.NoDispatch,
               P72ControlledMutationDisposition.Changed =>
                   response.CorrelationCategory == P72ControlledMutationCorrelationCategory.ReadBackConfirmed,
               _ => false
           };

    /// <summary>
    /// 驗證 image read payload 只使用定義過的 media kind 且 bytes 已由封閉 payload owner 複製。實際格式、
    /// dimensions、pixels 與 wire-size 檢查屬 connector/executor 的更早防線；response union 不保存 decoder/stream。
    /// </summary>
    private static bool IsValidContactImage(ContactImageResponseData response)
        => Enum.IsDefined(response.MediaKind);

    /// <summary>
    /// 驗證 P7.4 顯示封閉聯集的唯一分支。驗證使用 getter 的複本，因此不會把可變位元組陣列外洩給 envelope 或後續請求。
    /// </summary>
    private static bool IsValidContactImageDisplay(ContactImageDisplayResponseData response)
    {
        try
        {
            return response.Kind switch
            {
                ContactImageDisplayKind.Image =>
                    response.MediaKind is { } mediaKind &&
                    Enum.IsDefined(mediaKind) &&
                    response.LineRedirectUri is null &&
                    response.GenderCode is null &&
                    response.GetImageBytes().Length > 0,
                ContactImageDisplayKind.LineRedirect =>
                    response.MediaKind is null &&
                    response.GenderCode is null &&
                    response.LineRedirectUri is { IsAbsoluteUri: true } uri &&
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(uri.UserInfo) &&
                    string.IsNullOrEmpty(uri.Fragment),
                ContactImageDisplayKind.DefaultAvatar =>
                    response.MediaKind is null &&
                    response.LineRedirectUri is null,
                _ => false
            };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// 驗證 image write 成功只能表示 Changed 加 ReadBackConfirmed。這避免 timeout-after-dispatch、partial update
    /// 或未知 cleanup outcome 被序列化成成功，且此純值檢查不保留 fixture、image、lease 或 session。
    /// </summary>
    private static bool IsValidContactImageUpdate(ContactImageUpdateResponseData response)
        => Enum.IsDefined(response.Disposition) &&
           Enum.IsDefined(response.CorrelationCategory) &&
           response.Disposition == ContactImageUpdateDisposition.Changed &&
           response.CorrelationCategory == ContactImageUpdateCorrelationCategory.ReadBackConfirmed;

    /// <summary>
    /// 驗證 metadata option collection 的固定邊界。HashSet 只存在 constructor scope；value/order 必須各自唯一，
    /// label 不能為空或超限，避免 raw metadata/無界 localized string 進入 response/cache。
    /// </summary>
    private static bool IsValidOptionSetOptions(IReadOnlyList<OptionSetOptionRecord> options)
    {
        if (options.Count > MaximumOptionSetOptionRecords)
        {
            return false;
        }

        var values = new HashSet<int>();
        var orders = new HashSet<int>();
        foreach (var option in options)
        {
            if (option is null ||
                string.IsNullOrWhiteSpace(option.Label) ||
                option.Label.Length > MaximumOptionSetLabelCharacters ||
                option.ConfiguredOrder < 0 ||
                !values.Add(option.Value) ||
                !orders.Add(option.ConfiguredOrder))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 驗證 meeting result 的 row count、non-empty identity 與 bounded display name。paging cookie/page object 不在
    /// DTO 中；每列只能保留 primitive projections，杜絕跨 request/profile 的 continuation 或 CRM Entity retention。
    /// </summary>
    private static bool IsValidMeetingStatistics(IReadOnlyList<MeetingStatisticRecord> statistics)
    {
        if (statistics.Count > MaximumMeetingStatisticRecords)
        {
            return false;
        }

        var ids = new HashSet<Guid>();
        foreach (var statistic in statistics)
        {
            if (statistic is null ||
                statistic.MeetingStatisticId == Guid.Empty ||
                !ids.Add(statistic.MeetingStatisticId) ||
                statistic.Name?.Length > MaximumMeetingNameCharacters)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// P7.2 會友基本資料更新的受控結果種類。此 enum 只描述實際 mutation 是否發生，不能承載 contact、欄位值、
/// endpoint、credential、profile、token 或 request identifier；unknown、timeout 與 cleanup ambiguity 不屬於成功結果，
/// 必須由 executor 以 fail-closed error 回傳並讓 fixture bridge 依 read-back 規則處理。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactBasicInfoUpdateDisposition
{
    /// <summary>
    /// 沒有 allowlisted 欄位需要更新；executor 不得取得 connector lease 或呼叫 CE，並以
    /// <see cref="ContactBasicInfoUpdateCorrelationCategory.NoDispatch"/> 證明沒有 outbound write。
    /// </summary>
    NoChange = 0,

    /// <summary>
    /// allowlisted 寫入已完成，且兩個允許欄位的 read-back 已確認預期狀態；不能用於 timeout、部分讀回或
    /// 任意 CRM 回應，這些情況必須中止並保留 sanitized no-go evidence。
    /// </summary>
    Changed = 1
}

/// <summary>
/// P7.2 寫入結果可公開的固定 correlation 分類。它不是 correlation ID，故不會把 trace、使用者、contact、
/// profile、session 或 credential 關聯資料帶出 connector request scope；每個值僅描述可由本 capability
/// 安全證明的 bounded lifecycle outcome。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactBasicInfoUpdateCorrelationCategory
{
    /// <summary>
    /// 因為沒有有效的 allowlisted 欄位而沒有 dispatch；此值保證沒有 connector lease、CE client 或 outbound
    /// write 被建立，避免 no-change 路徑意外保留不必要的資源。
    /// </summary>
    NoDispatch = 0,

    /// <summary>
    /// 已完成一次 bounded update 後的 allowlisted read-back 與預期一致。它不表示 timeout 可重試，也不提供
    /// 任何可追溯至 contact 或 CRM transport 的識別資料。
    /// </summary>
    ReadBackConfirmed = 1
}

/// <summary>
/// P7.2 寫入 capability 的最小、安全 wire payload。這個 record 故意只有兩個 enum，藉此防止產品端重新接觸
/// 原始 Entity、OData JSON、CRM 欄位名稱或 fixture baseline；它不擁有非受控資源，也不得被用作跨請求、
/// 跨使用者、跨 profile 或跨 tenant 的 mutable cache/session state。
/// </summary>
public sealed record ContactBasicInfoUpdateResponseData
{
    /// <summary>
    /// 表示是否發生已確認的 allowlisted mutation；合法值及與 correlation 分類的配對由
    /// <see cref="OperationResponseData"/> 在 envelope 建構時驗證。
    /// </summary>
    [JsonPropertyName("disposition")]
    public required ContactBasicInfoUpdateDisposition Disposition { get; init; }

    /// <summary>
    /// 表示不洩漏識別資料的 lifecycle correlation 類別。它只能是 no-dispatch 或 read-back-confirmed，不能替代
    /// idempotency key、trace ID、credential reference 或 fixture identity。
    /// </summary>
    [JsonPropertyName("correlationCategory")]
    public required ContactBasicInfoUpdateCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>
/// LINE profile write 的受控 mutation 結果。只有 Changed 可用；不存在 NoChange 或 Unknown，避免未送出、
/// partial 或 timeout-after-dispatch 被誤報為成功。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactLineProfileUpdateDisposition
{
    /// <summary>三個固定 LINE profile 欄位已寫入，且 connector read-back 完全確認。</summary>
    Changed = 1
}

/// <summary>
/// LINE profile write 的安全 correlation category。它不是 correlation ID，不含 contact、LINE、profile 或 trace identity。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactLineProfileUpdateCorrelationCategory
{
    /// <summary>固定欄位 update 後的 bounded read-back 與預期完全一致。</summary>
    ReadBackConfirmed = 1
}

/// <summary>
/// LINE profile write 的最小安全 wire payload。它不保存欄位值或外部資源，合法 enum 配對由
/// <see cref="OperationResponseData"/> constructor 與 factory 共用驗證。
/// </summary>
public sealed record ContactLineProfileUpdateResponseData
{
    /// <summary>表示唯一合法的已確認 mutation 結果。</summary>
    [JsonPropertyName("disposition")]
    public required ContactLineProfileUpdateDisposition Disposition { get; init; }

    /// <summary>表示 connector 已完成 allowlisted read-back。</summary>
    [JsonPropertyName("correlationCategory")]
    public required ContactLineProfileUpdateCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>
/// P7.2 Slice C 固定 CRM mutation 的共用、封閉結果種類。它只描述 operation 是否由完整 pre-read/read-back
/// 證實，不攜帶 entity、欄位值、GUID、credential、profile、token、session、lease 或 CRM response。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum P72ControlledMutationDisposition
{
    /// <summary>完整 target projection 已相符；connector 不得取得 lease 或送出 CRM mutation。</summary>
    NoChange = 0,

    /// <summary>固定 action/write 已執行且完整 target projection 已 read-back 確認。</summary>
    Changed = 1
}

/// <summary>
/// P7.2 Slice C 受控 mutation 的安全 lifecycle 分類。它不是可追蹤的 correlation ID，故不能作為 contact、
/// list、weekly report、owner、fixture 或 credential 的旁路識別資料。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum P72ControlledMutationCorrelationCategory
{
    /// <summary>pre-read 證實無需 mutation，沒有 connector lease 或 outbound CRM request。</summary>
    NoDispatch = 0,

    /// <summary>完整固定 graph 已由 read-back 確認；這不表示未知 timeout 可以被盲目重送。</summary>
    ReadBackConfirmed = 1
}

/// <summary>
/// Slice C 回應 branch 共用的最小結構。每一個 concrete branch 仍有獨立 discriminator，避免 static membership、
/// small-group field、owner assignment 與 transfer composite 在產品端被混用。
/// </summary>
public interface IP72ControlledMutationResponse
{
    /// <summary>取得受控 mutation 是否被完整證實的 bounded 結果。</summary>
    P72ControlledMutationDisposition Disposition { get; }

    /// <summary>取得不含識別資料的 no-dispatch/read-back-confirmed 分類。</summary>
    P72ControlledMutationCorrelationCategory CorrelationCategory { get; }
}

/// <summary>static-list add/remove action 的封閉 response payload。</summary>
public sealed record StaticListMembershipMutationResponseData : IP72ControlledMutationResponse
{
    /// <summary>取得 membership target set 是否已確認。</summary>
    [JsonPropertyName("disposition")]
    public required P72ControlledMutationDisposition Disposition { get; init; }

    /// <summary>取得無識別資料的 reconciliation 分類。</summary>
    [JsonPropertyName("correlationCategory")]
    public required P72ControlledMutationCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>小組六欄 fixed-mode update 的封閉 response payload。</summary>
public sealed record SmallGroupFixedFieldsMutationResponseData : IP72ControlledMutationResponse
{
    /// <summary>取得六欄 projection 是否已完全確認。</summary>
    [JsonPropertyName("disposition")]
    public required P72ControlledMutationDisposition Disposition { get; init; }

    /// <summary>取得無識別資料的 reconciliation 分類。</summary>
    [JsonPropertyName("correlationCategory")]
    public required P72ControlledMutationCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>contact Assign action 的封閉 response payload。</summary>
public sealed record ContactOwnerAssignmentResponseData : IP72ControlledMutationResponse
{
    /// <summary>取得 ownerid 是否已確認為目標狀態。</summary>
    [JsonPropertyName("disposition")]
    public required P72ControlledMutationDisposition Disposition { get; init; }

    /// <summary>取得無識別資料的 reconciliation 分類。</summary>
    [JsonPropertyName("correlationCategory")]
    public required P72ControlledMutationCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>contact transfer composite 的封閉 response payload。</summary>
public sealed record ContactListTransferResponseData : IP72ControlledMutationResponse
{
    /// <summary>取得完整 membership／record／lookup／owner graph 是否已確認。</summary>
    [JsonPropertyName("disposition")]
    public required P72ControlledMutationDisposition Disposition { get; init; }

    /// <summary>取得無識別資料的 reconciliation 分類。</summary>
    [JsonPropertyName("correlationCategory")]
    public required P72ControlledMutationCorrelationCategory CorrelationCategory { get; init; }
}

/// <summary>
/// 未分組 commitment aggregate 的安全純值 row。Value 只作為產品 metadata segment key，不能當作排序順位；
/// Count 必須非負。此 record 不含 CRM SDK、Entity、FetchXML、label 或 contact identity。
/// </summary>
public sealed record UngroupedCommitmentCountRecord
{
    /// <summary>取得 raw contact.customertypecode OptionSet value；產品仍依 metadata sequence 排序。</summary>
    [JsonPropertyName("value")]
    public required int Value { get; init; }

    /// <summary>取得該 raw value 的非負 contact 筆數。</summary>
    [JsonPropertyName("count")]
    public required int Count { get; init; }
}

/// <summary>
/// WhoAmI 的最小安全投影。GUID 僅供受控 runtime/產品連線驗證使用；不保存 CRM 使用者名稱、組織 URL、
/// access token、cookie 或原始 OData response，因此不會形成可跨 request 重用的認證/工作階段狀態。
/// </summary>
public sealed record WhoAmIResponseData
{
    [JsonPropertyName("userId")]
    public Guid? UserId { get; init; }

    [JsonPropertyName("businessUnitId")]
    public Guid? BusinessUnitId { get; init; }

    [JsonPropertyName("organizationId")]
    public Guid? OrganizationId { get; init; }
}

/// <summary>
/// Package 1 fee 的 shared wire record。欄位與 ProductClient 的 FeeRecordDto 一一對應，但本專案不參考
/// ProductClient，因此抽象層仍可作為 Gateway/Embedded 的唯一安全邊界。nullable 欄位保留舊產品的相容性；
/// Amount 的零預設值避免缺欄時產生未初始化 money 物件或 raw OData money wrapper。
/// </summary>
public sealed record Package01FeeRecord
{
    [JsonPropertyName("feeId")]
    public Guid? FeeId { get; init; }

    [JsonPropertyName("createdOn")]
    public DateTimeOffset? CreatedOn { get; init; }

    [JsonPropertyName("payDate")]
    public DateTimeOffset? PayDate { get; init; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; } = 0m;

    [JsonPropertyName("payWayOption")]
    public int? PayWayOption { get; init; }

    [JsonPropertyName("payWayLabel")]
    public string? PayWayLabel { get; init; }

    [JsonPropertyName("categoryLabel")]
    public string? CategoryLabel { get; init; }

    [JsonPropertyName("others")]
    public string? Others { get; init; }

    [JsonPropertyName("paidPeriod")]
    public string? PaidPeriod { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>
/// Package 01 奉獻預約的 immutable wire projection。僅包含 ProductClient 所需的 allowlisted
/// scalar 值，不攜帶 CRM Entity、查詢、profile、credential、session 或其他可跨請求保留的狀態。
/// </summary>
public sealed record Package01DedicationBookingRecord
{
    /// <summary>奉獻預約識別碼；缺欄時保留 null，不以 caller 輸入補值。</summary>
    [JsonPropertyName("dedicationBookingId")]
    public Guid? DedicationBookingId { get; init; }

    /// <summary>奉獻類別的 CRM option-set 原始值。</summary>
    [JsonPropertyName("dedicationCategoryOption")]
    public int? DedicationCategoryOption { get; init; }

    /// <summary>奉獻類別的伺服器格式化標籤。</summary>
    [JsonPropertyName("dedicationCategoryLabel")]
    public string? DedicationCategoryLabel { get; init; }

    /// <summary>奉獻預約狀態的 CRM option-set 原始值。</summary>
    [JsonPropertyName("dedicationBookingStatusOption")]
    public int? DedicationBookingStatusOption { get; init; }

    /// <summary>奉獻預約狀態的伺服器格式化標籤。</summary>
    [JsonPropertyName("dedicationBookingStatusLabel")]
    public string? DedicationBookingStatusLabel { get; init; }

    /// <summary>每期奉獻金額。</summary>
    [JsonPropertyName("amountPerStage")]
    public decimal? AmountPerStage { get; init; }

    /// <summary>奉獻總期數的原始文字表示。</summary>
    [JsonPropertyName("totalStages")]
    public string? TotalStages { get; init; }

    /// <summary>奉獻預約總金額。</summary>
    [JsonPropertyName("dedicationAmount")]
    public decimal? DedicationAmount { get; init; }

    /// <summary>目前已付款期別。</summary>
    [JsonPropertyName("paidPeriod")]
    public string? PaidPeriod { get; init; }

    /// <summary>CRM rollup 計算的累計已繳金額。</summary>
    [JsonPropertyName("rollupPaidFee")]
    public decimal? RollupPaidFee { get; init; }

    /// <summary>奉獻預約起始 UTC 時間。</summary>
    [JsonPropertyName("startDate")]
    public DateTimeOffset? StartDate { get; init; }

    /// <summary>奉獻預約結束 UTC 時間。</summary>
    [JsonPropertyName("endDate")]
    public DateTimeOffset? EndDate { get; init; }
}

/// <summary>
/// ORG-CALL-00026 的不可變 wire row。它只描述一筆已由固定 CE 9.1 查詢投影完成的出席紀錄，
/// 因而不包含 CRM <c>Entity</c>、lookup、profile、credential、session、cache、connector、lease、
/// stream、timer、background task 或 cancellation token。每次 response 建構會複製列集合，並在
/// <see cref="OperationResponseData"/> 中驗證唯一非空 ID 與文字界限，避免另一 request 從可變上游
/// 集合重用或混入資料；此 record 本身不擁有外部資源，無需 dispose。
/// </summary>
public sealed record MemberInfoPresentRecordReadRecord
{
    /// <summary>
    /// <c>new_present_record</c> 的非空主鍵。response union 會在同一 envelope 內拒絕重複值，讓
    /// consumer 不會將不同來源或重複頁面資料誤合併；此 GUID 不代表授權權限，也不能選擇 profile。
    /// </summary>
    [JsonPropertyName("presentRecordId")]
    public Guid PresentRecordId { get; init; }

    /// <summary>
    /// 從固定 contact lookup 投影的可選顯示名稱。它只用於維持既有 MemberInfo row 顯示，且受
    /// constructor 的字元與 UTF-8 byte 界限約束；不保存 contact Entity、Session 或其他個資圖形。
    /// </summary>
    [JsonPropertyName("contactFullName")]
    public string? ContactFullName { get; init; }

    /// <summary>
    /// <c>new_sunday_date</c> 的可選日期值。null 代表來源缺值或年份小於等於一；此欄位刻意保留
    /// 原始 <see cref="DateTime"/> 語意，不進行 UTC、Local 或使用者時區轉換，以免改變既有顯示契約。
    /// </summary>
    [JsonPropertyName("sundayDate")]
    public DateTime? SundayDate { get; init; }

    /// <summary>
    /// 固定 schema 解析後的主日出席旗標。它是 closed boolean，不攜帶原始 OptionSet 或 formatted value，
    /// 避免 metadata、locale 或 session 狀態越過 abstraction 邊界。
    /// </summary>
    [JsonPropertyName("sunday")]
    public bool Sunday { get; init; }

    /// <summary>
    /// 固定 schema 解析後的小組出席旗標。connector 必須在投影前拒絕未知原始值，本 response 僅保留
    /// 無狀態的 bool，不能用於選取或重用連線、profile 或 credential。
    /// </summary>
    [JsonPropertyName("smallGroup")]
    public bool SmallGroup { get; init; }

    /// <summary>
    /// <c>new_explanation</c> 的可選有界說明文字。值在 response 建構時經過嚴格 UTF-8 byte 驗證，
    /// 不會建立共用 cache 或診斷保留；null 代表來源未提供說明而非另一筆 contact 的 fallback。
    /// </summary>
    [JsonPropertyName("prayItem")]
    public string? PrayItem { get; init; }
}

/// <summary>
/// P7.1 App-named 名單目錄的 immutable closed wire record。這個 record 只投影固定 FetchXML 可取得的五個
/// allowlisted scalar，讓未來產品在不接觸 CRM <c>Entity</c>、<c>EntityCollection</c>、formatted-value metadata、
/// QueryExpression、FetchXML、profile、credential、cookie、continuation 或 transport 資源的情況下呈現目錄。
/// 它不擁有或快取 request/user/profile state；每個實例只隨本次 response envelope 存活，集合 owner 會在
/// <see cref="OperationResponseData"/> 建構時 defensive copy。connector 必須在此 record 建立前完成 UTC 正規化、
/// page/byte/row bounds 驗證並釋放 page、lease 與 stream；取消、逾時或 fault 不得發布 partial record 集合。
/// </summary>
public sealed record AppNamedListCatalogRecord
{
    /// <summary>
    /// 名單唯一識別碼。固定 projection 的有效列必須由 connector 驗證其非空；wire contract 使用 non-null GUID，
    /// 使未來 consumer 不必從名稱、profile 或 caller input 推導資料定位或授權範圍。
    /// </summary>
    [JsonPropertyName("listId")]
    public Guid ListId { get; init; }

    /// <summary>
    /// 名單顯示名稱的純文字投影。它可為 null，以保留上游缺欄語意；文字的 UTF-8 byte budget、截斷拒絕與資料
    /// 所屬隔離由 connector request scope 負責，record 本身不會將內容寫入共享 cache、log 或 session。
    /// </summary>
    [JsonPropertyName("listName")]
    public string? ListName { get; init; }

    /// <summary>
    /// CRM <c>createdfromcode</c> 的原始 option-set 數值。null 表示上游未提供值；禁止以 formatted label、
    /// metadata graph 或 caller-selected locale 補值，避免 metadata/session state 越過封閉 response 邊界。
    /// </summary>
    [JsonPropertyName("createdFromCodeOption")]
    public int? CreatedFromCodeOption { get; init; }

    /// <summary>
    /// CRM <c>lastusedon</c> 已正規化為 UTC 的時間值。null 保留上游缺值；connector 是唯一的時區轉換 owner，
    /// 必須在釋放 CRM page 前完成轉換，record 不保存時區快取、使用者 locale、session 或可變 DateTime graph。
    /// </summary>
    [JsonPropertyName("lastUsedOn")]
    public DateTimeOffset? LastUsedOn { get; init; }

    /// <summary>
    /// CRM <c>purpose</c> 的純 scalar 投影。null 不代表可由 consumer 補查或變更 server filter；固定 purpose
    /// 條件仍完全由 server-owned template 擁有，這個欄位只用於安全呈現且不攜帶 Entity 或查詢資訊。
    /// </summary>
    [JsonPropertyName("purpose")]
    public string? Purpose { get; init; }
}

/// <summary>
/// P7.1 App-named 小組名單目錄的 immutable closed wire record。此 record 與一般 app-named catalog 明確分離，
/// 只投影固定 template 可取得的七個 allowlisted scalar：list ID、名稱、created-from code、UTC last-used、purpose
/// 及兩個 leader contact GUID。它不攜帶 CRM <c>Entity</c>、<c>EntityCollection</c>、<c>EntityReference.Name</c>、
/// formatted values、QueryExpression、FetchXML、profile、credential、cookie、continuation 或 transport 資源。
/// 每個實例只隨本次 response envelope 存活，集合 owner 會在 <see cref="OperationResponseData"/> 建構時 defensive-copy；
/// connector 必須在建立 record 前完成 UTC 正規化、leader ID 複製與 page/byte/row bounds 驗證，並釋放 page、lease、
/// stream。取消、逾時或 fault 不得發布 partial record 集合，也不得將資料存入 session、shared cache 或 static state。
/// </summary>
public sealed record SmallGroupAppNamedListCatalogRecord
{
    /// <summary>
    /// 小組名單唯一識別碼。固定 projection 的有效列必須由 connector 驗證其非空；wire contract 使用 non-null GUID，
    /// 使後續 consumer 不會從名稱、leader、profile 或 caller input 推導資料定位或授權範圍。
    /// </summary>
    [JsonPropertyName("listId")]
    public Guid ListId { get; init; }

    /// <summary>
    /// 小組名單顯示名稱的純文字投影。它可為 null，以保留上游缺欄語意；UTF-8 byte budget、截斷拒絕與資料所屬
    /// 隔離由 connector request scope 負責，record 不會將內容寫入 shared cache、log、session 或另一個 request。
    /// </summary>
    [JsonPropertyName("listName")]
    public string? ListName { get; init; }

    /// <summary>
    /// CRM <c>createdfromcode</c> 的原始 option-set 數值。null 表示上游未提供值；禁止以 formatted label、
    /// metadata graph 或 caller-selected locale 補值，避免 metadata/session state 越過封閉 response 邊界。
    /// </summary>
    [JsonPropertyName("createdFromCodeOption")]
    public int? CreatedFromCodeOption { get; init; }

    /// <summary>
    /// CRM <c>lastusedon</c> 已正規化為 UTC 的時間值。null 保留上游缺值；connector 是唯一時區轉換 owner，
    /// 必須在釋放 CRM page 前完成轉換，record 不保存時區快取、使用者 locale、session 或可變 DateTime graph。
    /// </summary>
    [JsonPropertyName("lastUsedOn")]
    public DateTimeOffset? LastUsedOn { get; init; }

    /// <summary>
    /// CRM <c>purpose</c> 的純 scalar 投影。null 不代表 consumer 可補查或變更 server filter；固定 purpose 條件
    /// 仍完全由 server-owned template 擁有，此欄位只用於安全呈現且不攜帶 Entity 或 query 資訊。
    /// </summary>
    [JsonPropertyName("purpose")]
    public string? Purpose { get; init; }

    /// <summary>
    /// CRM <c>new_contact_race_leager_list</c> lookup 的純 GUID 投影。null 保留無 leader 語意；connector 只能複製
    /// <c>EntityReference.Id</c>，不得保留名稱、EntityReference、本地快取或另一個 profile 的 contact graph。
    /// </summary>
    [JsonPropertyName("raceLeaderContactId")]
    public Guid? RaceLeaderContactId { get; init; }

    /// <summary>
    /// CRM <c>new_contact_family_leader_list</c> lookup 的純 GUID 投影。null 保留無 leader 語意；它不提供聯絡人名稱、
    /// 權限、session 或路由 authority，未來 consumer 如需資料必須在其已驗證的 request scope 另行授權讀取。
    /// </summary>
    [JsonPropertyName("familyLeaderContactId")]
    public Guid? FamilyLeaderContactId { get; init; }
}

/// <summary>
/// ORG-CALL-00057 的 immutable closed wire row。這個 record 只投影固定 QueryExpression 已確認的 list GUID 與
/// nullable list name，讓未來產品不用接觸 CRM <c>Entity</c>、<c>EntityCollection</c>、listmember、lookup、
/// formatted values、QueryExpression、profile、credential、cookie、continuation 或 transport 資源即可呈現已授權
/// contact 的 App-named membership。它不擁有或快取 request/user/profile state；<see cref="OperationResponseData"/>
/// 會建立列集合的獨立唯讀快照，connector 則必須在建立本 record 前完成 App-named/active/relationship filter、
/// 排序、single-page、列數、byte 與 duplicate-ID 驗證並釋放 page、lease 與 stream。取消、逾時或 fault 不得發布
/// partial records，也不能將名稱或 list identity 寫入 session、shared cache 或 static state。
/// </summary>
public sealed record AppNamedMembershipRecord
{
    /// <summary>
    /// 名單唯一識別碼。固定 projection 的有效列必須是非空 GUID，並在單一 response 中唯一；它是已授權 contact
    /// relationship 的結果值，不是讓 caller 選取 profile、connector、endpoint、credential 或其他資料的路由 authority。
    /// </summary>
    [JsonPropertyName("listId")]
    public Guid ListId { get; init; }

    /// <summary>
    /// 名單顯示名稱的 nullable 純文字投影。null 保留來源未提供名稱的語意，不能由 consumer 補查或以另一個
    /// request 的資料 fallback；constructor 以嚴格 UTF-8 和 32 KiB response budget 驗證，record 本身不會
    /// 將內容保存至 cache、session、log、queue 或任何需要 dispose 的資源。
    /// </summary>
    [JsonPropertyName("listName")]
    public string? ListName { get; init; }
}

/// <summary>
/// P7 MemberInfo assignment evidence 的封閉存取模式。它只描述 executor 已從固定 contact/list schema 驗證的
/// server-owned 結論，不能由 browser、Session、legacy login type、profile、credential 或 caller value 選取；
/// 未定義數值與 list/mode 不一致會由 <see cref="OperationResponseData"/> fail closed。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemberInfoAuthorizationAssignmentAccessMode
{
    /// <summary>
    /// subject 具有固定 Church-wide 職務。這個模式不得攜帶 assigned list，避免將較窄或另一人的 allowlist
    /// 混入全教會授權結果；它不是 feature gate、traffic enablement 或 CE evidence。
    /// </summary>
    ChurchWide = 1,

    /// <summary>
    /// subject 僅能以固定六個 lookup 查出的 active、app-named、有效日內小組 GUID 作為可見範圍。
    /// 零筆 list 是合法的 denied scope；不會因此重新查詢 legacy manager、Session 或 browser 值。
    /// </summary>
    AssignedLists = 2
}

/// <summary>
/// P7 MemberInfo 固定 operation 的 immutable subject assignment evidence。此型別只保留 subject GUID、封閉模式及
/// 已複製的 list GUID snapshot；不包含 job title、CRM Entity、EntityReference、QueryExpression、profile、endpoint、
/// credential、cookie、Session、cache、connector、lease、raw exception 或任何可釋放資源。它由目前 request 的
/// Data8 executor 產生，constructor 立即複製 collection，避免呼叫端或 serializer 在 authorization 與 adapter mapping
/// 間插入另一位使用者的 list；外部 lease/permit/client 仍由 executor owner 在 finally 中釋放。
/// </summary>
public sealed class MemberInfoAuthorizationAssignmentEvidenceResponseData
{
    /// <summary>
    /// 建立封閉 evidence snapshot，並在此資料邊界防禦性複製 incoming list IDs。這裡不推論 Church-wide、
    /// 不排序、不去重且不存取 CRM；完整 schema、重複、bound 與模式驗證屬 response union 的單一 fail-closed
    /// 防線。constructor 沒有 cache、timer、stream 或 background work，因此沒有額外資源釋放責任。
    /// </summary>
    /// <param name="subjectContactId">固定 operation request 所對應的 server-validated subject GUID。</param>
    /// <param name="accessMode">Data8 executor 已投影的封閉 server-owned access mode。</param>
    /// <param name="assignedListIds">目前 request 取得、將被複製的 GUID collection。</param>
    [JsonConstructor]
    public MemberInfoAuthorizationAssignmentEvidenceResponseData(
        Guid subjectContactId,
        MemberInfoAuthorizationAssignmentAccessMode accessMode,
        IReadOnlyList<Guid> assignedListIds)
    {
        ArgumentNullException.ThrowIfNull(assignedListIds);
        SubjectContactId = subjectContactId;
        AccessMode = accessMode;
        AssignedListIds = Array.AsReadOnly(assignedListIds.ToArray());
    }

    /// <summary>
    /// 已由伺服器驗證 Cookie scope 並傳至固定 operation 的 subject GUID。這是 response 與 request scope 的比對值，
    /// 不是 browser target、owner、profile、organization 或 connector selector；空 GUID 會由 envelope 拒絕。
    /// </summary>
    [JsonPropertyName("subjectContactId")]
    public Guid SubjectContactId { get; }

    /// <summary>
    /// Church-wide 或 assigned-list 的封閉 server evidence mode。未定義數值不能越過 response union，也不能由
    /// legacy login type 或 consumer fallback 補正。
    /// </summary>
    [JsonPropertyName("accessMode")]
    public MemberInfoAuthorizationAssignmentAccessMode AccessMode { get; }

    /// <summary>
    /// 防禦性複製後的目前 request list GUID snapshot。collection 不可由 caller 替換或加入元素；空集合僅在
    /// Church-wide 或沒有有效 assignment 的確定語意下使用，絕不觸發 Session、cache、legacy ListManager 或第二條 CRM I/O。
    /// </summary>
    [JsonPropertyName("assignedListIds")]
    public IReadOnlyList<Guid> AssignedListIds { get; }
}

/// <summary>
/// Package 1 stor lesson 的 shared wire record。欄位與 ProductClient 的 StorLessonRecordDto 一一對應，所有
/// lookup、日期與相容欄位都已是純值；它不攜帶 CRM logical property、formatted-value annotation、nextLink
/// 或 Entity/SDK 參考，因此序列化後可安全在 Gateway 與產品之間傳遞。
/// </summary>
/// <summary>
/// ORG-CALL-00031 的小組 descriptor 純量投影。此型別只承載固定查詢已投影的清單與顯示欄位，不保留 CRM
/// <c>Entity</c>、lookup graph、query、metadata、cookie、profile、credential、Session 或 connector 狀態。
/// response envelope 會以固定上限驗證所有值，避免未界定資料跨 request、使用者或 profile 存活。
/// </summary>
public sealed record MemberInfoSmallGroupDescriptorRecord
{
    /// <summary>固定 descriptor 查詢投影出的非空清單 GUID；它不是 caller 的 selector 或路由權限。</summary>
    [JsonPropertyName("listId")]
    public Guid ListId { get; init; }

    /// <summary>清單顯示名稱；只可作目前 immutable view 的顯示資料。</summary>
    [JsonPropertyName("listName")]
    public string? ListName { get; init; }

    /// <summary>小組區域顯示文字；不得用作授權、篩選或共享快取索引。</summary>
    [JsonPropertyName("areaName")]
    public string? AreaName { get; init; }

    /// <summary>牧區長名稱純量；不攜帶 CRM lookup graph 或登入身分。</summary>
    [JsonPropertyName("raceLeaderName")]
    public string? RaceLeaderName { get; init; }

    /// <summary>牧區長 contact 的可選 GUID；空 GUID 會使整個 snapshot fail closed。</summary>
    [JsonPropertyName("raceLeaderContactId")]
    public Guid? RaceLeaderContactId { get; init; }

    /// <summary>小組長名稱純量，只在目前 request 的 immutable snapshot 中存活。</summary>
    [JsonPropertyName("groupLeaderName")]
    public string? GroupLeaderName { get; init; }

    /// <summary>固定 projection 取得的聚會時間文字，不是 caller 指定的查詢條件。</summary>
    [JsonPropertyName("groupTime")]
    public string? GroupTime { get; init; }

    /// <summary>固定 projection 取得的聚會地點文字，沒有快取或背景工作所有權。</summary>
    [JsonPropertyName("groupPlace")]
    public string? GroupPlace { get; init; }
}

/// <summary>
/// ORG-CALL-00032 的清單成員關係純量投影。僅保留 descriptor list GUID 與 contact GUID，沒有 CRM relationship、
/// query、Entity、Session、profile、credential 或 transport state；response 會確認 list GUID 來自同一 descriptor set。
/// </summary>
public sealed record MemberInfoSmallGroupMembershipRecord
{
    /// <summary>同一 snapshot descriptor set 中的非空清單 GUID。</summary>
    [JsonPropertyName("listId")]
    public Guid ListId { get; init; }

    /// <summary>固定 relationship projection 的非空 contact GUID。</summary>
    [JsonPropertyName("contactId")]
    public Guid ContactId { get; init; }
}

/// <summary>
/// ORG-CALL-00031／00032 的唯一 composed 小組快照 response。建構時複製兩個來源集合，將可變來源限制在呼叫
/// frame 內；這個型別不擁有 connector、lease、permit、stream、timer 或 background work。
/// </summary>
public sealed class MemberInfoSmallGroupSnapshotResponseData
{
    /// <summary>
    /// 以 server-validated subject、封閉 access mode 與純量列建立 request-local snapshot。語意完整性（GUID、mode、
    /// subset、unique、UTF-8 與 byte/row bound）由 <see cref="OperationResponseData"/> 在發布 union branch 前驗證。
    /// </summary>
    [JsonConstructor]
    public MemberInfoSmallGroupSnapshotResponseData(
        Guid subjectContactId,
        MemberInfoAuthorizationAssignmentAccessMode accessMode,
        IReadOnlyList<MemberInfoSmallGroupDescriptorRecord> descriptors,
        IReadOnlyList<MemberInfoSmallGroupMembershipRecord> memberships)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(memberships);
        SubjectContactId = subjectContactId;
        AccessMode = accessMode;
        Descriptors = Array.AsReadOnly(descriptors.ToArray());
        Memberships = Array.AsReadOnly(memberships.ToArray());
    }

    /// <summary>server-derived subject GUID；不得由 browser、Session 或 caller profile 指定。</summary>
    [JsonPropertyName("subjectContactId")]
    public Guid SubjectContactId { get; }

    /// <summary>只能由 server-owned authorization evidence 產生的封閉 access mode。</summary>
    [JsonPropertyName("accessMode")]
    public MemberInfoAuthorizationAssignmentAccessMode AccessMode { get; }

    /// <summary>防禦性複製後的 descriptor 純量列。</summary>
    [JsonPropertyName("descriptors")]
    public IReadOnlyList<MemberInfoSmallGroupDescriptorRecord> Descriptors { get; }

    /// <summary>防禦性複製後的 membership 純量列。</summary>
    [JsonPropertyName("memberships")]
    public IReadOnlyList<MemberInfoSmallGroupMembershipRecord> Memberships { get; }
}

public sealed record Package01StorLessonRecord
{
    [JsonPropertyName("storLessonId")]
    public Guid? StorLessonId { get; init; }

    [JsonPropertyName("contactId")]
    public Guid? ContactId { get; init; }

    [JsonPropertyName("discipleLessonId")]
    public Guid? DiscipleLessonId { get; init; }

    [JsonPropertyName("createdOn")]
    public DateTimeOffset? CreatedOn { get; init; }

    [JsonPropertyName("payDate")]
    public DateTimeOffset? PayDate { get; init; }

    [JsonPropertyName("currentComplete")]
    public bool? CurrentComplete { get; init; }

    [JsonPropertyName("contactName")]
    public string? ContactName { get; init; }

    [JsonPropertyName("contactMobile")]
    public string? ContactMobile { get; init; }

    [JsonPropertyName("discipleLessonName")]
    public string? DiscipleLessonName { get; init; }

    /// <summary>
    /// 關聯 disciple lesson 的開課時間。connector 必須在 lesson alias 邊界將 CRM DateTime
    /// 正規化為 UTC <see cref="DateTimeOffset"/>；此 immutable 值只能隨本次 operation response
    /// 傳遞，不能保留 CRM Entity、時區 metadata 或另一個 request 的 session 狀態。
    /// </summary>
    [JsonPropertyName("classStartDate")]
    public DateTimeOffset? ClassStartDate { get; init; }

    /// <summary>
    /// 關聯 disciple lesson 的目前階段名稱。文字長度與 UTF-8 byte budget 由 connector owner
    /// 受控計算；ProductClient 與 UI 僅接收這個 request-local 投影，不能另以 SDK 補查。
    /// </summary>
    [JsonPropertyName("stageName")]
    public string? StageName { get; init; }

    [JsonPropertyName("feeAmount")]
    public decimal? FeeAmount { get; init; }
}
