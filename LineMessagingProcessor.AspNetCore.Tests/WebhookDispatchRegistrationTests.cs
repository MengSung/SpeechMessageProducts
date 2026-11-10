/*
檔案整體說明
目的與定位：保護 P3-06 事件分派器經正式 AddLineWebhook 組合入口註冊的契約，避免只有未接入的處理器或成功丟棄事件的測試替身。
主要功能與流程：以完全合成且無憑證的設定建立服務描述，斷言唯一 scoped envelope handler；不建立 Host 或解析遠端 CRM 依賴。第一輪應因正式分派服務尚不存在而失敗。
使用邊界與維護：服務集合只活於單一測試，不開啟 HTTP、D365、SQL、背景 task 或計時器；沒有跨案例的使用者狀態。後續實際事件測試另以窄 SDK 與 HTTP 替身驗證行為，此處不可用 descriptor 存在冒稱業務完成。
*/
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>正式註冊接縫的離線契約；僅讀描述，不建立連線或持有須釋放的服務。</summary>
public sealed class WebhookDispatchRegistrationTests
{
    /// <summary>
    /// 缺少正式 handler 時決定性失敗；同時解析真正 scoped Reply sender，
    /// 確認新增的 IHttpClientFactory 相依已在正式 AddLineWebhook 組合中可用。
    /// </summary>
    [Fact]
    public void AddLineWebhook_RegistersOneScopedRealDispatcher()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var owner = (IDisposable)configuration;
        var services = new ServiceCollection();
        services.AddLineWebhook(configuration);
        var descriptor = Assert.Single(services.Where(item => item.ServiceType == typeof(IWebhookEnvelopeHandler)));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal("WebhookDispatcher", descriptor.ImplementationType?.Name);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<LineWebhookReplySender>(
            scope.ServiceProvider.GetRequiredService<IWebhookReplySender>());
    }
}
