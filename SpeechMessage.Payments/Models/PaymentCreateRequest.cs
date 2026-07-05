// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Models/PaymentCreateRequest.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：record PaymentCreateRequest
// 主要成員：ProfileName、ProviderHint、ProductOrderId、Amount、Currency、Description、PaymentMethod、PaymentMethodSubType、Callbacks、Customer
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 建立付款的通用請求。
/// 此 DTO 不使用任何 provider 專屬欄位名稱；必要的 provider 相容參數放在 Metadata，
/// 由各 provider mapper 解讀，避免宿主產品綁死在 QPay/MyPay/TSPG 模型上。
/// </summary>
public sealed record PaymentCreateRequest
{
    public string ProfileName { get; init; } = string.Empty;
    // ProviderHint 用於防呆：指定 provider 時，gateway 會檢查 profile.Provider 是否一致。
    public PaymentProviderKind? ProviderHint { get; init; }
    public string ProductOrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string Description { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string PaymentMethodSubType { get; init; } = string.Empty;
    public PaymentCallbacks Callbacks { get; init; } = new();
    public PaymentCustomer Customer { get; init; } = new();
    public IReadOnlyList<PaymentLineItem> Items { get; init; } = Array.Empty<PaymentLineItem>();
    // Metadata 承載舊流程必要但非通用的欄位，例如 QPay Param1/Param2 或 MyPay PFN。
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
