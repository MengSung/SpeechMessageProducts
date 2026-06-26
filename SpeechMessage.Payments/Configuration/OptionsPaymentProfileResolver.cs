using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Abstractions;

namespace SpeechMessage.Payments.Configuration;

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
