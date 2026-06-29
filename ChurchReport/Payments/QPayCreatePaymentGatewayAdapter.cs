using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// 舊 QPay 命名的建單 adapter 相容外殼。
/// 實際建單轉換邏輯已移到 <see cref="DonationPaymentCreateGatewayAdapter"/>；此類別只保留舊建構子與方法名稱，
/// 讓尚未完成重命名的 QPayProcessor、QpayManager 與測試可以繼續運作。
/// </summary>
[Obsolete("Use DonationPaymentCreateGatewayAdapter. QPay naming is retained only for compatibility during the migration.")]
public sealed class QPayCreatePaymentGatewayAdapter : IDonationPaymentCreateGatewayAdapter
{
    private readonly DonationPaymentCreateGatewayAdapter _inner;

    public QPayCreatePaymentGatewayAdapter(
        IPaymentGateway paymentGateway,
        PaymentCreateRequestFactory requestFactory,
        ChurchReportPaymentProfileResolver profileResolver)
    {
        _inner = new DonationPaymentCreateGatewayAdapter(
            paymentGateway,
            requestFactory,
            profileResolver);
    }

    public Task<PaymentCreateResult> CreateCardPaymentAsync(
        QPayCreatePaymentInput input,
        CancellationToken cancellationToken = default)
    {
        return _inner.CreateCardPaymentAsync(input, cancellationToken);
    }

    public Task<CreOrder> CreateLegacyOrderAsync(
        QPayCreatePaymentInput input,
        CancellationToken cancellationToken = default)
    {
        return _inner.CreateLegacyOrderAsync(input, cancellationToken);
    }
}

/// <summary>
/// ChurchReport 建立付款訂單時使用的既有輸入 DTO。
/// 名稱仍保留 QPay 是為了降低第一階段改名風險；資料內容本身已是產品中性的付款輸入，
/// 後續可在處理器與 manager 改名完成後再建立 DonationPaymentCreateInput。
/// </summary>
public sealed record QPayCreatePaymentInput
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
