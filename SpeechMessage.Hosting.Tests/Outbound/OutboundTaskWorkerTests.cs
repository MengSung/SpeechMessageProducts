/*
 * 檔案整體說明
 *
 * 目的與定位：以離線合成替身鎖定 P2-07 OutboundTaskWorker 的認領、重試、停機
 * 排空及資源生命週期契約。測試不連 D365、LINE 或讀取任何 User Secrets；FakeStore
 * 模擬資料庫 RowVersion 的單一寫入者，FakeHandlerFactory 模擬每筆依教會建立的
 * 可釋放 scope。
 *
 * 主要功能與流程：測試先啟動 BackgroundService，讓 worker 讀取有界候選、用 CAS
 * 認領，再依 handler 結果完成或排程下一次嘗試。429、逾時與第五次失敗的測試
 * 分別斷言 1 分鐘退避、終止狀態及共用例外入口被呼叫；停機案例以可取消與可釋放
 * 的合成 handler 驗證最多 30 秒排空而不遺留 scope。
 *
 * 使用邊界與維護：所有測試輸入都是固定 GUID、UTC 時間及無敏感內容的純值。每個
 * worker 都在 finally 由 StopAsync 排空；FakeStore、scope、TaskCompletionSource
 * 只由建立它們的測試擁有，完成後不保留跨測試狀態。這些案例是離線契約證據，
 * 不得宣稱 D365 實機或 LINE 實際送達。
 */
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using SpeechMessage.Hosting.Outbound;
using Xunit;

namespace SpeechMessage.Hosting.Tests.Outbound;

