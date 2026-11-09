/*
檔案整體說明
目的與定位：驗證 queue 的事件期限、原例外通知、scope 清理與背景 ExecutionContext 隔離，防止正常成功測試漏掉資源故障。
主要功能與流程：以真 diagnostics 安全日誌、scope 清理故障及假時鐘觸發事件取消；以 AsyncLocal 與弱參照檢查背景不捕捉 HTTP 身分、完成 task 不延長 envelope／claim 生命。無 user 的來源用 group／room 合成事件驗證固定分區。
使用邊界與維護：每案例 fixture 唯一擁有 queue／容器／時鐘／日誌，所有關閉操作皆 await。GC 僅在明確排空後觀察弱參照，不以時間或進程記憶體偶然下降當證據。測試資料沒有部署憑證，最多固定一百筆回收探針，不接外部 SQL／LINE，原始證據留本 task evidence。
*/
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>有界 worker 的錯誤分類、取消及保留邊界；重點是實際 scope／timer 清除。</summary>
public sealed class WebhookQueueLifecycleTests
{
    /// <summary>使用者取消 callback 拋錯不應逸出 timer 或中斷 Stop；真正錯誤須通知，scope／permit 仍歸零。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingCancellationCallback_StillDrainsAndReports(bool timeout)
    {
        var entered = WebhookQueueFixture.Signal();
        await using var fixture = new WebhookQueueFixture(async (_, ct) =>
        {
            using var registration = ct.Register(() => throw new IOException("private-cancellation-marker"));
            entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });
        await fixture.StartAsync(); await fixture.EnqueueAsync("callback"); await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        if (timeout) fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await fixture.Queue.StopAsync(new CancellationToken(true)).WaitAsync(WebhookQueueFixture.Limit);
        fixture.AssertBaseline(); Assert.Equal(0, fixture.Clock.TimerCount);
        Assert.Contains("AggregateException", fixture.ExceptionLog, StringComparison.Ordinal);
        Assert.DoesNotContain("private-cancellation-marker", fixture.ExceptionLog, StringComparison.Ordinal);
    }

    /// <summary>缺正式 handler 必須 Start 拒絕，不能以空 consumer 靜默吞事件。</summary>
    [Fact]
    public async Task MissingHandler_RejectsStartup()
    {
        await using var fixture = new WebhookQueueFixture((_, _) => ValueTask.CompletedTask, includeHandler: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.StartAsync());
        fixture.AssertBaseline(); Assert.Equal(0, fixture.Clock.TimerCount);
    }

