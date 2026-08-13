// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/ListCatalog/SmallGroupAppNamedListCatalogRecordDto.cs
// 用途：定義 ORG-CALL-00065 小組 app-named 名單目錄可安全發佈的 request-local 純量快照。
//
// 本 DTO 不含 CRM Entity/EntityReference、lookup name、query、profile、credential、cookie、session、cache key、
// connector、stream、lease、timer、subscription 或 cancellation registration。client 每次讀取都建立新 DTO 與新 collection，
// 因此 immutable singleton client 不會把 A 使用者或 profile 的資料保留給 B；transport 資源的釋放由 executor owner 負責。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.ListCatalog;

/// <summary>
/// 表示 small-group app-named 名單目錄的一筆可安全公開資料列。
/// 它只攜帶 server-owned fixed operation 已投影的純量欄位與 leader contact GUID，不是 CRM 名單/lookup graph、可編輯
/// model 或 routing authority。呼叫端不能用任何欄位選擇 profile、workload、tenant、connector、organization、endpoint
/// 或 credential；每個 instance 只隨目前 request 的唯讀快照存活，沒有共享可變狀態或需釋放的資源。
/// </summary>
public sealed record SmallGroupAppNamedListCatalogRecordDto
{
    /// <summary>
    /// connector 驗證後投影的名單識別碼。
    /// 這是輸出資料而非授權或下一次讀取的 selector；任何空白 GUID 都是上游固定 projection 違約，client 必須在發佈
    /// collection 前 fail closed，不能從名稱、leader 或 caller input 推導替代 ID。
    /// </summary>
    public required Guid ListId { get; init; }

    /// <summary>
    /// 名單顯示名稱的純文字值；null 保留上游缺值語意。
    /// 這個 DTO 不保存 CRM formatted-value 字典、使用者 culture、metadata 或 shared cache，因此不會把語系/profile
    /// state 延長至目前 request 之外。
    /// </summary>
    public string? ListName { get; init; }

    /// <summary>
    /// <c>createdfromcode</c> 的 nullable option-set scalar。
    /// 缺值不觸發 ProductClient metadata 補查或 Entity rehydration，避免產生第二條 connector/transport 路徑和不受控資源。
    /// </summary>
    public int? CreatedFromCodeOption { get; init; }

    /// <summary>
    /// connector 已正規化的 UTC 最後使用時間；null 表示上游沒有可安全發佈的值。
    /// 時區轉換、頁面、lease 與 transport 的唯一 owner 是 executor request scope；DTO 不保存使用者時區、session 或
    /// 可變日期圖形。
    /// </summary>
    public DateTimeOffset? LastUsedOn { get; init; }

    /// <summary>
    /// <c>purpose</c> 的 nullable pure scalar。
    /// 它僅供安全呈現，不能讓 consumer 放寬 fixed server filter、改選 template 或進行另一個名單查詢。
    /// </summary>
    public string? Purpose { get; init; }

    /// <summary>
    /// 小組 race leader 的 nullable contact GUID。
    /// 這是由 connector 從 lookup 投影出的識別值，不含 EntityReference 名稱、Entity graph、profile 或授權結論；null
    /// 保留未設定 leader 的資料語意，而不是允許 client 重新查詢或猜測 leader。
    /// </summary>
    public Guid? RaceLeaderContactId { get; init; }

    /// <summary>
    /// 小組 family leader 的 nullable contact GUID。
    /// 此欄位同樣只是目前 request 的純量結果，不得作為 session、cache、tenant、owner 或 connector 選擇鍵，也不攜帶
    /// CRM lookup name 或可變 SDK object。
    /// </summary>
    public Guid? FamilyLeaderContactId { get; init; }
}
