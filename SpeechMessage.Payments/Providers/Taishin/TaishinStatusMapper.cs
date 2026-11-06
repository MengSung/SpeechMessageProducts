/*
 * 檔案整體說明
 *
 * 目的與定位：將台新 ret_code/state 正規化成通用 PaymentStatus。 建單、查詢、callback 的成功欄位組合不同，因此集中在此處維護狀態規則。
 *
 * 主要功能與流程：
 * 本檔宣告 TaishinStatusMapper。
 * 主要操作入口包含 Map、IsSuccessCode。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs、
 * SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs、
 * SpeechMessage.Payments/Providers/Taishin/TaishinPaymentProvider.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.Taishin;

/// <summary>
/// 將台新 ret_code/state 正規化成通用 PaymentStatus。
/// 建單、查詢、callback 的成功欄位組合不同，因此集中在此處維護狀態規則。
/// </summary>
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
