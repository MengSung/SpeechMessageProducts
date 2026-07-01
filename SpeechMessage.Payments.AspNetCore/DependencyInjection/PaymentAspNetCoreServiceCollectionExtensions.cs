using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Payments.Workflows;

namespace SpeechMessage.Payments.AspNetCore.DependencyInjection;

/// <summary>
/// 註冊 ASP.NET Core host integration 需要的共用金流服務。
/// 這個專案只處理 HTTP request/response、付款建立 DTO 與 provider-neutral workflow，
/// 不註冊 CRM、LINE、奉獻、會員或任何產品資料庫實作；那些責任由 host application 自己提供。
/// </summary>
public static class PaymentAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// 將可跨 ASP.NET Core 產品重用的金流 host 輔助服務加入 DI。
    /// </summary>
    public static IServiceCollection AddSpeechMessagePaymentAspNetCore(this IServiceCollection services)
    {
        services.AddScoped<PaymentHttpRequestMapper>();
        services.AddScoped<PaymentAcknowledgementResultMapper>();
        services.AddScoped<PaymentCreateRequestFactory>();
        services.AddScoped<PaymentWorkflowResultMapper>();
        services.AddScoped<PaymentPostPaymentWorkflow>();
        return services;
    }
}
