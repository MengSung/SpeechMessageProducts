/*
 * 檔案整體說明
 *
 * 目的與定位：以真實 P1 ChurchTextOverrideCache 驗證 P2-10 的指定教會失效、故障
 * 保留及兩分鐘重疊視窗，不連 D365、LINE 或讀取機密。
 * 主要功能與流程：合成來源提供純值改字版本及可控故障，真正重新 Load 快取後斷言
 * 只有目標教會換世代；固定 UTC 時鐘記錄查詢界線，不以 mock 呼叫次數冒充產品行為。
 * 使用邊界與維護：每案獨佔 source、cache、背景服務及取消 owner；以 await using
 * 排空 cache、StopAsync 排空服務，所有 barrier 有期限，沒有跨測試靜態可變資料。
 * 重複正式 ID 必須在任何失效前拒絕，故障原例外交共用入口，正常停止不告警。
 */
using SpeechMessage.Hosting.Localization;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
using Xunit;

namespace SpeechMessage.Hosting.Tests.Localization;

/// <summary>使用真快取與離線來源驗證輪詢契約，測試 owner 負責停止服務及排空快取。</summary>
public sealed class TextChangePollingServiceTests
{
    private static readonly DateTime Stamp = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>兩間已載入的教會只有 A 改字，重新讀取只有 A 換快照、B 保留原實例。</summary>
    [Fact]
    public async Task Poll_OnlyChangedChurchesInvalidated()
    {
        var a = Context();
        var b = Context();
        var values = new TextSource();
        await using var cache = Cache(values);
        var oldA = await cache.LoadAsync(a, Stamp);
        var oldB = await cache.LoadAsync(b, Stamp);
        values.Value = "new";
        var changes = new Changes((_, _) => Task.FromResult<IReadOnlyList<TextChangedChurch>>(
            new[] { new TextChangedChurch(a.ChurchId, Stamp.AddSeconds(1)) }));
        using var service = Create(changes, cache);
        Assert.True(await service.PollOnceAsync());
        var next = await cache.LoadAsync(a, Stamp);
        Assert.NotSame(oldA, next);
        Assert.True(next.TryGetText("*", new TextKey("Common.Diagnostics.Ping"), out var text));
        Assert.Equal("new", text);
        Assert.Same(oldB, await cache.LoadAsync(b, Stamp));
        Assert.Equal(0, cache.ActiveLoadCount);
    }

    /// <summary>注入真來源例外後保留快照與 watermark；重試成功只讓指定教會失效並上報原物件。</summary>
    [Fact]
    public async Task Poll_D365Unavailable_KeepsSnapshotsAndRetries()
    {
        var context = Context();
        await using var cache = Cache(new TextSource());
        var old = await cache.LoadAsync(context, Stamp);
        var failure = new InvalidOperationException("synthetic");
        var fail = true;
        var boundaries = new List<DateTime>();
        var reports = new List<Exception>();
        var clock = new Clock();
        var source = new Changes((since, _) =>
        {
            boundaries.Add(since);
            return fail ? Task.FromException<IReadOnlyList<TextChangedChurch>>(failure)
                : Task.FromResult<IReadOnlyList<TextChangedChurch>>(new[] { new TextChangedChurch(context.ChurchId, Stamp.AddSeconds(1)) });
        });
        using var service = Create(source, cache, clock, reports);
        Assert.False(await service.PollOnceAsync());
        Assert.Same(old, cache.GetSnapshot(context));
        Assert.Same(failure, Assert.Single(reports));
        fail = false;
        clock.Now = Stamp.AddMinutes(4);
        Assert.True(await service.PollOnceAsync());
        Assert.Equal(boundaries[0], boundaries[1]);
        Assert.NotSame(old, await cache.LoadAsync(context, Stamp));
    }

    /// <summary>延遲可見的舊改字仍落在開始時間減兩分鐘內，下一輪能失效，不用完成時刻跳過。</summary>
    [Fact]
    public async Task Poll_OverlapWindow_NoMissedChange()
    {
        var context = Context();
        await using var cache = Cache(new TextSource());
        var old = await cache.LoadAsync(context, Stamp);
        var clock = new Clock();
        var cutoffs = new List<DateTime>();
        var source = new Changes((since, _) =>
        {
            cutoffs.Add(since);
            if (cutoffs.Count == 1)
            {
                clock.Now = Stamp.AddMinutes(1);
                return Task.FromResult<IReadOnlyList<TextChangedChurch>>(Array.Empty<TextChangedChurch>());
            }
            return Task.FromResult<IReadOnlyList<TextChangedChurch>>(
                new[] { new TextChangedChurch(context.ChurchId, Stamp.AddSeconds(5)) });
        });
        using var service = Create(source, cache, clock);
        Assert.True(await service.PollOnceAsync());
        Assert.True(await service.PollOnceAsync());
        Assert.Equal(Stamp.AddMinutes(-2), cutoffs[0]);
        Assert.Equal(cutoffs[0], cutoffs[1]);
        Assert.NotSame(old, await cache.LoadAsync(context, Stamp));
    }

