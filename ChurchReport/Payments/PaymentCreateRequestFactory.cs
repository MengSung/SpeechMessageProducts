using System;
using System.Collections.Generic;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

public sealed class PaymentCreateRequestFactory
{
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
