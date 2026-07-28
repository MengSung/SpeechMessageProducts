// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs
// 目的：驗證 bounded admission、workload cap、permit 釋放、host slot 上限。
//
// 保母教學：
// - 這些測試不連真實 CRM。
// - 重點是「不會無限排隊、不會 slot 洩漏、不會被單一 workload 塞爆」。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class OrganizationAdmissionManagerTests
{
    [Fact]
    public void Plan_derives_local_max_in_flight()
    {
        var options = new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "9.1",
            MaxConnectionsPerServer = 2,
            Admission = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                AggregateMaxInFlight = 24,
                MaximumRuntimeHosts = 6,
                AdmissionNamespaceId = "a",
                LeaseNamespaceId = "b"
            }
        };

        OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out var error)
            .Should().BeTrue(error?.ErrorMessage);
        plan!.LocalMaxInFlight.Should().Be(4);
    }

    [Fact]
    public async Task Queue_full_is_rejected_without_unbounded_growth()
    {
        await using var manager = CreateManager(
            aggregate: 2,
            hosts: 2, // LocalMaxInFlight = 1
            queueCapacity: 1,
            perWorkload: 10,
            timeoutSeconds: 1);

        var first = await manager.AcquireAsync(CreateEnvelope("w1"), CancellationToken.None);
        first.Succeeded.Should().BeTrue();

        // second waits in queue (capacity 1)
        using var secondCts = new CancellationTokenSource();
        var secondTask = manager.AcquireAsync(CreateEnvelope("w2"), secondCts.Token);

        // give second a moment to enter queue
        await Task.Delay(50);

        var third = await manager.AcquireAsync(CreateEnvelope("w3"), CancellationToken.None);
        third.Succeeded.Should().BeFalse();
        third.Error!.ErrorCode.Should().Be(DynamicsErrorCodes.QueueFull);

        // cleanup
        secondCts.Cancel();
        var second = await secondTask;
        second.Succeeded.Should().BeFalse();
        await first.Permit!.DisposeAsync();

        var snap = manager.GetSnapshot();
        snap.InFlight.Should().Be(0);
        snap.Queued.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_burst_is_limited_to_local_queue_capacity_and_releases_all_reservations()
    {
        const int queueCapacity = 3;
        const int burstSize = 32;

        await using var manager = CreateManager(
            aggregate: 1,
            hosts: 1, // LocalMaxInFlight = 1
            queueCapacity: queueCapacity,
            perWorkload: burstSize + 1,
            timeoutSeconds: 30);

        var holder = await manager.AcquireAsync(CreateEnvelope("holder"), CancellationToken.None);
        holder.Succeeded.Should().BeTrue();

        using var burstCts = new CancellationTokenSource();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allWorkersReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyWorkers = 0;

        var burstTasks = Enumerable.Range(0, burstSize)
            .Select(index => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref readyWorkers) == burstSize)
                {
                    allWorkersReady.TrySetResult();
                }

                await start.Task;
                return await manager.AcquireAsync(CreateEnvelope($"burst-{index}"), burstCts.Token);
            }))
            .ToArray();

        await allWorkersReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        start.TrySetResult();

        var completionDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (burstTasks.Count(task => task.IsCompleted) < burstSize - queueCapacity)
        {
            if (DateTimeOffset.UtcNow >= completionDeadline)
            {
                throw new TimeoutException("The burst did not reject excess work before the queue deadline.");
            }

            await Task.Delay(10);
        }

        var waitingCount = burstTasks.Count(task => !task.IsCompleted);
        waitingCount.Should().Be(queueCapacity);
        manager.GetSnapshot().Queued.Should().Be(queueCapacity);

        var rejectedBeforeCancellation = burstTasks
            .Where(task => task.IsCompleted)
            .Select(task => task.GetAwaiter().GetResult())
            .ToArray();
        rejectedBeforeCancellation.Should().OnlyContain(result =>
            !result.Succeeded && result.Error!.ErrorCode == DynamicsErrorCodes.QueueFull);

        burstCts.Cancel();
        var burstResults = await Task.WhenAll(burstTasks);
        burstResults.Count(result => !result.Succeeded && result.Error!.ErrorCode == DynamicsErrorCodes.QueueFull)
            .Should().Be(burstSize - queueCapacity);
        burstResults.Count(result => !result.Succeeded && result.Error!.ErrorCode == DynamicsErrorCodes.AdmissionTimeout)
            .Should().Be(queueCapacity);

        foreach (var result in burstResults.Where(result => result.Succeeded))
        {
            await result.Permit!.DisposeAsync();
        }

        await holder.Permit!.DisposeAsync();

        var drained = manager.GetSnapshot();
        drained.Queued.Should().Be(0);
        drained.InFlight.Should().Be(0);

        var fresh = await manager.AcquireAsync(CreateEnvelope("fresh"), CancellationToken.None);
        fresh.Succeeded.Should().BeTrue();
        await fresh.Permit!.DisposeAsync();

        manager.GetSnapshot().InFlight.Should().Be(0);
        manager.GetSnapshot().Queued.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_burst_with_initial_free_permit_never_exceeds_total_admission_capacity()
    {
        const int queueCapacity = 3;
        const int burstSize = 32;
        const int rounds = 16;
        var totalAdmissionCapacity = queueCapacity + 1;

        for (var round = 0; round < rounds; round++)
        {
            await using var manager = CreateManager(
                aggregate: 1,
                hosts: 1, // LocalMaxInFlight = 1
                queueCapacity: queueCapacity,
                perWorkload: burstSize + 1,
                timeoutSeconds: 30);
            using var burstCts = new CancellationTokenSource();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allWorkersReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var readyWorkers = 0;

            var burstTasks = Enumerable.Range(0, burstSize)
                .Select(index => Task.Factory.StartNew(
                    async () =>
                    {
                        if (Interlocked.Increment(ref readyWorkers) == burstSize)
                        {
                            allWorkersReady.TrySetResult();
                        }

                        await start.Task;
                        return await manager.AcquireAsync(
                            CreateEnvelope($"free-permit-{round}-{index}"),
                            burstCts.Token);
                    },
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap())
                .ToArray();

            await allWorkersReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            start.TrySetResult();

            try
            {
                var observationDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
                var snapshot = manager.GetSnapshot();
                while (burstTasks.Count(task => task.IsCompleted) < burstSize - queueCapacity &&
                       snapshot.InFlight + snapshot.Queued <= totalAdmissionCapacity &&
                       DateTimeOffset.UtcNow < observationDeadline)
                {
                    await Task.Delay(10);
                    snapshot = manager.GetSnapshot();
                }

                (snapshot.InFlight + snapshot.Queued).Should().BeLessOrEqualTo(totalAdmissionCapacity);
                burstTasks.Count(task => task.IsCompleted).Should().Be(burstSize - queueCapacity);
            }
            finally
            {
                burstCts.Cancel();
                var results = await Task.WhenAll(burstTasks);
                foreach (var result in results.Where(result => result.Succeeded))
                {
                    await result.Permit!.DisposeAsync();
                }
            }

            var drained = manager.GetSnapshot();
            drained.InFlight.Should().Be(0);
            drained.Queued.Should().Be(0);
        }
    }

    [Fact]
    public async Task Workload_cap_prevents_one_product_from_filling_queue()
    {
        await using var manager = CreateManager(
            aggregate: 10,
            hosts: 5, // LocalMaxInFlight = 2
            queueCapacity: 20,
            perWorkload: 2,
            timeoutSeconds: 1);

        var p1 = await manager.AcquireAsync(CreateEnvelope("church-report"), CancellationToken.None);
        var p2 = await manager.AcquireAsync(CreateEnvelope("church-report"), CancellationToken.None);
        p1.Succeeded.Should().BeTrue();
        p2.Succeeded.Should().BeTrue();

        var p3 = await manager.AcquireAsync(CreateEnvelope("church-report"), CancellationToken.None);
        p3.Succeeded.Should().BeFalse();
        p3.Error!.ErrorCode.Should().Be(DynamicsErrorCodes.WorkloadCapExceeded);

        // other workload can still enter
        var other = await manager.AcquireAsync(CreateEnvelope("other-product"), CancellationToken.None);
        // may queue/timeout depending on in-flight; at least should not be workload-cap for other
        if (!other.Succeeded)
        {
            other.Error!.ErrorCode.Should().NotBe(DynamicsErrorCodes.WorkloadCapExceeded);
        }
        else
        {
            await other.Permit!.DisposeAsync();
        }

        await p1.Permit!.DisposeAsync();
        await p2.Permit!.DisposeAsync();
    }

    [Fact]
    public async Task Permit_release_returns_capacity()
    {
        await using var manager = CreateManager(
            aggregate: 2,
            hosts: 2, // LocalMaxInFlight=1
            queueCapacity: 0,
            perWorkload: 5,
            timeoutSeconds: 1);

        var first = await manager.AcquireAsync(CreateEnvelope("w1"), CancellationToken.None);
        first.Succeeded.Should().BeTrue();

        var blocked = await manager.AcquireAsync(CreateEnvelope("w2"), CancellationToken.None);
        blocked.Succeeded.Should().BeFalse();
        blocked.Error!.ErrorCode.Should().BeOneOf(
            DynamicsErrorCodes.QueueFull,
            DynamicsErrorCodes.AdmissionTimeout);

        await first.Permit!.DisposeAsync();

        var second = await manager.AcquireAsync(CreateEnvelope("w2"), CancellationToken.None);
        second.Succeeded.Should().BeTrue();
        await second.Permit!.DisposeAsync();

        manager.GetSnapshot().InFlight.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_acquire_initializes_only_one_host_lease_for_a_manager()
    {
        const int workerCount = 16;
        var coordinator = new BlockingHostSlotCoordinator();
        await using var manager = CreateManager(
            aggregate: workerCount,
            hosts: 1,
            queueCapacity: 0,
            perWorkload: workerCount,
            timeoutSeconds: 5,
            coordinator: coordinator);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allWorkersReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyWorkers = 0;
        var workers = Enumerable.Range(0, workerCount)
            .Select(index => Task.Factory.StartNew(
                async () =>
                {
                    if (Interlocked.Increment(ref readyWorkers) == workerCount)
                    {
                        allWorkersReady.TrySetResult();
                    }

                    await start.Task;
                    return await manager.AcquireAsync(CreateEnvelope($"lease-race-{index}"), CancellationToken.None);
                },
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap())
            .ToArray();

        await allWorkersReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        start.TrySetResult();
        await coordinator.FirstAcquireEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            coordinator.AcquireCalls.Should().Be(1);
        }
        finally
        {
            coordinator.AllowAcquires();
            var results = await Task.WhenAll(workers);
            foreach (var result in results.Where(result => result.Succeeded))
            {
                await result.Permit!.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Dispose_async_cancels_pending_host_slot_acquisition_without_leaving_waiters()
    {
        var coordinator = new CancellationAwareBlockingHostSlotCoordinator();
        var manager = CreateManager(
            aggregate: 1,
            hosts: 1,
            queueCapacity: 0,
            perWorkload: 1,
            timeoutSeconds: 5,
            coordinator: coordinator);
        var acquireTask = manager.AcquireAsync(CreateEnvelope("shutdown-race"), CancellationToken.None);

        await coordinator.AcquireEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposeTask = manager.DisposeAsync().AsTask();

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            disposeTask.IsCompleted.Should().BeTrue();

            var result = await acquireTask.WaitAsync(TimeSpan.FromSeconds(5));
            result.Succeeded.Should().BeFalse();
            result.Error!.ErrorCode.Should().Be(DynamicsErrorCodes.HostSlotUnavailable);
        }
        finally
        {
            coordinator.ReleaseAcquireWait();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Host_slot_limit_is_enforced_by_in_memory_coordinator()
    {
        var coordinator = new InMemoryRuntimeHostSlotCoordinator();
        var ns = new RuntimeHostSlotLeaseNamespace("shared-lease");

        var l1 = await coordinator.TryAcquireAsync(ns, "host-1", maximumRuntimeHosts: 1, TimeSpan.FromMinutes(1), CancellationToken.None);
        var l2 = await coordinator.TryAcquireAsync(ns, "host-2", maximumRuntimeHosts: 1, TimeSpan.FromMinutes(1), CancellationToken.None);

        l1.Should().NotBeNull();
        l2.Should().BeNull();

        await l1!.DisposeAsync();
        var l3 = await coordinator.TryAcquireAsync(ns, "host-2", maximumRuntimeHosts: 1, TimeSpan.FromMinutes(1), CancellationToken.None);
        l3.Should().NotBeNull();
        await l3!.DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_host_slot_acquire_allows_only_one_lease_and_releases_capacity()
    {
        var coordinator = new InMemoryRuntimeHostSlotCoordinator();
        var ns = new RuntimeHostSlotLeaseNamespace("concurrent-shared-lease");
        var ttl = TimeSpan.FromMinutes(1);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        var attempts = Enumerable.Range(0, 64).Select(i => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref readyCount) == 64)
            {
                ready.SetResult();
            }

            await start.Task;
            return await coordinator.TryAcquireAsync(ns, $"host-{i}", maximumRuntimeHosts: 1, ttl, CancellationToken.None);
        })).ToArray();

        await ready.Task;
        start.SetResult();
        var leases = await Task.WhenAll(attempts);
        leases.Count(lease => lease is not null).Should().Be(1);

        foreach (var lease in leases.OfType<RuntimeHostSlotLease>())
        {
            await lease.DisposeAsync();
        }

        var replacement = await coordinator.TryAcquireAsync(
            ns,
            "replacement-host",
            maximumRuntimeHosts: 1,
            ttl,
            CancellationToken.None);

        replacement.Should().NotBeNull();
        await replacement!.DisposeAsync();
    }

    [Fact]
    public async Task Expired_host_slot_cannot_be_renewed_or_resurrected()
    {
        var coordinator = new InMemoryRuntimeHostSlotCoordinator();
        var ns = new RuntimeHostSlotLeaseNamespace("expired-lease");
        var lease = await coordinator.TryAcquireAsync(
            ns,
            "host-1",
            maximumRuntimeHosts: 1,
            leaseTtl: TimeSpan.FromMilliseconds(20),
            CancellationToken.None);

        lease.Should().NotBeNull();
        var activeLease = lease!;
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var renewed = await coordinator.TryRenewAsync(
            activeLease,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        renewed.Should().BeFalse();
        var replacement = await coordinator.TryAcquireAsync(
            ns,
            "host-2",
            maximumRuntimeHosts: 1,
            leaseTtl: TimeSpan.FromMinutes(1),
            CancellationToken.None);

        replacement.Should().NotBeNull();
        await activeLease.DisposeAsync();
        await replacement!.DisposeAsync();
    }

    private static OrganizationAdmissionManager CreateManager(
        int aggregate,
        int hosts,
        int queueCapacity,
        int perWorkload,
        int timeoutSeconds,
        IRuntimeHostSlotCoordinator? coordinator = null)
    {
        var options = new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "8.2",
            MaxConnectionsPerServer = 1,
            Admission = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                AggregateMaxInFlight = aggregate,
                MaximumRuntimeHosts = hosts,
                LocalQueueCapacity = queueCapacity,
                MaxInFlightAndQueuedPerWorkload = perWorkload,
                QueueAdmissionTimeoutSeconds = timeoutSeconds,
                MaxDispatchEnvelopeBytes = 65536,
                AdmissionNamespaceId = "unit-admission",
                LeaseNamespaceId = "unit-lease",
                RequireDurableHostCoordinator = false
            }
        };

        OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out var error)
            .Should().BeTrue(error?.ErrorMessage);

        return new OrganizationAdmissionManager(
            plan!,
            coordinator ?? new InMemoryRuntimeHostSlotCoordinator(),
            NullLogger<OrganizationAdmissionManager>.Instance);
    }

    private static DispatchEnvelope CreateEnvelope(string workload)
        => new()
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = workload,
            TemplateId = "WhoAmI",
            TemplateHash = new string('a', 64),
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
            EstimatedEnvelopeBytes = 512
        };

    private sealed class BlockingHostSlotCoordinator : IRuntimeHostSlotCoordinator
    {
        private readonly InMemoryRuntimeHostSlotCoordinator _inner = new();
        private readonly TaskCompletionSource _allowAcquires = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _acquireCalls;

        public bool IsDurable => false;
        public int AcquireCalls => Volatile.Read(ref _acquireCalls);
        public TaskCompletionSource FirstAcquireEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RuntimeHostSlotLease?> TryAcquireAsync(
            RuntimeHostSlotLeaseNamespace leaseNamespace,
            string hostInstanceId,
            int maximumRuntimeHosts,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _acquireCalls);
            FirstAcquireEntered.TrySetResult();
            await _allowAcquires.Task.WaitAsync(cancellationToken);
            return await _inner.TryAcquireAsync(
                leaseNamespace,
                hostInstanceId,
                maximumRuntimeHosts,
                leaseTtl,
                cancellationToken);
        }

        public Task<bool> TryRenewAsync(
            RuntimeHostSlotLease lease,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
            => _inner.TryRenewAsync(lease, leaseTtl, cancellationToken);

        public ValueTask ReleaseAsync(RuntimeHostSlotLease lease, CancellationToken cancellationToken)
            => _inner.ReleaseAsync(lease, cancellationToken);

        public void AllowAcquires() => _allowAcquires.TrySetResult();
    }

    private sealed class CancellationAwareBlockingHostSlotCoordinator : IRuntimeHostSlotCoordinator
    {
        private readonly TaskCompletionSource _releaseAcquireWait = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsDurable => false;
        public TaskCompletionSource AcquireEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RuntimeHostSlotLease?> TryAcquireAsync(
            RuntimeHostSlotLeaseNamespace leaseNamespace,
            string hostInstanceId,
            int maximumRuntimeHosts,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
        {
            AcquireEntered.TrySetResult();
            await _releaseAcquireWait.Task.WaitAsync(cancellationToken);
            return null;
        }

        public Task<bool> TryRenewAsync(
            RuntimeHostSlotLease lease,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
            => Task.FromResult(false);

        public ValueTask ReleaseAsync(RuntimeHostSlotLease lease, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public void ReleaseAcquireWait() => _releaseAcquireWait.TrySetResult();
    }
}
