// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Models/PaymentAckKind.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：enum PaymentAckKind
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 描述金流 provider callback 完成後，主系統應回覆給 provider 的 HTTP 回應型態。
/// 這個 enum 是「協定層 acknowledgement」的抽象，不代表宿主產品的付款成功頁或失敗頁。
/// </summary>
public enum PaymentAckKind
{
    /// <summary>
    /// 不需要特殊內容，只依照狀態碼回覆；通常用於前端 return flow 或產品層自行接手的流程。
    /// </summary>
    None = 0,

    /// <summary>
    /// 回覆純文字內容，例如 MyPay callback 需要的固定文字 acknowledgement。
    /// </summary>
    PlainText = 1,

    /// <summary>
    /// 回覆 JSON 內容，例如台新 TSPG 後端通知所需的 JSON acknowledgement。
    /// </summary>
    Json = 2,

    /// <summary>
    /// 要求產品層轉址到指定 URL；核心只描述轉址需求，不直接依賴 ASP.NET。
    /// </summary>
    Redirect = 3
}
