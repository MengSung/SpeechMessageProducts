// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Payments/IDonationPaymentCreateGatewayAdapter.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：interface IDonationPaymentCreateGatewayAdapter
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System.Threading、System.Threading.Tasks、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 產品層建立付款訂單時使用的中性 adapter 契約。
/// 此介面保留在 ChurchReport 專案中，因為 <see cref="CreOrder"/> 是舊奉獻網頁流程仍需使用的相容 DTO；
/// 可重用的 provider protocol 仍由 <c>SpeechMessage.Payments</c> 負責，避免 ChurchReport 的舊 DTO 污染核心金流專案。
/// </summary>
public interface IDonationPaymentCreateGatewayAdapter
{
    /// <summary>
    /// 將 ChurchReport 的奉獻付款輸入轉成 provider-neutral request，並交給目前設定的金流 provider 建立付款。
    /// </summary>
    Task<PaymentCreateResult> CreateCardPaymentAsync(
        DonationPaymentCreateInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 建立付款後投影成既有 ChurchReport 頁面仍使用的 <see cref="CreOrder"/> 結果。
    /// 新產品若不需要舊 DTO，應直接使用 <see cref="CreateCardPaymentAsync"/> 的 provider-neutral 結果。
    /// </summary>
    Task<CreOrder> CreateLegacyOrderAsync(
        DonationPaymentCreateInput input,
        CancellationToken cancellationToken = default);
}
