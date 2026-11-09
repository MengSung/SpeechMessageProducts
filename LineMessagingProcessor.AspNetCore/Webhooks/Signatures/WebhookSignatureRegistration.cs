/*
檔案整體說明

目的與定位：實作 WebhookServiceRegistration 的 P3-03 partial composition hook，
只在 Host 提供非秘密 channel mapping 時註冊正式 Tenancy resolver、LineSignature adapter
與啟動守門；不改 Program 或 P3-02 HTTP 端點。

主要功能與流程：檢查部署是否至少提供一個 `LineWebhook:Channels` mapping，加入固定
GoodShepherd Tenancy 產品邊界，以 TryAdd 保留測試替身或其他明確 owner，最後掛上
startup validator。未提供 mapping 時不安裝假 verifier，既有 startup probe 仍會拒絕
未完成的 Host。

使用邊界與維護：本檔只組合 DI，不保存 configuration、request、secret 或租戶結果，
不建立 reload watcher、queue、timer 或 background work。正式設定的 secret 仍由
Host／User Secrets 擁有；啟動守門若缺 secret 只輸出固定錯誤，不輸出值或 mapping
內容。產品主機需以部署設定提供非秘密 mapping。
*/
using LineMessagingProcessor.AspNetCore.Webhooks.Signatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeechMessage.Tenancy;

namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>提供 P3-03 正式驗簽的 DI 接縫，重複呼叫不覆蓋既有 owner。</summary>
public static partial class WebhookServiceRegistration
{
    /// <summary>
    /// 只在部署 mapping 存在時接入正式驗簽；空設定不產生偽成功服務。
    /// </summary>
    /// <param name="services">Host 服務集合。</param>
    /// <param name="configuration">Host 擁有的部署設定。</param>
    static partial void RegisterSignature(IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetSection("LineWebhook:Channels").GetChildren().Any())
            return;

        services.AddSpeechMessageTenancy("GoodShepherd");
        services.TryAddSingleton<IWebhookChannelSecretResolver>(provider =>
            new ConfigurationWebhookChannelSecretResolver(
                provider.GetRequiredService<IConfiguration>(), "GoodShepherd"));
        services.TryAddSingleton<IWebhookSignatureVerifier, LineWebhookSignatureVerifier>();
        services.AddHostedService<WebhookSignatureStartupValidation>();
    }
}
