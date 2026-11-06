/*
 * 檔案整體說明
 *
 * 目的與定位：可重用金流核心的 DI 註冊點。 Host 專案只需要提供 Payment 設定區塊；核心會註冊 gateway、profile resolver、 provider typed HttpClient 與
 * options 驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 ServiceCollectionExtensions。
 * 主要操作入口包含 AddSpeechMessagePayments。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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

/// <summary>
/// 可重用金流核心的 DI 註冊點。
/// Host 專案只需要提供 Payment 設定區塊；核心會註冊 gateway、profile resolver、
/// provider typed HttpClient 與 options 驗證。
/// </summary>
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
        // 所有 provider 都使用 DI 管理的 HttpClient，避免在核心內自行 new HttpClient 造成連線生命週期問題。
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
