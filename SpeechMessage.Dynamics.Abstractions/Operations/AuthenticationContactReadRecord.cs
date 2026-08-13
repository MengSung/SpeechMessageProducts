// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/AuthenticationContactReadRecord.cs
// 用途：定義 ORG-CALL-00055／00056 可跨 Gateway/Embedded 邊界的最小認證聯絡人安全投影。
//
// 安全與生命週期邊界：
// 1. record 只容納已由 connector allowlist 投影的 contact locator、顯示名稱與 active 狀態；它沒有密碼、
//    雜湊、token、cookie、Entity、OData 文件、例外、endpoint、profile 或 credential 欄位。
// 2. record 是 immutable pure value，不擁有 EntityCollection、stream、buffer、lease、connection、timer、
//    cancellation registration 或 session。connector 必須在建立 envelope 前釋放全部外部資源。
// 3. secret-present 只能透過不含資料的安全分類表達，不能在此型別、JSON、log 或產品 DTO 投影秘密內容。
// ============================================================================

using System.Text.Json.Serialization;

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 認證聯絡人唯讀的 immutable wire record。這不是登入憑證驗證結果，也不能作為 profile、tenant、
/// authorization scope 或 session 建立的替代資料；未來 consumer 必須先在伺服器完成自己的身分驗證與授權。
/// </summary>
public sealed record AuthenticationContactReadRecord
{
    /// <summary>
    /// 取得已投影的 contact 定位識別。它只供目前安全回應辨識資料列，不能由產品回送來選擇 profile、
    /// connector、organization、credential 或其他主體的授權範圍。
    /// </summary>
    [JsonPropertyName("contactId")]
    public required Guid ContactId { get; init; }

    /// <summary>
    /// 取得 server-owned query 已投影的帳號 locator。值的 UTF-8 大小、非空與秘密分類配對由
    /// <see cref="OperationResponseData"/> 驗證；此 record 不保存原始 query、帳密比對值或 CRM 欄位集合。
    /// </summary>
    [JsonPropertyName("accountLocator")]
    public required string AccountLocator { get; init; }

    /// <summary>
    /// 取得可顯示的聯絡人名稱。它是單次 response 的純值，不會進 shared cache、static 欄位、Session、
    /// timer、queue 或背景工作；任何長生命週期使用者資料保存必須由後續 consumer 的隔離設計明確擁有。
    /// </summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// 取得 connector 以固定 schema 投影的 active 狀態。它不取代 server-side authorization，且不可與
    /// caller 提供的 profile、claims 或租用中的 connector state 一起快取或跨 request 重用。
    /// </summary>
    [JsonPropertyName("isActive")]
    public required bool IsActive { get; init; }
}

/// <summary>
/// 認證 contact read branch 的封閉安全分類。列舉值是去識別化控制資訊而非資料欄位；新增分類時必須先定義
/// ProductClient 的 fail-closed 對應與測試，不能把上游敏感值加入 record 來解釋錯誤。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthenticationContactReadSafetyClassification
{
    /// <summary>connector 只看到 allowlisted scalar，可依資料列基數建立安全 DTO。</summary>
    Safe = 0,

    /// <summary>
    /// connector 偵測到不允許離開其 request scope 的秘密資料。envelope 必須是空列集合，產品端只能發布
    /// 固定 fail-closed 狀態；分類不包含秘密欄位名、值、雜湊、來源 Entity 或原始 response。
    /// </summary>
    SecretPresent = 1
}
