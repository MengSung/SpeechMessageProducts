// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/SessionLifecycle/SessionScopedResourceDisposalCoordinatorTests.cs
// 測試責任：驗證 Session-owned cache generation 的唯一 owner、request lease、drain 與 host cleanup 狀態機。
// 信任邊界：測試只使用不可識別的合成 scope key 與記憶體內資源，不建立 Session、Credential、Token 或外部連線。
// 生命週期：每個測試都明確歸還 lease 並 Dispose coordinator/cache，確保故障注入不會把資源留給後續測試。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與 final CRLF。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Services.Caching;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.SessionLifecycle;

/// <summary>
/// 本 collection 會在單一測試中以最小空組態初始化 legacy DonationPaymentManager static state；
/// 暫時切換 process working directory 時不得與其他測試平行，避免跨測試檔案 I/O 邊界互相污染。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SessionLifecycleCollection
{
    public const string Name = "Session lifecycle isolated initialization";
}

/// <summary>
/// 驗證 <see cref="SessionScopedResourceDisposalCoordinator{TResource}"/> 只讓一個 owner 清理每個 cache generation，
/// 並以 request lease 保護仍在執行的 caller。測試刻意讓 eviction、drain 與 response completion 競爭，
/// 主要 assertion 是 Dispose 精確一次、ref-count 回到零，以及不同 Session 不共享阻塞或資源狀態。
/// </summary>
[Collection(SessionLifecycleCollection.Name)]
public sealed class SessionScopedResourceDisposalCoordinatorTests
{
    private static readonly TimeSpan TestExpiration = TimeSpan.FromMinutes(5);
    private static string Scope(char prefix) => prefix + new string('A', 42);

    /// <summary>
    /// 同時從 cache 與 coordinator 逐出同一世代，模擬 TTL callback 與 logout 顯式 drain 的競爭。
    /// lease 歸還後，唯一資源只能 Dispose 一次，且 coordinator 不得保留 entry 或 lease。
    /// </summary>
    [Fact]
    public void Concurrent_explicit_eviction_and_cache_callback_dispose_generation_once()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        var resource = new TrackingResource();
        var lease = coordinator.Acquire(
            Scope('A'),
            () => resource,
            TestExpiration,
            TestExpiration);

        Parallel.Invoke(
            () => cache.Compact(1.0),
            () => coordinator.EvictAndDrain(Scope('A')));
        lease.Dispose();

