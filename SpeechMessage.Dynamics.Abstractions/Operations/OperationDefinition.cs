// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/OperationDefinition.cs
// 目的：描述一個受控 CRM 操作（server-owned template + 型別參數）。
//
// 保母教學：
// - 產品不能傳 raw OData URL、FetchXML、Entity 任意屬性袋。
// - 只能傳「已註冊操作 ID + 命名參數」。
// - templateHash 之後會拿來跟 phase0 matrix / CI 比對，防止偷偷改模板。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 一個已註冊、可被 Gateway/Embedded 執行的受控操作定義。
/// </summary>
public sealed class OperationDefinition
{
    public required string CapabilityOperationId { get; init; }

    /// <summary>read / write / action / function / metadata / connection-runtime / batch</summary>
    public required string OperationKind { get; init; }

    /// <summary>odata-route / odata-function / odata-action / fetchxml / batch</summary>
    public required string TemplateKind { get; init; }

    public required string TemplateId { get; init; }

    /// <summary>
    /// 模板內容的穩定雜湊。Phase 1 先用固定字串，後續改由模板編譯產生。
    /// </summary>
    public required string TemplateHash { get; init; }

    public required string DataClassification { get; init; }

    public required string AuditRequirement { get; init; }

    public required string IdempotencyClass { get; init; }

    /// <summary>此操作允許的命名參數。</summary>
    public required IReadOnlyList<OperationParameterDefinition> Parameters { get; init; }

    /// <summary>是否已在 Package 0/1 首發範圍內。</summary>
    public required string Package { get; init; }
}

/// <summary>
/// 受控操作的單一命名參數定義。
/// </summary>
public sealed class OperationParameterDefinition
{
    public required string Name { get; init; }

    /// <summary>string/guid/integer/decimal/boolean/date-time/enum/string-array/guid-array/object</summary>
    public required string Type { get; init; }

    public required bool Required { get; init; }

    /// <summary>參數會被編碼到哪個位置：json-body / odata-query-option / fetchxml-attribute-value ...</summary>
    public required string EncodingContext { get; init; }
}
