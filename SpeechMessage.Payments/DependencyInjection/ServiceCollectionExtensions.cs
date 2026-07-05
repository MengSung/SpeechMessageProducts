// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class ServiceCollectionExtensions
// 主要成員：AddSpeechMessagePayments
// 引用命名空間：Microsoft.Extensions.Configuration、Microsoft.Extensions.DependencyInjection、Microsoft.Extensions.Options、SpeechMessage.Payments.Abstractions、SpeechMessage.Payments.Configuration、SpeechMessage.Payments.Gateway、SpeechMessage.Payments.Providers.MyPay、SpeechMessage.Payments.Providers.Sinopac
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