    /// <summary>重複正式教會 ID 使完整候選拒絕，不能先讓第一列失效而部分發布。</summary>
    [Fact]
    public async Task Poll_DuplicateIdentity_RejectsBeforeInvalidation()
    {
        var context = Context();
        await using var cache = Cache(new TextSource());
        var old = await cache.LoadAsync(context, Stamp);
        var row = new TextChangedChurch(context.ChurchId, Stamp.AddSeconds(1));
        var errors = new List<Exception>();
        using var service = Create(new Changes((_, _) =>
            Task.FromResult<IReadOnlyList<TextChangedChurch>>(new[] { row, row })), cache, errors: errors);
        Assert.False(await service.PollOnceAsync());
        Assert.Same(old, await cache.LoadAsync(context, Stamp));
        Assert.Single(errors);
    }

    /// <summary>停止取消當前來源並確實排空，不誤報正常取消；下一輪 timer 不得在停止後啟動。</summary>
    [Fact]
    public async Task Stop_CancelsAndDrainsSource_WithoutNotification()
    {
        await using var cache = Cache(new TextSource());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var errors = new List<Exception>();
        using var service = Create(new Changes(async (_, token) =>
        {
            Interlocked.Increment(ref active);
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { Interlocked.Decrement(ref active); }
            return Array.Empty<TextChangedChurch>();
        }), cache, errors: errors);
        await service.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(deadline.Token);
        Assert.Equal(0, Volatile.Read(ref active));
        Assert.Empty(errors);
    }

    /// <summary>真 BackgroundService 第一輪後以時鐘建立三十秒等待，StopAsync 排空並釋放 timer。</summary>
    [Fact]
    public async Task Run_ThirtySecondInterval_StopReleasesTimer()
    {
        await using var cache = Cache(new TextSource());
        var clock = new Clock();
        using var service = Create(new Changes((_, _) =>
            Task.FromResult<IReadOnlyList<TextChangedChurch>>(Array.Empty<TextChangedChurch>())), cache, clock);
        await service.StartAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(30), await clock.Scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(deadline.Token);
        Assert.Equal(0, clock.LiveTimers);
    }

    /// <summary>固定已授權測試部署，告警只保留原例外供決定性斷言，不發 LINE。</summary>
    private static TextChangePollingService Create(ITextChangeSource source, ChurchTextOverrideCache cache,
        Clock? clock = null, List<Exception>? errors = null) =>
        new(source, cache, "Alpha", "ChurchReport", clock ?? new Clock(),
            (exception, _, _) => errors?.Add(exception));
    /// <summary>每案獨佔真快取，只有合成文字來源；呼叫端 await DisposeAsync。</summary>
    private static ChurchTextOverrideCache Cache(TextSource source) =>
        new(source, new ChurchTextOverrideValidator(new SystemTextTable(), "這是一個長度剛好十二字的教會"),
            (_, _) => throw new InvalidOperationException("Unexpected cache failure."));
    /// <summary>每個資料庫教會 ID 不同，名稱不參與任何去重。</summary>
    private static TextContext Context() => new(Guid.NewGuid(), "zh-TW", "zh-TW", "Alpha", "ChurchReport");
    /// <summary>操作擁有的可變時間，不讀取本機時間或建立獨立計時程序。</summary>
    private sealed class Clock : TimeProvider
    {
        /// <summary>每案可控的 UTC 時刻，沒有跨測試共用狀態。</summary>
        internal DateTime Now { get; set; } = Stamp;
        /// <summary>回報實際輪詢間隔的 barrier，測試不等待真實三十秒。</summary>
        internal TaskCompletionSource<TimeSpan> Scheduled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>本案尚未 Dispose 的 timer owner 數目。</summary>
        internal int LiveTimers;
        /// <summary>回傳明確 UTC；服務不得用本機時區解讀 watermark。</summary>
        public override DateTimeOffset GetUtcNow() => new(Now);
        /// <summary>記錄產品建立的期限並回傳不觸發的 timer；正常停止必須 Dispose 它。</summary>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref LiveTimers);
            Scheduled.TrySetResult(dueTime);
            return new Timer(this);
        }
        /// <summary>只計數生命週期、不保存 callback 或 state 的合成 timer；由 Task.Delay owner 釋放。</summary>
        private sealed class Timer(Clock owner) : ITimer
        {
            private int disposed;
            /// <summary>本案不重設排程，也不觸發回呼；沒有外部工作需要排空。</summary>
            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;
            /// <summary>冪等歸還計數，避免取消與延遲完成競爭造成負數。</summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0) Interlocked.Decrement(ref owner.LiveTimers);
            }
            /// <summary>本 timer 無執行中回呼，等價同步清理。</summary>
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    /// <summary>注入改字結果與故障的單案來源；Task 由服務等待，不自建背景工作。</summary>
    private sealed class Changes(Func<DateTime, CancellationToken, Task<IReadOnlyList<TextChangedChurch>>> read) : ITextChangeSource
    {
        /// <summary>保留原 token 與 cutoff，提供可控版本及來源故障。</summary>
        public Task<IReadOnlyList<TextChangedChurch>> ReadAsync(DateTime sinceUtc, CancellationToken cancellationToken) =>
            read(sinceUtc, cancellationToken);
    }
    /// <summary>快取的合成資料庫來源；結果各有正式 ID 且只含該次教會文字。</summary>
    private sealed class TextSource : IChurchTextOverrideSource
    {
        /// <summary>下一次載入的合成本文，僅由單一案例更新。</summary>
        internal string Value { get; set; } = "old";
        /// <summary>回傳單次純值候選，不保留 context、SDK 或 caller token。</summary>
        public Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChurchTextOverride>>(new[] {
                new ChurchTextOverride(Guid.NewGuid(), context.ChurchId, new TextKey("Common.Diagnostics.Ping"), "*", Value) });
    }
}
