namespace SpeechMessage.Payments.Configuration;

/// <summary>
/// 表示金流 profile 或 provider 設定不完整。
/// 這類錯誤會被 gateway/provider 轉成 normalized PaymentError，
/// 讓產品層可以顯示可診斷的失敗訊息，而不是拋出未處理例外。
/// </summary>
public sealed class PaymentConfigurationException : Exception
{
    public PaymentConfigurationException(string message)
        : base(message)
    {
    }
}
