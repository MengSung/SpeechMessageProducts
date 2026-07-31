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
    Package01StorLessonRecords = 3
}

/// <summary>
/// Gateway/Embedded 成功回應的封閉 concrete union。所有 branch 由 immutable discriminator 決定，且 constructor
/// 強制只存在一個相符 branch；這讓產品可以在不保留 upstream document、頁面串流或 continuation 的情況下，
/// 驗證回應種類並映射到自己的 DTO。
/// </summary>
public sealed class OperationResponseData
{
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
        IReadOnlyList<Package01StorLessonRecord>? storLessonRecords = null)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("operationId is required.", nameof(operationId));
        }

        if (string.IsNullOrWhiteSpace(ceVersion))
        {
            throw new ArgumentException("ceVersion is required.", nameof(ceVersion));
        }

        ValidateSingleSafeBranch(responseKind, whoAmI, feeRecords, storLessonRecords);

        OperationId = operationId;
        CeVersion = ceVersion;
        ResponseKind = responseKind;
        WhoAmI = whoAmI;
        FeeRecords = feeRecords?.ToArray();
        StorLessonRecords = storLessonRecords?.ToArray();
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
    /// 建立明確的 unsupported envelope。connector/Gateway 應把它轉成受控失敗，而不是把未投影 metadata、
    /// OData annotation 或 endpoint detail 回傳給產品；此值不擁有背景資源或可清理 handle。
    /// </summary>
    public static OperationResponseData Unsupported(string operationId, string ceVersion)
        => new(operationId, ceVersion, OperationResponseKind.Unsupported);

    private static void ValidateSingleSafeBranch(
        OperationResponseKind responseKind,
        WhoAmIResponseData? whoAmI,
        IReadOnlyList<Package01FeeRecord>? feeRecords,
        IReadOnlyList<Package01StorLessonRecord>? storLessonRecords)
    {
        // 先計算所有非 null branch，再比對 discriminator；這在反序列化入口也生效，避免使用者或上游資料透過
        // 多 branch 讓資料跨 capability 混合。失敗時不保留任何集合或外部資源。
        var branchCount = (whoAmI is null ? 0 : 1) +
                          (feeRecords is null ? 0 : 1) +
                          (storLessonRecords is null ? 0 : 1);
        var isValid = responseKind switch
        {
            OperationResponseKind.Unsupported => branchCount == 0,
            OperationResponseKind.WhoAmI => branchCount == 1 && whoAmI is not null,
            OperationResponseKind.Package01FeeRecords => branchCount == 1 && feeRecords is not null,
            OperationResponseKind.Package01StorLessonRecords => branchCount == 1 && storLessonRecords is not null,
            _ => false
        };

        if (!isValid)
        {
            throw new ArgumentException(
                "responseKind must select exactly one matching safe response branch.",
                nameof(responseKind));
        }
    }
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
