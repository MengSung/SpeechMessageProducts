using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Providers.MyPay;

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
