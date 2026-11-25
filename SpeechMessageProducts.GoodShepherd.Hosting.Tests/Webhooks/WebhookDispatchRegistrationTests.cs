/*
檔案整體說明
目的與定位：保護好牧人產品的 P3-06 dispatcher 經 AddGoodShepherdLineWebhook 組合入口註冊的契約，避免只存在未接入處理器或成功丟棄事件的替身。
主要功能與流程：以完全合成且無憑證的設定建立服務描述，斷言唯一 scoped envelope handler；不建立 Host 或解析遠端 CRM 依賴。此測試只證明產品註冊 descriptor，不以此取代單獨驗證共用 pipeline 的啟動拒絕測試。
使用邊界與維護：服務集合只活於單一測試，不開啟 HTTP、D365、SQL、背景 task 或計時器；沒有跨案例的使用者狀態。後續實際事件測試另以窄 SDK 與 HTTP 替身驗證行為，此處不可用 descriptor 存在冒稱業務完成。
*/
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Tests.Webhooks;

/// <summary>正式好牧人註冊接縫的離線契約；僅解析 scoped adapter，不建立外部連線。</summary>
public sealed class WebhookDispatchRegistrationTests
{
    /// <summary>真實產品組合必須註冊單一 scoped dispatcher，且 Reply sender 可由同一 scope 解析。</summary>
    [Fact]
    public void AddGoodShepherdLineWebhook_RegistersOneScopedRealDispatcher()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var owner = (IDisposable)configuration;
        var services = new ServiceCollection();
        services.AddGoodShepherdLineWebhook(configuration);
        var descriptor = Assert.Single(services.Where(item => item.ServiceType == typeof(IWebhookEnvelopeHandler)));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal("WebhookDispatcher", descriptor.ImplementationType?.Name);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<LineWebhookReplySender>(
            scope.ServiceProvider.GetRequiredService<IWebhookReplySender>());
    }
}
