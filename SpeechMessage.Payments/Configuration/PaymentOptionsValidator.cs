using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Configuration;

public sealed class PaymentOptionsValidator : IValidateOptions<PaymentOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentOptions options)
    {
        if (options.Profiles.Count == 0)
        {
            return ValidateOptionsResult.Fail("At least one payment profile is required.");
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultProfile) &&
            !options.Profiles.ContainsKey(options.DefaultProfile))
        {
            return ValidateOptionsResult.Fail($"Default payment profile '{options.DefaultProfile}' is not configured.");
        }

        var invalidProfiles = options.Profiles
            .Where(profile => profile.Value.Provider == PaymentProviderKind.Unknown)
            .Select(profile => profile.Key)
            .ToArray();

        return invalidProfiles.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Payment profiles must specify a provider: {string.Join(", ", invalidProfiles)}.");
    }
}
