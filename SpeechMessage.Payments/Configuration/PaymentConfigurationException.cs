namespace SpeechMessage.Payments.Configuration;

public sealed class PaymentConfigurationException : Exception
{
    public PaymentConfigurationException(string message)
        : base(message)
    {
    }
}
