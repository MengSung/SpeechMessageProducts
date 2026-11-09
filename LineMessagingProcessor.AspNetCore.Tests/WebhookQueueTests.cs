/*
檔案整體說明
目的與定位：保護 P3-05 真正 bounded queue 的保序、並行、停止及資料邊界，不以假的 queue 驗證自己。
主要功能與流程：用事件處理器 barrier、合成 registration 與手動 TimeProvider 注入滿載、慢提交、取消及期限；直接觀察處理順序、原例外、scope 清理與全域容量基線。反射另驗 P3-02 envelope 不持有 HTTP 資源。
使用邊界與維護：每案例獨占 queue、DI provider、診斷目錄及時鐘，沒有 LINE／SQL 或實機依賴。測試 owner 先釋放 barrier，再 Stop／Dispose queue、容器及 diagnostics；原始證據僅在 task evidence，等待使用同步訊號而非 sleep。合成資料只留本案例，不宣稱正式 handler 或資料庫完成。
*/
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>指定案例與交接競態回歸；所有同步點皆以 handler／commit 進出訊號決定。</summary>
public sealed class WebhookQueueTests
{
    /// <summary>第一筆阻塞時依序送出同使用者事件；取消序列 consumer 或改以平行 dispatch 必須破壞順序斷言。</summary>
    [Fact]
    public async Task SameUser_EventsProcessedInOrder()
    {
        var entered = WebhookQueueFixture.Signal();
        var release = WebhookQueueFixture.Signal();
        var seen = new ConcurrentQueue<string>();
        await using var fixture = new WebhookQueueFixture(async (e, ct) =>
        {
            if (e.WebhookEventId == "e0") { entered.TrySetResult(); await release.Task.WaitAsync(ct); }
            seen.Enqueue(e.WebhookEventId);
        });
        await fixture.StartAsync();
        await fixture.EnqueueAsync("e0"); await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        for (var i = 1; i < 20; i++) await fixture.EnqueueAsync("e" + i);
        Assert.Empty(seen);
        release.TrySetResult(); await fixture.Queue.StopAsync(default);
        Assert.Equal(Enumerable.Range(0, 20).Select(i => "e" + i), seen);
    }

    /// <summary>兩個已手算為不同分區的使用者必須同時進入；全域序列化會卡在第二個進入訊號。</summary>
    [Fact]
    public async Task DifferentUsers_ProcessedConcurrently()
    {
        var both = WebhookQueueFixture.Signal(); var release = WebhookQueueFixture.Signal(); var active = 0;
        await using var fixture = new WebhookQueueFixture(async (_, ct) =>
        {
            if (Interlocked.Increment(ref active) == 2) both.TrySetResult();
            try { await release.Task.WaitAsync(ct); } finally { Interlocked.Decrement(ref active); }
        });
        await fixture.StartAsync();
        await fixture.EnqueueAsync("a", "A"); await fixture.EnqueueAsync("b", "B");
        await both.Task.WaitAsync(WebhookQueueFixture.Limit);
        Assert.Equal(2, Volatile.Read(ref active));
        release.TrySetResult(); await fixture.Queue.StopAsync(default);
        Assert.Equal(0, active);
    }

    /// <summary>停止先容許已接受事件完成，假時鐘到三十秒才取消阻塞 handler，剩餘 buffer 必須清空且新交接拒絕。</summary>
    [Fact]
    public async Task Shutdown_DrainsWithinTimeout()
    {
        var entered = WebhookQueueFixture.Signal(); var canceled = WebhookQueueFixture.Signal();
        await using var fixture = new WebhookQueueFixture(async (_, ct) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { canceled.TrySetResult(); }
        });
        await fixture.StartAsync(); await fixture.EnqueueAsync("one");
        await entered.Task.WaitAsync(WebhookQueueFixture.Limit);
        await fixture.EnqueueAsync("two");
        var stopping = fixture.Queue.StopAsync(default);
        await fixture.Clock.WaitForTimersAsync(TimeSpan.FromSeconds(30), 2);
        await using var rejected = new WebhookQueueClaim();
        Assert.False(await fixture.Queue.TryEnqueueAsync(WebhookQueueFixture.Envelope("late"), rejected, default));
        Assert.Equal(0, rejected.Commits);
        Assert.False(stopping.IsCompleted);
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await stopping.WaitAsync(WebhookQueueFixture.Limit); await canceled.Task;
        fixture.AssertBaseline();
    }

    /// <summary>反射限制 envelope 為字串、布林及 detached JsonElement，防止 HttpContext、憑證服務或 disposable 混入背景。</summary>
    [Fact]
    public void Envelope_ContainsNoHttpContext()
    {
        Type[] allowed = [typeof(string), typeof(bool), typeof(JsonElement)];
        foreach (var field in typeof(WebhookEnvelope).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            Assert.Contains(field.FieldType, allowed);
            Assert.False(typeof(HttpContext).IsAssignableFrom(field.FieldType));
            Assert.False(typeof(IDisposable).IsAssignableFrom(field.FieldType));
        }
        var envelope = WebhookQueueFixture.Envelope("detached");
        Assert.Equal("detached", envelope.Event.GetProperty("webhookEventId").GetString());
    }

    /// <summary>一百筆雙頻道同 ID／不同使用者只使用獨立 scope，drain／Dispose 後 timer、permit、handler 與 task 全歸零。</summary>
    [Fact]
    public async Task ResourceCounts_ReturnToBaseline()
    {
        var seen = new ConcurrentBag<(string, string)>();
        await using var fixture = new WebhookQueueFixture((e, _) =>
        {
            seen.Add((e.ChannelId, e.WebhookEventId)); return ValueTask.CompletedTask;
        });
        await fixture.StartAsync();
        for (var i = 0; i < 100; i++)
        {
            await fixture.EnqueueAsync("event" + i, "A", "123");
            await fixture.EnqueueAsync("event" + i, "B", "456");
        }
        await fixture.Queue.StopAsync(default); await fixture.Queue.DisposeAsync();
        Assert.Equal(200, seen.Count); Assert.Equal(200, seen.Distinct().Count());
        fixture.AssertBaseline(); Assert.Equal(0, fixture.Clock.TimerCount);
    }
}
