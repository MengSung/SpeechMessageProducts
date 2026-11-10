/*
檔案整體說明
目的與定位：階段三 LINE Webhook 的唯一組合根，只負責主機組裝與執行，不承接產品業務規則。
主要功能與流程：由受信任 builder 組裝 Hosting、Localization、固定 GoodShepherd ToolUtility 及 Webhook 轉接。Console provider 僅允許固定 Reply sender category 的 Information 回覆 API 紀錄；其他一般資訊不輸出，Error/Critical 維持由安全診斷 provider 處理。三個正式依賴不足時必須啟動拒絕，不使用假驗簽或記憶體去重掩蓋缺項。
使用邊界與維護：builder 與組態不是路由權限，成功建立後由 WebApplication 擁有；Main 以 await using 關閉主機、排空服務並釋放容器。建立失敗時組態仍由呼叫端釋放；Host 啟動及執行例外由 Hosting 安全 logger 接入，建立前的組態驗證失敗原樣交回呼叫端，不宣稱尚未建立的診斷 owner 已記錄。不啟動階段四 Payments。
*/
using ChurchReport.Logging;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;
using SpeechMessage.Hosting;
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
        builder.Services.AddLineWebhook(builder.Configuration);
        var app = builder.Build();
        app.MapLineWebhook();
        return app;
    }
}
