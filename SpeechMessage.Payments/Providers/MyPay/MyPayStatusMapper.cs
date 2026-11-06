/*
 * 檔案整體說明
 *
 * 目的與定位：將 MyPay PRC 交易狀態碼轉成通用 PaymentStatus。 產品層只看 Succeeded/Pending/Failed，不再理解 MyPay 自己的代碼表。
 *
 * 主要功能與流程：
 * 本檔宣告 MyPayStatusMapper。
 * 主要操作入口包含 Map。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.MyPay;

/// <summary>
/// 將 MyPay PRC 交易狀態碼轉成通用 PaymentStatus。
/// 產品層只看 Succeeded/Pending/Failed，不再理解 MyPay 自己的代碼表。
/// </summary>
internal static class MyPayStatusMapper
{
    public static PaymentStatus Map(string? prc)
    {
        return prc switch
        {
            "250" => PaymentStatus.Succeeded,
            "290" => PaymentStatus.Succeeded,
            "600" => PaymentStatus.Succeeded,
            "260" => PaymentStatus.Pending,
            "270" => PaymentStatus.Pending,
            "280" => PaymentStatus.Pending,
            "300" => PaymentStatus.Failed,
            "400" => PaymentStatus.Failed,
            _ => PaymentStatus.Unknown
        };
    }
}
