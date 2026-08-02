using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 runtime-host lease 從取得、續租、故障、排空到釋放的完整生命週期。
/// 測試特別覆蓋暫時性 coordinator 錯誤、明確 fencing、阻塞續租、釋放失敗與過短 lease，
/// 確保背景 Task、permit、取消註冊與 host slot 都能在有界時間回到基線。
/// </summary>
public sealed class OrganizationAdmissionLeaseLifecycleTests
{
    /// <summary>
    /// 暫時性續租例外不會竄改尚有效的 coordinator lease；明確拒絕後則立即取消 permit 並停止新 admission。
    /// </summary>
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

    /// <summary>Dispose 必須等待現有 permit 歸還後才釋放 fenced slot，避免替代主機與舊 outbound 工作重疊。</summary>
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

    /// <summary>注入永遠阻塞的續租，證明 shutdown 會取消並 await 自己擁有的背景工作。</summary>
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

    /// <summary>coordinator 釋放失敗時仍須清除本機續租與資源；遠端安全由 TTL/quarantine 接手。</summary>
    [Fact]
    public async Task Coordinator_release_failure_does_not_leak_owned_renewal_or_local_resources()
    {
        var coordinator = new FailingReleaseCoordinator();
        var manager = CreateManager(coordinator);
        await manager.EnsureHostSlotAsync(CancellationToken.None);

        await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        coordinator.ReleaseCalls.Should().Be(1);
        coordinator.ActiveRenewOperations.Should().Be(0);
    }

    /// <summary>無法容納最大工作生命週期的短 lease 不得進入 Ready 或派送任何工作。</summary>
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
        var admissionOptions = new OrganizationAdmissionOptions
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
        };

        OrganizationAdmissionPlan.TryCreate(
                "https://crm.example.local/org/",
                workerCount: 1,
                maxInFlightPerWorker: 1,
                admissionOptions,
                out var plan,
                out var error)
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

        // 這個 durable test double 必須實作與正式 SQL coordinator 相同的新 request overload；
        // 不能依賴 interface 的 namespace-only fallback，否則測試會不小心繞過 canonical organization
        // binding 的 fail-closed 契約。request 只在呼叫期間拆解，不保存 Session、Token 或其他跨測試狀態。
        public virtual Task<RuntimeHostSlotLease?> TryAcquireAsync(
            RuntimeHostSlotLeaseRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return TryAcquireAsync(
                request.LeaseNamespace,
                request.HostInstanceId,
                request.MaximumRuntimeHosts,
                request.LeaseTtl,
                cancellationToken);
        }

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

    private sealed class FailingReleaseCoordinator : RecordingCoordinator
    {
        public override ValueTask ReleaseAsync(
            RuntimeHostSlotLease lease,
            CancellationToken cancellationToken)
        {
            base.ReleaseAsync(lease, cancellationToken);
            return ValueTask.FromException(new InvalidOperationException("release-failed"));
        }
    }
}
