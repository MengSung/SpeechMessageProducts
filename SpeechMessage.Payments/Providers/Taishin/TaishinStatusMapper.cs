using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Taishin;

internal static class TaishinStatusMapper
{
    public static PaymentStatus Map(string? retCode, string? state)
    {
        var normalizedRetCode = retCode?.Trim();
        var normalizedState = state?.Trim();

        if (IsSuccessCode(normalizedRetCode) && string.IsNullOrEmpty(normalizedState))
        {
            return PaymentStatus.Succeeded;
        }

        if (string.Equals(normalizedState, "1", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(normalizedRetCode) || IsSuccessCode(normalizedRetCode)))
        {
            return PaymentStatus.Succeeded;
        }

        if (string.Equals(normalizedState, "0", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(normalizedRetCode))
        {
            return PaymentStatus.Failed;
        }

        return PaymentStatus.Unknown;
    }

    private static bool IsSuccessCode(string? retCode)
    {
        return string.Equals(retCode, "00", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(retCode, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(retCode, "0000", StringComparison.OrdinalIgnoreCase);
    }
}
