// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs
// 目的：單一 host 上的 bounded queue + in-flight admission 實作。
//
// 保母教學：
// 1. LocalMaxInFlight = floor(Aggregate / MaximumRuntimeHosts)。
// 2. queue 滿了直接拒絕，不可無限成長。
// 3. 單一 WorkloadSubjectId 有上限，避免一產品塞爆。
// 4. 不快取使用者 session / LINE ID / token。
// 5. 例外、取消、超時都要釋放 permit，否則會 memory/slot leak。
// ============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// 本機 Organization admission manager。
/// </summary>
public sealed class OrganizationAdmissionManager : IOrganizationAdmissionManager
{
    private readonly OrganizationAdmissionPlan _plan;
    private readonly IRuntimeHostSlotCoordinator _slotCoordinator;
    private readonly ILogger<OrganizationAdmissionManager> _logger;
    private readonly SemaphoreSlim _inFlight;
    private readonly SemaphoreSlim _totalAdmission;
    private readonly SemaphoreSlim _hostSlotGate = new(1, 1);
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, int> _workloadCounts = new(StringComparer.Ordinal);
    private readonly string _hostInstanceId = Environment.MachineName + ":" + Guid.NewGuid().ToString("N");

    private RuntimeHostSlotLease? _lease;
    private int _queued;
    private long _accepted;
    private long _rejected;
    private long _timeouts;
    private int _disposed;
    private readonly CancellationTokenSource _lifetimeCts = new();

