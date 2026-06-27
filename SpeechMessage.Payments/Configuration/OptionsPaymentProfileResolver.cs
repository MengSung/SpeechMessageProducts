using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Abstractions;

namespace SpeechMessage.Payments.Configuration;

/// <summary>
/// 從 DI options 解析 PaymentMerchantProfile。
/// 若呼叫端沒有指定 profile，才使用 Payment:DefaultProfile；
/// 找不到 profile 時以設定錯誤失敗，不再落回舊程式的硬編碼憑證表。
/// </summary>
public sealed class OptionsPaymentProfileResolver : IPaymentProfileResolver
{
    private readonly IOptions<PaymentOptions> _options;

    public OptionsPaymentProfileResolver(IOptions<PaymentOptions> options)
    {
        _options = options;
    }

    public PaymentMerchantProfile Resolve(string? profileName)
    {
        var options = _options.Value;
        // profileName 是產品層傳入的選擇；空值時才使用 DefaultProfile，避免 provider 自行猜測商店。
        var resolvedName = string.IsNullOrWhiteSpace(profileName)
            ? options.DefaultProfile
            : profileName;

        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            throw new PaymentConfigurationException("Payment profile was not specified and no default profile is configured.");
        }

        if (!options.Profiles.TryGetValue(resolvedName, out var profile))
        {
            throw new PaymentConfigurationException($"Payment profile '{resolvedName}' is not configured.");
        }

        return profile with { Name = resolvedName };
    }
}