        SpinWait.SpinUntil(() => resource.DisposeCount == 1, TimeSpan.FromSeconds(2)).Should().BeTrue();
        resource.DisposeCount.Should().Be(1, "cache callback 與顯式 drain 必須共用同一個冪等 cleanup owner");
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 重現「顯式 drain 先觀察不到 slot，但在真正呼叫 cache.Remove 前，另一個 request 已發佈新世代」的競爭。
    /// 測試用 cache 只暫停第一次 Remove，不干預 coordinator 的 slot lock 或 factory；因此故障注入精確落在
    /// dictionary 與 IMemoryCache 之間的信任邊界。舊的 no-op drain 不得刪除線性化點之後才建立的新世代，
    /// 否則下一次 acquire 會錯誤建立第二個 Manager／LINE client／semaphore graph，造成容量抖動與 owner 混亂。
    /// </summary>
    [Fact]
    public async Task Missing_slot_drain_does_not_remove_generation_published_after_linearization_point()
    {
        using var cache = new BlockingFirstRemoveMemoryCache();
        using var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        var scope = Scope('K');
        var first = new TrackingResource();
        var unexpectedSecond = new TrackingResource();
        var factoryCount = 0;

        var staleDrain = Task.Run(() => coordinator.EvictAndDrain(scope));
        cache.WaitUntilDrainHasLinearized(staleDrain);

        var firstLease = coordinator.Acquire(
            scope,
            () => Interlocked.Increment(ref factoryCount) == 1 ? first : unexpectedSecond,
            TestExpiration,
            TestExpiration);

        cache.ReleaseFirstRemove();
        (await staleDrain).Should().BeFalse("呼叫當下沒有可撤銷的世代");

        var secondLease = coordinator.Acquire(
            scope,
            () => Interlocked.Increment(ref factoryCount) == 1 ? first : unexpectedSecond,
            TestExpiration,
            TestExpiration);

        secondLease.Value.Should().BeSameAs(
            first,
            "較早的 no-op drain 不得跨越線性化點刪除稍後才發佈的新 cache generation");
        factoryCount.Should().Be(1);

        coordinator.EvictAndDrain(scope).Should().BeTrue();
        firstLease.Dispose();
        secondLease.Dispose();
        first.DisposeCount.Should().Be(1);
        unexpectedSecond.DisposeCount.Should().Be(0);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 先取得 lease，再要求舊世代 drain。資源在 lease 存活時仍可使用，且不會被提前 Dispose；
    /// 最後一個 lease 歸還才執行最終清理，保護正在完成的奉獻或 LINE 流程。
    /// </summary>
    [Fact]
    public void In_flight_lease_survives_drain_and_last_return_disposes_resource()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        var resource = new TrackingResource();
        var lease = coordinator.Acquire(
            Scope('B'),
            () => resource,
            TestExpiration,
            TestExpiration);

        coordinator.EvictAndDrain(Scope('B')).Should().BeTrue();

        resource.Use().Should().Be(1, "drain 只能阻止新 lease，不能破壞既有 request");
        resource.DisposeCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(1);

        lease.Dispose();

        resource.DisposeCount.Should().Be(1);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 舊世代已進入 draining 時，同一 resource key 的下一個 request 必須建立新世代，
    /// 不得重用即將關閉的 Manager，也不得等待舊 request 完成。兩個世代各由自己的最後 lease 精確清理。
    /// </summary>
    [Fact]
    public void Acquire_after_drain_returns_fresh_generation_without_waiting_for_old_lease()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        var first = new TrackingResource();
        var second = new TrackingResource();
        var generations = new Queue<TrackingResource>(new[] { first, second });
        var firstLease = coordinator.Acquire(
            Scope('C'),
            () => generations.Dequeue(),
            TestExpiration,
            TestExpiration);

        coordinator.EvictAndDrain(Scope('C'));
        var secondLease = coordinator.Acquire(
            Scope('C'),
            () => generations.Dequeue(),
            TestExpiration,
            TestExpiration);

        secondLease.Value.Should().BeSameAs(second);
        first.DisposeCount.Should().Be(0);
        second.DisposeCount.Should().Be(0);

        firstLease.Dispose();
        coordinator.EvictAndDrain(Scope('C'));
        secondLease.Dispose();

        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 模擬 MemoryCache 已撤銷舊 entry 可見性、但 post-eviction callback 尚未取得執行緒的真實時序。
    /// 舊世代仍有 in-flight lease，因此 stale detection 只能先標成 Draining，不能立即 Dispose；接下來的新世代
    /// 必須改在重新註冊的 slot 上發佈。若沿用已從 dictionary 移除的舊 slot，第三次 acquire 會再建立第三個資源，
    /// 且 host drain 無法透過 dictionary 找到第二個孤兒世代。本測試以延後 callback 的 cache 精確固定此競爭窗口。
    /// </summary>
    [Fact]
    public void Stale_cache_entry_retries_on_registered_slot_instead_of_publishing_orphan_generation()
    {
        using var cache = new DeferredEvictionMemoryCache();
        using var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        var scope = Scope('L');
        var first = new TrackingResource();
        var second = new TrackingResource();
        var unexpectedThird = new TrackingResource();
        var generations = new Queue<TrackingResource>(new[] { first, second, unexpectedThird });
        var factoryCount = 0;
        TrackingResource CreateGeneration()
        {
            Interlocked.Increment(ref factoryCount);
            return generations.Dequeue();
        }

        var firstLease = coordinator.Acquire(scope, CreateGeneration, TestExpiration, TestExpiration);
        cache.EvictCurrentEntryWithoutInvokingCallbacks();

        var secondLease = coordinator.Acquire(scope, CreateGeneration, TestExpiration, TestExpiration);
        var thirdLease = coordinator.Acquire(scope, CreateGeneration, TestExpiration, TestExpiration);

        secondLease.Value.Should().BeSameAs(second);
        thirdLease.Value.Should().BeSameAs(
            second,
            "stale detection 必須在新的 registered slot 發佈世代，讓後續 request 可重用且能被 host drain 找到");
        factoryCount.Should().Be(2);

        cache.InvokeDeferredCallbacks();
        coordinator.EvictAndDrain(scope).Should().BeTrue();
        firstLease.Dispose();
        secondLease.Dispose();
        thirdLease.Dispose();

        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(1);
        unexpectedThird.DisposeCount.Should().Be(0);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 第一個 Session 的 factory 被故障閘門刻意暫停時，第二個 Session 仍必須完成建立。
    /// 這會抓出以單一全域 lock 包住 factory 的錯誤實作；coordinator 只能短暫協調 key，不能序列化無關使用者。
    /// </summary>
    [Fact]
    public async Task Different_session_factories_are_not_globally_serialized()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        using var firstFactoryEntered = new ManualResetEventSlim();
        using var releaseFirstFactory = new ManualResetEventSlim();

        var firstTask = Task.Run(() => coordinator.Acquire(
            Scope('D'),
            () =>
            {
                firstFactoryEntered.Set();
                releaseFirstFactory.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                return new TrackingResource();
            },
            TestExpiration,
            TestExpiration));

        firstFactoryEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        var secondTask = Task.Run(() => coordinator.Acquire(
            Scope('E'),
            () => new TrackingResource(),
            TestExpiration,
            TestExpiration));

        try
        {
            (await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(2))))
                .Should().BeSameAs(secondTask, "不同 Session 不得被第一個 Session 的 factory 或 drain 阻塞");
        }
        finally
        {
            releaseFirstFactory.Set();
        }

