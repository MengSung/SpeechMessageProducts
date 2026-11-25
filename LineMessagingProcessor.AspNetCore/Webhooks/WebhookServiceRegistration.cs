/*
檔案整體說明

目的與定位：提供與產品無關的 LINE Webhook 管線註冊入口，所屬 LineMessagingProcessor.AspNetCore
共用主機層。好牧人 dispatcher、QR、代送或任何產品 handler 均由產品 Hosting 組合根另行註冊。

主要功能與流程：AddLineWebhookPipeline 先驗證並建立不可變 WebhookOptions 啟動快照，再透過
partial registration hook 接入驗簽、事件去重及有界佇列，加入 routing 與唯一啟動守門服務。
Host 啟動時在短生命週期 DI scope 中確認產品已註冊 IWebhookEnvelopeHandler，再依序解析三項
共用管線依賴；保留依賴 factory 的具體啟動設定錯誤，缺少 handler 時以固定訊息拒絕啟動。

使用邊界與維護：服務集合由單一 Host 組合根擁有；本檔不建立外部連線、不保存 request、
租戶、Channel、credential 或 scoped 服務。啟動探測器只保存 IServiceScopeFactory，探測
scope 在 StartingAsync 結束時釋放；queue、signature、deduplication 的背景資源由各自註冊
owner 在 Host 停止時排空並釋放。組態只讀取啟動快照，不建立 reload 訂閱。離線測試僅證明
DI 組合與 fail-closed 啟動契約，不代表 SQL、LINE 或任何實機環境已驗收。
*/
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;

namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>共用 LINE Webhook 管線註冊入口；不包含任何產品事件處理或產品資料存取。</summary>
public static partial class WebhookServiceRegistration
{
    /// <summary>
    /// 只註冊共用驗簽、去重、有界佇列、路由、啟動期設定快照及服務守門。
    /// 產品組合根必須在 Host 啟動前另行註冊至少一個 <see cref="IWebhookEnvelopeHandler"/>；
    /// 啟動守門會檢查最終 DI 註冊，缺少時拒絕接收事件。
    /// </summary>
    /// <param name="services">由單一 Host 組合根持有、尚未建置的服務集合。</param>
    /// <param name="configuration">Host 擁有的部署組態；本方法只建立不可變啟動快照。</param>
    /// <returns>原服務集合，供呼叫端繼續組裝同一個 Host。</returns>
    /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
    /// <exception cref="InvalidOperationException">設定不合法或同一 Host 重複註冊管線。</exception>
    public static IServiceCollection AddLineWebhookPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(WebhookOptions)))
            throw new InvalidOperationException("Webhook registration must have a single owner.");

        var flag = configuration.GetSection("LineWebhook:EnableLegacyCallback");
        var channel = configuration.GetSection("LineWebhook:LegacyChannelId");
        var enabled = false;
        if (flag.GetChildren().Any() ||
            (flag.Value is not null && !bool.TryParse(flag.Value, out enabled)) ||
            channel.GetChildren().Any())
        {
            throw new InvalidOperationException("Invalid webhook startup configuration.");
        }

        var options = new WebhookOptions(enabled, channel.Value);
        services.AddHostedService<WebhookStartupRegistration>();
        RegisterSignature(services, configuration);
        RegisterDeduplication(services, configuration);
        RegisterQueue(services, configuration);
        services.AddRouting();
        services.AddSingleton(options);
        return services;
    }

    /// <summary>P3-03 的唯一 hook：只註冊正式驗簽實作及其啟動設定守門。</summary>
    /// <param name="services">目前 Host 擁有的服務集合。</param>
    /// <param name="configuration">目前 Host 的部署設定。</param>
    static partial void RegisterSignature(IServiceCollection services, IConfiguration configuration);

    /// <summary>P3-04 的唯一 hook：註冊 SQL 去重 owner 與有界清理工作。</summary>
    /// <param name="services">目前 Host 擁有的服務集合。</param>
    /// <param name="configuration">目前 Host 的部署設定。</param>
    static partial void RegisterDeduplication(IServiceCollection services, IConfiguration configuration);

    /// <summary>P3-05 的唯一 hook：註冊有界事件佇列與背景 drain owner。</summary>
    /// <param name="services">目前 Host 擁有的服務集合。</param>
    /// <param name="configuration">目前 Host 的部署設定。</param>
    static partial void RegisterQueue(IServiceCollection services, IConfiguration configuration);
}

/// <summary>
/// Host 啟動前檢查最終 DI 組合的 fail-closed 守門。只持有根 scope factory，
/// 不捕獲或提升任何 scoped handler、SDK、佇列或租戶狀態。
/// </summary>
/// <param name="scopeFactory">由 Host 容器擁有的根 scope factory。</param>
internal sealed class WebhookStartupRegistration(IServiceScopeFactory scopeFactory) : IHostedLifecycleService
{
    /// <summary>
    /// 在任何 Host service 開始前檢查產品 handler 註冊及共用管線依賴。
    /// IServiceProviderIsService 只檢查註冊存在性，不為探測而建立長期 handler 或副作用。
    /// 探測 scope 在方法返回或失敗時立即釋放；缺少產品 handler 使用穩定錯誤訊息拒絕啟動。
    /// </summary>
    /// <param name="cancellationToken">Host 啟動取消訊號；取消時不保留探測 scope。</param>
    /// <exception cref="InvalidOperationException">產品 handler 或必要共用管線依賴未註冊。</exception>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = scopeFactory.CreateAsyncScope();
        var serviceProbe = scope.ServiceProvider.GetRequiredService<IServiceProviderIsService>();
        if (!serviceProbe.IsService(typeof(IWebhookEnvelopeHandler)))
            throw new InvalidOperationException("No product webhook handler is registered.");

        // 不以 || 串接解析：前項為 null 時短路會遮蔽後續 factory 的具體設定錯誤。
        var signatureVerifier = scope.ServiceProvider.GetService<IWebhookSignatureVerifier>();
        var eventDeduplicator = scope.ServiceProvider.GetService<IWebhookEventDeduplicator>();
        var eventQueue = scope.ServiceProvider.GetService<IWebhookEventQueue>();
        if (signatureVerifier is null || eventDeduplicator is null || eventQueue is null)
        {
            throw new InvalidOperationException(
                "Webhook requires signature, deduplication and queue implementations.");
        }
    }

    /// <summary>必要依賴已在 StartingAsync 完成驗證；不延後檢查或新增啟動空窗。</summary>
    /// <param name="cancellationToken">Host 啟動取消訊號。</param>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>啟動探測器不保留狀態；產品 handler 與佇列由各自 owner 管理。</summary>
    /// <param name="cancellationToken">Host 啟動取消訊號。</param>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>停止階段沒有此守門器擁有的背景工作或資源。</summary>
    /// <param name="cancellationToken">Host 停止取消訊號。</param>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>實際 queue drain 由 queue owner 負責，本守門器不攔截其停止或 Dispose。</summary>
    /// <param name="cancellationToken">Host 停止取消訊號。</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>沒有 timer、訂閱或跨 Host 狀態需要清除。</summary>
    /// <param name="cancellationToken">Host 停止取消訊號。</param>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
