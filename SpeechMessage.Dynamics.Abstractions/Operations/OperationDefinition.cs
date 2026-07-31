// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/OperationDefinition.cs
// 用途：描述 server-owned Dynamics capability 的固定模板、typed parameter、封閉回應種類與有限資源政策。
//
// 安全與生命週期邊界：
// 1. 定義由 registry owner 建立，產品只可提出 capability ID 與 named typed values；禁止 raw OData URL、
//    FetchXML、entity、header、credential 或 caller-controlled response policy。
// 2. page/byte 上限是 connector 讀取 response stream、buffer、visited continuation set 與 aggregate record
//    collection 的硬邊界；每次 request 必須在成功、取消、timeout、retry 或拒絕時依 runtime owner 清理資源。
// 3. TemplateHash 納入 template、response kind 和全部上限，避免 queue/audit/matrix 在舊 revision 下重用
//    不同的資料外洩或記憶體保留風險。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 一個 Gateway/Embedded 可執行的封閉 capability 定義。此型別只描述 immutable policy，沒有 client、stream、
/// token、timer 或 cache；實際資源必須由 profile-generation/request scope 擁有並在 drain/dispose 時釋放。
/// </summary>
public sealed class OperationDefinition
{
    /// <summary>
    /// server-owned capability ID；只能比對 registry allowlist，不能作為 CRM URL、profile 或 template 的替代輸入。
    /// </summary>
    public required string CapabilityOperationId { get; init; }

    /// <summary>read / write / action / function / metadata / connection-runtime / batch</summary>
    public required string OperationKind { get; init; }

    /// <summary>odata-route / odata-function / odata-action / fetchxml / batch</summary>
    public required string TemplateKind { get; init; }

    /// <summary>
    /// server-owned template 識別碼；connector 只能由它選取已審查的 URI/FetchXML 結構，不可接受 caller payload。
    /// </summary>
    public required string TemplateId { get; init; }

    /// <summary>
    /// 版本化 revision hash，必須涵蓋 template、capability ID、封閉回應種類與三個有限上限；它可安全作為
    /// queue/audit 的 immutable revision，不能取代 profile、credential 或 runtime generation key。
    /// </summary>
    public required string TemplateHash { get; init; }

    /// <summary>
    /// 產品可見回應的唯一 discriminator。Unsupported 表示 connector 必須在 raw metadata/extension 離開其
    /// request scope 前失敗關閉，不能建立 generic object 結果。
    /// </summary>
    public required OperationResponseKind ResponseKind { get; init; }

    /// <summary>
    /// 一次作業最多可處理的 server-driven response page 數。必須是有限正數，connector 在追蹤 continuation
    /// 與 aggregate records 前先檢查它，避免循環 nextLink 或未界定集合造成記憶體/credential request 增長。
    /// </summary>
    public required int MaximumPageCount { get; init; }

    /// <summary>
    /// 單一 response page 可讀取的最大位元組數。connector 必須先拒絕過大的 Content-Length，並在 chunked
    /// stream 讀取中同步執行此限制；任何 buffer 都由 request scope 釋放而非由 definition 保存。
    /// </summary>
    public required int MaximumPageBytes { get; init; }

    /// <summary>
    /// 一次作業所有 page 的最大累積位元組數。此上限與 page count/page bytes 一同阻止 page aggregation 造成
    /// 無界記憶體保留；超限時 connector 必須在下一次 credential-bearing request 前回傳受控失敗。
    /// </summary>
    public required int MaximumCumulativeResponseBytes { get; init; }

    /// <summary>
    /// 一次作業最多可投影的結果列數。connector 必須在將任一頁資料加入 request-scope 集合前檢查此
    /// 上限，讓位元組限制之外的物件配置也有明確邊界；超限時不可回傳 partial data、不可保留 upstream
    /// row 或繼續建立 credential-bearing request。此純 policy 不保存 stream、token、session 或快取。
    /// </summary>
    public required int MaximumResultItemCount { get; init; }

    /// <summary>
    /// 已分類資料標籤；它支援 audit/policy 決策，但不允許 caller 用分類繞過 capability 或 response projection。
    /// </summary>
    public required string DataClassification { get; init; }

    /// <summary>
    /// audit owner 的固定需求；實作端仍須在 dispatch 前後用 bounded durable intent/retention 流程處理資源與復原。
    /// </summary>
    public required string AuditRequirement { get; init; }

    /// <summary>
    /// 固定冪等語意；retry/queue runtime 只能以此 immutable 值決定可否重送，不能從 caller 資料推測。
    /// </summary>
    public required string IdempotencyClass { get; init; }

    /// <summary>
    /// 作業允許的 named typed parameter 定義。每個 encoding context 由 server-owned template 處理，避免 string
    /// 拼接、FetchXML/OData 結構注入或將使用者資料保存到 runtime key。
    /// </summary>
    public required IReadOnlyList<OperationParameterDefinition> Parameters { get; init; }

    /// <summary>
    /// 所屬 rollout package。Package 1 即使有 contract 仍受 feature gate 控制，不能因 registry 存在而自動產生流量。
    /// </summary>
    public required string Package { get; init; }
}

/// <summary>
/// 一個已登錄作業的最小 typed parameter schema。它是 immutable metadata，不保存實際 caller value、token、
/// profile、queue item 或可釋放資源；實際值只可在單一 request scope 內編碼及清理。
/// </summary>
public sealed class OperationParameterDefinition
{
    public required string Name { get; init; }

    /// <summary>string/guid/integer/decimal/boolean/date-time/enum/string-array/guid-array/object</summary>
    public required string Type { get; init; }

    public required bool Required { get; init; }

    /// <summary>
    /// 值的唯一 server-owned 編碼位置，例如 fetchxml-attribute-value；呼叫端不能以此欄位選擇任意 URL、
    /// raw filter、header 或 XML 結構，且編碼暫存資料必須在 request 完成時釋放。
    /// </summary>
    public required string EncodingContext { get; init; }
}
