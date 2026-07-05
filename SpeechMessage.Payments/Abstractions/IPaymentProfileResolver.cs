// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Abstractions/IPaymentProfileResolver.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：interface IPaymentProfileResolver
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：SpeechMessage.Payments.Configuration
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using SpeechMessage.Payments.Configuration;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 依 profile name 解析具體商店設定。
/// Profile 是「產品/組織選擇哪一家金流與哪組憑證」的邊界，
/// provider 實作只接收解析後的設定，不自行讀取宿主產品的設定來源。
/// </summary>
public interface IPaymentProfileResolver
{
    PaymentMerchantProfile Resolve(string? profileName);
}
