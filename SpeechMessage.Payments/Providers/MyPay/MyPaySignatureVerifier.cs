/*
 * 檔案整體說明
 *
 * 目的與定位：MyPay callback 的最低限度欄位驗證。 現有舊流程未提供可驗證的 shared secret 簽章，因此這裡先確認 callback 是否具備 MyPay 必要欄位與已知狀態碼，
 * 避免不完整資料進入產品 workflow。
 *
 * 主要功能與流程：
 * 本檔宣告 MyPaySignatureVerifier。
 * 主要操作入口包含 Validate。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.MyPay;

/// <summary>
/// MyPay callback 的最低限度欄位驗證。
/// 現有舊流程未提供可驗證的 shared secret 簽章，因此這裡先確認 callback 是否具備
/// MyPay 必要欄位與已知狀態碼，避免不完整資料進入產品 workflow。
/// </summary>
internal static class MyPaySignatureVerifier
{
    private static readonly HashSet<string> KnownPrcCodes = new(StringComparer.Ordinal)
    {
        "250",
        "290",
        "600",
        "300",
        "400",
        "260",
        "270",
        "280"
    };

    public static PaymentError Validate(IReadOnlyDictionary<string, string> fields)
    {
        var errors = new List<string>();

        // uid/key/prc/order_id 是 MyPay callback 用來識別交易與狀態的核心欄位。
        if (!fields.TryGetValue("uid", out var uid) || string.IsNullOrWhiteSpace(uid) || uid.Length != 32)
        {
            errors.Add("uid is required and must be 32 characters.");
        }

        if (!fields.TryGetValue("key", out var key) || string.IsNullOrWhiteSpace(key) || key.Length != 32)
        {
            errors.Add("key is required and must be 32 characters.");
        }

        if (!fields.TryGetValue("prc", out var prc) || !KnownPrcCodes.Contains(prc))
        {
            errors.Add("prc is missing or unsupported.");
        }

        if (!fields.TryGetValue("order_id", out var orderId) || string.IsNullOrWhiteSpace(orderId))
        {
            errors.Add("order_id is required.");
        }

        return errors.Count == 0
            ? PaymentError.None
            : new PaymentError
            {
                Kind = PaymentErrorKind.CallbackInvalid,
                Message = string.Join(" ", errors)
            };
    }
}
