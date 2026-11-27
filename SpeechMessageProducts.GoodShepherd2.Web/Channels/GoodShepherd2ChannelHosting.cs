/*
檔案整體說明
目的與定位：好牧人共通層 F3 的唯一主機組合入口，將固定 GoodShepherd2 產品目錄接到共用 Webhook 接收器；Program 僅需呼叫一次 Add 方法。
主要功能與流程：先驗證受信任的組織與六十四 bytes Base64 key 快照，再移除全部舊 verifier／resolver，接入正式 LineWebhookSignatureVerifier。完整缺設定時仍保留拒絕路徑，不解析來源或 manager；完整設定才註冊真正 SDK adapter、目錄、健康、有界傳輸委派及每分鐘接收已提交診斷的唯一 pump。固定 startup filter 只接單一數字段的 POST 路徑，執行共用 receiver 回傳結果，不保留舊 200 stub。
使用邊界與維護：路由只索引、不選產品或組織；不啟用 Scheduling、legacy 或設定式 Channel。來源、時鐘及 diagnostics 由既有容器擁有，目錄與健康 HttpClient 由單一 lifecycle owner 先 drain 再釋放。分支在產品錯誤 middleware 之外，真故障保留 Exception 交共用入口，正常 caller 取消不告警。每 Host 最多六十四個進入 receiver 的 request，滿載不等待、不租 body buffer，以減少同步 resolver 的 SDK 等待者；所有返回路徑歸還計數。沒有靜態可變狀態或 request 快取；獨立診斷 pump 只有一個有界 worker，停止時先取消並排空，再關目錄及借用依賴。合成 Kestrel 測試不代表外部實機驗收。
*/
using System.Diagnostics.CodeAnalysis;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Signatures;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.ChannelPool;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Channels;

/// <summary>F3 產品組合接點；所有設定驗證先於服務集合修改，不建立隱藏容器或接受 request 指定部署。</summary>
internal static class GoodShepherd2ChannelHosting
{
    /// <summary>在 Foundation 後註冊固定產品來源及驗簽，缺設定時只接拒絕解析器，不附帶啟用其他模組。</summary>
    /// <param name="builder">同一主機尚未建置的受信任 builder；不訂閱 reload，設定變更須重啟。</param>
    internal static void AddGoodShepherd2Channels(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        if (services.Any(service => service.ServiceType == typeof(GoodShepherd2ChannelConfiguration)))
            throw new InvalidOperationException("Channel hosting must have a single owner.");
        var snapshot = GoodShepherd2ChannelConfiguration.Read(builder.Configuration);
        services.AddSingleton(_ => snapshot);
        services.RemoveAll<IWebhookSignatureVerifier>();
        services.RemoveAll<IWebhookChannelSecretResolver>();
        services.RemoveAll<IChannelCredentialSource>();
        services.AddSingleton<IWebhookSignatureVerifier, LineWebhookSignatureVerifier>();
        services.AddTransient<IStartupFilter, GoodShepherd2ChannelStartupFilter>();
        if (!snapshot.IsConfigured)
        {
            services.AddSingleton<UnconfiguredChannelResolver>();
            services.AddSingleton<IWebhookChannelSecretResolver>(provider => provider.GetRequiredService<UnconfiguredChannelResolver>());
            services.AddSingleton<IChannelCredentialSource>(provider => provider.GetRequiredService<UnconfiguredChannelResolver>());
            return;
        }

        services.TryAddSingleton<IChurchClock, ChurchClock>();
        services.TryAddSingleton<IChannelDiagnostics, CommonChannelDiagnostics>();
        services.TryAddSingleton<IChannelDirectorySource>(provider =>
        {
            var manager = provider.GetRequiredService<IDataverseConnectionManager>();
            return new DataverseChannelDirectorySource(GoodShepherd2Product.Name, snapshot.OrganizationId, manager.Acquire,
                provider.GetRequiredService<IChannelDiagnostics>());
        });
        services.AddSingleton<GoodShepherd2ChannelLifetime>();
        // lifecycle 是唯一實際釋放 owner。目錄本身的 DI alias 也可能被容器 Dispose，
        // 但其關閉契約共用同一 closeTask，不能建立第二個 timer、key 或獨立清理工作。
        services.AddSingleton<DataverseChannelDirectory>(provider => provider.GetRequiredService<GoodShepherd2ChannelLifetime>().Directory);
        services.AddSingleton<IWebhookChannelSecretResolver>(provider => provider.GetRequiredService<DataverseChannelDirectory>());
        services.AddSingleton<IChannelCredentialSource>(provider => provider.GetRequiredService<DataverseChannelDirectory>());
        services.AddSingleton<ChannelHealthChecker>(provider => provider.GetRequiredService<GoodShepherd2ChannelLifetime>().HealthChecker);
        services.AddSingleton<ChannelCredentialTransport>(provider => new(provider.GetRequiredService<DataverseChannelDirectory>()));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<GoodShepherd2ChannelLifetime>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SlotRequestDiagnosticReceiver>(provider => new(GoodShepherd2Product.Name, snapshot.OrganizationId,
            provider.GetRequiredService<IDataverseConnectionManager>().Acquire, provider.GetRequiredService<IChurchClock>(),
            provider.GetRequiredService<ExceptionDiagnostics>()));
        services.AddSingleton<GoodShepherd2ChannelDiagnosticPump>();
        // 反向停止時先停 pump，再 drain 目錄；兩者都早於 Foundation diagnostics 與 manager 釋放。
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<GoodShepherd2ChannelDiagnosticPump>());
    }
}

