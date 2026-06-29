using Microsoft.Extensions.DependencyInjection;

namespace SpeechMessage.Payments.AspNetCore.DependencyInjection;

/// <summary>
/// Registers reusable ASP.NET host integration services for SpeechMessage payments.
/// Product-specific services such as CRM, LINE, views, and workflows remain in
/// the host application.
/// </summary>
public static class PaymentAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddSpeechMessagePaymentAspNetCore(this IServiceCollection services)
    {
        services.AddScoped<PaymentHttpRequestMapper>();
        services.AddScoped<PaymentAcknowledgementResultMapper>();
        return services;
    }
}
