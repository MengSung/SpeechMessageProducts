/*
 * 檔案整體說明
 *
 * 目的與定位：註冊 ASP.NET Core host integration 需要的共用金流服務。 這個專案只處理 HTTP request/response、付款建立 DTO 與 provider-neutral 
 * workflow， 不註冊 CRM、LINE、奉獻、會員或任何產品資料庫實作；那些責任由 host application 自己提供。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentAspNetCoreServiceCollectionExtensions。
 * - 將可跨 ASP.NET Core 產品重用的金流 host 輔助服務加入 DI。
 * 主要操作入口包含 AddSpeechMessagePaymentAspNetCore。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
