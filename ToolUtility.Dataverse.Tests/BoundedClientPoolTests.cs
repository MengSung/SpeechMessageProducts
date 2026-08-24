using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using PowerPlatform.Dataverse.Client;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using Xunit;

namespace ToolUtility.Dataverse.Tests;

/// <summary>
/// 驗證 keyed bounded pool 的租約互斥、故障淘汰、分池、逾時與冪等釋放契約。
/// 每個測試只使用假的 <see cref="IOrganizationService"/>，不連線真實 Dataverse。
/// </summary>
public sealed class BoundedClientPoolTests
{
    private static readonly DataverseConnectionKey DefaultKey = new(
        "ChurchReport", "Test", "https://example.test/jesus", "service-account");

    /// <summary>
    /// 驗證同一個池的 client 在第一條租約尚未歸還前，不會被第二條租約同時取得。
    /// </summary>
    [Fact]
    public async Task Acquire_does_not_return_same_client_to_parallel_lease()
    {
        var service = new Mock<IOrganizationService>(MockBehavior.Loose).Object;
        using var pool = CreatePool(
            (_, _) => service,
            new DataversePoolOptions
            {
                MinSize = 1,
                MaxN = 1,
                AcquireTimeout = TimeSpan.FromMilliseconds(50)
            });

        using var first = pool.Acquire(DefaultKey);
        Assert.Same(service, first.Service);

        var timeout = await Task.Run(() => Assert.Throws<TimeoutException>(() => pool.Acquire(DefaultKey)));
        Assert.Contains("50", timeout.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 驗證標記故障的租約歸還後會銷毀 client，不會重新加入 idle 集合。
    /// </summary>
    [Fact]
    public void Faulted_client_is_discarded_instead_of_returned_to_pool()
    {
        var created = 0;
        var services = new List<IOrganizationService>();
        using var pool = CreatePool(
            (_, _) =>
            {
                var service = new Mock<IOrganizationService>(MockBehavior.Loose).Object;
                services.Add(service);
                Interlocked.Increment(ref created);
                return service;
            },
            new DataversePoolOptions { MinSize = 1, MaxN = 1 });

        using (var lease = pool.Acquire(DefaultKey))
        {
            lease.MarkFaulted();
        }

        var afterFault = pool.GetMetrics();
        Assert.Equal(0, afterFault.Idle);
        Assert.Equal(0, afterFault.Leased);
        Assert.Equal(1, afterFault.Faulted);
        Assert.Equal(1, afterFault.Discarded);

        using var replacement = pool.Acquire(DefaultKey);
        Assert.Equal(2, created);
        Assert.NotSame(services[0], replacement.Service);
    }

    /// <summary>
    /// 驗證不同 Pool Key 會隔離子池，相同 key 則可重用同一個健康 client。
    /// </summary>
    [Fact]
    public void Pool_key_partitions_subpools_and_reuses_within_same_key()
    {
        var services = new ConcurrentDictionary<DataverseConnectionKey, IOrganizationService>();
        using var pool = CreatePool(
            (key, _) => services.GetOrAdd(key, _ => new Mock<IOrganizationService>(MockBehavior.Loose).Object),
            new DataversePoolOptions { MinSize = 1, MaxN = 2 });
        var secondKey = new DataverseConnectionKey(
            "ChurchReport", "Test", "https://other.test/jesus", "service-account");

        IOrganizationService firstService;
        using (var first = pool.Acquire(DefaultKey))
        {
            firstService = first.Service;
        }

        using var sameKey = pool.Acquire(DefaultKey);
        using var otherKey = pool.Acquire(secondKey);

        Assert.Same(firstService, sameKey.Service);
        Assert.NotSame(sameKey.Service, otherKey.Service);
        Assert.Equal(2, pool.GetMetrics().SubPoolCount);
    }

    /// <summary>
    /// 驗證超過 MaxN 時會在 AcquireTimeout 內擲出 TimeoutException，並累計 Metrics。
    /// </summary>
    [Fact]
    public void Acquire_timeout_is_counted_when_pool_is_at_capacity()
    {
        using var pool = CreatePool(
            (_, _) => new Mock<IOrganizationService>(MockBehavior.Loose).Object,
            new DataversePoolOptions
            {
                MinSize = 1,
                MaxN = 1,
                AcquireTimeout = TimeSpan.FromMilliseconds(40)
            });

        using var lease = pool.Acquire(DefaultKey);
        Assert.Throws<TimeoutException>(() => pool.Acquire(DefaultKey));

        var metrics = pool.GetMetrics();
        Assert.Equal(1, metrics.AcquireTimeouts);
        Assert.Equal(0, metrics.Waiting);
    }

    /// <summary>
    /// 驗證同一租約重複 Dispose 不會重複歸還 semaphore 或造成例外。
    /// </summary>
    [Fact]
    public void Lease_dispose_is_idempotent_and_returns_once()
    {
        using var pool = CreatePool(
            (_, _) => new Mock<IOrganizationService>(MockBehavior.Loose).Object,
            new DataversePoolOptions { MinSize = 1, MaxN = 1 });

        var lease = pool.Acquire(DefaultKey);
        lease.Dispose();
        lease.Dispose();

        var metrics = pool.GetMetrics();
        Assert.Equal(1, metrics.Idle);
        Assert.Equal(0, metrics.Leased);
        Assert.Equal(1, metrics.TotalReleases);
    }

    /// <summary>
    /// 保護 pooled OnPremiseClient 歸還前清除 CallerId 的隔離契約；先注入非空
    /// impersonation 身分，再借還一次，決定性斷言是下一條 lease 觀察到 Guid.Empty。
    /// </summary>
    [Fact]
    public void Returning_on_premise_client_clears_caller_id_before_idle()
    {
        var service = (OnPremiseClient)RuntimeHelpers.GetUninitializedObject(typeof(OnPremiseClient));
        using var pool = CreatePool(
            (_, _) => service,
            new DataversePoolOptions { MinSize = 1, MaxN = 1 });

        using (var lease = pool.Acquire(DefaultKey))
        {
            service.CallerId = Guid.NewGuid();
        }

        using var nextLease = pool.Acquire(DefaultKey);
        Assert.Equal(Guid.Empty, service.CallerId);
    }

    /// <summary>
    /// 保護 cleanup 選取 idle client 後、真正 Dispose 前若被重新租借的競態契約。
    /// 故障注入是可控的 cleanup callback；決定性斷言是 lease 仍可取得且 service 在 lease 期間
    /// 尚未 Dispose，歸還後才確定性淘汰，避免 cleanup 銷毀正交給呼叫端的 client。
    /// </summary>
    [Fact]
    public void Cleanup_does_not_dispose_client_leased_after_selection()
    {
        var services = new List<TrackingOrganizationService>();
        BoundedClientPool? pool = null;
        IClientLease? racedLease = null;
        pool = CreatePool(
            (_, _) =>
            {
                var service = new TrackingOrganizationService();
                services.Add(service);
                return service;
            },
            new DataversePoolOptions
            {
                MinSize = 1,
                MaxN = 2,
                IdleTimeout = TimeSpan.FromMilliseconds(1)
            },
            _ => racedLease ??= pool!.Acquire(DefaultKey));

        var initialLeases = new[] { pool.Acquire(DefaultKey), pool.Acquire(DefaultKey) };
        foreach (var lease in initialLeases)
            lease.Dispose();

        Thread.Sleep(20);
        pool.CleanupIdleClients();

        Assert.NotNull(racedLease);
        var leaseAcquiredDuringCleanup = racedLease!;
        var serviceAcquiredDuringCleanup = Assert.IsType<TrackingOrganizationService>(leaseAcquiredDuringCleanup.Service);
        Assert.False(serviceAcquiredDuringCleanup.IsDisposed);
        leaseAcquiredDuringCleanup.Service.Execute(new OrganizationRequest());
        leaseAcquiredDuringCleanup.Dispose();
        Assert.True(serviceAcquiredDuringCleanup.IsDisposed);
    }

    /// <summary>
    /// 保護 MinSize 保底：五條 idle 全部逾時時，cleanup 只能逐一淘汰到兩條，
    /// 不得因固定 idleCount 導致子池跌到零。斷言直接讀取 Idle metrics。
    /// </summary>
    [Fact]
    public void Cleanup_keeps_idle_count_at_min_size()
    {
        using var pool = CreatePool(
            (_, _) => new TrackingOrganizationService(),
            new DataversePoolOptions
            {
                MinSize = 2,
                MaxN = 5,
                IdleTimeout = TimeSpan.FromMilliseconds(1)
            });

        var leases = new[]
        {
            pool.Acquire(DefaultKey),
            pool.Acquire(DefaultKey),
            pool.Acquire(DefaultKey),
            pool.Acquire(DefaultKey),
            pool.Acquire(DefaultKey)
        };
        foreach (var lease in leases)
            lease.Dispose();

        Thread.Sleep(20);
        pool.CleanupIdleClients();

        Assert.Equal(2, pool.GetMetrics().Idle);
    }

    /// <summary>
    /// 保護 URL 組態不可靜默切換環境的契約；缺少 ServerUrl 時必須在 Manager 建構時
    /// 明確失敗，而不是回退到另一個產品環境的硬編碼端點。
    /// </summary>
    [Fact]
    public void Manager_rejects_missing_server_url_instead_of_using_environment_fallback()
    {
        var configuration = CreateManagerConfiguration(serverUrl: null, username: "service-user");
        var exception = Record.Exception(() =>
        {
            using var manager = new DataverseConnectionManager(
                new Mock<ICrmConnectionService>(MockBehavior.Strict).Object,
                configuration,
                "ChurchReport",
                "Test",
                new DataversePoolOptions { MinSize = 1, MaxN = 1 });
        });

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("CrmConnection:ServerUrl", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 保護有效身分不可被靜默替換的契約；缺少 Username 時必須明確拒絕，
    /// 避免以 service-account 連到未授權的環境或留下不可追蹤的身分漂移。
    /// </summary>
    [Fact]
    public void Manager_rejects_missing_username_instead_of_using_service_account_fallback()
    {
        var configuration = CreateManagerConfiguration(
            serverUrl: "https://org.test/XRMServices/2011/Organization.svc",
            username: null);
        var exception = Record.Exception(() =>
        {
            using var manager = new DataverseConnectionManager(
                new Mock<ICrmConnectionService>(MockBehavior.Strict).Object,
                configuration,
                "ChurchReport",
                "Test",
                new DataversePoolOptions { MinSize = 1, MaxN = 1 });
        });

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("CrmConnection:Username", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 釘住「建線不得在子池鎖內執行」的契約。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 斷言的是 factory 的<b>同時進行數</b>而非牆鐘時間，因此結果是決定性的、不受機器負載影響：
    /// 只要建線仍在 <c>lock (subPool.Sync)</c> 內，同時進行數在定義上就恆為 1。
    /// </para>
    /// <para>
    /// 這段行為的實測後果是三個同時到達的 request 以約 21 秒等差依序失敗（21.9 / 41.6 / 61.3 秒），
    /// 因此本測試守的是延遲不會隨併發數線性放大的性質。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Client_creation_runs_outside_the_subpool_lock()
    {
        const int threads = 3;
        var concurrent = 0;
        var maxConcurrent = 0;
        var totalCreated = 0;
        var ready = new Barrier(threads);

        using var pool = CreatePool(
            (_, _) =>
            {
                var now = Interlocked.Increment(ref concurrent);
                InterlockedMax(ref maxConcurrent, now);
                Interlocked.Increment(ref totalCreated);
                Thread.Sleep(150);
                Interlocked.Decrement(ref concurrent);
                return new Mock<IOrganizationService>(MockBehavior.Loose).Object;
            },
            new DataversePoolOptions { MinSize = 3, MaxN = 8, AcquireTimeout = TimeSpan.FromSeconds(30) });

        var leases = await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            ready.SignalAndWait();
            return pool.Acquire(DefaultKey);
        })));

        try
        {
            // 核心斷言：建線若仍在鎖內，這個值在定義上恆為 1。
            Assert.True(maxConcurrent > 1, $"建線仍被序列化，同時進行數只有 {maxConcurrent}。");

            // Pending 名額保留必須生效：否則三個執行緒會各自補一次 MinSize，建出 9 條連線。
            Assert.True(totalCreated <= _options_MinSizePlusOverflow(threads),
                $"建立了 {totalCreated} 條連線，超過保留名額應允許的上限。");

            // 隔離契約不得因此鬆動：每條租約仍必須是不同的 client。
            Assert.Equal(threads, leases.Select(lease => lease.Service).Distinct().Count());
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    /// <summary>MinSize 補足 3 條，另外 threads-1 個執行緒最多各自補建一條 overflow。</summary>
    private static int _options_MinSizePlusOverflow(int threads) => 3 + (threads - 1);

    /// <summary>以 CAS 迴圈記錄觀測到的最大值，避免讀改寫競態低估同時進行數。</summary>
    private static void InterlockedMax(ref int target, int candidate)
    {
        int observed;
        while (candidate > (observed = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, candidate, observed) == observed)
                return;
        }
    }

    private static BoundedClientPool CreatePool(
        Func<DataverseConnectionKey, CancellationToken, IOrganizationService> factory,
        DataversePoolOptions options,
        Action<IOrganizationService>? beforeDispose = null)
    {
        return new BoundedClientPool(factory, _ => true, options, beforeDispose);
    }

    private static IConfiguration CreateManagerConfiguration(string? serverUrl, string? username)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmConnection:ServerUrl"] = serverUrl,
                ["CrmConnection:Username"] = username,
                ["CrmConnection:Password"] = "test-secret"
            })
            .Build();
    }