    public OrganizationAdmissionManager(
        OrganizationAdmissionPlan plan,
        IRuntimeHostSlotCoordinator slotCoordinator,
        ILogger<OrganizationAdmissionManager> logger)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _slotCoordinator = slotCoordinator ?? throw new ArgumentNullException(nameof(slotCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inFlight = new SemaphoreSlim(plan.LocalMaxInFlight, plan.LocalMaxInFlight);
        var totalAdmissionCapacity = checked(plan.LocalMaxInFlight + plan.LocalQueueCapacity);
        _totalAdmission = new SemaphoreSlim(totalAdmissionCapacity, totalAdmissionCapacity);
    }

    public OrganizationAdmissionPlan Plan => _plan;

    public async Task EnsureHostSlotAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        await _hostSlotGate.WaitAsync(linked.Token).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            await EnsureHostSlotCoreAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _hostSlotGate.Release();
        }
    }

    private async Task EnsureHostSlotCoreAsync(CancellationToken cancellationToken)
    {

        if (_plan.RequireDurableHostCoordinator && !_slotCoordinator.IsDurable)
        {
            throw new InvalidOperationException(
                "Durable host-slot coordinator is required, but current coordinator is in-memory only.");
        }

        if (_lease is not null && _lease.ExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(5))
        {
            // 接近過期時續租。
            if (_lease.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddSeconds(30))
            {
                var renewed = await _slotCoordinator.TryRenewAsync(
                    _lease,
                    TimeSpan.FromMinutes(2),
                    cancellationToken).ConfigureAwait(false);
                if (!renewed)
                {
                    await DisposeLeaseUnderHostSlotGateAsync().ConfigureAwait(false);
                }
            }

            if (_lease is not null)
            {
                return;
            }
        }

        var lease = await _slotCoordinator.TryAcquireAsync(
            _plan.LeaseNamespace,
            _hostInstanceId,
            _plan.MaximumRuntimeHosts,
            TimeSpan.FromMinutes(2),
            cancellationToken).ConfigureAwait(false);

        if (lease is null)
        {
            throw new InvalidOperationException(
                "Unable to acquire runtime host slot within MaximumRuntimeHosts.");
        }

        _lease = lease;
        _logger.LogInformation(
            "Acquired runtime host slot for admission {AdmissionKey} fencing={FencingToken}",
            _plan.AdmissionKey,
            lease.FencingToken);
    }

    public async Task<AdmissionAcquireResult> AcquireAsync(
        DispatchEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.DeadlineUtc <= DateTimeOffset.UtcNow)
        {
            Interlocked.Increment(ref _rejected);
            return AdmissionAcquireResult.Failure(OperationExecutionResult.Failure(
                DynamicsErrorCodes.AdmissionTimeout,
                "Dispatch envelope deadline has already expired."));
        }

        if (envelope.EstimatedEnvelopeBytes > _plan.MaxDispatchEnvelopeBytes)
        {
            Interlocked.Increment(ref _rejected);
            return AdmissionAcquireResult.Failure(OperationExecutionResult.Failure(
                DynamicsErrorCodes.EnvelopeTooLarge,
                $"Envelope size {envelope.EstimatedEnvelopeBytes} exceeds MaxDispatchEnvelopeBytes {_plan.MaxDispatchEnvelopeBytes}."));
        }

        try
        {
            await EnsureHostSlotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Host slot acquisition failed.");
            Interlocked.Increment(ref _rejected);
            return AdmissionAcquireResult.Failure(OperationExecutionResult.Failure(
                DynamicsErrorCodes.HostSlotUnavailable,
                "Runtime host slot is unavailable."));
        }

        var lease = _lease;
        if (lease is null || lease.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            Interlocked.Increment(ref _rejected);
            return AdmissionAcquireResult.Failure(OperationExecutionResult.Failure(
                DynamicsErrorCodes.HostSlotUnavailable,
                "Runtime host slot lease is not ready."));
        }

        var workload = NormalizeWorkload(envelope.WorkloadSubjectId);
        var queuedHere = false;
        var workloadReserved = false;
        var totalAdmissionReserved = false;

        lock (_gate)
        {
            var workloadCount = _workloadCounts.GetValueOrDefault(workload);
            if (workloadCount >= _plan.MaxInFlightAndQueuedPerWorkload)
            {
                Interlocked.Increment(ref _rejected);
                return AdmissionAcquireResult.Failure(OperationExecutionResult.Failure(
                    DynamicsErrorCodes.WorkloadCapExceeded,
                    $"Workload '{workload}' exceeded MaxInFlightAndQueuedPerWorkload={_plan.MaxInFlightAndQueuedPerWorkload}."));
            }

            // Atomically reserve one bounded slot for either in-flight or queued work.
            if (!_totalAdmission.Wait(0))
            {
                Interlocked.Increment(ref _rejected);
                return AdmissionAcquireResult.Failure(OperationExecutionResult.Failure(
                    DynamicsErrorCodes.QueueFull,
                    $"Local admission queue is full (capacity={_plan.LocalQueueCapacity})."));
            }

            totalAdmissionReserved = true;
            _workloadCounts[workload] = workloadCount + 1;
            workloadReserved = true;
            _queued++;
            queuedHere = true;
        }

        var timeout = TimeSpan.FromSeconds(_plan.QueueAdmissionTimeoutSeconds);
        var remainingToDeadline = envelope.DeadlineUtc - DateTimeOffset.UtcNow;
        if (remainingToDeadline < timeout)
        {
            timeout = remainingToDeadline < TimeSpan.Zero ? TimeSpan.Zero : remainingToDeadline;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        var acquired = false;
        try
        {
            acquired = await _inFlight.WaitAsync(timeout, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // lifetime disposed
            acquired = false;
        }
        catch (OperationCanceledException)
        {
            ReleaseReservation(workload, queuedHere, workloadReserved, totalAdmissionReserved);
            Interlocked.Increment(ref _rejected);
            return AdmissionAcquireResult.Failure(OperationExecutionResult.Failure(
                DynamicsErrorCodes.AdmissionTimeout,
                "Admission wait was cancelled."));
        }
        catch
        {
            ReleaseReservation(workload, queuedHere, workloadReserved, totalAdmissionReserved);
            throw;
        }

        if (!acquired)
        {
            ReleaseReservation(workload, queuedHere, workloadReserved, totalAdmissionReserved);
            Interlocked.Increment(ref _timeouts);
            Interlocked.Increment(ref _rejected);
            return AdmissionAcquireResult.Failure(OperationExecutionResult.Failure(
                DynamicsErrorCodes.AdmissionTimeout,
                "Timed out waiting for local in-flight capacity."));
        }

        // 成功進入 in-flight：queued 計數減一，workload 計數保留到 permit 釋放。
        lock (_gate)
        {
            if (queuedHere)
            {
                _queued = Math.Max(0, _queued - 1);
                queuedHere = false;
            }
        }

        try
        {
            var permit = new AdmissionPermit(
                this,
                envelope.CorrelationId,
                lease.FencingToken,
                workload);
            Interlocked.Increment(ref _accepted);
            return AdmissionAcquireResult.Success(permit);
        }
        catch
        {
            ReleasePermit(workload);
            throw;
        }
    }

    public AdmissionMetricsSnapshot GetSnapshot()
    {
        return new AdmissionMetricsSnapshot
        {
            LocalMaxInFlight = _plan.LocalMaxInFlight,
            InFlight = _plan.LocalMaxInFlight - _inFlight.CurrentCount,
            Queued = _queued,
            LocalQueueCapacity = _plan.LocalQueueCapacity,
            AcceptedCount = Interlocked.Read(ref _accepted),
            RejectedCount = Interlocked.Read(ref _rejected),
            TimeoutCount = Interlocked.Read(ref _timeouts),
            HostSlotReady = _lease is not null && _lease.ExpiresAtUtc > DateTimeOffset.UtcNow,
            HostFencingToken = _lease?.FencingToken ?? 0
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetimeCts.Cancel();
        _hostSlotGate.Wait();
        try
        {
            DisposeLeaseUnderHostSlotGateAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            _hostSlotGate.Release();
        }

        _inFlight.Dispose();
        _totalAdmission.Dispose();
        _lifetimeCts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetimeCts.Cancel();
        await _hostSlotGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeLeaseUnderHostSlotGateAsync().ConfigureAwait(false);
        }
        finally
        {
            _hostSlotGate.Release();
        }

        _inFlight.Dispose();
        _totalAdmission.Dispose();
        _lifetimeCts.Dispose();
    }

    internal void ReleasePermit(string workload)
    {
        try
        {
            _inFlight.Release();
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
        catch (SemaphoreFullException)
        {
            _logger.LogError("Admission semaphore over-released; possible double free.");
        }

        ReleaseTotalAdmissionReservation();

        lock (_gate)
        {
            if (_workloadCounts.TryGetValue(workload, out var count))
            {
                if (count <= 1)
                {
                    _workloadCounts.TryRemove(workload, out _);
                }
                else
                {
                    _workloadCounts[workload] = count - 1;
                }
            }
        }
    }

    private void ReleaseReservation(
        string workload,
        bool queuedHere,
        bool workloadReserved,
        bool totalAdmissionReserved)
    {
        lock (_gate)
        {
            if (queuedHere)
            {
                _queued = Math.Max(0, _queued - 1);
            }

            if (workloadReserved)
            {
                if (_workloadCounts.TryGetValue(workload, out var count))
                {
                    if (count <= 1)
                    {
                        _workloadCounts.TryRemove(workload, out _);
                    }
                    else
                    {
                        _workloadCounts[workload] = count - 1;
                    }
                }
            }
        }

        if (totalAdmissionReserved)
        {
            ReleaseTotalAdmissionReservation();
        }
    }

    private void ReleaseTotalAdmissionReservation()
    {
        try
        {
            _totalAdmission.Release();
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
        catch (SemaphoreFullException)
        {
            _logger.LogError("Total admission semaphore over-released; possible double free.");
        }
    }

    private async ValueTask DisposeLeaseUnderHostSlotGateAsync()
    {
        if (_lease is null)
        {
            return;
        }

        var lease = _lease;
        _lease = null;
        await lease.DisposeAsync().ConfigureAwait(false);
    }

    private static string NormalizeWorkload(string workloadSubjectId)
    {
        var value = (workloadSubjectId ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "unknown-workload";
        }

        // 防爆：過長 subject 截斷，避免字典被惡意撐大。
        return value.Length <= 128 ? value : value[..128];
    }

    private sealed class AdmissionPermit : IAdmissionPermit
    {
        private readonly OrganizationAdmissionManager _owner;
        private readonly string _workload;
        private int _disposed;

        public AdmissionPermit(
            OrganizationAdmissionManager owner,
            Guid correlationId,
            long hostFencingToken,
            string workload)
        {
            _owner = owner;
            CorrelationId = correlationId;
            HostFencingToken = hostFencingToken;
            _workload = workload;
        }

        public Guid CorrelationId { get; }
        public long HostFencingToken { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _owner.ReleasePermit(_workload);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
