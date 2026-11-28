/*
檔案整體說明
目的與定位：F11正式readiness組合的queue容量邊界離線測試，必須觀察總保留槽而不是僅buffer，不擴張公開API。
主要功能與流程：以五槽單consumer真queue固定保留0、3、4、5筆，非空時阻擋第一個執行事件；執行由正式AddGoodShepherd2Readiness建立的四probe彙總，斷言低於80%才健康，恰80%與滿載都列queue。SDK為完整合成WhoAmI、SQL缺設定、Channel缺席，故不作任何外部I/O。
使用邊界與維護：每案清除部署來源並建立唯一DI、diagnostics、Meter及queue；claim是一次合成提交，JsonDocument複製後立即釋放。finally解除handler gate、完整停止queue及Dispose容器，沒有遺棄工作或靜態計數。失敗名稱不含事件內容，此測試不是實機資料庫或LINE驗收。
*/
using System.Reflection;
using System.Text.Json;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.AspNetCore.Builder;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Moq;
using SpeechMessage.Hosting.Observability;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>真queue與真四probe工廠的整合，不在測試重寫比例公式。</summary>
public sealed class QueueReadinessTests
{
    /// <summary>容量五時3筆健康、4筆未就緒；第一個active已離buffer仍必須納入比例，停止後計數及retained皆零。</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public async Task Queue_StrictlyBelowEightyPercent_IncludesActive(int retained, bool healthy)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sdk = new Mock<IOrganizationServiceAsync2>(MockBehavior.Strict);
        sdk.Setup(s => s.ExecuteAsync(It.IsAny<WhoAmIRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhoAmIResponse
            { Results = new ParameterCollection { ["UserId"] = Guid.NewGuid(), ["BusinessUnitId"] = Guid.NewGuid(), ["OrganizationId"] = Guid.NewGuid() } });
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        lease.SetupGet(l => l.Service).Returns(sdk.Object);
        lease.Setup(l => l.Dispose());
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(m => m.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        builder.Services.AddSingleton(manager.Object);
        builder.Services.AddSingleton(diagnostics);
        builder.Services.AddScoped<IWebhookEnvelopeHandler>(_ => new BlockedHandler(entered, release));
        builder.Services.AddBoundedWebhookQueue(new WebhookQueueOptions(5, 1));
        var type = typeof(Program).Assembly.GetType("SpeechMessageProducts.GoodShepherd2.Web.Health.GoodShepherd2Readiness")!;
        try
        {
            type.GetMethod("AddGoodShepherd2Readiness", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { builder });
            await using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var queue = Assert.IsType<BoundedWebhookEventQueue>(provider.GetRequiredService<IWebhookEventQueue>());
            try
            {
                await queue.StartAsync(default);
                for (var i = 0; i < retained; i++)
                {
                    using var document = JsonDocument.Parse("{\"webhookEventId\":\"synthetic-" + i + "\",\"type\":\"message\"}");
                    await using var claim = new Claim();
                    Assert.True(await queue.TryEnqueueAsync(new WebhookEnvelope("2012804957", document.RootElement), claim, default));
                    Assert.Equal(1, claim.Commits);
                }
                if (retained > 0) await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(retained, queue.GetSnapshot().Retained);
                var failed = await provider.GetRequiredService<ReadinessChecks>().CheckAsync(default);
                Assert.Equal(!healthy, failed.Contains("queue", StringComparer.Ordinal));
            }
            finally { release.TrySetResult(); await queue.StopAsync(default); }
            Assert.Equal(0, queue.GetSnapshot().Retained);
            Assert.Equal(0, provider.GetRequiredService<HostingMeters>().GetSnapshot().WebhookQueueDepth);
        }
        finally { release.TrySetResult(); ((IDisposable)builder.Configuration).Dispose(); }
    }

    /// <summary>單consumer的合作gate，停止時先由本案解除；不持任何外部資源或事件參照。</summary>
    private sealed class BlockedHandler(TaskCompletionSource entered, TaskCompletionSource release) : IWebhookEnvelopeHandler
    {
        public async ValueTask HandleAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        { entered.TrySetResult(); await release.Task.WaitAsync(cancellationToken); }
    }

    /// <summary>不開SQL的單次合成claim，僅記提交次數；正式registration回滾由F1既有測試覆蓋。</summary>
    private sealed class Claim : IWebhookEventRegistration
    {
        internal int Commits { get; private set; }
        public ValueTask CommitAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Commits++; return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
