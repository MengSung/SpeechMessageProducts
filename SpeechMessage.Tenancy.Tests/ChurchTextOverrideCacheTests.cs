using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Localization;
using Xunit;

namespace SpeechMessage.Tenancy.Tests;

/// <summary>保護覆寫快取完整發布及資源 owner；只注入有界合成來源，不接 D365、LINE 或任一 request／使用者。</summary>
public sealed class ChurchTextOverrideCacheTests
{
    /// <summary>固定 UTC 版本起點，不能用本機時區或字串文化改變快取世代。</summary>
    private static readonly DateTime Stamp = new(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>三十二位同範圍讀取者在 deterministic barrier 等待同一個來源；完成後 active flight 必須歸零。</summary>
    [Fact]
    public async Task Load_SameChurchConcurrently_SourceCalledOnce()
    {
        var context = Context();
        var entered = Signal();
        var release = Signal();
        var calls = 0;
        var source = new Source(async (scope, token) =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return Rows(scope, "one");
        });
        await using var cache = Cache(source);
        var tasks = Enumerable.Range(0, 32).Select(_ => cache.LoadAsync(context, Stamp)).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
        Assert.Equal(1, cache.ActiveLoadCount);
        release.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, snapshot => Assert.Same(results[0], snapshot));
        Assert.Equal(1, calls);
        Assert.Equal(0, cache.ActiveLoadCount);
        Assert.Equal("one", new TextCatalog(new SystemTextTable(), cache).GetText(context, new TextKey("Common.Diagnostics.Ping")));
    }

    /// <summary>冷載入故障傳同一例外、告警一次；失敗 flight 移除後下次可成功，不把錯誤快取成空清單。</summary>
    [Fact]
    public async Task Load_SourceThrows_NextCallRetries()
    {
        var failure = new InvalidOperationException("synthetic sensitive input");
        var errors = new List<Exception>();
        var calls = 0;
        await using var cache = Cache(new Source((context, _) => ++calls == 1
            ? Task.FromException<IReadOnlyList<ChurchTextOverride>>(failure)
            : Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Rows(context, "retry"))), errors);
        var context = Context();
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => cache.LoadAsync(context, Stamp)));
        Assert.Same(failure, Assert.Single(errors));
        Assert.Equal(0, cache.ActiveLoadCount);
        AssertText(await cache.LoadAsync(context, Stamp), "retry");
    }

    /// <summary>每種不完整候選都保留原完整快照並告警；修來源後同一版本仍可重試，不能把失敗 stamp 當已成功。</summary>
    /// <param name="fault">固定合成候選故障。</param>
    [Theory]
    [InlineData("duplicate-key")]
    [InlineData("duplicate-id")]
    [InlineData("unknown-key")]
    [InlineData("parameters")]
    [InlineData("length")]
    [InlineData("foreign-church")]
    public async Task Publish_InvalidCandidate_KeepsOldSnapshot(string fault)
    {
        var errors = new List<Exception>();
        var context = Context();
        IReadOnlyList<ChurchTextOverride> rows = Rows(context, "old");
        await using var cache = Cache(new Source((_, _) => Task.FromResult(rows)), errors);
        var old = await cache.LoadAsync(context, Stamp);
        var bad = new List<ChurchTextOverride>(Rows(context, "partial"));
        switch (fault)
        {
            case "duplicate-key": bad.Add(Row(context, "Common.Diagnostics.Ping", "duplicate")); break;
            case "duplicate-id": bad.Add(new ChurchTextOverride(bad[0].Id, context.ChurchId, new TextKey("Keyword.Journal"), "*", "duplicate")); break;
            case "unknown-key": bad.Add(Row(context, "Unknown.Resource", "unknown")); break;
            case "parameters": bad.Add(Row(context, "Common.Diagnostics.Culture", "missing")); break;
            case "length": bad.Add(Row(context, "Line.Sender.Template", "{churchName}1234567")); break;
            case "foreign-church": bad.Add(Row(Context(), "Keyword.Journal", "foreign")); break;
        }
        rows = bad;
        Assert.Same(old, await cache.LoadAsync(context, Stamp.AddSeconds(1)));
        Assert.Same(old, cache.GetSnapshot(context));
        Assert.Single(errors);
        rows = Rows(context, "repaired");
        AssertText(await cache.LoadAsync(context, Stamp.AddSeconds(1)), "repaired");
    }

    /// <summary>某位等待者取消只結束自己的等待，不能取消共享來源或另一位讀取者；排空後 registration 不保留 caller。</summary>
    [Fact]
    public async Task Load_CallerCancellation_DoesNotCancelSharedWork()
    {
        var context = Context();
        var entered = Signal();
        var release = Signal();
        var errors = new List<Exception>();
        await using var cache = Cache(new Source(async (scope, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return Rows(scope, "shared");
        }), errors);
        using var caller = new CancellationTokenSource();
        var canceled = cache.LoadAsync(context, Stamp, caller.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var survivor = cache.LoadAsync(context.WithCultures("en", "en-US"), Stamp);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        release.SetResult();
        AssertText(await survivor, "shared");
        Assert.Empty(errors);
        Assert.Equal(0, cache.ActiveLoadCount);
    }

    /// <summary>載入中 stamp 前進不能讓舊候選回寫，仍維持單一來源；所有原讀取者最後得到新完整世代。</summary>
    [Fact]
    public async Task Invalidate_InFlightGeneration_CannotPublishStaleRows()
    {
        var context = Context();
        var entered = Signal();
        var release = Signal();
        var calls = 0;
        await using var cache = Cache(new Source(async (scope, token) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1) { entered.SetResult(); await release.Task.WaitAsync(token); }
            return Rows(scope, call == 1 ? "stale" : "current");
        }));
        var first = cache.LoadAsync(context, Stamp);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(cache.Invalidate(context, Stamp.AddSeconds(1)));
        var second = cache.LoadAsync(context, Stamp.AddSeconds(1));
        Assert.Equal(1, calls);
        release.SetResult();
        Assert.All(await Task.WhenAll(first, second), snapshot => AssertText(snapshot, "current"));
        Assert.Equal(2, calls);
    }

    /// <summary>版本失效只重建指定教會；不同組織／產品即使教會 GUID 相同也不能共用來源或快照。</summary>
    [Fact]
    public async Task Invalidate_TextChangedOnAdvanced_RebuildsOnlyThatChurch()
    {
        var first = Context();
        var contexts = new[] { first, Context(), new TextContext(first.ChurchId, "zh-TW", "zh-TW", "Beta", first.Product),
            new TextContext(first.ChurchId, "zh-TW", "zh-TW", first.Organization, "OtherProduct") };
        var calls = 0;
        await using var cache = Cache(new Source((scope, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Rows(scope, scope.Organization + scope.Product + scope.ChurchId.ToString("D")));
        }));
        var initial = await Task.WhenAll(contexts.Select(context => cache.LoadAsync(context, Stamp)));
        Assert.True(cache.Invalidate(first, Stamp.AddMinutes(1)));
        Assert.False(cache.Invalidate(first, Stamp));
        for (var index = 0; index < contexts.Length; index++)
        {
            var loaded = await cache.LoadAsync(contexts[index], index == 0 ? Stamp.AddMinutes(1) : Stamp);
            if (index == 0) Assert.NotSame(initial[index], loaded);
            else Assert.Same(initial[index], loaded);
        }
        Assert.Equal(5, calls);
    }

    /// <summary>可控時鐘證明十分快速重讀期限不被命中重設；失敗保留舊快照，閒置三十分才完全移除。</summary>
    [Fact]
    public async Task Expiry_FakeTime_ReloadsAtTenMinutesAndExpiresIdleAtThirty()
    {
        var clock = new Clock();
        var calls = 0;
        var context = Context();
        await using var cache = Cache(new Source((scope, _) => Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Rows(scope, (++calls).ToString(System.Globalization.CultureInfo.InvariantCulture)))), clock: clock);
        var first = await cache.LoadAsync(context, Stamp);
        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Same(first, await cache.LoadAsync(context, Stamp));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.NotSame(first, await cache.LoadAsync(context, Stamp));
        Assert.Equal(2, calls);
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Throws<InvalidOperationException>(() => cache.GetSnapshot(context));
        Assert.Equal(0, cache.CachedScopeCount);
        AssertText(await cache.LoadAsync(context, Stamp), "3");
    }

    /// <summary>停用及 Dispose 都取消 owner、排空來源，再移除快照；忽略取消的晚回應亦不得復活，操作後計數歸零。</summary>
    /// <param name="dispose">true 測 owner 全域關閉，false 測單教會停用。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_LateSourceCompletion_CannotRepublishAndDrains(bool dispose)
    {
        var context = Context();
        var entered = Signal();
        var release = Signal();
        var errors = new List<Exception>();
        var cache = Cache(new Source(async (scope, _) =>
        {
            entered.SetResult();
            await release.Task;
            return Rows(scope, "too late");
        }), errors);
        var pending = cache.LoadAsync(context, Stamp);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task? drain = null;
        if (dispose) drain = cache.DisposeAsync().AsTask();
        else cache.Disable(context);
        if (drain is not null) Assert.False(drain.IsCompleted);
        release.SetResult();
        if (dispose) await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await cache.DisposeAsync();
        Assert.Empty(errors);
        Assert.Equal(0, cache.ActiveLoadCount);
        Assert.Equal(0, cache.CachedScopeCount);
        Assert.Throws<ObjectDisposedException>(() => cache.GetSnapshot(context));
        if (drain is not null) await drain;
    }

    /// <summary>快取與 flight 都有獨立入場上限；來源等候不能擠出無界工作或排隊，滿載後仍能 drain。</summary>
    [Fact]
    public async Task Capacity_InFlightAdmission_IsBoundedAndRecovers()
    {
        var release = Signal();
        var entered = Signal();
        var calls = 0;
        await using var cache = new ChurchTextOverrideCache(new Source(async (scope, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2) entered.SetResult();
            await release.Task.WaitAsync(token);
            return Rows(scope, "bounded");
        }), Validator(), static (_, _) => { }, maximumConcurrentLoads: 2);
        var first = cache.LoadAsync(Context(), Stamp);
        var second = cache.LoadAsync(Context(), Stamp);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.LoadAsync(Context(), Stamp));
        Assert.Equal(2, cache.ActiveLoadCount);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(0, cache.ActiveLoadCount);
        await cache.LoadAsync(Context(), Stamp);
    }

    /// <summary>兩教會各千次交錯實際 catalog 消費不串字，停用後計數歸零；不以 Mock 成功宣稱實機已驗收。</summary>
    [Fact]
    public async Task Cache_ABChurchesParallel_NoLeak_CountsReturnToBaseline()
    {
        var first = Context();
        var second = Context();
        await using var cache = Cache(new Source((scope, _) => Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Rows(scope, scope.ChurchId == first.ChurchId ? "A" : "B"))));
        var catalog = new TextCatalog(new SystemTextTable(), cache);
        await Task.WhenAll(Enumerable.Range(0, 1000).Select(async _ =>
        {
            await cache.LoadAsync(first, Stamp);
            await cache.LoadAsync(second, Stamp);
            Assert.Equal("A", catalog.GetText(first, new TextKey("Common.Diagnostics.Ping")));
            Assert.Equal("B", catalog.GetText(second.WithCultures("en", "en-US"), new TextKey("Common.Diagnostics.Ping")));
        }));
        cache.Disable(first);
        cache.Disable(second);
        Assert.Equal(0, cache.ActiveLoadCount);
        Assert.Equal(0, cache.CachedScopeCount);
    }

    /// <summary>GetSnapshot 未 preload 明確拒絕；UTC stamp 不可由 local 或 unspecified 偷換，未取消來源前不得產生 I/O。</summary>
    [Fact]
    public async Task Load_ColdAndInvalidStamp_FailClosed()
    {
        await using var cache = Cache(new Source((scope, _) => Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Rows(scope, "loaded"))));
        var context = Context();
        Assert.Throws<InvalidOperationException>(() => cache.GetSnapshot(context));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.LoadAsync(context, DateTime.SpecifyKind(Stamp, DateTimeKind.Unspecified)));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.LoadAsync(context, DateTime.SpecifyKind(Stamp, DateTimeKind.Local)));
    }

    /// <summary>惡意或故障取消 callback 不得中斷 owner 清理；真 AggregateException 仍交共用 notifier，所有來源及 timer 排空。</summary>
    [Fact]
    public async Task Dispose_CancellationCallbackThrows_StillDrainsAndReports()
    {
        var entered = Signal();
        var errors = new List<Exception>();
        var cache = Cache(new Source(async (scope, token) =>
        {
            using var registration = token.Register(static () => throw new InvalidOperationException("synthetic callback failure"));
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Rows(scope, "unreachable");
        }), errors);
        var pending = cache.LoadAsync(Context(), Stamp);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await cache.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
            Assert.IsType<AggregateException>(Assert.Single(errors));
            Assert.Equal(0, cache.ActiveLoadCount);
            Assert.Equal(0, cache.CachedScopeCount);
        }
        finally { try { await pending; } catch (Exception) { } }
    }

    /// <summary>內部 deadline 不是 caller 正常取消；即使來源丟已取消 token 的 OCE，也要傳真原例外並可重試。</summary>
    [Fact]
    public async Task Load_OwnerTimeout_IsReportedAndDoesNotPoisonRetry()
    {
        var errors = new List<Exception>();
        var calls = 0;
        var source = new Source(async (scope, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Rows(scope, "recovered");
        });
        await using var cache = new ChurchTextOverrideCache(source, Validator(), (failure, _) => errors.Add(failure),
            loadTimeout: TimeSpan.FromMilliseconds(100));
        var context = Context();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.LoadAsync(context, Stamp));
        Assert.Same(failure, Assert.Single(errors));
        Assert.Equal(0, cache.ActiveLoadCount);
        AssertText(await cache.LoadAsync(context, Stamp), "recovered");
    }

    /// <summary>deadline 由 timer 觸發的取消 callback 也可能故障；不得讓 AggregateException 逃到 ThreadPool 形成程序終止。</summary>
    [Fact]
    public async Task Deadline_CallbackThrows_IsObservedAndTimerIsReleased()
    {
        using var clock = new DeadlineClock();
        var entered = Signal();
        var errors = new List<Exception>();
        await using var cache = Cache(new Source(async (scope, token) =>
        {
            using var registration = token.Register(static () => throw new InvalidOperationException("synthetic callback failure"));
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Rows(scope, "unreachable");
        }), errors, clock);
        var pending = cache.LoadAsync(Context(), Stamp);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var timerFailure = Record.Exception(clock.Fire);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(timerFailure);
        Assert.Contains(errors, error => error is AggregateException);
        Assert.Contains(errors, error => error is OperationCanceledException);
        Assert.Equal(0, clock.LiveTimers);
        Assert.Equal(0, cache.ActiveLoadCount);
    }

    /// <summary>第一位 caller 的 AsyncLocal 可能保存 HttpContext／身分；共享來源與 deadline 不得繼承它，caller 自身環境仍保持不變。</summary>
    [Fact]
    public async Task Load_SharedOwner_DoesNotInheritCallerExecutionContext()
    {
        var ambient = new AsyncLocal<string?>();
        ambient.Value = "synthetic-user-A";
        await using var cache = Cache(new Source((scope, _) =>
        {
            Assert.Null(ambient.Value);
            return Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Rows(scope, "isolated"));
        }));
        try
        {
            AssertText(await cache.LoadAsync(Context(), Stamp), "isolated");
            Assert.Equal("synthetic-user-A", ambient.Value);
        }
        finally { ambient.Value = null; }
    }

    /// <summary>共享載入與關閉工作不得投遞到第一位 caller 的同步內容；以計數內容注入，保護 UI／測試排程器不保留完成 scope。</summary>
    [Fact]
    public async Task Load_SharedOwner_DoesNotCaptureCallerSynchronizationContext()
    {
        var synchronization = new CountingSynchronizationContext();
        var cache = Cache(new Source(static (scope, _) =>
            Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Rows(scope, "detached"))));
        try
        {
            Task<IChurchTextOverrideSnapshot> pending;
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(synchronization);
                pending = cache.LoadAsync(Context(), Stamp);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            AssertText(await pending, "detached");
            ValueTask disposal;
            try
            {
                SynchronizationContext.SetSynchronizationContext(synchronization);
                disposal = cache.DisposeAsync();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            await disposal;
            Assert.Equal(0, synchronization.PostCount);
        }
        finally { await cache.DisposeAsync(); }
    }

    /// <summary>真正二千項容量守門，超過後淘汰最舊非 flight；被淘汰 scope 不可回傳舊字，未受影響 scope 不重讀。</summary>
    [Fact]
    public async Task Capacity_TwoThousandEntries_EvictsWithoutRetainingOldSnapshots()
    {
        var clock = new Clock();
        await using var cache = Cache(new Source((scope, _) => Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Rows(scope, "bounded"))), clock: clock);
        var first = Context();
        await cache.LoadAsync(first, Stamp);
        for (var index = 0; index < 2000; index++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await cache.LoadAsync(Context(), Stamp);
            Assert.InRange(cache.CachedScopeCount, 1, 2000);
        }
        Assert.Equal(2000, cache.CachedScopeCount);
        Assert.Throws<InvalidOperationException>(() => cache.GetSnapshot(first));
        Assert.Equal(0, cache.ActiveLoadCount);
    }

    /// <summary>長生命 cache owner 連續百次載入／停用後，不可保留 context 或快照；每個 deadline timer 也必須回零。</summary>
    [Fact]
    public async Task Lifecycle_HundredScopes_ReleaseContextsSnapshotsAndTimers()
    {
        using var clock = new DeadlineClock();
        await using var cache = Cache(new Source(static (scope, _) =>
            Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Rows(scope, "released"))), clock: clock);
        var references = await ReleaseScopesAsync(cache, clock);
        await Task.Yield();
        for (var attempt = 0; attempt < 10 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            // await 完成不代表先前 ThreadPool continuation 的堆疊已退回；
            // 有界讓出排程後再驗回收，仍要求全部二百參照死亡，不能放寬成「多數可回收」。
            await Task.Delay(10);
        }
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        Assert.Equal(0, cache.CachedScopeCount);
        Assert.Equal(0, cache.ActiveLoadCount);
        Assert.Equal(0, clock.LiveTimers);
    }

    /// <summary>一萬次命中不重讀來源、不掃全表、不建立 deadline；記錄比例配置量與耗時，保守配置上限不代替實機效能測量。</summary>
    [Fact]
    public async Task Performance_TenThousandHits_HaveBoundedAllocationAndNoSourceReload()
    {
        var calls = 0;
        await using var cache = Cache(new Source((scope, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<ChurchTextOverride>>(Rows(scope, "hot"));
        }));
        var context = Context();
        await cache.LoadAsync(context, Stamp);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var index = 0; index < 10_000; index++)
        {
            var result = cache.LoadAsync(context, Stamp);
            Assert.True(result.IsCompletedSuccessfully);
            AssertText(await result, "hot");
        }
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1, calls);
        Assert.InRange(allocated, 0, 40_960_000);
        Assert.Equal(0, cache.ActiveLoadCount);
        Console.WriteLine(FormattableString.Invariant($"Override hits=10000 elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2} allocatedBytes={allocated} sourceCalls={calls}"));
    }

    /// <summary>通知管道自身故障不能取代來源原 Exception 或讓失敗 flight 卡死；只使用固定 stderr，不遞迴通知。</summary>
    [Fact]
    public async Task Load_NotifierThrows_PreservesOriginalFailureAndReleasesFlight()
    {
        var original = new InvalidOperationException("synthetic private source");
        var reports = 0;
        await using var cache = new ChurchTextOverrideCache(new Source((_, _) =>
            Task.FromException<IReadOnlyList<ChurchTextOverride>>(original)), Validator(), (_, _) =>
            {
                Interlocked.Increment(ref reports);
                throw new InvalidOperationException("synthetic notifier fault");
            });
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() => cache.LoadAsync(Context(), Stamp)));
        Assert.Equal(1, reports);
        Assert.Equal(0, cache.ActiveLoadCount);
    }

    /// <summary>在獨立非 inline 方法排空每次操作，回傳只含弱參照；來源列表、context 與快照不能被測試自身保留。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<List<WeakReference>> ReleaseScopesAsync(ChurchTextOverrideCache cache, DeadlineClock clock)
    {
        var references = new List<WeakReference>();
        for (var index = 0; index < 100; index++)
        {
            var context = Context();
            var snapshot = await cache.LoadAsync(context, Stamp);
            references.Add(new WeakReference(context));
            references.Add(new WeakReference(snapshot));
            cache.Disable(context);
            Assert.Equal(0, clock.LiveTimers);
        }
        return references;
    }

    /// <summary>本案獨佔快取 owner，告警只存原例外供斷言，沒有實際外送。</summary>
    private static ChurchTextOverrideCache Cache(Source source, List<Exception>? errors = null, TimeProvider? clock = null) =>
        new(source, Validator(), (failure, _) => { if (errors is not null) lock (errors) errors.Add(failure); }, clock);
    /// <summary>使用正式資源與完整最長樣本，長度不得由測試縮短。</summary>
    private static ChurchTextOverrideValidator Validator() => new(new SystemTextTable(), "這是一個長度剛好十二字的教會");
    /// <summary>單次合成完整已授權範圍。</summary>
    private static TextContext Context() => new(Guid.NewGuid(), "zh-TW", "zh-TW", "Alpha", "ChurchReport");
    /// <summary>不 inline 執行 continuation 的測試 barrier，沒有 timer 或持續背景工作。</summary>
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>操作擁有的純值來源集合。</summary>
    private static ChurchTextOverride[] Rows(TextContext context, string text) => new[] { Row(context, "Common.Diagnostics.Ping", text) };
    /// <summary>每列使用正式合成資料庫 ID，不以顯示文字或位置當識別。</summary>
    private static ChurchTextOverride Row(TextContext context, string key, string text) => new(Guid.NewGuid(), context.ChurchId, new TextKey(key), "*", text);
    /// <summary>從封閉快照取字，精確確認完整候選已替換而非只是存在資源檔。</summary>
    private static void AssertText(IChurchTextOverrideSnapshot snapshot, string expected)
    {
        Assert.True(snapshot.TryGetText("*", new TextKey("Common.Diagnostics.Ping"), out var text));
        Assert.Equal(expected, text);
    }

    /// <summary>由每案擁有的合成來源；只借用委派，不建立或保留未觀測背景 task。</summary>
    private sealed class Source(Func<TextContext, CancellationToken, Task<IReadOnlyList<ChurchTextOverride>>> read) : IChurchTextOverrideSource
    {
        /// <summary>保留 caller 指定 token 與原 Task，讓 barrier 精確注入故障、取消及晚回應。</summary>
        public Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext context, CancellationToken cancellationToken) => read(context, cancellationToken);
    }

    /// <summary>只計數 Post，實際委派交給標準 ThreadPool；不保存 callback、state 或任何 scope，沒有自建佇列與生命週期。</summary>
    private sealed class CountingSynchronizationContext : SynchronizationContext
    {
        private int posts;
        /// <summary>跨執行緒讀取實際排程次數，共享 owner 應完全不使用 caller 的同步內容。</summary>
        internal int PostCount => Volatile.Read(ref posts);
        /// <summary>記錄後直接轉交標準排程，不快取 caller 工作，不改變待執行委派。</summary>
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref posts);
            base.Post(callback, state);
        }
    }

    /// <summary>只控制 UTC 的測試時鐘；快取無輪詢 timer，來源 deadline 另有 owner token，不把假時間當系統文化。</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(Stamp);
        /// <summary>明確回傳本案時刻，不使用 DateTime.Now 或全域可變時鐘。</summary>
        public override DateTimeOffset GetUtcNow() => now;
        /// <summary>由單一測試執行緒推進，不在多執行緒改動同一時鐘。</summary>
        internal void Advance(TimeSpan elapsed) => now += elapsed;
    }

    /// <summary>單案 deadline 驅動器；以同步 Fire 捕捉真正 timer callback 的未處理故障，不讓測試主機崩潰掩蓋來源。</summary>
    private sealed class DeadlineClock : TimeProvider, IDisposable
    {
        private ManualTimer? timer;
        /// <summary>Timer owner 尚未 Dispose 時才為一，排空後須回零。</summary>
        internal int LiveTimers => timer is null ? 0 : 1;
        /// <summary>建立本案唯一 timer；不真的排程 OS callback，dueTime 仍由產品傳入。</summary>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Null(timer);
            timer = new ManualTimer(this, callback, state);
            return timer;
        }
        /// <summary>明確觸發產品交給 TimeProvider 的 callback，所有例外由測試捕捉。</summary>
        internal void Fire() => timer!.Fire();
        /// <summary>測試失敗時亦解除 timer，正常產品排空後此處為無操作，不掩蓋 LiveTimers 的決定性斷言。</summary>
        public void Dispose() => timer?.Dispose();

        /// <summary>測試擁有的手動 timer，Dispose 必須解除 callback/state 參照，不建立任何額外 task。</summary>
        private sealed class ManualTimer(DeadlineClock owner, TimerCallback callback, object? state) : ITimer
        {
            private TimerCallback? action = callback;
            private object? value = state;
            /// <summary>僅供本案觸發，排空前 callback 不可 null。</summary>
            internal void Fire() => action!(value);
            /// <summary>本測試只需要一次 deadline，不模擬週期輪詢。</summary>
            public bool Change(TimeSpan dueTime, TimeSpan period) => action is not null;
            /// <summary>釋放唯一 timer 與所有 closure，無背景工作需要等待。</summary>
            public void Dispose() { action = null; value = null; owner.timer = null; }
            /// <summary>與同步 Dispose 相同且冪等，沒有未排空 callback。</summary>
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
