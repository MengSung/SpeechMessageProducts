using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ControlPlane.Capacity;

/// <summary>
/// 擁有單一組織設定檔的本機有界 admission 預算，以及一個可續租的 runtime-host slot。
/// 此管理器只保存容量計數、不可變計畫與租約狀態，不保存要求本文、使用者、Session、Token、Cookie 或認證資料，
/// 因此不同 workload 與設定檔世代不會透過共用可變身分狀態互相污染。
/// 所有續租背景工作、取消來源、Semaphore 與租約都以本 singleton 為明確擁有者，關閉時必須先停止 admission、
/// 等待已保留工作排空，再取消並 await 背景工作，最後才釋放協調器租約與本機資源。
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
    private readonly object _shutdownGate = new();
    private readonly ConcurrentDictionary<string, int> _workloadCounts = new(StringComparer.Ordinal);
    private readonly string _hostInstanceId = Environment.MachineName + ":" + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _admissionStopCts = new();
    private readonly CancellationTokenSource _renewalStopCts = new();

    private RuntimeHostSlotLease? _lease;
    private CancellationTokenSource? _leaseLostCts;
    private Task? _renewalTask;
    private Task? _shutdownTask;
    private TaskCompletionSource _reservationsDrained = CreateCompletedSignal();
    private int _queued;
    private int _activePermits;
    private int _admissionReservations;
    private int _accepting = 1;
    private int _terminalLeaseFailure;
    private int _renewalLoopActive;
    private int _disposed;
    private long _accepted;
    private long _rejected;
    private long _timeouts;

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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _accepting) == 0 || Volatile.Read(ref _terminalLeaseFailure) != 0)
        {
            throw new InvalidOperationException("Runtime host slot admission is closed.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _admissionStopCts.Token);
        await _hostSlotGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await EnsureHostSlotCoreAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _hostSlotGate.Release();
        }
    }

    private async Task EnsureHostSlotCoreAsync(CancellationToken cancellationToken)
    {
        // 生產容量保證同時依賴「跨行程持久化」與「AdmissionEpoch/設定摘要 fencing」。
        // 只符合其中一項仍可能讓舊設定主機與新設定主機同時取得容量，因此必須 fail-closed。
        if (_plan.RequireDurableHostCoordinator && !_slotCoordinator.IsDurable)
        {
            throw new InvalidOperationException(
                "Durable host-slot coordinator is required, but current coordinator is in-memory only.");
        }

        if (_plan.RequireDurableHostCoordinator && !_slotCoordinator.SupportsAdmissionEpoch)
        {
            throw new InvalidOperationException(
                "Durable host-slot coordinator must enforce AdmissionEpoch and configuration digest fencing.");
        }

        if (Volatile.Read(ref _terminalLeaseFailure) != 0)
        {
            throw new InvalidOperationException("Runtime host slot was fenced or lost and requires process restart.");
        }

        var lease = GetLease();
        if (lease is not null)
        {
            // 新工作只能在 lease expiry fence 之前完整容納其最大宣告生命週期；
            // 這可避免工作尚未結束時槽位已被協調器回收並交給另一個主機，造成總容量瞬間倍增。
            if (CanFitMaximumWork(lease, DateTimeOffset.UtcNow))
            {
                return;
            }

            bool renewed;
            try
            {
                renewed = await _slotCoordinator.TryRenewAsync(
                    lease,
                    _plan.RuntimeHostSlotLeaseTtl,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Runtime host slot renewal failed while admission waited for a safe lease window.");
                throw;
            }

            if (!renewed || !CanFitMaximumWork(lease, DateTimeOffset.UtcNow))
            {
                MarkLeaseLost("Runtime host slot renewal was rejected or returned an unsafe expiry window.");
                throw new InvalidOperationException("Runtime host slot lease was rejected or fenced.");
            }

            return;
        }

        lease = await _slotCoordinator.TryAcquireAsync(
            new RuntimeHostSlotLeaseRequest(
                _plan.CanonicalKey,
                _plan.LeaseNamespace,
                _hostInstanceId,
                _plan.MaximumRuntimeHosts,
                _plan.RuntimeHostSlotLeaseTtl,
                _plan.AdmissionEpoch,
                _plan.ConfigurationDigest),
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            throw new InvalidOperationException(
                "Unable to acquire runtime host slot within MaximumRuntimeHosts.");
        }

        lock (_gate)
        {
            _lease = lease;
            _leaseLostCts = new CancellationTokenSource();
        }

        if (!CanFitMaximumWork(lease, DateTimeOffset.UtcNow))
        {
            MarkLeaseLost("Coordinator returned a lease that cannot contain the declared maximum work lifetime.");
            await DisposeLeaseUnderHostSlotGateAsync().ConfigureAwait(false);
            throw new InvalidOperationException("Runtime host slot lease has an unsafe expiry window.");
        }

        StartRenewalLoopUnderHostSlotGate();
        _logger.LogInformation(
            "Acquired runtime host slot for admission {AdmissionKey} fencing={FencingToken}",
            _plan.AdmissionKey,
            lease.FencingToken);
    }

    public async Task<AdmissionAcquireResult> AcquireAsync(
        DispatchEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.DeadlineUtc <= DateTimeOffset.UtcNow)
        {
            return Reject(DynamicsErrorCodes.AdmissionTimeout, "Dispatch envelope deadline has already expired.");
        }

        if (envelope.EstimatedEnvelopeBytes > _plan.MaxDispatchEnvelopeBytes)
        {
            return Reject(
                DynamicsErrorCodes.EnvelopeTooLarge,
                $"Envelope size {envelope.EstimatedEnvelopeBytes} exceeds MaxDispatchEnvelopeBytes {_plan.MaxDispatchEnvelopeBytes}.");
        }

        try
        {
            await EnsureHostSlotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Host slot acquisition failed.");
            return Reject(DynamicsErrorCodes.HostSlotUnavailable, "Runtime host slot is unavailable.");
        }

        if (!TryGetAdmissibleLease(envelope, out _, out var leaseLostToken))
        {
            return Reject(DynamicsErrorCodes.HostSlotUnavailable, "Runtime host slot lease is not ready.");
        }

        var workload = NormalizeWorkload(envelope.WorkloadSubjectId);
        var queuedHere = false;
        var workloadReserved = false;
        var totalAdmissionReserved = false;
        // admission 依序保留三層資源：每 workload 上限、總排隊/執行容量、實際 in-flight permit。
        // 任一後續步驟失敗都必須用對稱旗標精確回收已取得的層級，避免計數永久占用或雙重 Release。
        lock (_gate)
        {
            if (_accepting == 0 || _terminalLeaseFailure != 0)
            {
                return Reject(DynamicsErrorCodes.HostSlotUnavailable, "Runtime host slot admission is closed.");
            }

            var workloadCount = _workloadCounts.GetValueOrDefault(workload);
            if (workloadCount >= _plan.MaxInFlightAndQueuedPerWorkload)
            {
                return Reject(
                    DynamicsErrorCodes.WorkloadCapExceeded,
                    $"Workload '{workload}' exceeded MaxInFlightAndQueuedPerWorkload={_plan.MaxInFlightAndQueuedPerWorkload}.");
            }

            if (!_totalAdmission.Wait(0))
            {
                return Reject(
                    DynamicsErrorCodes.QueueFull,
                    $"Local admission queue is full (capacity={_plan.LocalQueueCapacity}).");
            }

            totalAdmissionReserved = true;
            _workloadCounts[workload] = workloadCount + 1;
            workloadReserved = true;
            _queued++;
            queuedHere = true;
            ReserveAdmissionUnderLock();
        }

        var timeout = TimeSpan.FromSeconds(_plan.QueueAdmissionTimeoutSeconds);
        var remainingToDeadline = envelope.DeadlineUtc - DateTimeOffset.UtcNow;
        if (remainingToDeadline < timeout)
        {
            timeout = remainingToDeadline < TimeSpan.Zero ? TimeSpan.Zero : remainingToDeadline;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _admissionStopCts.Token,
            leaseLostToken);
        var acquired = false;
        try
        {
            acquired = await _inFlight.WaitAsync(timeout, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ReleaseReservation(workload, queuedHere, workloadReserved, totalAdmissionReserved);
            if (cancellationToken.IsCancellationRequested)
            {
                return Reject(DynamicsErrorCodes.AdmissionTimeout, "Admission wait was cancelled.");
            }

            return Reject(DynamicsErrorCodes.HostSlotUnavailable, "Runtime host slot admission closed while waiting.");
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
            return Reject(DynamicsErrorCodes.AdmissionTimeout, "Timed out waiting for local in-flight capacity.");
        }

        lock (_gate)
        {
            if (queuedHere)
            {
                _queued = Math.Max(0, _queued - 1);
                queuedHere = false;
            }
        }

        if (!TryGetAdmissibleLease(envelope, out var admittedLease, out leaseLostToken) || admittedLease is null)
        {
            _inFlight.Release();
            ReleaseReservation(workload, queuedHere, workloadReserved, totalAdmissionReserved);
            return Reject(DynamicsErrorCodes.HostSlotUnavailable, "Runtime host slot lease became unsafe before dispatch.");
        }

        lock (_gate)
        {
            if (_accepting == 0 || _terminalLeaseFailure != 0)
            {
                _inFlight.Release();
                ReleaseReservationUnderLock(workload, workloadReserved, totalAdmissionReserved);
                return Reject(DynamicsErrorCodes.HostSlotUnavailable, "Runtime host slot admission closed before dispatch.");
            }

            _activePermits++;
        }

        var permit = new AdmissionPermit(
            this,
            envelope.CorrelationId,
            admittedLease.FencingToken,
            workload,
            leaseLostToken);
        Interlocked.Increment(ref _accepted);
        return AdmissionAcquireResult.Success(permit);
    }

    public AdmissionMetricsSnapshot GetSnapshot()
    {
        RuntimeHostSlotLease? lease;
        int queued;
        int activePermits;
        lock (_gate)
        {
            lease = _lease;
            queued = _queued;
            activePermits = _activePermits;
        }

        var ready = Volatile.Read(ref _accepting) != 0 &&
                    Volatile.Read(ref _terminalLeaseFailure) == 0 &&
                    lease is not null &&
                    CanFitMaximumWork(lease, DateTimeOffset.UtcNow);
        return new AdmissionMetricsSnapshot
        {
            LocalMaxInFlight = _plan.LocalMaxInFlight,
            InFlight = _plan.LocalMaxInFlight - _inFlight.CurrentCount,
            Queued = queued,
            LocalQueueCapacity = _plan.LocalQueueCapacity,
            AcceptedCount = Interlocked.Read(ref _accepted),
            RejectedCount = Interlocked.Read(ref _rejected),
            TimeoutCount = Interlocked.Read(ref _timeouts),
            HostSlotReady = ready,
            HostFencingToken = lease?.FencingToken ?? 0,
            HostLeaseExpiresAtUtc = lease?.ExpiresAtUtc,
            ActivePermits = activePermits,
            RenewalLoopActive = Volatile.Read(ref _renewalLoopActive) != 0,
            TrackedWorkloadCount = _workloadCounts.Count
        };
    }

    public void Dispose()
    {
        Task.Run(ShutdownOnceAsync).GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync() => new(ShutdownOnceAsync());

    internal void ReleasePermit(string workload)
    {
        try
        {
            _inFlight.Release();
        }
        catch (ObjectDisposedException)
        {
            // 有界工作可能恰好在關閉完成後才回傳 permit；Semaphore 已釋放代表其擁有者已完成終止流程，
            // 此競態不需要再次釋放，但 workload 與 reservation 計數仍會在下方走完對稱清理。
        }
        catch (SemaphoreFullException)
        {
            _logger.LogError("Admission semaphore over-released; possible double free.");
        }

        ReleaseTotalAdmissionReservation();
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            ReleaseWorkloadUnderLock(workload);
            _activePermits = Math.Max(0, _activePermits - 1);
            drained = ReleaseAdmissionUnderLock();
        }

        drained?.TrySetResult();
    }

    private void StartRenewalLoopUnderHostSlotGate()
    {
        lock (_gate)
        {
            _renewalTask ??= RenewHostSlotLoopAsync(_renewalStopCts.Token);
        }
    }

    private async Task RenewHostSlotLoopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _renewalLoopActive, 1);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_plan.RuntimeHostSlotRenewalInterval, cancellationToken).ConfigureAwait(false);
                await _hostSlotGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref _terminalLeaseFailure) != 0)
                    {
                        return;
                    }

                    var lease = GetLease();
                    if (lease is null)
                    {
                        MarkLeaseLost("Runtime host slot lease disappeared while renewal was active.");
                        return;
                    }

                    if (DateTimeOffset.UtcNow >= GetExpiryFenceUtc(lease))
                    {
                        MarkLeaseLost("Runtime host slot reached its expiry fence before renewal.");
                        return;
                    }

                    bool renewed;
                    try
                    {
                        renewed = await _slotCoordinator.TryRenewAsync(
                            lease,
                            _plan.RuntimeHostSlotLeaseTtl,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Runtime host slot renewal failed transiently; the current coordinator lease remains authoritative until its expiry fence.");
                        if (DateTimeOffset.UtcNow >= GetExpiryFenceUtc(lease))
                        {
                            MarkLeaseLost("Runtime host slot expired after renewal failures.");
                            return;
                        }

                        continue;
                    }

                    if (!renewed)
                    {
                        MarkLeaseLost("Runtime host slot renewal was explicitly rejected or fenced.");
                        return;
                    }

                    if (!CanFitMaximumWork(lease, DateTimeOffset.UtcNow))
                    {
                        MarkLeaseLost("Renewed runtime host slot cannot contain the maximum outbound work lifetime.");
                        return;
                    }
                }
                finally
                {
                    _hostSlotGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 這是管理器擁有的正常關閉取消路徑；finally 仍會清除 RenewalLoopActive，
            // ShutdownCoreAsync 也會 await 此工作，確保沒有背景 Task 在資源 Dispose 後繼續執行。
        }
        finally
        {
            Interlocked.Exchange(ref _renewalLoopActive, 0);
        }
    }

    private Task ShutdownOnceAsync()
    {
        lock (_shutdownGate)
        {
            return _shutdownTask ??= ShutdownCoreAsync();
        }
    }

    private async Task ShutdownCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _accepting, 0);
        _admissionStopCts.Cancel();

        // 第一階段提供設定內的優雅排空期限；逾時後先標記 lease lost，強制取消仍在等待或執行的工作，
        // 再以「最大 outbound 生命週期 + expiry fence」作最後有界等待。未真正排空時絕不提前交還 host slot。
        var drained = await WaitForReservationsToDrainAsync(_plan.ShutdownDrainTimeout).ConfigureAwait(false);
        if (!drained)
        {
            MarkLeaseLost("Graceful shutdown drain timed out; cancelling remaining outbound work.");
            drained = await WaitForReservationsToDrainAsync(
                _plan.MaximumOutboundWorkLifetime + _plan.RuntimeHostSlotExpiryFence).ConfigureAwait(false);
        }

        if (!drained)
        {
            throw new TimeoutException(
                "Runtime host shutdown could not drain bounded outbound work; the slot was not released.");
        }

        _renewalStopCts.Cancel();
        Task? renewalTask;
        lock (_gate)
        {
            renewalTask = _renewalTask;
        }

        if (renewalTask is not null)
        {
            await renewalTask.ConfigureAwait(false);
        }

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
        _hostSlotGate.Dispose();
        _admissionStopCts.Dispose();
        _renewalStopCts.Dispose();
    }

    private async Task<bool> WaitForReservationsToDrainAsync(TimeSpan timeout)
    {
        Task waitTask;
        lock (_gate)
        {
            if (_admissionReservations == 0)
            {
                return true;
            }

            waitTask = _reservationsDrained.Task;
        }

        try
        {
            await waitTask.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private bool TryGetAdmissibleLease(
        DispatchEnvelope envelope,
        out RuntimeHostSlotLease? lease,
        out CancellationToken leaseLostToken)
    {
        lock (_gate)
        {
            lease = _lease;
            leaseLostToken = _leaseLostCts?.Token ?? new CancellationToken(canceled: true);
            if (_accepting == 0 || _terminalLeaseFailure != 0 || lease is null)
            {
                return false;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var declaredLifetime = envelope.DeadlineUtc - now;
        if (declaredLifetime > _plan.MaximumOutboundWorkLifetime)
        {
            declaredLifetime = _plan.MaximumOutboundWorkLifetime;
        }

        return declaredLifetime > TimeSpan.Zero &&
               now + declaredLifetime <= GetExpiryFenceUtc(lease);
    }

    private bool CanFitMaximumWork(RuntimeHostSlotLease lease, DateTimeOffset now)
        => now + _plan.MaximumOutboundWorkLifetime <= GetExpiryFenceUtc(lease);

    private DateTimeOffset GetExpiryFenceUtc(RuntimeHostSlotLease lease)
        => lease.ExpiresAtUtc - _plan.RuntimeHostSlotExpiryFence;

    private RuntimeHostSlotLease? GetLease()
    {
        lock (_gate)
        {
            return _lease;
        }
    }

    private void MarkLeaseLost(string reason)
    {
        if (Interlocked.Exchange(ref _terminalLeaseFailure, 1) != 0)
        {
            return;
        }

        CancellationTokenSource? leaseLost;
        lock (_gate)
        {
            leaseLost = _leaseLostCts;
        }

        SafeCancel(leaseLost, "lease-loss callbacks");
        // lease 一旦被拒絕、過期或 fencing，行程不得自行恢復 admission；重啟才能建立新的不可變世代與 host 身分。
        // 這項終局狀態避免短暫錯誤後舊主機在未知租約所有權下繼續送出 CRM 流量。
        _logger.LogError("{Reason} New Dynamics admission is closed until process restart.", reason);
    }

    private async ValueTask DisposeLeaseUnderHostSlotGateAsync()
    {
        RuntimeHostSlotLease? lease;
        CancellationTokenSource? leaseLost;
        lock (_gate)
        {
            lease = _lease;
            _lease = null;
            leaseLost = _leaseLostCts;
            _leaseLostCts = null;
        }

        if (lease is not null)
        {
            try
            {
                // 本地釋放是盡力而為；跨主機的真正安全界線仍由 coordinator 的 fencing token、TTL 與 quarantine 維持。
                await lease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Runtime host slot fenced release failed; local ownership is still disposed and the coordinator TTL/quarantine remains authoritative.");
            }
        }

        if (leaseLost is not null)
        {
            SafeCancel(leaseLost, "lease disposal callbacks");
            leaseLost.Dispose();
        }
    }

    private void SafeCancel(CancellationTokenSource? source, string callbackScope)
    {
        if (source is null || source.IsCancellationRequested)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (AggregateException ex)
        {
            _logger.LogError(
                ex,
                "One or more {CallbackScope} threw during cancellation; admission remains fail-closed.",
                callbackScope);
        }
    }

    private AdmissionAcquireResult Reject(string code, string message)
    {
        Interlocked.Increment(ref _rejected);
        return AdmissionAcquireResult.Failure(OperationExecutionResult.Failure(code, message));
    }

    private void ReserveAdmissionUnderLock()
    {
        if (_admissionReservations == 0)
        {
            _reservationsDrained = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _admissionReservations++;
    }

    private TaskCompletionSource? ReleaseAdmissionUnderLock()
    {
        if (_admissionReservations <= 0)
        {
            _logger.LogError("Admission reservation over-released; possible double free.");
            return null;
        }

        _admissionReservations--;
        return _admissionReservations == 0 ? _reservationsDrained : null;
    }

    private void ReleaseReservation(
        string workload,
        bool queuedHere,
        bool workloadReserved,
        bool totalAdmissionReserved)
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            if (queuedHere)
            {
                _queued = Math.Max(0, _queued - 1);
            }

            if (workloadReserved)
            {
                ReleaseWorkloadUnderLock(workload);
            }

            if (totalAdmissionReserved)
            {
                drained = ReleaseAdmissionUnderLock();
            }
        }

        if (totalAdmissionReserved)
        {
            ReleaseTotalAdmissionReservation();
        }

        drained?.TrySetResult();
    }

    private void ReleaseReservationUnderLock(
        string workload,
        bool workloadReserved,
        bool totalAdmissionReserved)
    {
        if (workloadReserved)
        {
            ReleaseWorkloadUnderLock(workload);
        }

        var drained = totalAdmissionReserved ? ReleaseAdmissionUnderLock() : null;
        if (totalAdmissionReserved)
        {
            ReleaseTotalAdmissionReservation();
        }

        drained?.TrySetResult();
    }

    private void ReleaseWorkloadUnderLock(string workload)
    {
        if (!_workloadCounts.TryGetValue(workload, out var count))
        {
            return;
        }

        if (count <= 1)
        {
            _workloadCounts.TryRemove(workload, out _);
        }
        else
        {
            _workloadCounts[workload] = count - 1;
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
            // 等待者在 shutdown 取消後可能比 Semaphore.Dispose 更晚進入回收路徑；
            // reservation 計數仍已對稱扣除，因此此處只忽略已完成擁有者生命週期的 Semaphore。
        }
        catch (SemaphoreFullException)
        {
            _logger.LogError("Total admission semaphore over-released; possible double free.");
        }
    }

    private static string NormalizeWorkload(string workloadSubjectId)
    {
        var value = (workloadSubjectId ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "unknown-workload";
        }

        return value.Length <= 128 ? value : value[..128];
    }

    private static TaskCompletionSource CreateCompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult();
        return signal;
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
            string workload,
            CancellationToken leaseLostToken)
        {
            _owner = owner;
            CorrelationId = correlationId;
            HostFencingToken = hostFencingToken;
            _workload = workload;
            LeaseLostToken = leaseLostToken;
        }

        public Guid CorrelationId { get; }
        public long HostFencingToken { get; }
        public CancellationToken LeaseLostToken { get; }

        public void Dispose()
        {
            // permit 採冪等釋放，防止同步 Dispose 與 await using/例外清理同時執行時重複增加 Semaphore 容量。
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
