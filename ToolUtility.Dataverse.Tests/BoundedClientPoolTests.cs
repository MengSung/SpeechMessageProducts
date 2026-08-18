using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Moq;
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

    private static BoundedClientPool CreatePool(
        Func<DataverseConnectionKey, CancellationToken, IOrganizationService> factory,
        DataversePoolOptions options)
    {
        return new BoundedClientPool(factory, _ => true, options);
    }
}
