/*
檔案整體說明
目的與定位：F11 在好牧人 Program 的單行組合入口，集中就緒 probe、每日健康註冊與內部 F5 推播 owner，不提供任何新 HTTP 業務路由。
主要功能與流程：既有 F3 完整目錄描述子存在才註冊固定組織的 Gateway；未配置不註冊無憑證後備。推播 Host owner 按需建立無 Cookie／認證／轉址的單一 HttpClient 與真批次觀測 handler；Gateway 每次重新查權威 membership 與發送紀錄。
使用邊界與維護：Host lifecycle 在 F3 之後註冊，反向 Stopping 先停止並完整排空正式 queue，再排空 Gateway 及關 HttpClient，之後 F3 才可關目錄。未實際解析 Gateway 時不建傳輸，既有外部測試 queue 不被新增生命週期接管；實際解析時只接受具有停止契約的正式 BoundedWebhookEventQueue，未知實作拒絕發送。DI、queue、目錄、manager、Meter 與 diagnostics 均只有既有 owner，不留 request 或跨使用者狀態；每個新候選建構失敗即清理未移交資源。
*/
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Gateway;
using SpeechMessage.Hosting.Observability;
using SpeechMessageProducts.GoodShepherd2.Web.Health;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using System.Runtime.ExceptionServices;

namespace SpeechMessageProducts.GoodShepherd2.Web.Observability;

/// <summary>只供受信任組合根呼叫一次；不從 HTTP 決定部署產品、組織或憑證。</summary>
internal static class GoodShepherd2ObservationHosting
{
    /// <summary>按現有 F3 前置發布描述子，不解析目錄或建立外部連線；所有 F11 入口在同一行組合。</summary>
    internal static void AddGoodShepherd2Observation(WebApplicationBuilder builder)
    {
        GoodShepherd2Readiness.AddGoodShepherd2Readiness(builder);
        GoodShepherd2HealthSchedule.AddGoodShepherd2HealthSchedule(builder);
        if (!builder.Services.Any(d => d.ServiceType == typeof(DataverseChannelDirectory))) return;
        if (!Guid.TryParseExact(builder.Configuration["Channels:OrganizationId"], "D", out var organization) || organization == Guid.Empty)
            throw new InvalidOperationException("Push hosting requires a fixed validated organization.");
        if (builder.Services.Any(d => d.ServiceType == typeof(ILinePushGateway)))
            throw new InvalidOperationException("Push hosting requires a single gateway owner.");
        builder.Services.AddSingleton(provider => new GoodShepherd2PushLifetime(organization,
            provider.GetRequiredService<IDataverseConnectionManager>(),
            new ChannelCredentialExecutor(provider.GetRequiredService<DataverseChannelDirectory>()),
            provider.GetRequiredService<HostingMeters>(), provider.GetRequiredService<ExceptionDiagnostics>(),
            provider.GetRequiredService<IWebhookEventQueue>()));
        builder.Services.AddSingleton<ILinePushGateway>(provider => provider.GetRequiredService<GoodShepherd2PushLifetime>().Gateway);
        builder.Services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<GoodShepherd2PushLifetime>());
    }
}

/// <summary>單一產品 HttpClient 的唯一 owner；正式 queue 先 drain，才可關發送與後續 F3 目錄。</summary>
internal sealed class GoodShepherd2PushLifetime(Guid organization, IDataverseConnectionManager manager,
    ILineCredentialExecutor credentials, HostingMeters meters, ExceptionDiagnostics diagnostics,
    IWebhookEventQueue queue, Func<HttpMessageHandler>? transportFactory = null) : IHostedLifecycleService, IAsyncDisposable
{
    private readonly object gate = new();
    private GoodShepherd2LinePushGateway? gateway;
    private HttpClient? client;
    private bool closing;
    private Task? disposal;

    /// <summary>只有正式 queue 的完整生命週期可發送；建構一次無 request 狀態的傳輸，不寫預設 Authorization。</summary>
    internal ILinePushGateway Gateway
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(closing, this);
                if (gateway is not null) return gateway;
                if (queue is not BoundedWebhookEventQueue)
                    throw new InvalidOperationException("Push hosting requires the bounded queue lifecycle.");
                HttpClient? candidate = null;
                try
                {
                    // 正式組合根不傳工廠；internal 故障測試才借此接縫替換最內層網路，觀測 handler 始終是真的。
                    candidate = new HttpClient(new PushBatchTrackingHandler(meters, transportFactory?.Invoke() ?? new SocketsHttpHandler
                    {
                        UseCookies = false, AllowAutoRedirect = false, Credentials = null,
                        ConnectTimeout = TimeSpan.FromSeconds(5), MaxConnectionsPerServer = 32,
                        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
                    })) { Timeout = Timeout.InfiniteTimeSpan };
                    var value = new GoodShepherd2LinePushGateway(organization, manager, credentials, new LinePushClient(candidate), diagnostics);
                    client = candidate; gateway = value; return value;
                }
                catch { candidate?.Dispose(); throw; }
            }
        }
    }

    /// <summary>啟動不發送、不查租戶，也不建立 HTTP transport；按需解析才啟用唯一 owner。</summary>
    public Task StartingAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    /// <summary>沒有獨立 worker 或 timer，正常啟動只尊重 caller 取消。</summary>
    public Task StartAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    /// <summary>啟動後不新增訂閱或背景工作。</summary>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>在 F3 的 Stopping 前完成 queue／Gateway 排空；停止 token 不能令清理提前返回。</summary>
    public Task StoppingAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();
    /// <summary>啟動失敗或一般停止共用同一清理工作，不重複關其他 owner。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();
    /// <summary>資源已排空，沒有後續補償 task。</summary>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>發布唯一關閉工作；沒有建立 Gateway 的 Host 不接管外部測試 queue 或額外建立傳輸。</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closing = true;
            return new(disposal ??= CloseAsync());
        }
    }

    /// <summary>先等待所有 queue handler 與 scope，再完整排空發送／HTTP；任何清理故障都安全告警且不略過後續。</summary>
    private async Task CloseAsync()
    {
        if (gateway is null) return;
        Exception? primary = null;
        try { await ((BoundedWebhookEventQueue)queue).StopAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) { primary = exception; diagnostics.Report(exception, "GoodShepherd2.Push.QueueDrain"); }
        try { await gateway.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception)
        {
            diagnostics.Report(exception, "GoodShepherd2.Push.Drain");
            primary = primary is null ? exception : new AggregateException(primary, exception);
        }
        finally
        {
            try { Interlocked.Exchange(ref client, null)?.Dispose(); }
            catch (Exception exception)
            {
                diagnostics.Report(exception, "GoodShepherd2.Push.HttpDispose");
                primary = primary is null ? exception : new AggregateException(primary, exception);
            }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }
}
