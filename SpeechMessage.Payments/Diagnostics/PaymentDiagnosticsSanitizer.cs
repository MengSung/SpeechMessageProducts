// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class PaymentDiagnosticsSanitizer
// 主要成員：SanitizeValue、IsTokenKey、IsSecretKey、IsSafeBankAccountKey、IsCardNumber
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