        using var firstLease = await firstTask;
        using var secondLease = await secondTask;
        coordinator.EvictAndDrain(Scope('D'));
        coordinator.EvictAndDrain(Scope('E'));
    }

    /// <summary>
    /// host shutdown 先切斷新 acquire，再讓所有可見世代進入 drain。已有 lease 不會被強制中斷；
    /// 它們歸還後 active entry、lease 與 owned resource 必須全部回到 baseline。
    /// </summary>
    [Fact]
    public void Host_shutdown_rejects_new_acquire_and_cleans_entries_after_last_lease()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        var first = new TrackingResource();
        var second = new TrackingResource();
        var firstLease = coordinator.Acquire(Scope('F'), () => first, TestExpiration, TestExpiration);
        var secondLease = coordinator.Acquire(Scope('G'), () => second, TestExpiration, TestExpiration);

        coordinator.Dispose();

        Action acquireAfterStop = () => coordinator.Acquire(
            Scope('H'),
            () => new TrackingResource(),
            TestExpiration,
            TestExpiration);
        acquireAfterStop.Should().Throw<ObjectDisposedException>();
        first.DisposeCount.Should().Be(0);
        second.DisposeCount.Should().Be(0);

        firstLease.Dispose();
        secondLease.Dispose();

        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(1);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 模擬手動建立且不會由 DI scope Dispose 的 context：資源透過目前 <see cref="HttpResponse"/> 完成事件持有 request lease。
    /// response 完成後 ref-count 必須歸零；entry 仍由 cache owner 持有，直到顯式逐出後才 Dispose owned resource。
    /// </summary>
    [Fact]
    public async Task Completed_http_request_returns_request_lease_without_relying_on_scope_disposal()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<DonationPaymentManager>(cache);
        var httpContext = new DefaultHttpContext();
        var responseFeature = new RecordingResponseFeature();
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);
        var session = new ConcurrentTestSession("synthetic-manual-context-session");
        session.SetString("_DonationPaymentResourceScopeId", Scope('I'));
        httpContext.Session = session;
        var manager = CreateUninitializedDonationPaymentManager();
        using (var seedLease = coordinator.Acquire(Scope('I'), () => manager, TestExpiration, TestExpiration))
        {
        }

        var context = new InMemoryDataContextSmallGroup(
            new HttpContextAccessor { HttpContext = httpContext },
            cache,
            new ThrowingToolUtilityProvider(),
            sessionResourceCoordinator: coordinator);

        context.DonationPaymentManager.Should().BeSameAs(manager);
        coordinator.OutstandingLeaseCount.Should().Be(1);

        await responseFeature.CompleteResponseAsync();

        coordinator.OutstandingLeaseCount.Should().Be(0, "request completion callback 必須歸還 lease");
        ReadDisposeState(manager).Should().Be(0, "cache entry 尚未逐出時仍可供下一個 request 取得新 lease");

        coordinator.EvictAndDrain(Scope('I'));
        SpinWait.SpinUntil(() => ReadDisposeState(manager) == 1, TimeSpan.FromSeconds(2)).Should().BeTrue();
        coordinator.ActiveEntryCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 legacy Controller 手動建立 context 時仍必須從目前 request 的主 DI container 取得唯一 coordinator。
    /// 若測試或 hosting wiring 遺漏這個 singleton，建構式要在任何 Donation manager、LINE client 或 cache generation
    /// 建立前立即 fail closed；不得偷偷建立 static／ConditionalWeakTable fallback，否則該 owner 不受 host shutdown 管理，
    /// 會使資源在測試 host、重新啟動世代或長時間執行後無法確定性回到基準線。
    /// </summary>
    [Fact]
    public void Manual_context_without_main_di_coordinator_fails_closed_before_resource_creation()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var httpContext = new DefaultHttpContext
        {
            Session = new ConcurrentTestSession("synthetic-missing-owner-session")
        };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        Action construct = () => new InMemoryDataContextSmallGroup(
            accessor,
            cache,
            new ThrowingToolUtilityProvider());

        construct.Should().Throw<InvalidOperationException>()
            .WithMessage("*Session resource coordinator*");
    }

    /// <summary>
    /// 驗證身份重設先撤銷目前 Session 的 Donation generation 可見性，再移除 Session 內的 opaque scope。
    /// 既有 request lease 仍可安全完成，最後 lease 歸還後才 Dispose；下一次登入因舊 scope 已移除，
    /// 只能建立全新的 opaque scope／generation，不會重新取得上一位使用者的 Manager 物件圖。
    /// </summary>
    [Fact]
    public void Identity_reset_drains_old_scope_before_removing_it_and_last_lease_owns_cleanup()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        var session = new ConcurrentTestSession("synthetic-identity-reset-session");
        var scope = coordinator.GetOrCreateResourceScopeId(session);
        var resource = new TrackingResource();
        var lease = coordinator.Acquire(scope, () => resource, TestExpiration, TestExpiration);

        coordinator.DrainSessionResourceScope(session).Should().BeTrue();

        session.TryGetValue("_DonationPaymentResourceScopeId", out _).Should().BeFalse();
        resource.DisposeCount.Should().Be(0, "身份重設不得破壞仍在執行的 request");

        lease.Dispose();

        resource.DisposeCount.Should().Be(1);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
        coordinator.GetOrCreateResourceScopeId(session).Should().NotBe(scope);
    }

    /// <summary>
    /// 重現舊 request 已取得 Session、但 Donation generation 尚在 factory 建立中的 logout 競爭。
    /// Session-bound acquire 必須從讀取／建立 opaque scope 一直到 lease publication 都持有同一 identity-reset stripe；
    /// 因此 logout drain 在 factory 放行前不得完成。放行後 drain 會看見剛發佈的世代並撤銷它，舊 request 仍靠 lease
    /// 完成，最後歸還才 Dispose。這避免 logout 已清 Session 後，遲到 request 又以舊 scope 建立 TTL 長壽命資源。
    /// </summary>
    [Fact]
    public async Task Identity_reset_waits_for_scope_bound_acquire_publication_before_draining()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        using var factoryEntered = new ManualResetEventSlim(false);
        using var allowFactoryToPublish = new ManualResetEventSlim(false);
        var session = new ConcurrentTestSession("synthetic-scope-publication-race-session");
        var httpContext = new DefaultHttpContext { Session = session };
        httpContext.Features.Set<IHttpResponseFeature>(new RecordingResponseFeature());
        var resource = new TrackingResource();

        var acquireTask = Task.Run(() => coordinator.AcquireForSessionRequest(
            httpContext,
            session,
            () =>
            {
                factoryEntered.Set();
                if (!allowFactoryToPublish.Wait(TimeSpan.FromSeconds(2)))
                {
                    throw new TimeoutException("測試未在期限內放行 Session resource factory。");
                }

                return resource;
            },
            TestExpiration,
            TestExpiration));

        factoryEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        var drainTask = Task.Run(() => coordinator.DrainSessionResourceScope(session));
        SpinWait.SpinUntil(() => drainTask.IsCompleted, TimeSpan.FromMilliseconds(100)).Should().BeFalse(
            "identity reset 必須等待舊 scope 的 generation/lease publication 線性化完成");

        allowFactoryToPublish.Set();
        var lease = await acquireTask;
        (await drainTask).Should().BeTrue();

        session.TryGetValue("_DonationPaymentResourceScopeId", out _).Should().BeFalse();
        resource.DisposeCount.Should().Be(0, "drain 不得中止已取得 lease 的舊 request");
        lease.Dispose();
        resource.DisposeCount.Should().Be(1);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 注入第一次 Dispose 失敗、第二次成功的資源，驗證 cleanup 例外不會讓 coordinator 假裝回到乾淨基準線。
    /// 最後 lease 歸還仍須傳回原始例外，但 entry 必須由 failed-cleanup owner 集合持續保留，Active 維持一；
    /// 後續 host Dispose 再取得唯一 retry owner，成功後才移除強參考並把 Active 歸零。總失敗計數保持一，
    /// 讓健康檢查能區分「最後已回收但曾發生故障」與從未故障的正常路徑。
    /// </summary>
    [Fact]
    public void Cleanup_failure_remains_owned_until_later_host_drain_retry_succeeds()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new SessionScopedResourceDisposalCoordinator<RetryableDisposeResource>(cache);
        var resource = new RetryableDisposeResource(failuresBeforeSuccess: 1);
        var lease = coordinator.Acquire(
            Scope('M'),
            () => resource,
            TestExpiration,
            TestExpiration);
        coordinator.EvictAndDrain(Scope('M')).Should().BeTrue();

        Action firstCleanup = lease.Dispose;
        firstCleanup.Should().Throw<InvalidOperationException>()
            .WithMessage("synthetic cleanup failure");

        resource.DisposeAttemptCount.Should().Be(1);
        coordinator.ActiveEntryCount.Should().Be(1, "cleanup 未成功前 owner 不得宣告資源已回到基準線");
        coordinator.OutstandingLeaseCount.Should().Be(0);
        coordinator.CleanupFailureCount.Should().Be(1);

        coordinator.Dispose();

        resource.DisposeAttemptCount.Should().Be(2);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.CleanupFailureCount.Should().Be(1);
        coordinator.Dispose();
        resource.DisposeAttemptCount.Should().Be(2, "成功 cleanup 後的重複 host Dispose 必須是冪等 no-op");
    }

    /// <summary>
    /// 將 host shutdown 精確插入 factory 已建立資源、但尚未 publish cache entry 的窗口。
    /// Coordinator 必須拒絕 publication 並清理該資源；若第一次 cleanup 失敗，仍要建立可追蹤 entry、Active 保持一、
    /// failure count 增加，讓第二次 host Dispose 可重試成功。這保護尚未進入 cache 的 LINE client／semaphore graph，
    /// 避免單純在例外路徑直接呼叫 Dispose 後失去 owner 參考。
    /// </summary>
    [Fact]
    public async Task Host_stop_during_factory_retains_failed_prepublication_cleanup_for_retry()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new SessionScopedResourceDisposalCoordinator<RetryableDisposeResource>(cache);
        using var factoryEntered = new ManualResetEventSlim(false);
        using var allowFactoryReturn = new ManualResetEventSlim(false);
        var resource = new RetryableDisposeResource(failuresBeforeSuccess: 1);

        var acquireTask = Task.Run(() => coordinator.Acquire(
            Scope('N'),
            () =>
            {
                factoryEntered.Set();
                if (!allowFactoryReturn.Wait(TimeSpan.FromSeconds(2)))
                {
                    throw new TimeoutException("測試未在期限內放行 pre-publication factory。");
                }

                return resource;
            },
            TestExpiration,
            TestExpiration));
        factoryEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        var firstHostDispose = Task.Run(coordinator.Dispose);
        SpinWait.SpinUntil(
                () => ReadCoordinatorDisposeState(coordinator) == 1,
                TimeSpan.FromSeconds(2))
            .Should().BeTrue("host Dispose 必須先原子終止新 publication");
        allowFactoryReturn.Set();

        Func<Task> awaitAcquire = async () => await acquireTask;
        await awaitAcquire.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("synthetic cleanup failure");
        await firstHostDispose;

        resource.DisposeAttemptCount.Should().Be(1);
        coordinator.ActiveEntryCount.Should().Be(1);
        coordinator.CleanupFailureCount.Should().Be(1);

        coordinator.Dispose();

        resource.DisposeAttemptCount.Should().Be(2);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 以 64 個 caller 同時歸還同一 lease，驗證 Interlocked gate 只扣一次 ref-count；
    /// drain 後資源只能 Dispose 一次，計數不得 underflow 或因重複 release 變成負值。
    /// </summary>
    [Fact]
    public void Concurrent_lease_dispose_releases_ref_count_once()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        var resource = new TrackingResource();
        var lease = coordinator.Acquire(Scope('J'), () => resource, TestExpiration, TestExpiration);

        coordinator.EvictAndDrain(Scope('J'));
        Parallel.For(0, 64, _ => lease.Dispose());

        resource.DisposeCount.Should().Be(1);
        coordinator.OutstandingLeaseCount.Should().Be(0);
        coordinator.ActiveEntryCount.Should().Be(0);
    }

    /// <summary>
    /// 同一 Session 首次沒有 Donation scope ID 時，以平行呼叫 coordinator 的正式建立方法注入競爭。
    /// 所有 caller 必須取得同一個 bounded opaque ID；再把該 ID 交給 fake-resource coordinator，factory 只能執行一次，
    /// 最後 drain/lease return 也只能 Dispose 一次。測試字串全為合成值，不含真實 Session 或使用者識別資料。
    /// </summary>
    [Fact]
    public void Concurrent_first_scope_id_creation_does_not_double_own_resource()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var donationCoordinator = new SessionScopedResourceDisposalCoordinator<DonationPaymentManager>(cache);
        var session = new ConcurrentTestSession("synthetic-lifecycle-session");
        var results = new ConcurrentBag<string>();

        results.Add(donationCoordinator.GetOrCreateResourceScopeId(session));
        Parallel.For(0, 64, _ =>
            results.Add(donationCoordinator.GetOrCreateResourceScopeId(session)));

        results.Should().OnlyContain(value => value.Length == 43);
        results.Distinct(StringComparer.Ordinal).Should().ContainSingle();

        using var resourceCoordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        var resource = new TrackingResource();
        var factoryCount = 0;
        var leases = Enumerable.Range(0, 64)
            .AsParallel()
            .Select(_ => resourceCoordinator.Acquire(
                results.First(),
                () =>
                {
                    Interlocked.Increment(ref factoryCount);
                    return resource;
                },
                TestExpiration,
                TestExpiration))
            .ToArray();

        factoryCount.Should().Be(1);
        resourceCoordinator.EvictAndDrain(results.First());
        Parallel.ForEach(leases, lease => lease.Dispose());
        resource.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// Coordinator 只接受固定長度 Base64Url opaque scope；若呼叫端誤把 Session/user/fingerprint 組合字串直接送入，
    /// 必須在 factory 執行前 fail closed，避免可識別資料進入 singleton dictionary 或 memory-cache key。
    /// </summary>
    [Fact]
    public void Direct_session_component_key_is_rejected_before_factory_runs()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new SessionScopedResourceDisposalCoordinator<TrackingResource>(cache);
        var factoryCount = 0;

        Action acquire = () => coordinator.Acquire(
            "session-user-fingerprint-timestamp",
            () =>
            {
                Interlocked.Increment(ref factoryCount);
                return new TrackingResource();
            },
            TestExpiration,
            TestExpiration);

        acquire.Should().Throw<ArgumentException>();
        factoryCount.Should().Be(0);
        coordinator.ActiveEntryCount.Should().Be(0);
        coordinator.OutstandingLeaseCount.Should().Be(0);
    }

    private static int ReadDisposeState(DonationPaymentManager manager)
    {
        var field = typeof(DonationPaymentManager).GetField(
            "_disposeState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (int)field!.GetValue(manager)!;
    }

    /// <summary>
    /// 只讀取 coordinator 的 host terminal sentinel，以固定 factory/shutdown 測試時序；不修改 private state，
    /// 不繞過 production lock，也不把 Session/resource key 暴露到測試輸出。
    /// </summary>
    private static int ReadCoordinatorDisposeState<TResource>(
        SessionScopedResourceDisposalCoordinator<TResource> coordinator)
        where TResource : class, IDisposable
    {
        var field = typeof(SessionScopedResourceDisposalCoordinator<TResource>).GetField(
            "_disposeState",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Session resource coordinator 缺少 host terminal sentinel。");
        return (int)(field.GetValue(coordinator) ?? 0);
    }

    /// <summary>
    /// 使用唯一暫存目錄中的空 JSON 初始化 legacy static ConfigurationBuilder，再建立不執行 instance 建構式的 Manager。
    /// 空組態不含 credential、token、endpoint 或真實環境資料；working directory 在 finally 立即還原，檔案與目錄也確定性刪除。
    /// Collection 禁止平行執行，避免 process-global current directory 在切換期間影響其他測試。
    /// </summary>
    private static DonationPaymentManager CreateUninitializedDonationPaymentManager()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var temporaryDirectory = Directory.CreateTempSubdirectory("churchreport-session-lifecycle-");
        var configurationPath = Path.Combine(temporaryDirectory.FullName, "appsettings.json");

        try
        {
            File.WriteAllText(configurationPath, "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Directory.SetCurrentDirectory(temporaryDirectory.FullName);
            RuntimeHelpers.RunClassConstructor(typeof(DonationPaymentManager).TypeHandle);
            return (DonationPaymentManager)RuntimeHelpers.GetUninitializedObject(
                typeof(UninitializedDonationPaymentManager));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            if (File.Exists(configurationPath))
            {
                File.Delete(configurationPath);
            }

            temporaryDirectory.Delete();
        }
    }

    /// <summary>
    /// 只用於略過 legacy DonationPaymentManager 建構式與其 appsettings/LINE/CRM 邊界的真實衍生 instance。
    /// RuntimeHelpers 不執行此型別或基底建構式；context 與 coordinator 仍以 production DonationPaymentManager 合約處理它，
    /// 最終 Dispose 也執行 production 實作與 sentinel。測試不新增 production 測試入口、不讀 credential，也不建立網路資源。
    /// </summary>
    private sealed class UninitializedDonationPaymentManager : DonationPaymentManager
    {
    }

    /// <summary>
    /// 可觀測的 session-owned 測試資源。<see cref="Use"/> 在 Dispose 後立即失敗，用來證明 drain 期間沒有提前清理；
    /// Dispose 計數以 Interlocked 維持競爭下的可靠性，讓 callback 與 explicit eviction 的重複釋放不會被測試 race 掩蓋。
    /// </summary>
    private sealed class TrackingResource : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public int Use()
        {
            ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
            return 1;
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
        }
    }

    /// <summary>
    /// 前 N 次 Dispose 會同步失敗、之後成功的可重試測試資源。計數使用 Interlocked，讓測試能證明
    /// coordinator 在 cleanup failure 後仍保有唯一 owner，且不會由 callback、lease 與 host drain 同時重試。
    /// 此替身不持有外部 handle；它只模擬真實 HttpClient/handler/semaphore cleanup 可能丟例外的控制流程。
    /// </summary>
    private sealed class RetryableDisposeResource : IDisposable
    {
        private readonly int _failuresBeforeSuccess;
        private int _disposeAttemptCount;

        public RetryableDisposeResource(int failuresBeforeSuccess)
        {
            if (failuresBeforeSuccess < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(failuresBeforeSuccess));
            }

            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        /// <summary>
        /// 取得已執行的 cleanup 嘗試次數；不代表成功次數，供測試驗證 retry ownership。
        /// </summary>
        public int DisposeAttemptCount => Volatile.Read(ref _disposeAttemptCount);

        /// <summary>
        /// 前置故障次數內拋出固定、無敏感資料的例外；超過後視為成功。方法不等待、不配置背景工作。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposeAttemptCount) <= _failuresBeforeSuccess)
            {
                throw new InvalidOperationException("synthetic cleanup failure");
            }
        }
    }

    /// <summary>
    /// 包裝真實 <see cref="MemoryCache"/>，只在第一次 <see cref="Remove"/> 進入時提供可控制的競爭閘門。
    /// 其餘 CreateEntry／TryGetValue 行為完全委派給框架實作，避免測試以不真實的 cache 語意製造假陽性。
    /// 閘門有兩秒上限並在 Dispose 時強制放行，確保測試失敗或取消也不會遺留背景工作、wait handle 或未完成 Task。
    /// </summary>
    private sealed class BlockingFirstRemoveMemoryCache : IMemoryCache
    {
        private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(2);
        private readonly MemoryCache _inner = new(new MemoryCacheOptions());
        private readonly ManualResetEventSlim _firstRemoveEntered = new(false);
        private readonly ManualResetEventSlim _allowFirstRemove = new(false);
        private int _blockFirstRemove = 1;
        private int _disposeState;

        /// <summary>
        /// 等待舊 drain 已到達可觀測的線性化結果：修正後會直接完成 no-op；舊缺陷則會停在第一次 Remove。
        /// 同時接受這兩種狀態，才能讓同一個回歸測試先穩定呈現 RED，再在移除錯誤 Remove 後呈現 GREEN；
        /// 逾時立即失敗，避免 production 路徑卡死時讓測試執行緒無界等待。
        /// </summary>
        public void WaitUntilDrainHasLinearized(Task staleDrain)
        {
            ArgumentNullException.ThrowIfNull(staleDrain);
            SpinWait.SpinUntil(
                    () => staleDrain.IsCompleted || _firstRemoveEntered.IsSet,
                    GateTimeout)
                .Should().BeTrue(
                    "舊 drain 必須直接完成 no-op，或到達受控的 cache Remove 邊界");
        }

        /// <summary>
        /// 放行第一次 Remove；重複呼叫安全，且不會影響後續正常 eviction。
        /// </summary>
        public void ReleaseFirstRemove() => _allowFirstRemove.Set();

        /// <summary>
        /// 將讀取完整委派給真實 cache；不加鎖、不複製 value，確保 production coordinator 觀察框架原生可見性語意。
        /// </summary>
        public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);

        /// <summary>
        /// 建立由內部 MemoryCache 唯一擁有的 entry；測試替身不保留 entry 或 callback 的額外強參考。
        /// </summary>
        public ICacheEntry CreateEntry(object key) => _inner.CreateEntry(key);

        /// <summary>
        /// 第一次 Remove 在委派給真實 cache 前暫停，讓測試能在同一 key 發佈稍後的新世代；
        /// timeout 採 fail-closed，若測試端沒有放行便拋出，而不是永久佔住測試執行緒。
        /// </summary>
        public void Remove(object key)
        {
            if (Interlocked.Exchange(ref _blockFirstRemove, 0) == 1)
            {
                _firstRemoveEntered.Set();
                if (!_allowFirstRemove.Wait(GateTimeout))
                {
                    throw new TimeoutException("測試未在期限內放行第一次 cache Remove。");
                }
            }

            _inner.Remove(key);
        }

        /// <summary>
        /// 本測試替身是其 wait handles 與內部 MemoryCache 的唯一 owner。先放行等待者，再依逆序清理 cache 與閘門；
        /// Interlocked 讓 xUnit 清理與顯式 Dispose 競爭時只執行一次，避免 ObjectDisposedException 掩蓋主要 assertion。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            _allowFirstRemove.Set();
            _inner.Dispose();
            _allowFirstRemove.Dispose();
            _firstRemoveEntered.Dispose();
        }
    }

    /// <summary>
    /// 最小但具真實 ICacheEntry 契約的記憶體 cache，用來把「移除可見性」與「執行 eviction callback」拆成兩個可控制步驟。
    /// Production 的 MemoryCache 本來就允許 callback 稍後在線程池執行；此替身不改變 key/value 可見性，只延後 callback，
    /// 讓 stale-entry 競爭能百分之百重現。所有 entry、callback 與鎖都由本 instance 唯一擁有，Dispose 時會先撤銷可見性、
    /// 再執行尚未送出的 callbacks，最後清空集合，避免測試留下 coordinator 或 resource 的強參考。
    /// </summary>
    private sealed class DeferredEvictionMemoryCache : IMemoryCache
    {
        private readonly object _gate = new();
        private readonly Dictionary<object, DeferredCacheEntry> _visibleEntries = new();
        private readonly Queue<DeferredCacheEntry> _deferredEvictions = new();
        private int _disposeState;

        /// <summary>
        /// 讀取目前仍可見的 entry。測試不模擬 TTL 時鐘，因為故障注入只關注 Remove 與 callback 的先後關係；
        /// lock 讓 acquire 執行緒與測試控制執行緒看到一致的 publication 邊界。
        /// </summary>
        public bool TryGetValue(object key, out object? value)
        {
            lock (_gate)
            {
                if (_visibleEntries.TryGetValue(key, out var entry))
                {
                    value = entry.Value;
                    return true;
                }

                value = null;
                return false;
            }
        }

        /// <summary>
        /// 建立尚未 publish 的 entry；只有 caller Dispose entry 時才會在同一把 gate 下原子發佈，
        /// 對應 MemoryCache.Set extension 的正式 CreateEntry／Dispose 生命週期。
        /// </summary>
        public ICacheEntry CreateEntry(object key)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            return new DeferredCacheEntry(this, key);
        }

        /// <summary>
        /// 撤銷指定 key 的可見性並把 callback 排入延後佇列。此方法不執行 callback，精確模擬框架 callback
        /// 已排程但尚未執行的窗口；若 key 不存在則為冪等 no-op，不建立額外狀態。
        /// </summary>
        public void Remove(object key)
        {
            lock (_gate)
            {
                if (_visibleEntries.Remove(key, out var removed))
                {
                    _deferredEvictions.Enqueue(removed);
                }
            }
        }

        /// <summary>
        /// 撤銷目前唯一可見 entry 而不執行 callback。測試只建立單一 scope；若沒有恰好一個 entry，
        /// 立即 fail closed，避免錯誤的測試排列掩蓋 production 競爭。
        /// </summary>
        public void EvictCurrentEntryWithoutInvokingCallbacks()
        {
            object key;
            lock (_gate)
            {
                if (_visibleEntries.Count != 1)
                {
                    throw new InvalidOperationException("測試預期恰好一個可見 cache entry。");
                }

                key = _visibleEntries.Keys.Single();
            }

            Remove(key);
        }

        /// <summary>
        /// 在不持有 cache gate 時依 FIFO 執行先前延後的 callbacks，避免 callback 重新進入 coordinator／cache 時死鎖。
        /// callback state、key 與 value 都來自 production 註冊內容；例外直接傳回測試，不會被替身吞掉。
        /// </summary>
        public void InvokeDeferredCallbacks()
        {
            while (true)
            {
                DeferredCacheEntry? entry;
                lock (_gate)
                {
                    entry = _deferredEvictions.Count == 0 ? null : _deferredEvictions.Dequeue();
                }

                if (entry == null)
                {
                    return;
                }

                entry.InvokeCallbacks(EvictionReason.Removed);
            }
        }

        /// <summary>
        /// 發佈已完成設定的 entry。若同 key 已有可見 entry，先撤銷舊 entry 並延後其 callback，
        /// 對應真實 MemoryCache.Set 的 replacement 語意；新 entry 隨後成為唯一可見值。
        /// </summary>
        private void Publish(DeferredCacheEntry entry)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposeState != 0, this);
                if (_visibleEntries.Remove(entry.Key, out var replaced))
                {
                    _deferredEvictions.Enqueue(replaced);
                }

                _visibleEntries.Add(entry.Key, entry);
            }
        }

        /// <summary>
        /// 本替身的唯一 cleanup owner。先將所有可見 entry 移至延後佇列，再於 gate 外送出 callbacks；
        /// 最後清空集合。Interlocked 保證 xUnit 與顯式清理競爭時只執行一次，不保留 Timer、Task 或 wait handle。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            lock (_gate)
            {
                foreach (var entry in _visibleEntries.Values)
                {
                    _deferredEvictions.Enqueue(entry);
                }

                _visibleEntries.Clear();
            }

            InvokeDeferredCallbacks();
        }

        /// <summary>
        /// 保存一次 CreateEntry 到 Dispose publication 期間的 bounded 設定。它不建立 timer，也不自行執行 callback；
        /// owner cache 在 replacement/remove/dispose 時決定 callback 時序。所有集合均為測試框架要求的可變集合，
        /// 最長只存活於單一測試方法，不包含 Session、Credential、Token 或真實使用者資料。
        /// </summary>
        private sealed class DeferredCacheEntry : ICacheEntry
        {
            private readonly DeferredEvictionMemoryCache _owner;
            private int _publishState;

            public DeferredCacheEntry(DeferredEvictionMemoryCache owner, object key)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                Key = key ?? throw new ArgumentNullException(nameof(key));
            }

            public object Key { get; }

            public object? Value { get; set; }

            public DateTimeOffset? AbsoluteExpiration { get; set; }

            public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }

            public TimeSpan? SlidingExpiration { get; set; }

            public IList<IChangeToken> ExpirationTokens { get; } = new List<IChangeToken>();

            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } =
                new List<PostEvictionCallbackRegistration>();

            public CacheItemPriority Priority { get; set; }

            public long? Size { get; set; }

            /// <summary>
            /// 第一次 Dispose 將 entry 原子發佈給 owner；重複 Dispose 為 no-op，避免測試替身製造重複 replacement callback。
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _publishState, 1) == 0)
                {
                    _owner.Publish(this);
                }
            }

            /// <summary>
            /// 依註冊順序同步送出 callbacks；不持有 owner gate，因此 callback 可安全查詢或修改 coordinator/cache。
            /// 每個 registration 的 state 由 production options 建立，本方法不額外捕捉或保存 request graph。
            /// </summary>
            public void InvokeCallbacks(EvictionReason reason)
            {
                foreach (var registration in PostEvictionCallbacks)
                {
                    registration.EvictionCallback?.Invoke(Key, Value, reason, registration.State);
                }
            }
        }
    }

    /// <summary>
    /// 模擬 ASP.NET server 在 response 完成時依反向註冊順序執行 <see cref="IHttpResponseFeature.OnCompleted"/>。
    /// <see cref="DefaultHttpContext"/> 單獨存在時沒有 Kestrel/TestServer 觸發這些 callbacks，因此測試 feature 只補上 server lifecycle，
    /// 不替 production coordinator 直接呼叫 Dispose，也不保存 Session、使用者資料或外部資源。
    /// </summary>
    private sealed class RecordingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _completedCallbacks = new();

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state)
        {
            ArgumentNullException.ThrowIfNull(callback);
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _completedCallbacks.Add((callback, state));
        }

        public async Task CompleteResponseAsync()
        {
            HasStarted = true;
            for (var index = _completedCallbacks.Count - 1; index >= 0; index--)
            {
                var registration = _completedCallbacks[index];
                await registration.Callback(registration.State);
            }
        }
    }

    /// <summary>
    /// Thread-safe 的合成 Session；只保存本測試建立的 opaque scope ID，不模擬 credential、token 或真實 identity。
    /// ConcurrentDictionary 讓 64 個首次存取 caller 的 Get/Set 競爭可重現，而不由測試替 production 額外加鎖。
    /// </summary>
    private sealed class ConcurrentTestSession : ISession
    {
        private readonly ConcurrentDictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public ConcurrentTestSession(string id)
        {
            Id = id;
        }

        public bool IsAvailable => true;

        public string Id { get; }

        public IEnumerable<string> Keys => _values.Keys;

        public void Clear() => _values.Clear();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(string key) => _values.TryRemove(key, out _);

        public void Set(string key, byte[] value) => _values[key] = value.ToArray();

        public bool TryGetValue(string key, out byte[] value)
        {
            if (_values.TryGetValue(key, out var stored))
            {
                value = stored.ToArray();
                return true;
            }

            value = Array.Empty<byte>();
            return false;
        }
    }

    /// <summary>
    /// 防止 opaque scope ID 測試越界碰觸 CRM Factory；任何意外存取都立即失敗，不建立外部連線。
    /// </summary>
    private sealed class ThrowingToolUtilityProvider : IToolUtilityProvider
    {
        public ToolUtilityClass GetToolUtility()
        {
            throw new InvalidOperationException("Session lifecycle test 不得建立 CRM ToolUtility。");
        }
    }
}
