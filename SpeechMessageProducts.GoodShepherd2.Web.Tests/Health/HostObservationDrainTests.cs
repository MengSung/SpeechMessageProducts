/*
檔案整體說明
目的與定位：F11 一百事件跨四接點共同排空的離線整合，使用真正 queue、排程 scoped 核心、SDK 觀測殼與 F5 Gateway，而非直接 Track 四個票據冒稱整合。
主要功能與流程：事件進真 queue，由測試專用 handler 呼叫真 ScheduledJobDispatcher 的 scoped 核心，再由 scheduled handler 把同一 wh correlation 交主機 Gateway 及真發送紀錄 adapter。HTTP barrier 期間四個計數均在途；解除後一百筆完成並逐筆核對 correlation、lease、request／response 與 scope 清理。另注入 HTTP、SDK 故障及 caller 取消。
使用邊界與維護：排程持久 SQL／Hangfire worker 沒有啟動，本案只驗其真 scope 執行接點，不能冒稱 F4 SQL 實機。反射只呼叫正式私有 scoped 方法，沒有複製產品邏輯或載入外部組件。全部資料合成；finally 解除 barrier、await queue Stop／Dispose 與所有工作，再關 DI 及 fixture，不遺棄 Task 或保留使用者狀態。
*/
using System.Reflection;
using System.Text.Json;
using Line.Messaging;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using LineMessagingProcessor.Core.Gateway;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SpeechMessage.Hosting.Observability;
using SpeechMessage.Scheduling;
using SpeechMessage.Scheduling.Hangfire;
using SpeechMessageProducts.GoodShepherd2.Web.Observability;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>一百筆真 queue 到真 F5 接點的共同生命週期；不呼叫任何外部服務。</summary>
public sealed class HostObservationDrainTests
{
    /// <summary>正常、HTTP／SDK 故障、caller 取消都須消費一百事件；成功逐筆同編號，全部資源歸零且沒有假 accepted。</summary>
    [Theory]
    [InlineData("normal")]
    [InlineData("http")]
    [InlineData("sdk")]
    [InlineData("cancel")]
    public async Task Shutdown_CountersReturnToBaseline(string scenario)
    {
        await using var fixture = new PushObservationFixture();
        using var cancellation = new CancellationTokenSource();
        var state = new PipelineState(fixture, cancellation.Token);
        fixture.Transport.Delay = scenario != "sdk";
        fixture.Transport.Fail = scenario == "http";
        fixture.FailSdk = scenario == "sdk";
        var services = new ServiceCollection();
        services.AddSingleton(fixture.Meters); services.AddSingleton(fixture.Diagnostics); services.AddLogging();
        services.AddScoped<IWebhookEnvelopeHandler>(provider => new QueueHandler(provider.GetRequiredService<ScheduledJobDispatcher>(), state));
        services.AddScoped<IScheduledJobScopeValidator, SyntheticValidator>();
        services.AddScoped<IScheduledJobHandler>(_ => new ScheduledHandler(state));
        services.AddProductScheduling(new SchedulingOptions("GoodShepherd2", fixture.Organization,
            "Server=unopened.example;Database=synthetic;Integrated Security=true;Encrypt=true"));
        services.AddBoundedWebhookQueue(new WebhookQueueOptions(128, 4));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var queue = Assert.IsType<BoundedWebhookEventQueue>(provider.GetRequiredService<IWebhookEventQueue>());
        using var transport = new PushObservationFixture.ControlledTransport
        { Delay = scenario != "sdk", Fail = scenario == "http" };
        await using var lifetime = new GoodShepherd2PushLifetime(fixture.Organization, fixture.Manager,
            fixture.Credentials, fixture.Meters, fixture.Diagnostics, queue, () => transport);
        state.Gateway = lifetime.Gateway;
        await lifetime.StartingAsync(default);
        await lifetime.StartAsync(default);
        await queue.StartAsync(default);
        Task? stopping = null;
        try
        {
            for (var index = 0; index < 100; index++)
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { webhookEventId = "event-" + index, type = "message" }));
                var registration = new Mock<IWebhookEventRegistration>(MockBehavior.Strict);
                registration.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
                Assert.True(await queue.TryEnqueueAsync(new WebhookEnvelope("2012804957", json.RootElement), registration.Object, default));
                registration.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            if (scenario != "sdk")
            {
                await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var peak = fixture.Meters.GetSnapshot();
                Assert.True(peak.WebhookQueueDepth > 0); Assert.True(peak.DataverseLeasesActive > 0);
                Assert.True(peak.SchedulerJobsRunning > 0); Assert.True(peak.PushBatchesInflight > 0);
            }
            if (scenario == "cancel") cancellation.Cancel();
            stopping = lifetime.StoppingAsync(default);
            if (scenario is "normal" or "http") Assert.False(stopping.IsCompleted);
        }
        finally
        {
            transport.Release.TrySetResult();
            await (stopping ?? lifetime.StoppingAsync(default)).WaitAsync(TimeSpan.FromSeconds(20));
            await queue.StopAsync(default);
            await lifetime.StopAsync(default);
            await lifetime.StoppedAsync(default);
        }
        Assert.Equal(100, state.Completed);
        Assert.Equal(100, state.ScopeDisposals);
        Assert.Equal(fixture.LeaseAcquires, fixture.LeaseReleases);
        Assert.Equal(transport.Requests, transport.RequestDisposals);
        Assert.Equal(scenario == "normal" ? 100 : 0, transport.ResponseDisposals);
        Assert.Equal(new HostingMeterSnapshot(0, 0, 0, 0), fixture.Meters.GetSnapshot());
        if (scenario == "normal")
        {
            Assert.Equal(100, state.Accepted);
            var correlations = fixture.Correlations(); Assert.Equal(100, correlations.Length);
            Assert.Equal(Enumerable.Range(0, 100).Select(i => "wh-event-" + i).Order(), correlations.Order());
            Assert.Equal(100, transport.Requests);
        }
        else Assert.Equal(0, state.Accepted);
    }

    /// <summary>單案純計數與借用 owner；不保留 envelope 或 SDK 回應，最多一百次操作。</summary>
    private sealed class PipelineState(PushObservationFixture fixture, CancellationToken cancellation)
    {
        internal PushObservationFixture Fixture { get; } = fixture;
        internal CancellationToken Cancellation { get; } = cancellation;
        internal ILinePushGateway? Gateway { get; set; }
        internal int Completed;
        internal int ScopeDisposals;
        internal int Accepted;
    }

    /// <summary>測試專用事件路由；產品 InactiveEnvelopeHandler 不被替換或新增使用者業務。</summary>
    private sealed class QueueHandler(ScheduledJobDispatcher dispatcher, PipelineState state) : IWebhookEnvelopeHandler
    {
        /// <summary>原封不動將 envelope correlation 放進合成工作 payload，真 dispatcher 仍自行建立獨立 job scope。</summary>
        public async ValueTask HandleAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            try
            {
                var method = typeof(ScheduledJobDispatcher).GetMethod("RunScopedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var task = Assert.IsAssignableFrom<Task>(method.Invoke(dispatcher,
                    [new JobKey("GoodShepherd2", "observed", "events"), new JobScope("GoodShepherd2", state.Fixture.Organization, state.Fixture.Tenant),
                        JsonSerializer.Serialize(envelope.CorrelationId), envelope.WebhookEventId, cancellationToken]));
                await task;
            }
            finally { Interlocked.Increment(ref state.Completed); }
        }
    }

    /// <summary>合成已授權 scope 替身；真正租戶／Channel／測試旗標仍由 F5 membership 重查。</summary>
    private sealed class SyntheticValidator : IScheduledJobScopeValidator
    {
        /// <summary>只允許固定測試產品，不連 SQL 或外部 CRM，故不代表實機的排程權限驗證。</summary>
        public Task<bool> CanExecuteAsync(JobScope scope, CancellationToken ct) => Task.FromResult(scope.Product == "GoodShepherd2");
    }

    /// <summary>測試工作把同一 correlation 交真 F5，DI async scope 是此 handler 唯一 owner。</summary>
    private sealed class ScheduledHandler(PipelineState state) : IScheduledJobHandler, IAsyncDisposable
    {
        public string Name => "observed";
        /// <summary>將 queue token 與測試 caller 取消連結，成功數只採正式 Gateway 的 accepted 純值。</summary>
        public async Task ExecuteAsync(JobScope scope, string payloadJson, CancellationToken ct)
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, state.Cancellation);
            var result = await state.Gateway!.SendAsync(state.Fixture.Scope, [PushObservationFixture.User(1)],
                new ISendMessage[] { new TextMessage("synthetic-body") }, MessageCategory.Transactional,
                JsonSerializer.Deserialize<string>(payloadJson)!, operation.Token);
            Interlocked.Add(ref state.Accepted, result.AcceptedRecipientCount);
        }
        /// <summary>真 dispatcher 必須在扣回 scheduler 計數之前完整清理本 scope，不持有後續資源。</summary>
        public ValueTask DisposeAsync() { Interlocked.Increment(ref state.ScopeDisposals); return ValueTask.CompletedTask; }
    }
}
