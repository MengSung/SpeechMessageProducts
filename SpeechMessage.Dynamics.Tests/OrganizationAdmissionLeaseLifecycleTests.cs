using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class OrganizationAdmissionLeaseLifecycleTests
{
    [Fact]
    public async Task Transient_renewal_failure_keeps_valid_lease_until_explicit_rejection()
    {
        var coordinator = new TransientThenRejectCoordinator();
        await using var manager = CreateManager(coordinator);

        var acquired = await manager.AcquireAsync(CreateEnvelope(), CancellationToken.None);
        acquired.Succeeded.Should().BeTrue();
        var permit = acquired.Permit!;

        await coordinator.FirstRenewAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        manager.GetSnapshot().HostSlotReady.Should().BeTrue(
            "one transient coordinator error must not revoke a still-valid lease");
        permit.LeaseLostToken.IsCancellationRequested.Should().BeFalse();

        var leaseLost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = permit.LeaseLostToken.Register(leaseLost.SetResult);
        coordinator.AllowExplicitRejection();
        await leaseLost.Task.WaitAsync(TimeSpan.FromSeconds(5));

        manager.GetSnapshot().HostSlotReady.Should().BeFalse();
        var rejected = await manager.AcquireAsync(CreateEnvelope(), CancellationToken.None);
        rejected.Succeeded.Should().BeFalse();
        rejected.Error!.ErrorCode.Should().Be(DynamicsErrorCodes.HostSlotUnavailable);

        await permit.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_waits_for_in_flight_permits_before_fenced_release()
    {
        var coordinator = new RecordingCoordinator();
        var manager = CreateManager(
            coordinator,
            leaseTtlSeconds: 20,
            renewalIntervalSeconds: 10);
        var acquired = await manager.AcquireAsync(CreateEnvelope(), CancellationToken.None);
        acquired.Succeeded.Should().BeTrue();

        var disposeTask = manager.DisposeAsync().AsTask();
        await Task.Delay(100);

        disposeTask.IsCompleted.Should().BeFalse();
        coordinator.ReleaseCalls.Should().Be(0,
            "a graceful stop must retain the host slot while outbound work is active");

        await acquired.Permit!.DisposeAsync();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.ReleaseCalls.Should().Be(1);
        coordinator.ActiveRenewOperations.Should().Be(0);
    }

    [Fact]
    public async Task Dispose_cancels_and_awaits_owned_renewal_operation()
    {
        var coordinator = new BlockingRenewCoordinator();
        var manager = CreateManager(coordinator);
        await manager.EnsureHostSlotAsync(CancellationToken.None);
        await coordinator.RenewEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.ActiveRenewOperations.Should().Be(0);
        coordinator.ReleaseCalls.Should().Be(1);
    }

    [Fact]
    public async Task Lease_that_cannot_fit_maximum_work_lifetime_is_not_admitted()
    {
        var coordinator = new ShortLeaseCoordinator(TimeSpan.FromSeconds(2));
        await using var manager = CreateManager(
            coordinator,
            leaseTtlSeconds: 10,
            expiryFenceSeconds: 1,
            maximumWorkSeconds: 3);

        var acquired = await manager.AcquireAsync(CreateEnvelope(), CancellationToken.None);

        acquired.Succeeded.Should().BeFalse();
        acquired.Error!.ErrorCode.Should().Be(DynamicsErrorCodes.HostSlotUnavailable);
        manager.GetSnapshot().HostSlotReady.Should().BeFalse();
    }

    private static OrganizationAdmissionManager CreateManager(
        IRuntimeHostSlotCoordinator coordinator,
        int leaseTtlSeconds = 8,
        int renewalIntervalSeconds = 1,
        int expiryFenceSeconds = 1,
        int maximumWorkSeconds = 2)
    {
        var options = new DynamicsWebApiOptions
        {
            OrganizationBaseUri = "https://crm.example.local/org/",
            CeVersion = "9.1",
            MaxConnectionsPerServer = 1,
            Admission = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("12121212-1212-1212-1212-121212121212"),
                AggregateMaxInFlight = 2,
                MaximumRuntimeHosts = 2,
                LocalQueueCapacity = 2,
                MaxInFlightAndQueuedPerWorkload = 2,
                QueueAdmissionTimeoutSeconds = 2,
                MaxDispatchEnvelopeBytes = 65536,
                AdmissionNamespaceId = "lease-lifecycle-admission",
                LeaseNamespaceId = "lease-lifecycle-slot",
                RuntimeHostSlotLeaseTtlSeconds = leaseTtlSeconds,
                RuntimeHostSlotRenewalIntervalSeconds = renewalIntervalSeconds,
                RuntimeHostSlotExpiryFenceSeconds = expiryFenceSeconds,
                MaximumOutboundWorkLifetimeSeconds = maximumWorkSeconds,
                ShutdownDrainTimeoutSeconds = 5
            }
        };

        OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out var error)
            .Should().BeTrue(error?.ErrorMessage);

        return new OrganizationAdmissionManager(
            plan!,
            coordinator,
            NullLogger<OrganizationAdmissionManager>.Instance);
    }

    private static DispatchEnvelope CreateEnvelope()
        => new()
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "lease-lifecycle-workload",
            TemplateId = "WhoAmI",
            TemplateHash = new string('a', 64),
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
            EstimatedEnvelopeBytes = 512
        };

    private class RecordingCoordinator : IRuntimeHostSlotCoordinator
    {
        private int _releaseCalls;
        private int _activeRenewOperations;

        public bool IsDurable => true;
        public int ReleaseCalls => Volatile.Read(ref _releaseCalls);
        public int ActiveRenewOperations => Volatile.Read(ref _activeRenewOperations);

        public virtual Task<RuntimeHostSlotLease?> TryAcquireAsync(
            RuntimeHostSlotLeaseNamespace leaseNamespace,
            string hostInstanceId,
            int maximumRuntimeHosts,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
            => Task.FromResult<RuntimeHostSlotLease?>(new RuntimeHostSlotLease(
                this,
                leaseNamespace,
                hostInstanceId,
                fencingToken: 1,
                expiresAtUtc: DateTimeOffset.UtcNow.Add(leaseTtl),
                slotOrdinal: 0));

        public virtual Task<bool> TryRenewAsync(
            RuntimeHostSlotLease lease,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
            => Task.FromResult(true);

        public virtual ValueTask ReleaseAsync(
            RuntimeHostSlotLease lease,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _releaseCalls);
            return ValueTask.CompletedTask;
        }

        protected void RenewStarted() => Interlocked.Increment(ref _activeRenewOperations);
        protected void RenewFinished() => Interlocked.Decrement(ref _activeRenewOperations);
    }

    private sealed class TransientThenRejectCoordinator : RecordingCoordinator
    {
        private readonly TaskCompletionSource _allowExplicitRejection =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _renewCalls;

        public TaskCompletionSource FirstRenewAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<bool> TryRenewAsync(
            RuntimeHostSlotLease lease,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _renewCalls);
            if (call == 1)
            {
                FirstRenewAttempted.TrySetResult();
                throw new TimeoutException("transient-renewal-timeout");
            }

            await _allowExplicitRejection.Task.WaitAsync(cancellationToken);
            return false;
        }

        public void AllowExplicitRejection() => _allowExplicitRejection.TrySetResult();
    }

    private sealed class BlockingRenewCoordinator : RecordingCoordinator
    {
        public TaskCompletionSource RenewEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<bool> TryRenewAsync(
            RuntimeHostSlotLease lease,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
        {
            RenewStarted();
            RenewEntered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            }
            finally
            {
                RenewFinished();
            }
        }
    }

    private sealed class ShortLeaseCoordinator : RecordingCoordinator
    {
        private readonly TimeSpan _actualTtl;

        public ShortLeaseCoordinator(TimeSpan actualTtl)
        {
            _actualTtl = actualTtl;
        }

        public override Task<RuntimeHostSlotLease?> TryAcquireAsync(
            RuntimeHostSlotLeaseNamespace leaseNamespace,
            string hostInstanceId,
            int maximumRuntimeHosts,
            TimeSpan leaseTtl,
            CancellationToken cancellationToken)
            => Task.FromResult<RuntimeHostSlotLease?>(new RuntimeHostSlotLease(
                this,
                leaseNamespace,
                hostInstanceId,
                fencingToken: 1,
                expiresAtUtc: DateTimeOffset.UtcNow.Add(_actualTtl),
                slotOrdinal: 0));
    }
}
