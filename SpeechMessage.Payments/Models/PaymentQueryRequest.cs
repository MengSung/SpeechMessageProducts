namespace SpeechMessage.Payments.Models;

public sealed record PaymentQueryRequest
{
    public string ProfileName { get; init; } = string.Empty;
    public PaymentProviderKind? ProviderHint { get; init; }
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProviderOrderRef { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
