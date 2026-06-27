using System;
using System.Collections.Generic;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 建立 provider-neutral <see cref="PaymentCreateRequest"/> 的薄工廠。
/// 這個類別不組 provider SDK payload，也不做加密、簽章或 endpoint 選擇；
/// provider-specific request mapping 一律交給 <c>SpeechMessage.Payments</c>。
/// </summary>
public sealed class PaymentCreateRequestFactory
{
    /// <summary>
    /// 將 ChurchReport workflow 收集到的付款資料轉成金流核心公開 contract。
    /// 目前是欄位對欄位搬運，保留此工廠是為了讓未來多個產品流程共用同一個入口。
    /// </summary>
    public PaymentCreateRequest Create(PaymentCreateRequestInput input)
    {
        return new PaymentCreateRequest
        {
            ProfileName = input.ProfileName,
            ProductOrderId = input.ProductOrderId,
            Amount = input.Amount,
            Currency = input.Currency,
            Description = input.Description,
            PaymentMethod = input.PaymentMethod,
            PaymentMethodSubType = input.PaymentMethodSubType,
            Callbacks = input.Callbacks,
            Customer = input.Customer,
            Items = input.Items,
            Metadata = input.Metadata
        };
    }
}

/// <summary>
/// ChurchReport 產品流程建立付款時的輸入模型。
/// 它可以包含產品訂單、CRM entity id、callback URL、客戶與品項資訊；
/// 但 provider 的加密資料、簽章欄位與原始 DTO 不應放進此模型。
/// </summary>
public sealed record PaymentCreateRequestInput
{
    public string ProfileName { get; init; } = string.Empty;
    public string ProductOrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string Description { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string PaymentMethodSubType { get; init; } = string.Empty;
    public PaymentCallbacks Callbacks { get; init; } = new();
    public PaymentCustomer Customer { get; init; } = new();
    public IReadOnlyList<PaymentLineItem> Items { get; init; } = Array.Empty<PaymentLineItem>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
