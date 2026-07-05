// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Models/PaymentErrorKind.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：enum PaymentErrorKind
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
