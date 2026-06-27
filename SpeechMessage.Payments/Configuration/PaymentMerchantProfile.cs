using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Configuration;

public sealed record PaymentMerchantProfile
{
    public string Name { get; set; } = string.Empty;
    public PaymentProviderKind Provider { get; set; } = PaymentProviderKind.Unknown;
    public PaymentEnvironment Environment { get; set; } = PaymentEnvironment.Sandbox;
    public Dictionary<string, string> Credentials { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Endpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
