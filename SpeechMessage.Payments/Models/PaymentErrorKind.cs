namespace SpeechMessage.Payments.Models;

/// <summary>
/// 金流核心對外公開的標準化錯誤分類。
/// provider 原始錯誤碼會保留在 <see cref="PaymentError.Code"/> 或 sanitized provider data，
/// 但宿主產品與未來產品應優先依照此分類做流程決策。
/// </summary>
public enum PaymentErrorKind
{
    /// <summary>
    /// 沒有錯誤；搭配 <see cref="PaymentError.None"/> 使用。
    /// </summary>
    None = 0,

    /// <summary>
    /// profile、credential、endpoint 或 provider 設定不完整或不一致。
    /// </summary>
    ConfigurationInvalid = 1,

    /// <summary>
    /// 產品層送入的建立付款、查詢或 callback request 缺少必要欄位。
    /// </summary>
    RequestInvalid = 2,

    /// <summary>
    /// provider 已收到請求但明確拒絕，例如金鑰不符、欄位格式錯誤或交易規則不通過。
    /// </summary>
    ProviderRejected = 3,

    /// <summary>
    /// provider endpoint 無法使用、HTTP 狀態碼失敗或服務暫時不可達。
    /// </summary>
    ProviderUnavailable = 4,

    /// <summary>
    /// callback 或 provider response 的簽章、hash、mac 驗證失敗。
    /// </summary>
    SignatureInvalid = 5,

    /// <summary>
    /// callback 內容無法解析、缺少必要欄位或不符合 provider callback contract。
    /// </summary>
    CallbackInvalid = 6,

    /// <summary>
    /// 網路傳輸層發生錯誤，例如 timeout、DNS、連線中斷。
    /// </summary>
    NetworkFailure = 7,

    /// <summary>
    /// JSON、form、加解密資料或 provider payload 序列化/反序列化失敗。
    /// </summary>
    SerializationFailure = 8,

    /// <summary>
    /// provider 或目前第一版核心尚未支援此操作，例如 MyPay 查詢或退款類功能。
    /// </summary>
    UnsupportedOperation = 9,

    /// <summary>
    /// 無法歸類的非預期錯誤；應保留 sanitized diagnostics 方便後續追查。
    /// </summary>
    Unexpected = 10
}
