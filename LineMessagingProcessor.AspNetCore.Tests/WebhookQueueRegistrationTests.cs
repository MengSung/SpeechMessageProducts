/*
檔案整體說明
目的與定位：驗證 P3-05 獨立組合入口及 P3-02 partial hook，不修改 Program 或既有測試主機。
主要功能與流程：用真 Generic Host 驗 handler 缺失時即使平行啟動也拒絕；以服務集合驗部署快照、非法容量及重複 owner 的拒絕；以預設容量的實際滿載驗證一千筆含執行中事件。
使用邊界與維護：測試只注入外借 diagnostics，Host Dispose 不得接管它；每個測試 provider 與 fixture 在結束完整釋放，沒有全域組態或秘密。容量測試用手動時鐘與固定一千零一筆上限，不能以離線結果宣稱正式 P3-06 handler、SQL 或 LINE 已驗收。
*/
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>組合根的真 Host 契約與有界預設值，避免只測手動 new 的服務而漏接註冊。</summary>
public sealed class WebhookQueueRegistrationTests
{
    /// <summary>缺 handler 在 StartingAsync 失敗，平行啟動時後續 hosted Start 不得被呼叫。</summary>
    [Fact]
    public async Task ParallelHostStartup_MissingHandlerRejectsBeforeAnyServiceStarts()
    {
        await using var fixture = new WebhookQueueFixture((_, _) => ValueTask.CompletedTask);
        await using var diagnostics = new ExceptionDiagnostics(fixture.LogPath, outputOptions: new ExceptionOutputOptions(false, false));
        var probe = new StartProbe();
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(diagnostics);
            services.AddBoundedWebhookQueue();
            services.Configure<HostOptions>(options => options.ServicesStartConcurrently = true);
            services.AddSingleton<IHostedService>(probe);
        }).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.Equal(0, probe.Starts);
        var queue = Assert.IsType<BoundedWebhookEventQueue>(host.Services.GetRequiredService<IWebhookEventQueue>());
        Assert.Equal(0, queue.GetSnapshot().Workers);
        Assert.True(host.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout >= TimeSpan.FromSeconds(45));
    }

    /// <summary>既有 partial hook 讀到設定後產生不可變快照；後續組態變更不影響目前 Host。</summary>
    [Fact]
    public async Task PartialHook_UsesValidatedSnapshotAndSingleOwner()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["LineWebhook:Queue:Capacity"] = "7", ["LineWebhook:Queue:PartitionCount"] = "2" });
        var services = new ServiceCollection(); services.AddLineWebhook(configuration);
        configuration["LineWebhook:Queue:Capacity"] = "99";
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<WebhookQueueOptions>();
        Assert.Equal(7, options.Capacity); Assert.Equal(2, options.PartitionCount);
        Assert.Single(services.Where(d => d.ServiceType == typeof(IWebhookEventQueue)));
        var count = services.Count;
        Assert.Throws<InvalidOperationException>(() => services.AddBoundedWebhookQueue()); Assert.Equal(count, services.Count);
    }

    /// <summary>非法部署值不建立任何 queue descriptor，不能以 fallback 掩蓋錯誤或產生部分 owner。</summary>
    [Theory]
    [InlineData("Capacity", "0")]
    [InlineData("Capacity", "100001")]
    [InlineData("Capacity", "-1")]
    [InlineData("Capacity", "1.5")]
    [InlineData("PartitionCount", "0")]
    [InlineData("PartitionCount", "65")]
    public void InvalidConfiguration_RejectsBeforeQueuePublication(string name, string value)
    {
        using var configuration = new ConfigurationManager();
        configuration["LineWebhook:Queue:" + name] = value;
        var services = new ServiceCollection();
        Assert.ThrowsAny<Exception>(() => services.AddLineWebhook(configuration));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IWebhookEventQueue));
    }

    /// <summary>預設四個 worker 與一千筆總容量，第一筆未完成時第千零一筆必拒絕且不 Commit。</summary>
    [Fact]
    public async Task DefaultCapacity_IsExactlyOneThousandIncludingActiveEvent()
    {
        var entered = WebhookQueueFixture.Signal();
        await using var fixture = new WebhookQueueFixture(async (_, ct) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); });
        await fixture.StartAsync(); await fixture.EnqueueAsync("first"); await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        for (var i = 1; i < 1000; i++) await fixture.EnqueueAsync("e" + i);
        Assert.Equal(1000, fixture.Queue.GetSnapshot().Retained); Assert.Equal(4, fixture.Queue.GetSnapshot().Workers);
        await using var claim = new WebhookQueueClaim();
        var waiting = fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("overflow", "B"), claim, default).AsTask();
        await fixture.Clock.WaitForTimersAsync(TimeSpan.FromSeconds(2)); fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(await waiting); Assert.Equal(0, claim.Commits);
        await fixture.Queue.StopAsync(new CancellationToken(true)); fixture.AssertBaseline();
        Assert.Equal(0, fixture.Clock.TimerCount);
    }

    /// <summary>只記錄 Host 是否越過 Starting 階段，沒有背景資源或業務操作。</summary>
    private sealed class StartProbe : IHostedService
    {
        /// <summary>Start 次數；缺 handler 時應維持零。</summary>
        internal int Starts;
        /// <summary>記錄真正 Host 的啟動呼叫，不透過手動呼叫模擬。</summary>
        public Task StartAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref Starts); return Task.CompletedTask; }
        /// <summary>此探針無資源，不啟動新工作。</summary>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
