using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Gateway;

namespace SpeechMessage.Payments.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSpeechMessagePayments(
        this IServiceCollection services,
        IConfiguration paymentSection)
    {
        services.Configure<PaymentOptions>(paymentSection);
        services.AddSingleton<IValidateOptions<PaymentOptions>, PaymentOptionsValidator>();
        services.AddSingleton<IPaymentProfileResolver, OptionsPaymentProfileResolver>();
        services.AddSingleton<IPaymentGateway, PaymentGateway>();
        services.AddHttpClient("SpeechMessage.Payments");

        return services;
    }
}
