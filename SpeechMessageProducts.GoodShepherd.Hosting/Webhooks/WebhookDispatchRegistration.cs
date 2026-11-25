/*
檔案整體說明
目的與定位：承載好牧人產品層的 LINE Webhook 組合入口，將 LineMessagingProcessor.AspNetCore 的共用管線接到本產品的 scoped handler、QR 背景工作與會員代送；不把 GoodShepherd.Core 相依留在共用組件。
主要功能與流程：AddGoodShepherdLineWebhook 先註冊 AddLineWebhookPipeline，再註冊 dispatcher 與 Reply／HTTP／scope／letter adapter，最後依部署旗標接入課程 QR、會友 QR 及代送。產品 handler 以 queue 每事件 scope 執行；命名 HttpClient 透過 Host factory 管理 handler，禁止 cookie 與 redirect。時鐘為 UTC 純值服務，文字覆寫後備回傳當次空快照。
使用邊界與維護：服務集合由單一好牧人 Host 組合根持有；各背景 singleton 只保留 scope factory，不能捕獲 scoped SDK、事件、token 或教會狀態。缺少組織／教會／Channel 授權資料時由 resolver fail closed；所有 I/O 在呼叫或工作完成前排空，scope 由 queue／worker owner 關閉。設定只讀取啟動快照，不建立 reload 訂閱。離線註冊測試驗證 descriptor 與 SDK／HTTP 接縫，不代表外部服務或正式組織驗收。
*/
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeechMessage.Localization;
using LineMessagingProcessor.AspNetCore.Webhooks;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;

/// <summary>好牧人 Webhook 的產品註冊組合根；共用佇列及簽章 owner 仍由 pipeline 管理。</summary>
public static partial class GoodShepherdWebhookRegistration
{
    /// <summary>
    /// 註冊共用 LINE Webhook 管線，以及好牧人的 dispatcher、課程 QR 與會友代送功能。
    /// 共用管線只持有簽章、去重、queue、routing、選項與啟動守門；本產品註冊則在同一
    /// Host 服務集合加入 scoped handler 與明確旗標控制的背景工作。啟動 probe 於 Host
    /// 啟動時解析最終服務集合，因此即使先加入 probe，仍能看見本方法隨後註冊的 handler。
    /// </summary>
    /// <param name="services">單一好牧人 Host 擁有、尚未建置的依賴注入服務集合。</param>
    /// <param name="configuration">Host 擁有的部署設定；本方法只將必要設定交給各註冊建立快照。</param>
    /// <returns>原服務集合，供組合根接續註冊其他 Host 元件。</returns>
    public static IServiceCollection AddGoodShepherdLineWebhook(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddLineWebhookPipeline(configuration);
        RegisterDispatch(services, configuration);
        RegisterLessonQr(services, configuration);
        RegisterMemberNotifyRelay(services, configuration);
        return services;
    }

    /// <summary>註冊預設 scoped handler 與可回收 HTTP factory；已有產品 handler 時保留其唯一註冊 owner。</summary>
    private static void RegisterDispatch(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<SpeechMessage.Localization.IChurchClock>(_ => new SpeechMessage.Localization.ChurchClock(new SystemChurchUtcClock()));
        services.AddSingleton<IKeywordCatalog>(_ => GoodShepherdKeywordCatalog.Create());
        services.TryAddSingleton<IMemberCultureResolver>(_ => new MemberCultureResolver(new LocalizationOptions()));
        services.TryAddSingleton<IChurchTextOverrideSnapshotProvider, EmptyWebhookTextOverrideSnapshotProvider>();
        services.TryAddSingleton<GoodShepherdTextCatalog>();
        services.TryAddScoped<IWebhookScopeResolver, WebhookScopeResolver>();
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
