/*
檔案整體說明

目的與定位：在 Webhook Host 開始接收 HTTP 前驗證所有已配置 channel 的 secret，
避免空 secret 使驗簽被略過或改用其他頻道後備；本檔不處理 request 或實際 HMAC。

主要功能與流程：Host lifecycle 的 StartingAsync 建立短命 async scope，解析
IWebhookChannelSecretResolver 並呼叫 ValidateConfiguration；缺少或格式不合法時
以固定 InvalidOperationException 阻止啟動。其餘 lifecycle 事件不保存狀態，讓真正的
queue／diagnostics owner 依各自契約停止。

使用邊界與維護：本服務只保存 IServiceScopeFactory，不捕獲 scoped service、secret、
HttpContext 或 logger state；scope 在 StartingAsync 返回前確定釋放。沒有 timer、
subscription、background task 或持久 cache。通過只表示本機配置門檻成立，不代表
DMZ／IIS／LINE 實機可用。
*/
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Tenancy;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Signatures;

/// <summary>Webhook 驗簽的啟動 fail-closed lifecycle owner。</summary>
public sealed class WebhookSignatureStartupValidation(IServiceScopeFactory scopeFactory) : IHostedLifecycleService
{
    /// <summary>Host DI 提供的 scope factory；不保存 scope 或 secret。</summary>
    private readonly IServiceScopeFactory scopeFactory = scopeFactory
        ?? throw new ArgumentNullException(nameof(scopeFactory));

    /// <summary>在任何 HTTP 接收前完成頻道 secret 格式及存在性驗證。</summary>
    /// <param name="cancellationToken">Host 啟動取消訊號。</param>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IWebhookChannelSecretResolver>().ValidateConfiguration();
    }

    /// <summary>驗證已於 StartingAsync 完成，這裡不延後或重複建立資源。</summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>啟動後不保存狀態或背景資源。</summary>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>停止階段不擁有 HTTP 或 queue 資源，不執行額外工作。</summary>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>沒有本服務自有資源可排空，直接完成。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>停止後沒有 task、scope 或 secret 參照殘留。</summary>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
