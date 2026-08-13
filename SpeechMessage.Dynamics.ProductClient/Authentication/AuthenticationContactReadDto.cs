// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/Authentication/AuthenticationContactReadDto.cs
// 用途：定義認證聯絡人查詢公開的最小 DTO 與固定 fail-closed 結果分類。
//
// 安全與生命週期邊界：
// 1. DTO 只複製 allowlisted contact scalar，不含 password、hash、token、cookie、Entity、profile、endpoint、
//    credential、raw response 或 exception。密碼驗證與 Session/claims 建立不屬於這個 API。
// 2. Result 只在單一呼叫後發布；沒有可變集合或任何 Dispose owner，故不會持有 connector lease、stream、buffer、
//    cancellation registration、client、timer、cache 或背景工作。
// 3. 所有失敗分類都去識別化，避免藉由錯誤訊息回顯 CRM ID、名稱、秘密、transport 或另一使用者的資料。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.Authentication;

/// <summary>
/// ProductClient 對外的 immutable authentication contact DTO。ContactId 是已投影資料列的 locator，並非
/// authorization decision；未來 consumer 必須先自行完成 server-side authorization 才能用它讀取其他資料。
/// </summary>
public sealed record AuthenticationContactReadDto
{
    /// <summary>取得安全投影的 contact locator；不能作為 profile、tenant、credential 或 session selector。</summary>
    public required Guid ContactId { get; init; }

    /// <summary>取得 bounded account locator；它不是密碼、密碼雜湊或登入驗證結果。</summary>
    public required string AccountLocator { get; init; }

    /// <summary>取得 bounded display name；不得由此 DTO 延伸保存 CRM Entity 或其他使用者資料。</summary>
    public required string DisplayName { get; init; }

    /// <summary>取得固定 query 投影的 active 狀態；不取代後續 consumer 的伺服器授權檢查。</summary>
    public required bool IsActive { get; init; }
}

/// <summary>
/// 認證 contact read 的固定去識別化結果狀態。未知 executor/transport failure、response mismatch、
/// profile unavailable 與 secret detection 全部 fail closed；列舉不包含 raw error 或 credential 資料。
/// </summary>
public enum AuthenticationContactReadStatus
{
    /// <summary>唯一合法、安全且 active 的 contact record 已映射為新 DTO。</summary>
    Found = 0,

    /// <summary>lookup 空白、含無效 Unicode 或超出限制；executor、pool、host 與 I/O 均未被呼叫。</summary>
    InvalidInput = 1,

    /// <summary>固定查詢沒有資料列；結果沒有 Contact，不能作為帳號、LINE ID 或存在性的細節回顯。</summary>
    NotFound = 2,

    /// <summary>固定查詢有多筆資料列；結果沒有 Contact，絕不可猜選第一筆或重用先前 request 的資料。</summary>
    Ambiguous = 3,

    /// <summary>connector 偵測到禁止跨 boundary 的秘密分類；結果沒有 Contact 或秘密欄位。</summary>
    SecretPresent = 4,

    /// <summary>deployment profile 或 executor 不可用；不 fallback 至 legacy SDK、不重試且不回顯下游錯誤。</summary>
    ProfileUnavailable = 5
}

/// <summary>
/// 每次 authentication contact lookup 新建的 immutable result。Found 只能包含一份新 DTO；所有其他狀態
/// 都強制 Contact 為 null，防止 zero／duplicate／secret/fault 路徑誤發布部分資料或前次 request 資料。
/// </summary>
public sealed record AuthenticationContactReadResult
{
    /// <summary>取得封閉、去識別化狀態分類。</summary>
    public required AuthenticationContactReadStatus Status { get; init; }

    /// <summary>取得 Found 時的新 DTO；所有 fail-closed 狀態均為 null。</summary>
    public AuthenticationContactReadDto? Contact { get; init; }

    /// <summary>建立唯一合法的 Found result，並保留呼叫專屬 DTO instance。</summary>
    /// <param name="contact">已由 client 從安全 wire record 新建的 DTO。</param>
    /// <returns>只含單筆安全 DTO 的 immutable success result。</returns>
    public static AuthenticationContactReadResult Found(AuthenticationContactReadDto contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return new AuthenticationContactReadResult
        {
            Status = AuthenticationContactReadStatus.Found,
            Contact = contact
        };
    }

    /// <summary>建立不帶 contact、raw error 或 transport detail 的固定 fail-closed result。</summary>
    /// <param name="status">除 Found 外的封閉安全狀態。</param>
    /// <returns>Contact 必為 null 的 immutable result。</returns>
    public static AuthenticationContactReadResult Failure(AuthenticationContactReadStatus status)
    {
        if (status == AuthenticationContactReadStatus.Found || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new AuthenticationContactReadResult { Status = status, Contact = null };
    }
}