/// <summary>
/// 保護對外待辦 worker 的 CAS、重試、停止排空及資源回基線契約。
/// 所有替身均為 operation-local，不保存跨使用者、教會或測試的可變狀態。
/// </summary>
public sealed class OutboundTaskWorkerTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 8, 30, 0, DateTimeKind.Utc);

    /// <summary>兩個 worker 同時看到同一候選時，FakeStore 的 CAS 只允許一個 owner 執行。</summary>
    [Fact]
    public async Task Claim_TwoWorkersParallel_OnlyOneExecutes()
    {
        var store = new FakeStore(Item());
        var executed = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeHandlerFactory(async (_, token) =>
        {
            Interlocked.Increment(ref executed);
            started.TrySetResult();
            await Task.Yield();
        });
        using var first = CreateWorker(store, factory);
        using var second = CreateWorker(store, factory);

        await first.StartAsync(CancellationToken.None);
        await second.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => store.Settlements.Count == 1);
        await first.StopAsync(CancellationToken.None);
        await second.StopAsync(CancellationToken.None);

        Assert.Equal(1, executed);
        Assert.Single(store.Settlements);
        Assert.Equal(OutboundWorkStatus.Completed, store.Settlements[0].Status);
    }

    /// <summary>429 只排程下一次嘗試，並使用規定的第一段一分鐘退避。</summary>
    [Fact]
    public async Task Retry_429_BackoffSchedule()
    {
        var store = new FakeStore(Item());
        var factory = new FakeHandlerFactory((_, _) =>
            throw new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests));
        using var worker = CreateWorker(store, factory);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => store.Settlements.Count == 1);
        await worker.StopAsync(CancellationToken.None);

        var settlement = Assert.Single(store.Settlements);
        Assert.Equal(OutboundWorkStatus.Pending, settlement.Status);
        Assert.Equal(1, settlement.Attempts);
        Assert.Equal(Now.AddMinutes(1), settlement.NextAttemptOn);
        Assert.Equal("Http429", settlement.ErrorCode);
    }

    /// <summary>第五次已排程重試仍逾時時，改成失敗並交給共用告警入口。</summary>
    [Fact]
    public async Task Retry_ExceedsMax_FailedAndNotified()
    {
        var store = new FakeStore(Item(attempts: 5));
        var factory = new FakeHandlerFactory((_, _) => throw new TimeoutException("synthetic-timeout"));
        var notifications = new ConcurrentQueue<(Exception Exception, string Operation)>();
        using var worker = CreateWorker(store, factory,
            (exception, operation, _) => notifications.Enqueue((exception, operation)));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => store.Settlements.Count == 1);
        await worker.StopAsync(CancellationToken.None);

        var settlement = Assert.Single(store.Settlements);
        Assert.Equal(OutboundWorkStatus.Failed, settlement.Status);
        Assert.Equal(5, settlement.Attempts);
        Assert.Null(settlement.NextAttemptOn);
        var notification = Assert.Single(notifications);
        Assert.IsType<TimeoutException>(notification.Exception);
        Assert.Equal("OutboundTaskWorker.TerminalFailure", notification.Operation);
    }

    /// <summary>超過十分鐘的 Processing 候選可被新 owner 認領並完成。</summary>
    [Fact]
    public async Task StaleProcessing_Reclaimed()
    {
        var store = new FakeStore(Item(
            status: OutboundWorkStatus.Processing,
            claimedOn: Now.AddMinutes(-11)));
        var factory = new FakeHandlerFactory((_, _) => Task.CompletedTask);
        using var worker = CreateWorker(store, factory);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => store.Settlements.Count == 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, store.ClaimCount);
        Assert.Equal(OutboundWorkStatus.Completed, Assert.Single(store.Settlements).Status);
    }

    /// <summary>停止新輪詢後，當前 batch 會等可合作 handler 完成而不提前釋放 scope。</summary>
    [Fact]
    public async Task Shutdown_DrainsCurrentBatch_WithinTimeout()
    {
        var store = new FakeStore(Item());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeHandlerFactory(async (_, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
        });
        using var worker = CreateWorker(store, factory);

        await worker.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopping = worker.StopAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(stopping.IsCompleted);
        release.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, factory.CreatedScopes);
        Assert.Equal(1, factory.DisposedScopes);
        Assert.Single(store.Settlements);
    }

    /// <summary>多筆處理完成後，所有每筆 scope 都歸還，沒有保留跨教會資源。</summary>
    [Fact]
    public async Task Worker_ResourceCountsReturnToBaseline()
    {
        var store = new FakeStore(Item(), Item(churchId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        var factory = new FakeHandlerFactory((_, _) => Task.CompletedTask);
        using var worker = CreateWorker(store, factory);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => store.Settlements.Count == 2);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, factory.CreatedScopes);
        Assert.Equal(2, factory.DisposedScopes);
        Assert.Equal(0, factory.ActiveScopes);
        Assert.Equal(2, store.Settlements.Count);
    }

    /// <summary>同一消費集合內的重複正式 ID 會整批拒絕，不以位置或 payload 靜默去重。</summary>
    [Fact]
    public async Task Worker_DuplicateStableIdentity_FailsClosed()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var store = new FakeStore(Item(id), Item(id));
        var factory = new FakeHandlerFactory((_, _) => Task.CompletedTask);
        var notifications = new ConcurrentQueue<(Exception Exception, string Operation)>();
        using var worker = CreateWorker(store, factory,
            (exception, operation, _) => notifications.Enqueue((exception, operation)));

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => !notifications.IsEmpty);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(store.Settlements);
        Assert.Equal(0, factory.CreatedScopes);
        Assert.Equal("OutboundTaskWorker.CandidateValidation", Assert.Single(notifications).Operation);
    }

    private static OutboundTaskWorker CreateWorker(
        FakeStore store,
        FakeHandlerFactory factory,
        Action<Exception, string, CancellationToken>? report = null) =>
        new(store, factory, report, new FixedTimeProvider(Now),
            new OutboundTaskWorkerOptions(TimeSpan.FromHours(1), 50, TimeSpan.FromSeconds(30)));

    private static OutboundWorkItem Item(
        Guid? id = null,
        Guid? churchId = null,
        OutboundWorkStatus status = OutboundWorkStatus.Pending,
        int attempts = 0,
        DateTime? claimedOn = null) =>
        new(
            id ?? Guid.NewGuid(),
            "1",
            churchId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
            null,
            OutboundWorkType.QrImageGenerate,
            "{\"source\":\"synthetic\"}",
            "synthetic-key",
            status,
            attempts,
            null,
            claimedOn);

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Synthetic worker did not reach its assertion.");
            await Task.Delay(10);
        }
    }

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }

    private sealed class FakeStore : IOutboundWorkStore
    {
        private readonly object _gate = new();
        private readonly List<OutboundWorkItem> _candidates;
        private readonly HashSet<Guid> _claimed = new();

        internal FakeStore(params OutboundWorkItem[] candidates) => _candidates = candidates.ToList();

        internal int ClaimCount { get; private set; }
        internal List<Settlement> Settlements { get; } = new();

        public IReadOnlyList<OutboundWorkItem> ReadCandidates(DateTime utcNow, int maximum, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) return _candidates.ToArray();
        }

        public OutboundWorkItem? TryClaim(OutboundWorkItem candidate, DateTime utcNow, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ClaimCount++;
                if (!_claimed.Add(candidate.Id)) return null;
                return candidate with
                {
                    RowVersion = "2",
                    Status = OutboundWorkStatus.Processing,
                    ClaimedOn = utcNow,
                    NextAttemptOn = null
                };
            }
        }

        public bool TrySettle(
            OutboundWorkItem claim,
            OutboundWorkStatus status,
            int attempts,
            DateTime utcNow,
            DateTime? nextAttemptOn,
            string? errorCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                Settlements.Add(new Settlement(status, attempts, nextAttemptOn, errorCode));
                return true;
            }
        }
    }

    private sealed record Settlement(
        OutboundWorkStatus Status,
        int Attempts,
        DateTime? NextAttemptOn,
        string? ErrorCode);

    private sealed class FakeHandlerFactory : IOutboundTaskHandlerFactory
    {
        private readonly Func<OutboundWorkItem, CancellationToken, Task> _execute;
        private int _created;
        private int _disposed;
        private int _active;

        internal FakeHandlerFactory(Func<OutboundWorkItem, CancellationToken, Task> execute) => _execute = execute;

        internal int CreatedScopes => Volatile.Read(ref _created);
        internal int DisposedScopes => Volatile.Read(ref _disposed);
        internal int ActiveScopes => Volatile.Read(ref _active);

        public ValueTask<IOutboundTaskHandlerScope> OpenAsync(
            OutboundWorkItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _created);
            Interlocked.Increment(ref _active);
            return ValueTask.FromResult<IOutboundTaskHandlerScope>(
                new Scope(_execute, item, () =>
                {
                    Interlocked.Increment(ref _disposed);
                    Interlocked.Decrement(ref _active);
                }));
        }

        private sealed class Scope : IOutboundTaskHandlerScope
        {
            private readonly Func<OutboundWorkItem, CancellationToken, Task> _execute;
            private readonly OutboundWorkItem _item;
            private readonly Action _dispose;
            private int _disposed;

            internal Scope(
                Func<OutboundWorkItem, CancellationToken, Task> execute,
                OutboundWorkItem item,
                Action dispose)
            {
                _execute = execute;
                _item = item;
                _dispose = dispose;
            }

            public IOutboundTaskHandler Handler => new Handler(_execute, _item);

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) _dispose();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class Handler : IOutboundTaskHandler
        {
            private readonly Func<OutboundWorkItem, CancellationToken, Task> _execute;
            private readonly OutboundWorkItem _item;

            internal Handler(Func<OutboundWorkItem, CancellationToken, Task> execute, OutboundWorkItem item)
            {
                _execute = execute;
                _item = item;
            }

            public Task ExecuteAsync(CancellationToken cancellationToken) => _execute(_item, cancellationToken);
        }
    }
}
