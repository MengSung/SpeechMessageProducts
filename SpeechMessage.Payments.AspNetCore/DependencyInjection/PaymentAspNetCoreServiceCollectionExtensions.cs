using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Payments.Workflows;

namespace SpeechMessage.Payments.AspNetCore.DependencyInjection;

/// <summary>
/// 註冊可被多個 ASP.NET Core 產品共用的金流 host integration 服務。
/// 這裡只提供 HTTP request/response 映射、建立付款請求與付款後流程管線；
/// CRM、LINE、畫面、資料庫與產品流程的具體實作仍由 host application 自行註冊。
/// </summary>
public static class PaymentAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// 將 SpeechMessage.Payments.AspNetCore 的薄整合層加入 DI。
    /// 其他 ASP.NET Core 產品引用此專案後，可透過同一個方法取得一致的金流接線行為。
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
