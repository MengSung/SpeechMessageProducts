/*
檔案整體說明
目的與定位：提供共用 Webhook 契約註冊與真正 Host 啟動前的 fail-closed 守門，只驗證必要依賴，不安裝任何假正式服務。
主要功能與流程：從部署 section 建立獨立 WebhookOptions，再以 HostedLifecycleService.StartingAsync 在所有服務 StartAsync 之前驗證三依賴可在 scope 內解析；Host 平行啟動也不能提前接收 HTTP。只保存 ScopeFactory，不捕獲 scoped verifier、SQL 連線或 queue。
使用邊界與維護：服務集合僅組合根單寫；同入口註冊重複拒絕，避免設定衝突。探測 scope 在 StartAsync 返回前釋放，缺任一依賴就固定錯誤拒絕，容器仍由 Host Dispose。正式空 secret／SQL／queue 初始化政策由後續包實作；本守門不宣稱遠端健康。
*/
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>只註冊不可變選項及啟動守門；正式 verifier／deduplicator／queue 必須由後續包明確提供。</summary>
public static partial class WebhookServiceRegistration
{
    /// <summary>從部署設定嚴格讀取 legacy 布林及 channel；不訂閱 reload、不建立隱藏容器或預設服務。</summary>
    /// <param name="services">尚未 Build 的單一 Host 服務集合。</param>
    /// <param name="configuration">Host 擁有的受信任組態，只借用於建立快照。</param>
    /// <returns>原集合；缺正式依賴於 Host.StartAsync 拒絕，避免接收未受保護的 HTTP。</returns>
    public static IServiceCollection AddLineWebhook(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(d => d.ServiceType == typeof(WebhookOptions)))
            throw new InvalidOperationException("Webhook registration must have a single owner.");
        var flag = configuration.GetSection("LineWebhook:EnableLegacyCallback");
        var channel = configuration.GetSection("LineWebhook:LegacyChannelId");
        var enabled = false;
        if (flag.GetChildren().Any() || (flag.Value is not null && !bool.TryParse(flag.Value, out enabled)) || channel.GetChildren().Any())
            throw new InvalidOperationException("Invalid webhook startup configuration.");
        var options = new WebhookOptions(enabled, channel.Value);
        // 三個具名編譯期接縫各有獨立檔案 owner；本包沒有實作，呼叫會被編譯器移除。
        // 不是反射探索或 no-op 正式服務；因此未完成任何後續包時仍由 startup probe 拒絕。
        RegisterSignature(services, configuration);
        RegisterDeduplication(services, configuration);
        RegisterQueue(services, configuration);
        services.AddRouting();
        services.AddSingleton(options);
        services.AddHostedService<WebhookStartupRegistration>();
        return services;
    }

    /// <summary>P3-03 在 WebhookSignatureRegistration.cs 實作此 partial method，只註冊正式驗簽及啟動設定守門，不改 Program。</summary>
    static partial void RegisterSignature(IServiceCollection services, IConfiguration configuration);

    /// <summary>P3-04 在 WebhookDeduplicationRegistration.cs 實作此 partial method，獨立註冊 SQL owner 及有界清理，不增加 A.8 欄位。</summary>
    static partial void RegisterDeduplication(IServiceCollection services, IConfiguration configuration);

    /// <summary>P3-05 在 WebhookQueueRegistration.cs 實作此 partial method，註冊有界 queue／背景 drain owner，不改其他包檔案。</summary>
    static partial void RegisterQueue(IServiceCollection services, IConfiguration configuration);

    /// <summary>啟動的短生命週期依賴探測器；scope 自行釋放，從不將 request service 提升成 singleton。</summary>
    private sealed class WebhookStartupRegistration(IServiceScopeFactory scopeFactory) : IHostedLifecycleService
    {
        /// <summary>三個必填介面都能解析才允許開始 HTTP；缺任何一個直接拋固定失敗，不讀遠端或輸出憑證。</summary>
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            if (scope.ServiceProvider.GetService<IWebhookSignatureVerifier>() is null ||
                scope.ServiceProvider.GetService<IWebhookEventDeduplicator>() is null ||
                scope.ServiceProvider.GetService<IWebhookEventQueue>() is null)
                throw new InvalidOperationException("Webhook requires signature, deduplication and queue implementations.");
        }

        /// <summary>必要驗證已在 StartingAsync 完成，這裡不可再延後檢查，否則平行啟動會產生接收空窗。</summary>
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>不保存啟動後狀態或訂閱；真正 queue 的 consumer 由後續包擁有。</summary>
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>探測 scope 已確定釋放，不提早停止其他 owner 的在途 HTTP 或 queue。</summary>
        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>本探測器不保存業務資源，停止無需背景工作；真正 queue drain 由 queue owner 執行。</summary>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>停止後無剩餘 task、timer 或資料集合，不新增跨 Host 可變狀態。</summary>
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
