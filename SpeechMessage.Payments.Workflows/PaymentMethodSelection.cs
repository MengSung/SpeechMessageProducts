namespace SpeechMessage.Payments.Workflows;

/// <summary>
/// Product-neutral payment method chosen by the host product.
/// Provider profile is metadata for routing; provider protocol remains in SpeechMessage.Payments.
/// </summary>
public sealed record PaymentMethodSelection
{
    public string Method { get; init; } = string.Empty;
    public string SubType { get; init; } = string.Empty;
    public string ProviderProfileName { get; init; } = string.Empty;
}