    /// <summary>內部三十秒取消即使 token 已取消仍通知；未失敗分區保持運作，scope、timer 及槽位皆回收。</summary>
    [Fact]
    public async Task EventTimeout_ReportsOriginalFailureAndWorkerContinues()
    {
        var entered = WebhookQueueFixture.Signal(); var failureObserved = WebhookQueueFixture.Signal();
        var processed = new ConcurrentQueue<string>(); Exception? original = null;
        await using var fixture = new WebhookQueueFixture(async (e, ct) =>
        {
            if (e.WebhookEventId == "timeout")
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException exception)
                { original = exception; failureObserved.TrySetResult(); throw; }
            }
            processed.Enqueue(e.WebhookEventId);
        });
        await fixture.StartAsync(); await fixture.EnqueueAsync("timeout"); await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        fixture.Clock.Advance(TimeSpan.FromSeconds(29)); Assert.False(failureObserved.Task.IsCompleted);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1)); await failureObserved.Task.WaitAsync(WebhookQueueFixture.Limit);
        await fixture.EnqueueAsync("after"); await fixture.Queue.StopAsync(default);
        Assert.Equal("after", Assert.Single(processed)); Assert.NotNull(original); Assert.False(fixture.ReportAgain(original));
        Assert.Contains("TaskCanceledException", fixture.ExceptionLog, StringComparison.Ordinal);
        fixture.AssertBaseline(); Assert.Equal(0, fixture.Clock.TimerCount);
    }

    /// <summary>Host 提早取消時先排空在途交接再取消 handler，未讀 buffer 清空且正常取消不通知。</summary>
    [Fact]
    public async Task HostCancellation_ClearsBufferWithoutAlert()
    {
        var entered = WebhookQueueFixture.Signal(); var calls = 0;
        await using var fixture = new WebhookQueueFixture(async (_, ct) =>
        { Interlocked.Increment(ref calls); entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); });
        await fixture.StartAsync(); await fixture.EnqueueAsync("first"); await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        for (var i = 0; i < 20; i++) await fixture.EnqueueAsync("buffer" + i);
        await fixture.Queue.StopAsync(new CancellationToken(true)).WaitAsync(WebhookQueueFixture.Limit);
        Assert.Equal(1, calls); Assert.Empty(fixture.ExceptionLog);
        fixture.AssertBaseline(); Assert.Equal(0, fixture.Clock.TimerCount);
    }

    /// <summary>handler 與 scope Dispose 同時失敗仍各報原物件；不能讓清理錯誤遮蔽主要錯誤或漏容量。</summary>
    [Fact]
    public async Task HandlerAndScopeFailure_BothReportedAndResourcesReleased()
    {
        var primary = new InvalidOperationException("private-handler-marker");
        var cleanup = new IOException("private-cleanup-marker");
        await using var fixture = new WebhookQueueFixture((_, _) => ValueTask.FromException(primary), disposeFailure: cleanup);
        await fixture.StartAsync(); await fixture.EnqueueAsync("fault"); await fixture.Queue.StopAsync(default);
        Assert.False(fixture.ReportAgain(primary)); Assert.False(fixture.ReportAgain(cleanup));
        Assert.Contains("InvalidOperationException", fixture.ExceptionLog, StringComparison.Ordinal);
        Assert.Contains("IOException", fixture.ExceptionLog, StringComparison.Ordinal);
        Assert.DoesNotContain("private-", fixture.ExceptionLog, StringComparison.Ordinal);
        fixture.AssertBaseline(); Assert.Equal(0, fixture.Clock.TimerCount);
    }

    /// <summary>背景 task 不繼承啟動者 AsyncLocal；前一事件寫入的身分也不可流至下一事件。</summary>
    [Fact]
    public async Task BackgroundContext_DoesNotFlowCallerOrPreviousEventIdentity()
    {
        var identity = new AsyncLocal<string?> { Value = "caller-private" };
        var observed = new ConcurrentQueue<string?>();
        await using var fixture = new WebhookQueueFixture((e, _) =>
        { observed.Enqueue(identity.Value); identity.Value = e.WebhookEventId; return ValueTask.CompletedTask; }, partitions: 1);
        await fixture.StartAsync(); await fixture.EnqueueAsync("A"); await fixture.EnqueueAsync("B");
        await fixture.Queue.StopAsync(default);
        Assert.Equal(2, observed.Count); Assert.All(observed, Assert.Null);
        Assert.Equal("caller-private", identity.Value); identity.Value = null;
        fixture.AssertBaseline();
    }

    /// <summary>沒有 userId 的不同 group／room 仍可並行，不可全部以 HTTP 或空 user 建分區。</summary>
    [Theory]
    [InlineData("group", "groupId", "G1", "G2")]
    [InlineData("room", "roomId", "R1", "R2")]
    public async Task SourceFallback_SeparatesGroupsAndRooms(string kind, string key, string firstId, string secondId)
    {
        var both = WebhookQueueFixture.Signal(); var release = WebhookQueueFixture.Signal(); var active = 0;
        await using var fixture = new WebhookQueueFixture(async (_, ct) =>
        {
            if (Interlocked.Increment(ref active) == 2) both.TrySetResult();
            try { await release.Task.WaitAsync(ct); } finally { Interlocked.Decrement(ref active); }
        });
        await fixture.StartAsync();
        foreach (var id in new[] { firstId, secondId })
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
            { type = "join", webhookEventId = id, source = new Dictionary<string, string> { ["type"] = kind, [key] = id } }));
            await using var claim = new WebhookQueueClaim();
            Assert.True(await fixture.Queue.TryEnqueueAsync(new WebhookEnvelope("123", document.RootElement), claim, default));
        }
        await both.Task.WaitAsync(WebhookQueueFixture.Limit); Assert.Equal(2, active);
        release.TrySetResult(); await fixture.Queue.StopAsync(default); fixture.AssertBaseline();
    }

    /// <summary>保留已完成呼叫的 Task 與已 Dispose queue，不可因此延長一百筆 envelope／registration 生命。</summary>
    [Fact]
    public async Task CompletedTasks_DoNotRetainEnvelopeOrClaim()
    {
        await using var fixture = new WebhookQueueFixture((_, _) => ValueTask.CompletedTask);
        await fixture.StartAsync();
        var probes = Enumerable.Range(0, 100).Select(i => OfferForCollection(fixture.Queue, i)).ToArray();
        await Task.WhenAll(probes.Select(p => p.Completion));
        await fixture.Queue.StopAsync(default); await fixture.Queue.DisposeAsync();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(probes, p => { Assert.False(p.Envelope.IsAlive); Assert.False(p.Claim.IsAlive); });
        GC.KeepAlive(probes); GC.KeepAlive(fixture.Queue); fixture.AssertBaseline();
    }

    /// <summary>隔離 JIT stack roots，讓回收斷言只針對產品保留；claim 由本 helper 在完成後唯一 Dispose。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task Completion, WeakReference Envelope, WeakReference Claim) OfferForCollection(BoundedWebhookEventQueue queue, int index)
    {
        var envelope = WebhookQueueFixture.Envelope("collect" + index);
        var claim = new WebhookQueueClaim();
        return (CompleteOfferAsync(queue, envelope, claim), new WeakReference(envelope), new WeakReference(claim));
    }

    /// <summary>完成工作只持有 bool 結果；清空 helper 自己的參照，不能把測試漏失誤認為產品漏失。</summary>
    private static async Task CompleteOfferAsync(BoundedWebhookEventQueue queue, WebhookEnvelope envelope, WebhookQueueClaim claim)
    {
        try { Assert.True(await queue.TryEnqueueAsync(envelope, claim, default)); }
        finally { await claim.DisposeAsync(); claim = null!; envelope = null!; }
    }
}
