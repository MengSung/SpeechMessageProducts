using SpeechMessage.Payments.Configuration;

namespace SpeechMessage.Payments.Abstractions;

public interface IPaymentProfileResolver
{
    PaymentMerchantProfile Resolve(string? profileName);
}
