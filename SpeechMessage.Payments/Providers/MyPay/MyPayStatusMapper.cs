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
