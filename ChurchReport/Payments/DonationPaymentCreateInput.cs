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
