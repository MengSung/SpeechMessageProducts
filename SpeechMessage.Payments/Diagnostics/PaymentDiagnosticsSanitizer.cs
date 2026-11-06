/*
 * 檔案整體說明
 *
 * 目的與定位：對外回傳 provider diagnostics 前的遮罩工具。 Payment core 可以保留除錯需要的 provider 欄位，但不能把金鑰、簽章、token、 完整卡號等敏感資料暴露給宿主產品畫面、
 * log 或未來其他產品。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentDiagnosticsSanitizer。
 * 主要操作入口包含 Sanitize、SanitizeValue、IsTokenKey、IsSecretKey、IsSafeBankAccountKey、IsCardNumber。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Diagnostics/PaymentDiagnosticsSanitizerTests.cs、
 * SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs、
 * SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs、
 * SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs、
 * SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Diagnostics;

/// <summary>
/// 對外回傳 provider diagnostics 前的遮罩工具。
/// Payment core 可以保留除錯需要的 provider 欄位，但不能把金鑰、簽章、token、
/// 完整卡號等敏感資料暴露給宿主產品畫面、log 或未來其他產品。
/// </summary>
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
        // ATM 虛擬帳號是付款指示，不是信用卡號；必須保留完整號碼讓使用者能付款。
        if (IsSafeBankAccountKey(key))
        {
            return value;
        }

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

    private static bool IsSafeBankAccountKey(string key)
    {
        var normalizedKey = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return normalizedKey.Contains("atmpayno", StringComparison.OrdinalIgnoreCase) ||
            normalizedKey.Contains("virtualaccount", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCardNumber(string value)
    {
        return value.Length is >= 13 and <= 19 && value.All(char.IsDigit);
    }
}
