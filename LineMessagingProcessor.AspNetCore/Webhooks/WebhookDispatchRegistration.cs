/*
檔案整體說明
目的與定位：P3-06 的唯一 DI 組合接縫，連結真 scoped dispatcher、scope resolver、Core 領域規則及有界 HTTP adapter。
主要功能與流程：HttpClientHandler 禁用 cookie jar 與 redirect，timeout 無限由 queue per-event 取消統一限定；HttpClient 由 DI factory 持有。dispatcher 與 scope resolver 均 scoped，Core resolver 無共享輸入，時鐘只讀 UTC。
使用邊界與維護：registration 僅於單一 Host 組建；缺 TokenKey／組織映射在實際事件 fail closed，沒有假預設。禁止擷取 scoped 服務至 singleton；HttpClientFactory owner 負責 handler lifetime 與 socket recycle，單筆 request/response 另行 Dispose。修改需證明正式組合與 queue startup probe 可解析且 scope 關閉後沒有資源殘留。
*/
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;

namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>實作 P3-06 經核定 partial registration，不觸碰既有 queue／簽章／去重 owner。</summary>
public static partial class WebhookServiceRegistration
{
    /// <summary>註冊預設 scoped handler 與可回收 HTTP factory；已由組合根提供 handler 時保留該唯一 owner。</summary>
    static partial void RegisterDispatch(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<SpeechMessage.Localization.IChurchClock>(_ => new SpeechMessage.Localization.ChurchClock(new SystemChurchUtcClock()));
        services.AddScoped<IWebhookScopeResolver, WebhookScopeResolver>();
        services.AddScoped<WebhookTextHandler>();
        services.AddHttpClient<WebhookHttpAdapter>(client => client.Timeout = System.Threading.Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            { UseCookies = false, AllowAutoRedirect = false, MaxConnectionsPerServer = 32 });
        services.AddScoped<WebhookAudioHandler>();
        if (!services.Any(item => item.ServiceType == typeof(IWebhookEnvelopeHandler)))
            services.AddScoped<IWebhookEnvelopeHandler, WebhookDispatcher>();
    }
}

/// <summary>將 Core 文字規則所需純 UTC 時鐘由共用實作注入，無租戶快取或資源。</summary>
internal sealed class SystemChurchUtcClock : SpeechMessage.Localization.IClock
{
    /// <summary>直接讀取 UTC 時鐘，無靜態可變資料與程序 local 偏差。</summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
