/*
檔案整體說明
目的與定位：保護 P3-02 與 P3-05 兩階段交接，特別是成功 Commit 之後不能因 caller 取消或 Host 停止而遺失事件。
主要功能與流程：以可控 Commit barrier 安排慢提交、取消中間等待者及停止競態；以假時鐘推進滿載兩秒，確認未提交拒絕可重試。觀察真 queue、handler 順序與全域槽位，而不是讓 queue 替身自行宣稱成功。
使用邊界與維護：每筆 registration 由測試 await using 唯一 Dispose，queue 僅借用；fixture 擁有所有 timer、scope 與 task 並於結束回到基線。只用合成頻道／使用者，真 SQL 與 LINE 不在本離線驗證範圍，故障原始證據留 task evidence。
*/
using System.Collections.Concurrent;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>真正 queue 的交接競態測試，所有事件有固定上限且不用 sleep 安排順序。</summary>
public sealed class WebhookQueueHandoffTests
{
    /// <summary>四分區共用一個容量；第一筆執行時第二分區也必須等兩秒並拒絕，不得先 Commit。</summary>
    [Fact]
    public async Task FullAcrossPartitions_DoesNotCommitAndReturnsPermit()
    {
        var entered = WebhookQueueFixture.Signal(); var release = WebhookQueueFixture.Signal();
        await using var fixture = new WebhookQueueFixture(async (_, ct) =>
        { entered.TrySetResult(); await release.Task.WaitAsync(ct); }, capacity: 1);
        await fixture.StartAsync(); await fixture.EnqueueAsync("one", "A");
        await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        await using var rejected = new WebhookQueueClaim();
        var waiting = fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("two", "B"), rejected, default).AsTask();
        await fixture.Clock.WaitForTimersAsync(TimeSpan.FromSeconds(2));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1)); Assert.False(waiting.IsCompleted);
        Assert.Equal(0, rejected.Commits); Assert.Equal(1, fixture.Queue.GetSnapshot().Retained);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(await waiting.WaitAsync(WebhookQueueFixture.Limit)); Assert.Equal(0, rejected.Commits);
        Assert.Equal(0, rejected.Disposals);
        release.TrySetResult(); await fixture.Queue.StopAsync(default);
        fixture.AssertBaseline(); Assert.Empty(fixture.ExceptionLog);
    }

    /// <summary>Commit 原例外原樣上拋、不發布；同一容量可再接收成功事件，且通知仍為原例外身分。</summary>
    [Fact]
    public async Task CommitFailure_DoesNotPublishAndCapacityCanBeReused()
    {
        var seen = new ConcurrentQueue<string>();
        await using var fixture = new WebhookQueueFixture((e, _) =>
        { seen.Enqueue(e.WebhookEventId); return ValueTask.CompletedTask; }, capacity: 1);
        await fixture.StartAsync();
        var error = new IOException("synthetic-private-marker");
        await using var failed = new WebhookQueueClaim(_ => ValueTask.FromException(error));
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => fixture.Queue.TryEnqueueAsync(
            WebhookQueueFixture.Envelope("failed"), failed, default).AsTask()));
        Assert.Equal(0, fixture.Queue.GetSnapshot().Retained); Assert.Empty(seen);
        Assert.Equal(0, failed.Disposals); Assert.False(fixture.ReportAgain(error));
        await fixture.EnqueueAsync("retry"); await fixture.Queue.StopAsync(default);
        Assert.Equal("retry", Assert.Single(seen)); fixture.AssertBaseline();
        Assert.DoesNotContain("synthetic-private-marker", fixture.ExceptionLog, StringComparison.Ordinal);
    }

    /// <summary>caller 在容量等待時取消必須拋取消例外、不 Commit、不通知，也不提前 Dispose claim。</summary>
    [Fact]
    public async Task WaitingCallerCancellation_ThrowsWithoutCommit()
    {
        var entered = WebhookQueueFixture.Signal(); var release = WebhookQueueFixture.Signal();
        await using var fixture = new WebhookQueueFixture(async (_, ct) =>
        { entered.TrySetResult(); await release.Task.WaitAsync(ct); }, capacity: 1);
        await fixture.StartAsync(); await fixture.EnqueueAsync("one"); await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        using var caller = new CancellationTokenSource(); await using var claim = new WebhookQueueClaim();
        var waiting = fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("two"), claim, caller.Token).AsTask();
        await fixture.Clock.WaitForTimersAsync(TimeSpan.FromSeconds(2)); caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(0, claim.Commits); Assert.Equal(0, claim.Disposals);
        release.TrySetResult(); await fixture.Queue.StopAsync(default); fixture.AssertBaseline();
        Assert.Empty(fixture.ExceptionLog); Assert.Equal(0, fixture.Clock.TimerCount);
    }

    /// <summary>Commit 已入場且之後成功時，caller 取消與停止皆不能撤銷發布；Stop 必須等待此交接。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedEvent_SurvivesCallerCancellationAndStopRace(bool cancelCaller)
    {
        var commitEntered = WebhookQueueFixture.Signal(); var commitRelease = WebhookQueueFixture.Signal();
        var seen = new ConcurrentQueue<string>();
        await using var fixture = new WebhookQueueFixture((e, _) =>
        { seen.Enqueue(e.WebhookEventId); return ValueTask.CompletedTask; });
        await fixture.StartAsync(); using var caller = new CancellationTokenSource();
        await using var claim = new WebhookQueueClaim(async _ =>
        { commitEntered.TrySetResult(); await commitRelease.Task; });
        var enqueue = fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("committed"), claim, caller.Token).AsTask();
        await commitEntered.Task.WaitAsync(WebhookQueueFixture.Limit);
        if (cancelCaller) caller.Cancel();
        var stopping = fixture.Queue.StopAsync(default);
        Assert.False(stopping.IsCompleted); Assert.Equal(1, fixture.Queue.GetSnapshot().Retained);
        commitRelease.TrySetResult(); Assert.True(await enqueue.WaitAsync(WebhookQueueFixture.Limit));
        await stopping.WaitAsync(WebhookQueueFixture.Limit);
        Assert.Equal("committed", Assert.Single(seen)); Assert.Equal(1, claim.Commits); Assert.Equal(0, claim.Disposals);
        fixture.AssertBaseline(); Assert.Empty(fixture.ExceptionLog);
    }

    /// <summary>慢 Commit 與已取消的中間節點不能讓第三筆超車；返回後節點／timer 清除，不形成取消鏈漏失。</summary>
    [Fact]
    public async Task SamePartition_SlowCommitAndCanceledMiddle_PreserveOrder()
    {
        var entered = WebhookQueueFixture.Signal(); var release = WebhookQueueFixture.Signal();
        var seen = new ConcurrentQueue<string>();
        await using var fixture = new WebhookQueueFixture((e, _) =>
        { seen.Enqueue(e.WebhookEventId); return ValueTask.CompletedTask; });
        await fixture.StartAsync();
        await using var first = new WebhookQueueClaim(async _ => { entered.TrySetResult(); await release.Task; });
        await using var second = new WebhookQueueClaim(); await using var third = new WebhookQueueClaim();
        using var caller = new CancellationTokenSource();
        var a = fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("first"), first, default).AsTask();
        await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        var b = fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("canceled"), second, caller.Token).AsTask();
        var c = fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("third"), third, default).AsTask();
        caller.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b);
        Assert.Equal(0, third.Commits); Assert.False(c.IsCompleted);
        release.TrySetResult(); Assert.True(await a); Assert.True(await c);
        await fixture.Queue.StopAsync(default);
        Assert.Equal(new[] { "first", "third" }, seen); Assert.Equal(0, second.Commits);
        fixture.AssertBaseline(); Assert.Equal(0, fixture.Clock.TimerCount);
    }

    /// <summary>Host 停止時已保留的提交必須完成，但尚在等待容量的另一呼叫立即拒絕且不 Commit。</summary>
    [Fact]
    public async Task Stop_UnblocksWaitingOffersWithoutLosingReservedPublish()
    {
        var entered = WebhookQueueFixture.Signal(); var release = WebhookQueueFixture.Signal();
        await using var fixture = new WebhookQueueFixture(async (_, ct) =>
        { entered.TrySetResult(); await release.Task.WaitAsync(ct); }, capacity: 1);
        await fixture.StartAsync(); await fixture.EnqueueAsync("first"); await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        await using var claim = new WebhookQueueClaim();
        var offer = fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("second", "B"), claim, default).AsTask();
        var stop = fixture.Queue.StopAsync(default);
        Assert.False(await offer.WaitAsync(WebhookQueueFixture.Limit)); Assert.Equal(0, claim.Commits);
        release.TrySetResult(); await stop.WaitAsync(WebhookQueueFixture.Limit); fixture.AssertBaseline();
    }
}
