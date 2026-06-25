using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Configuration;

public sealed record PaymentMerchantProfile
{
    public string Name { get; init; } = string.Empty;
    public PaymentProviderKind Provider { get; init; } = PaymentProviderKind.Unknown;
    public PaymentEnvironment Environment { get; init; } = PaymentEnvironment.Sandbox;
    public IReadOnlyDictionary<string, string> Credentials { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Endpoints { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
}
