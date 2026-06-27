using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Configuration;

/// <summary>
/// 啟動期檢查 Payment 設定的最低必要條件。
/// 真正的 provider 憑證欄位仍由各 provider mapper 檢查，因為不同金流需要的欄位不同。
/// </summary>
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
