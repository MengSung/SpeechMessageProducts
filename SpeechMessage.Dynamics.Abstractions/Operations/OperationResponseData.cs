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
    ContactListTransfer = 10
}

/// <summary>
/// Gateway/Embedded 成功回應的封閉 concrete union。所有 branch 由 immutable discriminator 決定，且 constructor
/// 強制只存在一個相符 branch；這讓產品可以在不保留 upstream document、頁面串流或 continuation 的情況下，
/// 驗證回應種類並映射到自己的 DTO。
/// </summary>
public sealed class OperationResponseData
{
    private const int MaximumUngroupedCommitmentCountRecords = 4096;

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
        ContactListTransferResponseData? contactListTransfer = null)
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
            contactListTransfer);

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
        ContactListTransferResponseData? contactListTransfer)
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
                          (contactListTransfer is null ? 0 : 1);
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
/// Package 1 stor lesson 的 shared wire record。欄位與 ProductClient 的 StorLessonRecordDto 一一對應，所有
/// lookup、日期與相容欄位都已是純值；它不攜帶 CRM logical property、formatted-value annotation、nextLink
/// 或 Entity/SDK 參考，因此序列化後可安全在 Gateway 與產品之間傳遞。
/// </summary>
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

    [JsonPropertyName("feeAmount")]
    public decimal? FeeAmount { get; init; }
}
