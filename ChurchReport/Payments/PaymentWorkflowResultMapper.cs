using System.Collections.Generic;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

public sealed class PaymentWorkflowResultMapper
{
    public PaymentWorkflowResult Map(PaymentCallbackResult result)
    {
        return new PaymentWorkflowResult
        {
            Status = result.Status,
            ProductOrderId = result.ProductOrderId,
            ProviderTransactionId = result.ProviderTransactionId,
            Amount = result.Amount,
            Currency = result.Currency,
            ProviderMessage = ReadProviderMessage(result.ProviderData),
            ProviderData = result.ProviderData
        };
    }

    private static string ReadProviderMessage(IReadOnlyDictionary<string, string> providerData)
    {
        if (providerData.TryGetValue("provider_message", out var providerMessage))
        {
            return providerMessage;
        }

        return providerData.TryGetValue("message", out var message) ? message : string.Empty;
    }
}

public sealed record PaymentWorkflowResult
{
    public PaymentStatus Status { get; init; } = PaymentStatus.Unknown;
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProviderTransactionId { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string ProviderMessage { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
}
