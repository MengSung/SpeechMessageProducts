// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class TaishinHashVerifier
// 主要成員：Validate、CalculateNotificationHash、TryGetCredential、GetValue
// 引用命名空間：System.Security.Cryptography、System.Text、SpeechMessage.Payments.Configuration、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Security.Cryptography;
using System.Text;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Taishin;

/// <summary>
/// 台新 TSPG callback hash 驗證。
/// 驗證規則依序串接 StoreKey、transaction_id、order_id、state、StoreIV 後做 SHA256。
/// StoreKey/StoreIV 只存在 profile credentials，不回傳給產品層。
/// </summary>
internal static class TaishinHashVerifier
{
    public static PaymentError Validate(
        PaymentMerchantProfile profile,
        IReadOnlyDictionary<string, string> fields)
    {
        if (!TryGetCredential(profile, "StoreKey", out var storeKey) ||
            !TryGetCredential(profile, "StoreIV", out var storeIV))
        {
            return new PaymentError
            {
                Kind = PaymentErrorKind.ConfigurationInvalid,
                Message = $"Taishin profile '{profile.Name}' is missing StoreKey or StoreIV."
            };
        }

        var transactionId = GetValue(fields, "transaction_id");
        var orderId = GetValue(fields, "order_id");
        var state = GetValue(fields, "state");
        var hash = GetValue(fields, "hash");

        if (string.IsNullOrWhiteSpace(transactionId) ||
            string.IsNullOrWhiteSpace(orderId) ||
            string.IsNullOrWhiteSpace(state))
        {
            return new PaymentError
            {
                Kind = PaymentErrorKind.CallbackInvalid,
                Message = "Taishin callback is missing transaction_id, order_id, or state."
            };
        }

        if (string.IsNullOrWhiteSpace(hash))
        {
            return new PaymentError
            {
                Kind = PaymentErrorKind.SignatureInvalid,
                Message = "Taishin callback is missing hash."
            };
        }

        var expected = CalculateNotificationHash(storeKey, transactionId, orderId, state, storeIV);
        // Hash 不符時 fail closed；不可讓未驗證的付款成功 callback 更新宿主產品收費單。
        return string.Equals(expected, hash, StringComparison.OrdinalIgnoreCase)
            ? PaymentError.None
            : new PaymentError
            {
                Kind = PaymentErrorKind.SignatureInvalid,
                Message = "Taishin callback hash is invalid."
            };
    }

    private static string CalculateNotificationHash(
        string storeKey,
        string transactionId,
        string orderId,
        string state,
        string storeIV)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{storeKey}{transactionId}{orderId}{state}{storeIV}"));
        return Convert.ToHexString(bytes);
    }

    private static bool TryGetCredential(
        PaymentMerchantProfile profile,
        string key,
        out string value)
    {
        return profile.Credentials.TryGetValue(key, out value!) &&
            !string.IsNullOrWhiteSpace(value);
    }

    private static string GetValue(IReadOnlyDictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
