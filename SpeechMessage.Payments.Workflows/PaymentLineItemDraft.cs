namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Host product supplied payable item before provider-specific mapping.
/// The external item id is carried as metadata because the payment core does not interpret it.
/// </summary>
public sealed record PaymentLineItemDraft
{
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public string Currency { get; init; } = "TWD";
    public string ExternalItemId { get; init; } = string.Empty;
}
