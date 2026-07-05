// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Models/PaymentCallbackRequest.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：record PaymentCallbackRequest
// 主要成員：ProfileName、ProviderHint、HttpMethod、ContentType、RawBody
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 產品層將 web request 攤平成這個 callback DTO 後交給金流核心。
/// 核心因此不需要參考 ASP.NET web runtime 型別，保持可被其他產品重用。
/// </summary>
public sealed record PaymentCallbackRequest
{
    public string ProfileName { get; init; } = string.Empty;
    public PaymentProviderKind? ProviderHint { get; init; }
    public string HttpMethod { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    // RawBody 保留給 JSON 或 form-urlencoded callback parser；產品層讀取後必須 rewind request body。
    public string RawBody { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Form { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}
