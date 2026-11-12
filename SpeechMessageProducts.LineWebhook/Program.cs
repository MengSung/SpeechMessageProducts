/*
檔案整體說明
目的與定位：LINE Webhook 的唯一組合根，只負責主機組裝與執行，不承接付款或產品資料規則。
主要功能與流程：由受信任 builder 組裝 Hosting、Localization、固定 GoodShepherd ToolUtility、Webhook 與共用付款 gateway。付款對外待辦僅在 Payment:Outbound:Enabled 明確為 true 時註冊；ATM／信用卡回呼僅在 Payment:Callbacks:Enabled 明確為 true 時映射。缺少 Payment 區段仍維持原有主機組合。Console provider 僅允許固定 Reply sender category 的 Information 回覆 API 紀錄，Error/Critical 由安全診斷 provider 處理。
使用邊界與維護：builder 與組態不是路由權限，成功建立後由 WebApplication 擁有；Main 以 await using 關閉主機、排空服務並釋放容器。每筆 outbound handler 與每次 store 操作都建立獨立 scope，避免跨教會重用 scoped SDK。建立失敗時組態仍由呼叫端釋放；Host 例外由 Hosting 安全 logger 接入，不宣稱尚未建立的診斷 owner 已記錄。
*/
using ChurchReport.Logging;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;
using SpeechMessage.Hosting;
using SpeechMessage.Payments.AspNetCore.Callbacks;
using SpeechMessage.Payments.AspNetCore.DependencyInjection;
using SpeechMessage.Payments.AspNetCore.Outbound;
using SpeechMessage.Payments.DependencyInjection;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using Microsoft.Extensions.Logging.Console;

namespace SpeechMessageProducts.LineWebhook;

/// <summary>僅承接主機組合及生命週期；端點與共用契約各自維護有界所有權。</summary>
public static class Program
{
    /// <summary>建立唯一主機並等待停止；非同步清理確保容器資源不延長至程序下一次執行。</summary>
    /// <param name="args">受信任程序啟動參數，不接受 HTTP 值作為部署設定。</param>
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        WebApplication? app = null;
        try
        {
            app = BuildApplication(builder);
            await using (app.ConfigureAwait(false))
                await app.RunAsync().ConfigureAwait(false);
        }
        finally
        {
            if (app is null)
                ((IDisposable)builder.Configuration).Dispose();
        }
    }

    /// <summary>以呼叫端提供的組合根建立主機；成功後組態由主機接管，呼叫端必須 Dispose 主機。</summary>
    /// <param name="builder">僅供部署組合根使用的 builder；不得保存 request 或使用者憑證。</param>
    /// <returns>尚未開始接收 HTTP 的主機；呼叫端負責 Start／Stop／Dispose。</returns>
    public static WebApplication BuildApplication(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSpeechMessageHosting(Path.Combine(builder.Environment.ContentRootPath, "Logs"),
            ExceptionOutputOptions.FromConfiguration(builder.Configuration));
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddFilter<ConsoleLoggerProvider>(_ => false);
        builder.Logging.AddFilter<ConsoleLoggerProvider>(
            typeof(LineWebhookReplySender).FullName!,
            level => level == LogLevel.Information);
        builder.Services.AddSingleton<ILoggerProvider, ExceptionLoggerProvider>();
        builder.Services.AddHostedService<LineWebhookDiagnosticsRegistration>();
        builder.Services.AddSpeechMessageLocalization(builder.Configuration, builder.Environment.IsProduction());
        builder.Services.AddToolUtility(builder.Configuration, "GoodShepherd");
        builder.Services.AddSpeechMessagePayments(builder.Configuration.GetSection("Payment"));
        builder.Services.AddSpeechMessagePaymentOutbound(builder.Configuration);
        builder.Services.AddSpeechMessagePaymentAspNetCore();
        builder.Services.AddLineWebhook(builder.Configuration);
        var app = builder.Build();
        app.MapLineWebhook();
        app.MapSpeechMessagePaymentCallbacks();
        return app;
    }
}
