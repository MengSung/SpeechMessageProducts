/*
 * 檔案整體說明
 *
 * 目的與定位：定義 provider-neutral 綁卡輸入、結果及核心內部 capability。宿主只經公開 IPaymentGateway 傳入 profile 名稱，
 * PaymentGateway 再以伺服器端 resolver 選出單一商店；本檔不暴露 PaymentMerchantProfile 或 provider DTO。
 *
 * 主要功能與流程：
 * CreateCardBindAsync 建立綁卡付款頁；QueryCardBindAsync 以 PayToken 查詢完成結果，並回傳可安全持久化但仍須視為敏感資料的
 * profile-bound authenticated ciphertext。明文 BindToken 不得成為任何公開 DTO 欄位，也不得進入日誌、錯誤或跨請求記憶體。
 *
 * 使用邊界與維護：
 * protected token 的明文有效期只限 provider 單次操作；此 ciphertext 由產品資料層持久化並依永豐實際效期清除。
 * profile 憑證輪替會使舊 ciphertext 無法解封，呼叫端須重新查詢／綁卡。Gateway 只傳遞呼叫端取消權杖；
 * provider 借用 DI 管理的 HTTP client，請求與回應由 provider 呼叫路徑確定性 Dispose。離線測試使用合成 profile 與記憶體 handler，
 * 不連線永豐、正式金流或其他組織。
 */
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 建立或查詢付款卡綁定的中立 Gateway 請求。ProfileName 僅供伺服器端 resolver 查設定，
/// 不是憑證或授權來源；BindNo 應由受信任產品服務依業務事件產生並保持唯一。
/// </summary>
public sealed record CardBindRequest
{
    /// <summary>由伺服器端 resolver 查找的商店 profile 名稱。</summary>
    public string ProfileName { get; init; } = string.Empty;

    /// <summary>選用的 provider 防呆提示；Gateway 會拒絕與解析 profile 不相符的值。</summary>
    public PaymentProviderKind? ProviderHint { get; init; }

    /// <summary>受信任產品服務為此綁卡事件產生的唯一綁定編號。</summary>
    public string BindNo { get; init; } = string.Empty;

    /// <summary>銀行綁卡頁完成後導回的 HTTPS 網址；不得承載使用者身分或秘密。</summary>
    public string ReturnUrl { get; init; } = string.Empty;
}

/// <summary>綁卡建立結果。成功只代表已建立銀行付款頁，不代表使用者完成綁卡或任何付款。</summary>
public sealed record CardBindResult
{
    /// <summary>建立操作的中立狀態；付款頁建立成功時為 Pending。</summary>
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;

    /// <summary>由請求及銀行回應關聯確認的綁卡識別。</summary>
    public string BindNo { get; init; } = string.Empty;

    /// <summary>銀行提供的綁卡付款頁絕對 HTTPS 網址。</summary>
    public string PaymentPageUrl { get; init; } = string.Empty;

    /// <summary>不包含憑證或銀行回應本文的標準錯誤。</summary>
    public PaymentError Error { get; init; } = PaymentError.None;
}

/// <summary>
/// 以銀行 PayToken 查詢綁卡結果的中立請求。ExpectedBindNo 用於防止把其他綁卡事件的結果配到本次操作。
/// </summary>
public sealed record CardBindQueryRequest
{
    /// <summary>由伺服器端 resolver 查找的商店 profile 名稱。</summary>
    public string ProfileName { get; init; } = string.Empty;

    /// <summary>選用的 provider 防呆提示；不是 profile 的權威來源。</summary>
    public PaymentProviderKind? ProviderHint { get; init; }

    /// <summary>銀行付款頁回傳、供後端查詢的交易識別；不得記入一般日誌。</summary>
    public string PayToken { get; init; } = string.Empty;

    /// <summary>此綁卡操作原先由伺服器產生的預期 BindNo。</summary>
    public string ExpectedBindNo { get; init; } = string.Empty;
}

/// <summary>
/// 綁卡查詢結果。BindToken 僅以 profile、商店及 BindNo 關聯的 authenticated ciphertext 跨核心邊界；
/// 呼叫端仍須將其視為可重用付款憑證，禁止寫入 log、遙測或不受控前端儲存。
/// </summary>
public sealed record CardBindQueryResult
{
    /// <summary>查詢後的中立狀態；只有完整關聯驗證成功才會是 Succeeded。</summary>
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;

    /// <summary>通過預期識別核對的綁卡編號。</summary>
    public string BindNo { get; init; } = string.Empty;

    /// <summary>由 provider 加密並驗證完整性的 BindToken 封裝，不是銀行原始 response envelope。</summary>
    public string ProtectedBindToken { get; init; } = string.Empty;

    /// <summary>固定分類且不含 token、卡片資料或原始銀行本文的錯誤。</summary>
    public PaymentError Error { get; init; } = PaymentError.None;
}

/// <summary>
/// 永豐等 provider 可選擇實作的綁卡 capability。此介面只供核心 Gateway 內部路由，
/// 產品不得傳入或持有已解析的商店 profile。
/// </summary>
internal interface IPaymentCardBinding
{
    /// <summary>建立綁卡付款頁，未通過 HTTPS、輸入或 profile 檢查時不得發出 HTTP。</summary>
    /// <param name="profile">Gateway 已解析的單一商店 profile，只供本次操作使用。</param>
    /// <param name="request">綁卡編號與回跳網址等中立輸入。</param>
    /// <param name="cancellationToken">由產品呼叫端提供並傳遞至各次 HTTP I/O 的取消權杖。</param>
    /// <returns>不包含明文綁卡憑證的付款頁建立結果。</returns>
    Task<CardBindResult> CreateCardBindAsync(
        PaymentMerchantProfile profile,
        CardBindRequest request,
        CancellationToken cancellationToken);

    /// <summary>查詢綁卡完成結果並將 BindToken 立即封裝後才交回呼叫端。</summary>
    /// <param name="profile">Gateway 已解析的單一商店 profile，只供本次操作及封裝金鑰衍生使用。</param>
    /// <param name="request">含 PayToken 與預期 BindNo 的中立查詢輸入。</param>
    /// <param name="cancellationToken">由產品呼叫端提供並傳遞至各次 HTTP I/O 的取消權杖。</param>
    /// <returns>含 profile-bound ciphertext 的查詢結果；失敗時不發布 token。</returns>
    Task<CardBindQueryResult> QueryCardBindAsync(
        PaymentMerchantProfile profile,
        CardBindQueryRequest request,
        CancellationToken cancellationToken);
}
