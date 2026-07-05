// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Payments/DonationPaymentCreateInput.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：record DonationPaymentCreateInput
// 主要成員：ProfileName、Amount、Currency、ProductName、ProductOrderId、ProductEntityId、PaymentOrganization、PaymentCategory、PaymentMethod、PaymentMethodSubType
// 引用命名空間：System、System.Collections.Generic、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 建立奉獻付款時使用的產品層輸入 DTO。
///
/// 這個 DTO 的責任是把 ChurchReport 產品流程需要的資料整理成一個明確物件，
/// 再交給 DonationPaymentCreateGatewayAdapter 轉成 SpeechMessage.Payments 的 PaymentCreateRequest。
///
/// 它不是銀行協定 DTO：
/// - 不代表永豐 QPay 的 request。
/// - 不代表高鉅 MyPay 的 encrypted payload。
/// - 不代表台新 TSPG 的 API request。
///
/// 它只是 ChurchReport 產品層的「我要建立一筆奉獻付款」資料包。
/// 這樣命名後，高鉅與台新流程使用同一個 adapter 時，不會再出現「明明不是永豐卻呼叫 QPay input」
/// 的認知負擔。
/// </summary>
public sealed record DonationPaymentCreateInput
{
    public string ProfileName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string ProductName { get; init; } = string.Empty;
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProductEntityId { get; init; } = string.Empty;
    public string PaymentOrganization { get; init; } = string.Empty;
    public string PaymentCategory { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string PaymentMethodSubType { get; init; } = string.Empty;
    public string ReturnUrl { get; init; } = string.Empty;
    public string BackendUrl { get; init; } = string.Empty;
    public string SuccessUrl { get; init; } = string.Empty;
    public string FailureUrl { get; init; } = string.Empty;
    public string AutoBilling { get; init; } = "Y";
    public string Staging { get; init; } = string.Empty;
    public int DeductTotalNum { get; init; }
    public string PeriodType { get; init; } = string.Empty;
    public int DeductFreq { get; init; }
    public string CreditCardToken { get; init; } = string.Empty;
    public string ExpireDate { get; init; } = string.Empty;
    public PaymentCustomer Customer { get; init; } = new();
    public IReadOnlyList<PaymentLineItem> Items { get; init; } = Array.Empty<PaymentLineItem>();
}
