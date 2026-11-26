/*
檔案整體說明
目的與定位：集中好牧人 2.0 F2 的服務組合、共用診斷生命週期與尚未啟用的事件插槽，讓 Program 保持單一註冊入口。
主要功能與流程：驗證本階段不載入 Channel 或例外 LINE 外送；統一 ToolUtility 的兩個環境鍵為已驗 Host 環境，註冊固定 GoodShepherd2 管理器、Hosting、ICU／語系快照及有界 Webhook 管線。空驗簽器永遠拒絕，事件處理器意外被呼叫則拋錯；F2 的空 HTTP 端點不接入這些插槽。
使用邊界與維護：沒有任何正式 Channel、token、Session 或 request 參照進入 singleton。診斷 lifetime 在所有管線服務之前註冊，Host 反向停止時先排空 HTTP／queue，再解除程序相容入口及釋放 diagnostics。DI 最終 Dispose 可重入相同清理工作；取消不能略過資源釋放。所有設定只活至目前 Host；不建立隱藏容器、sender、重試或額外背景 task。真實啟停回歸保護 owner 解除，實機驗證仍獨立列待補。
*/
using ChurchReport.Logging;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using SpeechMessage.Hosting;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Startup;

/// <summary>F2 組合根入口；只在 Build 前由單一部署者組裝，不接受 HTTP 指定產品或外送憑證。</summary>
public static class GoodShepherd2HostRegistration
{
    /// <summary>驗證無外送設定後註冊主機擁有的共用服務；未通過時在建線、通知或管線啟動前拒絕。</summary>
    /// <param name="services">目前 Host 的唯一服務集合。</param>
    /// <param name="configuration">目前 Host 擁有的部署組態；環境鍵統一後不可由 request 改寫。</param>
    /// <param name="environment">已通過 F2 測試組織守門的環境快照。</param>
    /// <param name="logging">同一 builder 的 logging 組合；只保留安全 Error／Critical provider。</param>
    /// <returns>完成註冊的原服務集合。</returns>
    public static IServiceCollection AddGoodShepherd2Foundation(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment, ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logging);
        var output = ExceptionOutputOptions.FromConfiguration(configuration);
        if (output.SendLine || configuration.GetSection("LineWebhook:Channels").Value is not null ||
            configuration.GetSection("LineWebhook:Channels").GetChildren().Any() ||
            configuration.GetValue<bool>("LineWebhook:EnableLegacyCallback"))
            throw new InvalidOperationException("GoodShepherd2 F2 does not enable outbound LINE or product channels.");

        configuration["DOTNET_ENVIRONMENT"] = environment.EnvironmentName;
        configuration["ASPNETCORE_ENVIRONMENT"] = environment.EnvironmentName;
        services.AddSpeechMessageHosting(Path.Combine(environment.ContentRootPath, "Logs"), output);
        logging.ClearProviders();
        services.AddSingleton<ILoggerProvider, ExceptionLoggerProvider>();
        services.AddHostedService<GoodShepherd2DiagnosticsLifetime>();
        StartupGuards.ValidateIcuMode();
        services.AddSingleton(StartupGuards.ValidateLocalizationConfiguration(configuration, isProduction: false));
        services.AddToolUtility(configuration, GoodShepherd2Product.Name);
        services.AddSingleton<IWebhookSignatureVerifier, InactiveSignatureVerifier>();
        services.AddScoped<IWebhookEnvelopeHandler, InactiveEnvelopeHandler>();
        services.AddLineWebhookPipeline(configuration);
        return services;
    }
}

/// <summary>程序級診斷綁定的 Host owner；沒有 LINE sender，完整停止後才能建立下一個主機。</summary>
/// <param name="diagnostics">DI 擁有的唯一診斷 owner，於解除綁定後排空。</param>
/// <param name="adapter">DI 擁有的程序相容入口，只借用同一 diagnostics。</param>
internal sealed class GoodShepherd2DiagnosticsLifetime(
    ExceptionDiagnostics diagnostics, ExceptionReportingAdapter adapter) : IHostedService, IAsyncDisposable
{
    /// <summary>僅序列化停止工作發布，不在 gate 內等待 I/O。</summary>
    private readonly object gate = new();
    /// <summary>正常停止與 DI 最終清理共用一次工作，不保留 request 或事件內容。</summary>
    private Task? disposal;

    /// <summary>依賴解析已綁定唯一相容 owner；取消只拒絕啟動，不建立新的通知工作。</summary>
    /// <param name="cancellationToken">Host 啟動取消訊號。</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <summary>較晚註冊的 HTTP／queue 已停止後，完整解除及排空；不以停止 token 放棄清理。</summary>
    /// <param name="cancellationToken">Host 停止訊號；資源釋放仍完整等待。</param>
    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    /// <summary>發布唯一清理工作，重入只等待同一工作，不重複解除另一個 Host 的 owner。</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate) return new ValueTask(disposal ??= DisposeCoreAsync());
    }

    /// <summary>先解除程序入口，再排空診斷及釋放 writer／mutex；解除故障也不能略過 drain。</summary>
    private async Task DisposeCoreAsync()
    {
        try { adapter.Dispose(); }
        finally { await diagnostics.DisposeAsync().ConfigureAwait(false); }
    }
}

/// <summary>F3 前的拒絕驗簽插槽；不讀秘密、不保留 body，也不能把任何事件視為已授權。</summary>
internal sealed class InactiveSignatureVerifier : IWebhookSignatureVerifier
{
    /// <summary>空 HTTP 回呼不呼叫本介面；若其他呼叫端誤用，一律拒絕並尊重正常取消。</summary>
    /// <param name="channelId">未驗證識別，不保存或輸出。</param>
    /// <param name="body">不讀取、不保存的借入本文。</param>
    /// <param name="signature">不讀取、不保存的借入簽章。</param>
    /// <param name="cancellationToken">呼叫端取消。</param>
    /// <returns>固定 false，絕不授權事件。</returns>
    public ValueTask<bool> VerifyAsync(string channelId, ReadOnlyMemory<byte> body, string signature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}

/// <summary>F3 前沒有產品事件業務；誤入佇列時保留可告警的失敗，不能靜默吃掉事件。</summary>
internal sealed class InactiveEnvelopeHandler : IWebhookEnvelopeHandler
{
    /// <summary>拒絕事件處理，讓 queue 的既有安全診斷記錄真正組合錯誤；不保存 envelope。</summary>
    /// <param name="envelope">不讀取、不保存的事件。</param>
    /// <param name="cancellationToken">正常取消優先傳回呼叫端。</param>
    /// <returns>本階段沒有成功處理路徑。</returns>
    public ValueTask HandleAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("GoodShepherd2 product webhook handling is not enabled.");
    }
}
