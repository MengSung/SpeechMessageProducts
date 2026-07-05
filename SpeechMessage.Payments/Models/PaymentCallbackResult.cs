// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Models/PaymentCallbackResult.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：record PaymentCallbackResult
// 主要成員：Status、ProductOrderId、ProviderTransactionId、Amount、Currency、Acknowledgement、Error
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace SpeechMessage.Payments.Models;

/// <summary>
/// provider callback 解析後的通用結果。
/// Acknowledgement 描述 provider 需要的回應格式，宿主產品只負責轉成實際 web response。
/// </summary>
public sealed record PaymentCallbackResult
{
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProviderTransactionId { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    // 不同 provider 可能要回 plain text、JSON 或 redirect，規則由核心 parser 決定。
    public PaymentCallbackAcknowledgement Acknowledgement { get; init; } = PaymentCallbackAcknowledgement.None;
    public PaymentError Error { get; init; } = PaymentError.None;
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } = new Dictionary<string, string>();
}