    /// <summary>
    /// 可觀測的測試 CRM service；Dispose 狀態代表 cleanup 是否錯誤銷毀仍在使用的 client。
    /// Execute 保持可呼叫，讓競態測試能證明 lease 仍然可用，而不是只觀察旗標。
    /// </summary>
    private sealed class TrackingOrganizationService : IOrganizationService, IDisposable
    {
        /// <summary>
        /// 取得底層資源是否已由 pool 的淘汰或關閉路徑釋放；競態測試以此確認
        /// cleanup 不會提前銷毀已成功租借給另一個 request 的 service。
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>此測試 double 不保存關聯資料，因為測試的唯一契約是 lease 可安全使用。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { }

        /// <summary>回傳新的識別值，避免測試因建立作業的非核心行為而阻塞。</summary>
        public Guid Create(Entity entity) => Guid.NewGuid();

        /// <summary>此測試不持久化實體，因此刪除作業不保留任何跨測試狀態。</summary>
        public void Delete(string entityName, Guid id) { }

        /// <summary>此測試 double 不保存關聯資料，避免引入與 cleanup 競態無關的可變狀態。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { }

        /// <summary>
        /// 在尚未釋放時回傳空回應；若 pool 錯誤提前釋放，明確擲出例外，
        /// 使競態測試能以真實服務呼叫證明 lease 仍可用。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(TrackingOrganizationService));
            return new OrganizationResponse();
        }

        /// <summary>回傳空實體，因本測試不驗證資料存取內容。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => new();

        /// <summary>回傳空集合，確保 double 不快取任何 request 或使用者資料。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => new();

        /// <summary>此測試不持久化實體，因此更新作業不保留任何跨測試狀態。</summary>
        public void Update(Entity entity) { }

        /// <summary>標記資源已釋放，供競態測試驗證 pool 的唯一 Dispose 所有權。</summary>
        public void Dispose() => IsDisposed = true;
    }
}
