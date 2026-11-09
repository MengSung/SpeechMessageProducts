/*
檔案整體說明
目的與定位：提供 queue 獨立組合入口，區分 DI 唯一 Dispose owner 與 Host 的非擁有轉接。
主要功能與流程：IWebhookEventQueue descriptor 只建立一次 queue；轉接服務借用同一 instance，不註冊重複 Dispose 的 concrete alias。Starting 驗 handler、Stopping 拒收、Stop 等交接與排空，容器最後 Dispose。
使用邊界與維護：集合只允許 Build 前單寫；重複 queue／選項拒絕。Host shutdown 至少四十五秒，涵蓋 P3-02 最多五秒提交、三十秒 drain 及合作清理。TimeProvider 與共用 diagnostics 外借，不持有 scoped handler、HTTP 或 credential；正式 handler 由 P3-06 提供，沒有預設成功實作。
*/
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolUtilityNameSpace.Diagnostics;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Queue;

/// <summary>正式 queue 組合入口，沒有缺省 handler 或外部 I/O，不另建容器。</summary>
public static class WebhookQueueServiceCollectionExtensions
{
    /// <summary>註冊快照、唯一 queue 與非擁有轉接；必要依賴缺項時啟動拒絕。</summary>
    /// <param name="services">尚未 Build 的服務集合，不能從 request 呼叫。</param>
    /// <param name="options">容量政策，省略為一千筆及四個分區。</param>
    /// <returns>原集合，不接管外借診斷 owner。</returns>
    public static IServiceCollection AddBoundedWebhookQueue(this IServiceCollection services, WebhookQueueOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(d => d.ServiceType == typeof(IWebhookEventQueue) || d.ServiceType == typeof(WebhookQueueOptions)))
            throw new InvalidOperationException("Webhook queue requires a single owner.");
        services.AddSingleton(options ?? new WebhookQueueOptions());
        services.AddSingleton<IWebhookEventQueue>(provider => new BoundedWebhookEventQueue(
            provider.GetRequiredService<WebhookQueueOptions>(), provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ExceptionDiagnostics>(), provider.GetService<TimeProvider>()));
        services.AddHostedService<QueueLifecycle>();
        services.PostConfigure<HostOptions>(host =>
        { if (host.ShutdownTimeout < TimeSpan.FromSeconds(45)) host.ShutdownTimeout = TimeSpan.FromSeconds(45); });
        return services;
    }

    /// <summary>只借用 DI queue，不實作 Dispose，避免同一 instance 由多 descriptor 接管。</summary>
    private sealed class QueueLifecycle(IWebhookEventQueue queue) : IHostedLifecycleService
    {
        private readonly BoundedWebhookEventQueue owner = (BoundedWebhookEventQueue)queue;
        /// <summary>所有服務開始前驗 handler，平行啟動亦無接收空窗；探測 scope 完整釋放。</summary>
        public Task StartingAsync(CancellationToken cancellationToken) => owner.ValidateHandlerAsync(cancellationToken);
        /// <summary>啟動唯一 queue 的固定 consumer。</summary>
        public Task StartAsync(CancellationToken cancellationToken) => owner.StartAsync(cancellationToken);
        /// <summary>啟動後沒有額外訂閱或 timer。</summary>
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>各服務 Stop 前先拒收，已進 Commit 的發布仍受保護。</summary>
        public Task StoppingAsync(CancellationToken cancellationToken) { owner.BeginStop(); return Task.CompletedTask; }
        /// <summary>等待交接、drain 及合作取消；容器稍後才釋放同步資源。</summary>
        public Task StopAsync(CancellationToken cancellationToken) => owner.StopAsync(cancellationToken);
        /// <summary>唯一 queue 由容器 DisposeAsync，此轉接沒有第二份資源。</summary>
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
