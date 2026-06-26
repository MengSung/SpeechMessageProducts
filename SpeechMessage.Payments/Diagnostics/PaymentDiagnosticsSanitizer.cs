namespace SpeechMessage.Payments.Diagnostics;

public static class PaymentDiagnosticsSanitizer
{
    private static readonly string[] SecretKeyFragments =
    [
        "signature",
        "hash",
        "storekey",
        "storeiv",
        "secret",
        "xkey",
        "cvv",
        "cvc"
    ];

    private static readonly HashSet<string> ExactSecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "key",
        "iv",
        "a1",
        "a2",
        "b1",
        "b2"
    };

    public static IReadOnlyDictionary<string, string> Sanitize(IReadOnlyDictionary<string, string> values)
    {
        return values.ToDictionary(
            pair => pair.Key,
            pair => SanitizeValue(pair.Key, pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string SanitizeValue(string key, string value)
    {
        if (IsTokenKey(key) && value.Length > 8)
        {
            return $"{value[..4]}...{value[^4..]}";
        }

        if (IsCardNumber(value))
        {
            return $"{value[..6]}******{value[^4..]}";
        }

        if (IsSecretKey(key))
        {
            return "***";
        }

        return value;
    }

    private static bool IsTokenKey(string key)
    {
        return key.Contains("token", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSecretKey(string key)
    {
        return ExactSecretKeys.Contains(key) ||
            SecretKeyFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            (key.Contains("key", StringComparison.OrdinalIgnoreCase) && !IsTokenKey(key));
    }

    private static bool IsCardNumber(string value)
    {
        return value.Length is >= 13 and <= 19 && value.All(char.IsDigit);
    }
}
