using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Gateway;
using SpeechMessage.Payments.Providers.MyPay;
using SpeechMessage.Payments.Providers.Sinopac;
using SpeechMessage.Payments.Providers.Taishin;

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
        services.AddHttpClient<SinopacPaymentProvider>();
        services.AddHttpClient<MyPayPaymentProvider>();
        services.AddHttpClient<TaishinPaymentProvider>();
        services.AddTransient<IPaymentProvider>(provider => provider.GetRequiredService<SinopacPaymentProvider>());
        services.AddTransient<IPaymentProvider>(provider => provider.GetRequiredService<MyPayPaymentProvider>());
        services.AddTransient<IPaymentProvider>(provider => provider.GetRequiredService<TaishinPaymentProvider>());

        return services;
    }
}
