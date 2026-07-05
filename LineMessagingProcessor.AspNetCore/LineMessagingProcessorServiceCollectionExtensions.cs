// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs
// 所屬區塊：LINE Messaging Processor 的 ASP.NET Core DI 與整合測試區塊。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineMessagingProcessorServiceCollectionExtensions
// 主要成員：AddLineMessagingProcessor、AddLineRichMenus
// 引用命名空間：Line.Messaging、LineMessagingProcessor、LineMessagingProcessor.RichMenus、LineMessagingProcessor.Workflows、Microsoft.Extensions.DependencyInjection、Microsoft.Extensions.DependencyInjection.Extensions、Microsoft.Extensions.Options
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.RichMenus;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LineMessagingProcessor.AspNetCore;

/// <summary>
/// ASP.NET Core DI 註冊入口，集中註冊共用 LINE processor 家族。
/// 註冊流程刻意拆開：未來產品可以只使用共用 RichMenu 核心，
/// 不必同時被迫提供產品專屬的 catalog、policy 或持久化 state store。
/// </summary>
public static class LineMessagingProcessorServiceCollectionExtensions
{
    private const string HttpClientName = "LineMessagingProcessor";

    /// <summary>
    /// 註冊共用 LINE Messaging client、低階 processor 與預設工作流。
    /// RichMenu 基礎服務會在這裡一併掛上，但產品專屬 catalog 仍由 <see cref="AddLineRichMenuProvisioning{TCatalog}"/> 明確提供。
    /// </summary>
    /// <param name="services">ASP.NET Core service collection。</param>
    /// <param name="configure">設定 LINE channel token 與 API base URI 的委派。</param>
    public static IServiceCollection AddLineMessagingProcessor(
        this IServiceCollection services,
        Action<LineMessagingProcessorOptions> configure)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        services.Configure(configure);
        services.AddHttpClient(HttpClientName);
        services.AddTransient(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LineMessagingProcessorOptions>>().Value;
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            return new LineMessagingClient(httpClient, options.ChannelAccessToken, options.ApiBaseUri);
        });
        services.AddTransient(sp =>
            new LineMessagingProcessorClass(sp.GetRequiredService<LineMessagingClient>()));
        services.AddTransient<ILineNotificationWorkflow, LineNotificationWorkflow>();
        services.AddTransient<ILineReplyWorkflow, LineReplyWorkflow>();
        // 基礎 RichMenu 指派、文字觸發與舊版 create/upload/link workflow 可與一般 LINE workflow 一起註冊；
        // 產品 catalog 另外註冊，避免共用套件知道產品選單內容。
        services.AddLineRichMenus();

        return services;
    }

    /// <summary>
    /// 註冊產品中立的 RichMenu services。
    /// 此方法不註冊 <see cref="ILineRichMenuCatalog"/>，因為 catalog 內容屬於各產品。
    /// 未來 ASP.NET Core 產品可先註冊 <see cref="LineMessagingProcessorClass"/>，
    /// 再加上自己的 catalog、policies 與持久化 state store。
    /// </summary>
    public static IServiceCollection AddLineRichMenus(
        this IServiceCollection services,
        Action<LineRichMenuTextTriggerOptions>? configureTextTriggers = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureTextTriggers != null || !services.Any(descriptor => descriptor.ServiceType == typeof(LineRichMenuTextTriggerOptions)))
        {
            var textTriggerOptions = new LineRichMenuTextTriggerOptions();
            configureTextTriggers?.Invoke(textTriggerOptions);

            // 允許產品在 AddLineMessagingProcessor 之後再次呼叫 AddLineRichMenus 覆蓋文字觸發設定；
            // 這樣 DI 註冊順序不會讓預設空設定卡住後續產品設定。
            services.RemoveAll<LineRichMenuTextTriggerOptions>();
            services.AddSingleton(textTriggerOptions);
        }
        // TryAdd* 讓產品測試或正式環境可以替換 cache/state store/processor，
        // 例如改用 Redis 或資料庫保存 RichMenu 狀態，而不需要 fork 共用註冊方法。
        services.TryAddSingleton<ILineRichMenuIdCache, InMemoryLineRichMenuIdCache>();
        services.TryAddSingleton<IRichMenuStateStore, InMemoryRichMenuStateStore>();
        services.TryAddTransient<ILineRichMenuProcessor, LineMessagingProcessorRichMenuAdapter>();
        services.TryAddTransient<ILineRichMenuWorkflow, LineRichMenuWorkflow>();
        services.TryAddTransient<ILineRichMenuAssignmentWorkflow, LineRichMenuAssignmentWorkflow>();
        services.TryAddTransient<ILineRichMenuTextTriggerResolver, LineRichMenuTextTriggerResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IRichMenuPolicy, LineRichMenuTextTriggerPolicy>());
        services.TryAddTransient<IRichMenuOrchestrator>(sp =>
            new RichMenuOrchestrator(
                sp.GetServices<IRichMenuPolicy>(),
                sp.GetRequiredService<ILineRichMenuAssignmentWorkflow>()));
        services.TryAddTransient<IRichMenuExpirationSweepWorkflow, RichMenuExpirationSweepWorkflow>();

        return services;
    }

    /// <summary>
    /// 使用產品擁有的 catalog 註冊 RichMenu provisioning。
    /// 將 provisioning 與 <see cref="AddLineRichMenus"/> 分開，可避免共用核心要求每個應用程式
    /// 必須先定義選單，才能使用 assignment、文字 trigger 或舊版 workflow services。
    /// </summary>
    public static IServiceCollection AddLineRichMenuProvisioning<TCatalog>(this IServiceCollection services)
        where TCatalog : class, ILineRichMenuCatalog
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddLineRichMenus();
        services.TryAddTransient<ILineRichMenuCatalog, TCatalog>();
        services.TryAddTransient<ILineRichMenuProvisioningWorkflow, LineRichMenuProvisioningWorkflow>();

        return services;
    }
}

