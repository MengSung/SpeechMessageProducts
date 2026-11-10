/*
檔案整體說明
目的與定位：P3-06 的唯一 DI 組合接縫，連結真 scoped dispatcher、scope resolver、Core 領域規則及有界 HTTP adapter。
主要功能與流程：Webhook HTTP adapter 與 LINE Reply 各使用獨立命名 HttpClient；兩者的 HttpClientHandler 均禁用 cookie jar 與 redirect，Webhook timeout 由 queue per-event 取消統一限定，Reply 保留 HttpClient 有限預設逾時。HttpClient 由 DI factory 建立並回收 handler／socket；dispatcher 與 scope resolver 均 scoped，Core resolver 無共享輸入，時鐘只讀 UTC。Reply 文字紀錄開關在註冊時由 `LineWebhook:ReplyLog:Enabled` 讀取一次，缺省 false，並由 Host logger 與 sender 共用固定 EventId。
使用邊界與維護：registration 僅於單一 Host 組建；缺 TokenKey／組織映射在實際事件 fail closed，沒有假預設。禁止擷取 scoped 服務至 singleton；Reply 每次建立 SDK client，Authorization 僅屬單筆 request，傳輸排空後由 sender Dispose SDK credential 與 factory client；factory 擁有共用 handler 並負責 recycle。Reply log 只在部署啟動快照明確開啟時輸出，不能把開關設成全環境預設開啟。修改需證明正式組合與 queue startup probe 可解析且 scope 關閉後沒有資源殘留。
*/
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddSingleton<IKeywordCatalog>(_ => GoodShepherdKeywordCatalog.Create());
        services.TryAddSingleton<IMemberCultureResolver>(_ => new MemberCultureResolver(new LocalizationOptions()));
        services.TryAddSingleton<IChurchTextOverrideSnapshotProvider, EmptyWebhookTextOverrideSnapshotProvider>();
        services.TryAddSingleton<GoodShepherdTextCatalog>();
        services.AddScoped<IWebhookScopeResolver, WebhookScopeResolver>();
        services.AddScoped<WebhookTextHandler>();
        services.AddHttpClient<WebhookHttpAdapter>(client => client.Timeout = System.Threading.Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            { UseCookies = false, AllowAutoRedirect = false, MaxConnectionsPerServer = 32 });
        services.AddHttpClient("LineWebhookReplySender")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            { UseCookies = false, AllowAutoRedirect = false, MaxConnectionsPerServer = 32 });
        services.AddScoped<WebhookAudioHandler>();
        var replyLoggingEnabled = configuration.GetValue<bool>("LineWebhook:ReplyLog:Enabled");
        services.AddScoped<IWebhookReplySender>(provider => new LineWebhookReplySender(
            provider.GetRequiredService<GoodShepherdTextCatalog>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<LineWebhookReplySender>>(),
            replyLoggingEnabled));
        services.AddScoped<IWebhookLetterRecorder, WebhookLetterRecorder>();
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

/// <summary>
/// 沒有啟用教會覆寫來源的純離線／最小 Host 後備；只發布帶本次 scope 的空快照，
/// 不掩蓋正式 Host 已註冊的 provider，也不保存任何跨事件資料。
/// </summary>
internal sealed class EmptyWebhookTextOverrideSnapshotProvider : IChurchTextOverrideSnapshotProvider
{
    /// <summary>回傳目前 scope 的空候選，讓 catalog 仍維持完整 scope 驗證。</summary>
    public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
        new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
}
