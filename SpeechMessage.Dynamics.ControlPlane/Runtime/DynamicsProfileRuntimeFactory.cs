// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Runtime/DynamicsProfileRuntimeFactory.cs
// 目的：建立官方 Worker-backed Runtime Generation，並在任一步驟失敗時反向 rollback 全部資源。
// ============================================================================

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// 官方 NuGet Worker Runtime Factory 的預設實作。
/// 每次 <see cref="CreateAsync"/> 都建立新的 admission registration、有限 Worker process 集合、named pipes、
/// process handles、stream drain tasks 與 retirement state；Factory 自身只保存 registry reference，
/// 不保存 Request、User、Session、Credential、Token、Worker result 或跨 generation mutable cache。
/// </summary>
public sealed class DynamicsProfileRuntimeFactory :
    IDynamicsProfileRuntimeFactory,
    IAsyncDisposable,
    IDisposable
{
    private const string CleanupFailureMessage =
        "The official Dynamics worker cleanup did not complete.";
    private static readonly TimeSpan FactoryEntrantDrainTimeout = TimeSpan.FromSeconds(10);

    private readonly IOrganizationAdmissionRegistry _admissionRegistry;
    private readonly SemaphoreSlim _creationGate = new(1, 1);
    private readonly object _disposeSync = new();

    private PartialCreationOwner? _retainedPartialCreationOwner;
    private Task? _disposeTask;
    private long _cleanupTimeoutTicks = FactoryEntrantDrainTimeout.Ticks;
    private int _accepting = 1;
    private int _createEntrants;

    /// <summary>
    /// 建立 Factory。Registry 是 Organization capacity 的唯一共用邊界；Worker executor、Process、Pipe 與
    /// CRM SDK/WCF state 永遠不放入 Registry，也不由 Factory 跨呼叫快取。
    /// </summary>
    /// <param name="admissionRegistry">建立引用計數 admission registration 的 host-owned registry。</param>
    public DynamicsProfileRuntimeFactory(IOrganizationAdmissionRegistry admissionRegistry)
    {
        _admissionRegistry = admissionRegistry ??
            throw new ArgumentNullException(nameof(admissionRegistry));
    }

    /// <summary>
    /// 建立一個尚未發布但已完成全部 Worker READY handshake 的隔離 Runtime Generation。
    /// 方法先取得 admission registration，再依設定數量逐一啟動 Worker；每個 Worker 都驗證 executable hash、
    /// nonce、package lock、CE kind 與 worker-profile.xml generation identity。只有 Runtime 成功建構後 ownership 才轉移。
    /// </summary>
    /// <remarks>
    /// 任一 Worker 啟動、取消或 Runtime 建構失敗時，Factory 會先反向終止所有已啟動 Worker，
    /// 再釋放 admission registration；每一個 cleanup 都會被嘗試。原始失敗保持第一原因，
    /// cleanup failure 只以 AggregateException 附加，避免半建立 Process、Pipe、Registration 或 Host Slot 洩漏。
    /// </remarks>
    public async Task<IDynamicsProfileRuntime> CreateAsync(
        DynamicsProfileDefinition definition,
        long generation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (generation < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generation),
                "Runtime generation must be at least one.");
        }

        ObserveCleanupTimeout(definition.DrainTimeout);
        var cleanupDeadline = FactoryCleanupDeadline.Start(definition.DrainTimeout);
        Interlocked.Increment(ref _createEntrants);
        var gateAcquired = false;
        try
        {
            if (Volatile.Read(ref _accepting) == 0)
            {
                throw new ObjectDisposedException(nameof(DynamicsProfileRuntimeFactory));
            }

            var gateRemaining = cleanupDeadline.GetRemaining();
            if (gateRemaining <= TimeSpan.Zero ||
                !await _creationGate.WaitAsync(gateRemaining, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new InvalidOperationException(CleanupFailureMessage);
            }

            gateAcquired = true;
            if (Volatile.Read(ref _accepting) == 0)
            {
                throw new ObjectDisposedException(nameof(DynamicsProfileRuntimeFactory));
            }

            // Factory 嚴格只保留一個 composite partial-creation owner；舊 owner 未能歸零時禁止再取得
            // registration 或啟動新 process，避免 repeated failure 形成無界 resource collection。
            if (!await TryFinishRetainedPartialCreationCleanupAsync(cleanupDeadline)
                    .ConfigureAwait(false))
            {
                throw new InvalidOperationException(CleanupFailureMessage);
            }

            return await CreateCoreAsync(
                    definition,
                    generation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (gateAcquired)
            {
                _creationGate.Release();
            }

            Interlocked.Decrement(ref _createEntrants);
        }
    }

    /// <summary>
    /// 在 Factory 已取得唯一 creation gate 後建立未發布 Runtime。任一 rollback 未完成時，成功 Worker、
    /// startup owner、registration 與各自 cleanup attempt 會留在同一 composite owner；caller 仍收到原始
    /// timeout、cancellation 或 protocol failure，cleanup failure 不會取代第一原因。
    /// </summary>
    private async Task<IDynamicsProfileRuntime> CreateCoreAsync(
        DynamicsProfileDefinition definition,
        long generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var partialOwner = new PartialCreationOwner(definition.DrainTimeout);
        try
        {
            partialOwner.OwnRegistration(_admissionRegistry.Acquire(definition.AdmissionPlan));
            for (var index = 0; index < definition.WorkerCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 每次都建立全新的 options instance；雖然 worker-local profile identity 相同，
                // Process nonce、pipe name、operation gate 與 SDK/WCF graph 仍由 Supervisor 個別擁有。
                var worker = await OfficialWorkerProfileExecutor.StartAsync(
                    CreateWorkerOptions(definition),
                    cancellationToken).ConfigureAwait(false);
                partialOwner.OwnWorker(worker);
            }

            var key = new ProfileRuntimeKey(
                definition.ProfileAlias,
                generation,
                definition.CeVersion,
                definition.AdmissionPlan.CanonicalKey);
            var runtime = new DynamicsProfileRuntime(
                key,
                definition,
                partialOwner.Registration,
                partialOwner.Workers);

            partialOwner.ReleaseToRuntime();
            return runtime;
        }
        catch (Exception creationFailure)
        {
            var reportedFailure = creationFailure;
            if (creationFailure is OfficialWorkerStartupException startupOwner)
            {
                partialOwner.OwnStartupOwner(startupOwner);
                reportedFailure = startupOwner.InnerException ?? startupOwner;
            }

            var failures = new List<Exception> { reportedFailure };
            var cleanupFailureCountBefore = failures.Count;
            // 前置 gate／retained-owner deadline 不可消耗本次 partial owner 的 rollback budget；
            // ownership failure 當下才開始完整 DrainTimeout，避免啟動未被目前 caller 觀察完的 exit wait。
            var cleanupDeadline = FactoryCleanupDeadline.Start(partialOwner.CleanupTimeout);
            var cleanupComplete = await partialOwner.TryCleanupAsync(
                    cleanupDeadline,
                    failures)
                .ConfigureAwait(false);
            if (!cleanupComplete)
            {
                if (_retainedPartialCreationOwner is not null)
                {
                    throw new InvalidOperationException(CleanupFailureMessage);
                }

                _retainedPartialCreationOwner = partialOwner;
                if (failures.Count == cleanupFailureCountBefore)
                {
                    failures.Add(new InvalidOperationException(CleanupFailureMessage));
                }
            }

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(reportedFailure).Throw();
            }

            throw new AggregateException(
                "Official worker profile runtime construction and one or more rollback operations failed.",
                failures);
        }
    }

    /// <summary>
    /// 回傳 retained composite 內 startup generation 的 bounded snapshot。此 internal seam 供相容診斷與
    /// no-leak 測試使用，不暴露 owner 本身、Process、Pipe、Task、Nonce、Credential、Token 或 Session。
    /// </summary>
    internal OfficialWorkerLifecycleSnapshot? GetRetainedStartupLifecycleSnapshot() =>
        Volatile.Read(ref _retainedPartialCreationOwner)?.GetStartupLifecycleSnapshot();

    /// <summary>
    /// 回傳單一 composite partial-creation owner 的 bounded ownership counters。snapshot 只包含資源數量，
    /// 不暴露 Process、Pipe、Task、registration、Profile、Credential、Token、Session 或 caller state。
    /// </summary>
    internal PartialCreationLifecycleSnapshot? GetRetainedPartialCreationSnapshot() =>
        Volatile.Read(ref _retainedPartialCreationOwner)?.GetSnapshot();

    /// <summary>
    /// 同步相容路徑完整等待 Factory 的非同步 cleanup。Task.Run 只隔離舊同步 context；所有 retained
    /// Process／reader cleanup 與失敗仍由目前 caller 觀察，不建立 fire-and-forget owner。
    /// </summary>
    public void Dispose() =>
        Task.Run(async () => await DisposeAsync().ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// 關閉新 Create admission，並讓並行 Dispose caller 共享同一 cleanup attempt。失敗 attempt 不永久
    /// 快取；retained composite owner 與 creation gate 仍由 Factory 持有，下一次 Dispose 可安全重試。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Task task;
        lock (_disposeSync)
        {
            task = _disposeTask ??= DisposeAttemptAsync();
        }

        return new ValueTask(task);
    }

    /// <summary>
    /// 發布一次 Factory cleanup task；先讓出排程以確保並行 caller 取得相同 task。失敗時只清除 task
    /// owner，不清除 retained composite resource owner，亦不允許後續 Create 重新開啟 admission。
    /// </summary>
    private async Task DisposeAttemptAsync()
    {
        await Task.Yield();
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
        }
        catch
        {
            lock (_disposeSync)
            {
                _disposeTask = null;
            }

            throw;
        }
    }

    /// <summary>
    /// 先永久封閉 Create，再取得 creation gate 並清理唯一 retained composite owner。只有 owner 歸零且
    /// 所有已進入 CreateAsync 的 caller 都離開後才 Dispose semaphore，避免 use-after-dispose；任何
    /// 未確認狀態均以固定錯誤 fail closed，讓 DI host 可在下一次 shutdown attempt 重試。
    /// </summary>
    private async Task DisposeCoreAsync()
    {
        Volatile.Write(ref _accepting, 0);
        var cleanupDeadline = FactoryCleanupDeadline.Start(GetCleanupTimeout());
        var gateRemaining = cleanupDeadline.GetRemaining();
        if (gateRemaining <= TimeSpan.Zero ||
            !await _creationGate.WaitAsync(gateRemaining).ConfigureAwait(false))
        {
            throw new InvalidOperationException(CleanupFailureMessage);
        }

        var cleanupComplete = false;
        try
        {
            cleanupComplete = await TryFinishRetainedPartialCreationCleanupAsync(cleanupDeadline)
                .ConfigureAwait(false);
        }
        finally
        {
            _creationGate.Release();
        }

        if (!cleanupComplete ||
            !await WaitForCreateEntrantsToDrainAsync(cleanupDeadline).ConfigureAwait(false))
        {
            throw new InvalidOperationException(CleanupFailureMessage);
        }

        _creationGate.Dispose();
    }

    /// <summary>
    /// 對 Factory 目前唯一 retained composite owner 執行一次 bounded retry。成功且 snapshot 歸零後才
    /// 清除精確 owner reference；任何 cleanup 例外或非零 snapshot 都正規化為 false 並保留 reference，
    /// 由 Create 回報固定 cleanup failure、由 rollback 保留原始 creation failure 為第一原因。
    /// 此方法不建立第二個 owner、不重送 startup，也不釋放尚未確認歸零的 resource graph。
    /// </summary>
    private async Task<bool> TryFinishRetainedPartialCreationCleanupAsync(
        FactoryCleanupDeadline cleanupDeadline)
    {
        var owner = _retainedPartialCreationOwner;
        if (owner is null)
        {
            return true;
        }

        var cleanupFailures = new List<Exception>();
        if (!await owner.TryCleanupAsync(cleanupDeadline, cleanupFailures).ConfigureAwait(false))
        {
            return false;
        }

        if (ReferenceEquals(_retainedPartialCreationOwner, owner))
        {
            _retainedPartialCreationOwner = null;
        }

        return _retainedPartialCreationOwner is null;
    }

    /// <summary>
    /// 有限等待已進入 CreateAsync 的 caller（包含 gate waiter）離開。accepting 已永久關閉，因此歸零後
    /// 不會再有 caller 觸碰即將 Dispose 的 semaphore；timeout 時保留 gate owner供下一次 Dispose 重試。
    /// </summary>
    private async Task<bool> WaitForCreateEntrantsToDrainAsync(
        FactoryCleanupDeadline cleanupDeadline)
    {
        while (Volatile.Read(ref _createEntrants) != 0)
        {
            var remaining = cleanupDeadline.GetRemaining();
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(
                    remaining < TimeSpan.FromMilliseconds(10)
                        ? remaining
                        : TimeSpan.FromMilliseconds(10))
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Copies one immutable profile definition into an independently owned worker bootstrap snapshot.
    /// </summary>
    private static OfficialWorkerProfileOptions CreateWorkerOptions(
        DynamicsProfileDefinition definition)
    {
        var recycle = definition.RecyclePolicyOptions;
        return new OfficialWorkerProfileOptions
        {
            ProfileAlias = definition.ProfileAlias,
            ProfileGenerationId = definition.WorkerProfileGenerationId,
            WorkerVersion = definition.WorkerVersion,
            WorkerExecutablePath = definition.WorkerExecutablePath,
            WorkerExecutableSha256 = definition.WorkerExecutableSha256,
            PackageLockId = definition.PackageLockId,
            StartupTimeout = definition.StartupTimeout,
            OperationTimeout = definition.OperationTimeout,
            DrainTimeout = definition.DrainTimeout,
            RecyclePolicyOptions = new OfficialWorkerRecyclePolicyOptions(
                recycle.MaximumWorkerAge,
                recycle.MaximumCompletedOperations,
                recycle.MaximumPrivateBytes,
                recycle.MaximumWorkingSet,
                recycle.MaximumConsecutiveCompleteWorkerTimeouts)
        };
    }

    /// <summary>
    /// 記錄 Factory 曾接收的最短 DrainTimeout，讓 Dispose 在沒有 retained owner 可提供 timeout 時仍採用
    /// 保守且有限的 monotonic budget。只保存 ticks scalar，不保留 definition、Profile 或 caller reference。
    /// </summary>
    private void ObserveCleanupTimeout(TimeSpan cleanupTimeout)
    {
        var candidate = cleanupTimeout.Ticks;
        while (true)
        {
            var current = Interlocked.Read(ref _cleanupTimeoutTicks);
            if (current <= candidate ||
                Interlocked.CompareExchange(ref _cleanupTimeoutTicks, candidate, current) == current)
            {
                return;
            }
        }
    }

    /// <summary>取得 Factory cleanup attempt 的有限 budget；無任何 Create 時使用固定 entrant 上限。</summary>
    private TimeSpan GetCleanupTimeout()
    {
        var retainedTimeout = Volatile.Read(ref _retainedPartialCreationOwner)?.CleanupTimeout;
        var observedTimeout = TimeSpan.FromTicks(Interlocked.Read(ref _cleanupTimeoutTicks));
        return retainedTimeout.HasValue && retainedTimeout.Value < observedTimeout
            ? retainedTimeout.Value
            : observedTimeout;
    }

    /// <summary>
    /// 以 Stopwatch 建立 Factory lifecycle attempt 的絕對 deadline。creation gate、retained retry、所有
    /// rollback task observation 與 entrant drain 都只能消耗 remaining budget，不建立 Timer 或 CTS。
    /// </summary>
    private readonly struct FactoryCleanupDeadline
    {
        private readonly long _startedTimestamp;
        private readonly TimeSpan _timeout;

        private FactoryCleanupDeadline(long startedTimestamp, TimeSpan timeout)
        {
            _startedTimestamp = startedTimestamp;
            _timeout = timeout;
        }

        internal static FactoryCleanupDeadline Start(TimeSpan timeout) =>
            new(Stopwatch.GetTimestamp(), timeout);

        internal TimeSpan GetRemaining()
        {
            var elapsed = Stopwatch.GetElapsedTime(_startedTimestamp);
            return elapsed >= _timeout ? TimeSpan.Zero : _timeout - elapsed;
        }
    }

    /// <summary>
    /// 擁有一次尚未發布 Runtime 的完整部分建立 graph：registration、所有已 READY workers，以及失敗
    /// startup executor owner。每項 cleanup task 都保存在自己的 entry；deadline 到期時 task 不會遺失，
    /// 下一次 serialized retry 先觀察同一 task，只有 terminal failure 才允許重新呼叫 Dispose。成功項目
    /// 立即從 composite 移除，未確認項目持續被強引用，composite 歸零前 Factory 禁止下一個 Create。
    /// </summary>
    private sealed class PartialCreationOwner
    {
        private readonly object _sync = new();
        private readonly List<CleanupEntry<OfficialWorkerProfileExecutor>> _workers = [];
        private CleanupEntry<OfficialWorkerStartupException>? _startupOwner;
        private CleanupEntry<IOrganizationAdmissionRegistration>? _registration;
        private bool _releasedToRuntime;

        internal PartialCreationOwner(TimeSpan cleanupTimeout)
        {
            CleanupTimeout = cleanupTimeout;
        }

        internal TimeSpan CleanupTimeout { get; }

        internal IOrganizationAdmissionRegistration Registration
        {
            get
            {
                lock (_sync)
                {
                    return _registration?.Resource ?? throw new InvalidOperationException();
                }
            }
        }

        internal IReadOnlyCollection<OfficialWorkerProfileExecutor> Workers
        {
            get
            {
                lock (_sync)
                {
                    return _workers.Select(static entry => entry.Resource).ToArray();
                }
            }
        }

        internal void OwnRegistration(IOrganizationAdmissionRegistration registration)
        {
            ArgumentNullException.ThrowIfNull(registration);
            lock (_sync)
            {
                if (_releasedToRuntime || _registration is not null)
                {
                    throw new InvalidOperationException();
                }

                _registration = new CleanupEntry<IOrganizationAdmissionRegistration>(
                    registration,
                    static owner => owner.DisposeAsync().AsTask(),
                    static _ => true,
                    allowCompletionAfterFailure: false);
            }
        }

        internal void OwnWorker(OfficialWorkerProfileExecutor worker)
        {
            ArgumentNullException.ThrowIfNull(worker);
            lock (_sync)
            {
                if (_releasedToRuntime)
                {
                    throw new InvalidOperationException();
                }

                _workers.Add(new CleanupEntry<OfficialWorkerProfileExecutor>(
                    worker,
                    static owner => owner.DisposeAsync().AsTask(),
                    static owner => IsFullyReleased(owner.GetLifecycleSnapshot()),
                    allowCompletionAfterFailure: true));
            }
        }

        internal void OwnStartupOwner(OfficialWorkerStartupException startupOwner)
        {
            ArgumentNullException.ThrowIfNull(startupOwner);
            lock (_sync)
            {
                if (_releasedToRuntime || _startupOwner is not null)
                {
                    throw new InvalidOperationException();
                }

                _startupOwner = new CleanupEntry<OfficialWorkerStartupException>(
                    startupOwner,
                    static owner => owner.DisposeAsync().AsTask(),
                    static owner => IsFullyReleased(owner.GetLifecycleSnapshot()),
                    allowCompletionAfterFailure: true);
            }
        }

        /// <summary>
        /// 在 Runtime constructor 成功後原子放棄所有 partial ownership。constructor 失敗前不可呼叫，
        /// 否則 registration／workers 會同時沒有 Factory 或 Runtime owner。
        /// </summary>
        internal void ReleaseToRuntime()
        {
            lock (_sync)
            {
                if (_releasedToRuntime || _registration is null || _startupOwner is not null)
                {
                    throw new InvalidOperationException();
                }

                _releasedToRuntime = true;
                _registration = null;
                _workers.Clear();
            }
        }

        /// <summary>
        /// 依 acquisition 反序啟動 cleanup（startup、workers reverse、registration），再只等待共同
        /// absolute deadline 的 remaining budget。所有 entry 都保存 attempt task，故 timeout 不會形成
        /// fire-and-forget；完成失敗會被觀察並保留 resource 供 retry，只有成功且 snapshot 歸零才移除。
        /// </summary>
        internal async Task<bool> TryCleanupAsync(
            FactoryCleanupDeadline cleanupDeadline,
            ICollection<Exception> failures)
        {
            ArgumentNullException.ThrowIfNull(failures);
            ICleanupEntry[] entries;
            lock (_sync)
            {
                if (_releasedToRuntime)
                {
                    return true;
                }

                var ordered = new List<ICleanupEntry>(_workers.Count + 2);
                if (_startupOwner is not null)
                {
                    ordered.Add(_startupOwner);
                }

                for (var index = _workers.Count - 1; index >= 0; index--)
                {
                    ordered.Add(_workers[index]);
                }

                if (_registration is not null)
                {
                    ordered.Add(_registration);
                }

                entries = ordered.ToArray();
            }

            // rollback 必須逐層、反向完成 ownership，而不是只按照反向順序同時啟動。
            // 較上層 Worker 若仍保留 Process/reader owner，提早釋放 admission registration
            // 會讓新 generation 與未退休舊行程重疊，破壞容量與 Session 隔離。
            foreach (var entry in entries)
            {
                var attempt = entry.GetOrStartAttempt();
                if (!await WaitForCleanupAttemptCompletionAsync(attempt, cleanupDeadline)
                        .ConfigureAwait(false))
                {
                    return false;
                }

                if (entry.ObserveCompletedAttempt(failures) != CleanupAttemptOutcome.Complete)
                {
                    return false;
                }

                RemoveExactEntry(entry);
            }

            lock (_sync)
            {
                return _startupOwner is null && _workers.Count == 0 && _registration is null;
            }
        }

        internal OfficialWorkerLifecycleSnapshot? GetStartupLifecycleSnapshot()
        {
            lock (_sync)
            {
                return _startupOwner?.Resource.GetLifecycleSnapshot();
            }
        }

        internal PartialCreationLifecycleSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                var workerSnapshots = _workers
                    .Select(static entry => entry.Resource.GetLifecycleSnapshot())
                    .ToArray();
                var startupSnapshot = _startupOwner?.Resource.GetLifecycleSnapshot();
                return new PartialCreationLifecycleSnapshot(
                    _workers.Count,
                    _startupOwner is null ? 0 : 1,
                    _registration is null ? 0 : 1,
                    (_startupOwner?.OwnedAttemptCount ?? 0) +
                    _workers.Sum(static entry => entry.OwnedAttemptCount) +
                    (_registration?.OwnedAttemptCount ?? 0),
                    workerSnapshots.Sum(static snapshot => snapshot.OwnedProcessCount) +
                    (startupSnapshot?.OwnedProcessCount ?? 0),
                    workerSnapshots.Sum(static snapshot => snapshot.OwnedPipeCount) +
                    (startupSnapshot?.OwnedPipeCount ?? 0),
                    workerSnapshots.Sum(static snapshot => snapshot.OwnedBackgroundTaskCount) +
                    (startupSnapshot?.OwnedBackgroundTaskCount ?? 0),
                    workerSnapshots.Sum(static snapshot => snapshot.OwnedOperationGateCount) +
                    (startupSnapshot?.OwnedOperationGateCount ?? 0),
                    workerSnapshots.Sum(static snapshot => snapshot.OwnedOutputReaderCount) +
                    (startupSnapshot?.OwnedOutputReaderCount ?? 0),
                    workerSnapshots.Sum(static snapshot => snapshot.OwnedOutputTaskCount) +
                    (startupSnapshot?.OwnedOutputTaskCount ?? 0),
                    workerSnapshots.Sum(static snapshot => snapshot.OwnedOutputCancellationSourceCount) +
                    (startupSnapshot?.OwnedOutputCancellationSourceCount ?? 0),
                    workerSnapshots.Sum(static snapshot => snapshot.OwnedProcessExitWaitCount) +
                    (startupSnapshot?.OwnedProcessExitWaitCount ?? 0),
                    workerSnapshots.Sum(static snapshot => snapshot.OperationEntrantCount) +
                    (startupSnapshot?.OperationEntrantCount ?? 0),
                    workerSnapshots.Sum(static snapshot => snapshot.ActiveOperationCount) +
                    (startupSnapshot?.ActiveOperationCount ?? 0));
            }
        }

        private void RemoveExactEntry(ICleanupEntry entry)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_startupOwner, entry))
                {
                    _startupOwner = null;
                    return;
                }

                if (ReferenceEquals(_registration, entry))
                {
                    _registration = null;
                    return;
                }

                for (var index = _workers.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(_workers[index], entry))
                    {
                        _workers.RemoveAt(index);
                        return;
                    }
                }
            }
        }

        private static bool IsFullyReleased(OfficialWorkerLifecycleSnapshot snapshot) =>
            snapshot.OwnedProcessCount == 0 &&
            snapshot.OwnedPipeCount == 0 &&
            snapshot.OwnedBackgroundTaskCount == 0 &&
            snapshot.OwnedOperationGateCount == 0 &&
            snapshot.OwnedOutputReaderCount == 0 &&
            snapshot.OwnedOutputTaskCount == 0 &&
            snapshot.OwnedOutputCancellationSourceCount == 0 &&
            snapshot.OwnedProcessExitWaitCount == 0 &&
            snapshot.OperationEntrantCount == 0 &&
            snapshot.ActiveOperationCount == 0;

        /// <summary>
        /// 以單一 monotonic deadline 輪詢 exact cleanup task 是否 terminal。刻意不對尚未完成的
        /// task 建立 <c>Task.WhenAll</c> 或 <c>WaitAsync</c> continuation，避免每次 retry 都把
        /// 新 aggregate graph 掛在同一個長時間 owner 上。輪詢只暫時擁有 bounded Delay timer，
        /// 返回後即釋放；resource entry 則始終只保留同一個 cleanup attempt。
        /// </summary>
        private static async Task<bool> WaitForCleanupAttemptCompletionAsync(
            Task attempt,
            FactoryCleanupDeadline cleanupDeadline)
        {
            while (!attempt.IsCompleted)
            {
                var remaining = cleanupDeadline.GetRemaining();
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }

                await Task.Delay(
                        remaining < TimeSpan.FromMilliseconds(10)
                            ? remaining
                            : TimeSpan.FromMilliseconds(10))
                    .ConfigureAwait(false);
            }

            return true;
        }

        private interface ICleanupEntry
        {
            int OwnedAttemptCount { get; }

            Task GetOrStartAttempt();

            CleanupAttemptOutcome ObserveCompletedAttempt(ICollection<Exception> failures);
        }

        private enum CleanupAttemptOutcome
        {
            Incomplete = 0,
            Complete = 1
        }

        /// <summary>
        /// 保存單一 resource 與至多一個進行中的 Dispose task。Task 未 terminal 時後續 retry 只能等待同一
        /// task；fault 被觀察後才清除 attempt 並允許下一次 Dispose，避免兩個 cleanup owner 同時觸碰資源。
        /// </summary>
        private sealed class CleanupEntry<TResource> : ICleanupEntry
            where TResource : class
        {
            private readonly object _sync = new();
            private readonly Func<TResource, Task> _disposeAsync;
            private readonly Func<TResource, bool> _isCleanupComplete;
            private readonly bool _allowCompletionAfterFailure;
            private Task? _attempt;

            internal CleanupEntry(
                TResource resource,
                Func<TResource, Task> disposeAsync,
                Func<TResource, bool> isCleanupComplete,
                bool allowCompletionAfterFailure)
            {
                Resource = resource;
                _disposeAsync = disposeAsync;
                _isCleanupComplete = isCleanupComplete;
                _allowCompletionAfterFailure = allowCompletionAfterFailure;
            }

            internal TResource Resource { get; }

            /// <summary>
            /// 回報此 exact entry 目前保留的 cleanup task owner 數量；合法值只有零或一，
            /// 讓 retry retention 能由 snapshot 直接驗證。
            /// </summary>
            public int OwnedAttemptCount
            {
                get
                {
                    lock (_sync)
                    {
                        return _attempt is null ? 0 : 1;
                    }
                }
            }

            public Task GetOrStartAttempt()
            {
                lock (_sync)
                {
                    if (_attempt is not null)
                    {
                        return _attempt;
                    }

                    try
                    {
                        _attempt = _disposeAsync(Resource);
                    }
                    catch (Exception failure)
                    {
                        _attempt = Task.FromException(failure);
                    }

                    return _attempt;
                }
            }

            public CleanupAttemptOutcome ObserveCompletedAttempt(
                ICollection<Exception> failures)
            {
                Task? attempt;
                lock (_sync)
                {
                    attempt = _attempt;
                }

                if (attempt is null || !attempt.IsCompleted)
                {
                    return CleanupAttemptOutcome.Incomplete;
                }

                var attemptSucceeded = false;
                try
                {
                    attempt.GetAwaiter().GetResult();
                    attemptSucceeded = true;
                }
                catch (AggregateException aggregateException)
                {
                    foreach (var failure in aggregateException.Flatten().InnerExceptions)
                    {
                        failures.Add(failure);
                    }
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }

                // Worker 與 startup owner 有獨立 lifecycle snapshot；即使 Dispose 回報原始
                // failure，只要 snapshot 證明資源已歸零，仍可移除 exact owner 並繼續下層。
                // Registration 沒有等價 snapshot，必須 Dispose 成功才可判定完成。
                if (attemptSucceeded || _allowCompletionAfterFailure)
                {
                    try
                    {
                        if (_isCleanupComplete(Resource))
                        {
                            return CleanupAttemptOutcome.Complete;
                        }
                    }
                    catch (Exception failure)
                    {
                        failures.Add(failure);
                    }
                }

                lock (_sync)
                {
                    if (ReferenceEquals(_attempt, attempt))
                    {
                        _attempt = null;
                    }
                }

                return CleanupAttemptOutcome.Incomplete;
            }
        }
    }
}

/// <summary>
/// Factory retained composite owner 的 bounded diagnostic snapshot；只揭露 ownership counters，不包含
/// Process、Pipe、Task、registration、Profile、Credential、Token、Session 或任何 mutable reference。
/// </summary>
internal readonly record struct PartialCreationLifecycleSnapshot(
    int OwnedWorkerCount,
    int OwnedStartupOwnerCount,
    int OwnedRegistrationCount,
    int OwnedCleanupAttemptCount,
    int OwnedProcessCount,
    int OwnedPipeCount,
    int OwnedBackgroundTaskCount,
    int OwnedOperationGateCount,
    int OwnedOutputReaderCount,
    int OwnedOutputTaskCount,
    int OwnedOutputCancellationSourceCount,
    int OwnedProcessExitWaitCount,
    int OperationEntrantCount,
    int ActiveOperationCount);
