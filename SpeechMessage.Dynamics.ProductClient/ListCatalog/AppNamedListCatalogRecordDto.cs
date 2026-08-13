// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListCatalog/AppNamedListCatalogRecordDto.cs
// 用途：定義 ORG-CALL-00014 可從 ProductClient 安全發佈的 app-named 名單目錄純量快照。
//
// 此 DTO 不承載 CRM Entity、OData 文件、query、profile、credential、cookie、session、cache key、stream、
// cancellation registration 或任何可釋放資源。每次讀取由 client 建立新的值物件，避免不同使用者、profile、
// workload 或 request 之間共用可變 wire record；collection 的 defensive copy 與唯讀包裝由 client 擁有。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.ListCatalog;

/// <summary>
/// 表示 app-named 名單目錄的一筆可安全發佈資料列。
/// 此型別只含固定 operation 已投影的 allowlisted scalar，不是 CRM 名單的可編輯模型或路由權威；呼叫端不得以
/// 任何欄位選擇 profile、workload、connector、organization、endpoint、credential 或授權範圍。每個 instance 僅
/// 屬於產生它的 request，沒有靜態狀態、快取、訂閱、計時器或其他需要 Dispose 的生命週期。
/// </summary>
public sealed record AppNamedListCatalogRecordDto
{
    /// <summary>
    /// 由 server-owned fixed operation 驗證後投影的名單識別碼。
    /// 它是回應資料而不是下一次呼叫的授權或路由輸入；不可由 consumer 用來切換 profile、owner、organization
    /// 或查詢範圍，因為這些隔離邊界只能由 deployment composition 與 executor 建立。
    /// </summary>
    public required Guid ListId { get; init; }

    /// <summary>
    /// 名單的純文字顯示名稱；null 保留上游缺值語意。
    /// 這個值不攜帶 formatted-value 字典、語系快取或 CRM metadata，並且不會被寫入跨 request 的 shared collection。
    /// </summary>
    public string? ListName { get; init; }

    /// <summary>
    /// <c>createdfromcode</c> 的原始 option-set scalar；null 表示固定投影沒有值。
    /// ProductClient 不查詢或重建 metadata/Entity 來補值，避免新增另一條 connector 路徑、快取或 session 依賴。
    /// </summary>
    public int? CreatedFromCodeOption { get; init; }

    /// <summary>
    /// 已由 connector 正規化的 UTC 最後使用時間；null 表示上游沒有安全可發佈的值。
    /// DTO 不保留使用者時區、culture、session 或可變 <see cref="DateTime"/> graph，timezone conversion 的單一 owner
    /// 是 executor/connector request scope，必須在其釋放 lease 與 transport 資源前完成。
    /// </summary>
    public DateTimeOffset? LastUsedOn { get; init; }

    /// <summary>
    /// <c>purpose</c> 的純 scalar 投影；null 不授權 consumer 放寬或重寫固定篩選條件。
    /// 篩選、排序、分頁與資料界線仍完全由 server-owned registry/connector 擁有，本欄位只能做結果呈現。
    /// </summary>
    public string? Purpose { get; init; }
}
