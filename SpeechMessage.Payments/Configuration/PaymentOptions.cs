namespace SpeechMessage.Payments.Configuration;

public sealed class PaymentOptions
{
    public string DefaultProfile { get; set; } = string.Empty;
    public Dictionary<string, PaymentMerchantProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
