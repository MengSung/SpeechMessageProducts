/*
 * 檔案整體說明
 *
 * 目的與定位：台新 TSPG callback hash 驗證。 驗證規則依序串接 StoreKey、transaction_id、order_id、state、StoreIV 後做 SHA256。 
 * StoreKey/StoreIV 只存在 profile credentials，不回傳給產品層。
 *
 * 主要功能與流程：
 * 本檔宣告 TaishinHashVerifier。
 * 主要操作入口包含 Validate、CalculateNotificationHash、TryGetCredential、GetValue。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs 追查目前工作樹中的合作／使用位置。
 */
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
