using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Configuration;

/// <summary>
/// 單一金流商店 profile。
/// Credentials 放金流憑證，Endpoints 放 API 網址，Settings 放 provider 非敏感選項。
/// 這個模型刻意不包含宿主產品的 CRM、通知、奉獻類別或資料庫資訊。
/// </summary>
public sealed record PaymentMerchantProfile
{
    public string Name { get; set; } = string.Empty;
    public PaymentProviderKind Provider { get; set; } = PaymentProviderKind.Unknown;
    public PaymentEnvironment Environment { get; set; } = PaymentEnvironment.Sandbox;
    public Dictionary<string, string> Credentials { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Endpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
