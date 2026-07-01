namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Product-neutral payment order draft used by host products before provider execution.
/// Keep product-specific data in Metadata and keep provider-specific protocol out of this type.
/// </summary>
public sealed record PaymentOrderDraft
{
    public string ProfileName { get; init; } = string.Empty;
    public string ProductOrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string Description { get; init; } = string.Empty;
    public PaymentPayerDraft Payer { get; init; } = new();
    public PaymentMethodSelection Method { get; init; } = new();
    public PaymentScheduleDraft Schedule { get; init; } = new();
    public IReadOnlyList<PaymentLineItemDraft> Items { get; init; } = Array.Empty<PaymentLineItemDraft>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