/// <summary>固定 /webhook 分支；只持有無租戶資料的局部處理流程，HTTP context 僅活至當次 await 完成。</summary>
internal sealed class GoodShepherd2ChannelStartupFilter : IStartupFilter
{
    /// <summary>單一 Host 的在途接收計數，上限低於目錄的一百二十八操作容量，保留健康與傳輸空間。</summary>
    private int activeRequests;
    /// <summary>將單一安全分支置於既有路由之前，其他工作包路徑仍委派原 next。</summary>
    /// <param name="next">原本的主機組裝流程，不保存到全域或背景工作。</param>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Map("/webhook", branch => branch.Run(ReceiveAsync));
        next(app);
    };

    /// <summary>先檢 HTTP 方法及單一數字段，再交共用 receiver 驗原始 bytes；真正故障不輸出原始本文。</summary>
    /// <param name="context">當次 request 借用 context，方法返回後不保留參照。</param>
    private async Task ReceiveAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "POST";
            return;
        }
        var path = context.Request.Path.Value;
        // Map 已移除固定前綴；禁止額外斜線、URL 編碼分隔符、非 ASCII 數字及超過二十位。
        if (path is not { Length: >= 2 and <= 21 } || path[0] != '/' || !WebhookOptions.IsValidChannelId(path[1..]))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        if (Interlocked.Increment(ref activeRequests) > 64)
        {
            Interlocked.Decrement(ref activeRequests);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        try
        {
            var result = await WebhookHttpReceiver.ReceiveAsync(context, path[1..]).ConfigureAwait(false);
            await result.ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = 499;
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ExceptionDiagnostics>()
                .Report(exception, "GoodShepherd2.Channels.Http", context.RequestAborted);
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        finally { Interlocked.Decrement(ref activeRequests); }
    }
}

/// <summary>整對設定皆缺時的明確拒絕來源；不解析 SDK、設定 resolver 或預設 Channel，不擁有外部資源。</summary>
internal sealed class UnconfiguredChannelResolver : IWebhookChannelSecretResolver, IChannelCredentialSource
{
    /// <summary>沒有已授權頻道，回不可變空集合，不觸發任何讀取。</summary>
    public IReadOnlyList<string> ConfiguredChannelIds => Array.Empty<string>();
    /// <summary>所有索引都回 false；不保留或記錄 channelId，不能把缺設定當驗簽成功。</summary>
    public bool TryResolveSecret(string channelId, [NotNullWhen(true)] out string? secret)
    {
        secret = null;
        return false;
    }
    /// <summary>供未配置產品的 credential 接點明確拒絕，不回退其他產品憑證。</summary>
    public bool TryGetCredential(string channelKey, out LineChannelCredential? credential)
    {
        credential = null;
        return false;
    }
    /// <summary>整對設定缺席是允許的封閉模式，零 Channel 不代表外部功能就緒。</summary>
    public void ValidateConfiguration() { }
}
