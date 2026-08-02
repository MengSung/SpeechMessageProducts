// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Runtime/DynamicsProfileRuntime.cs
// 目的：擁有一個 immutable Profile Generation 的 Worker process pool、執行租約與可重試 drain。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// 官方 Worker-backed 的單一 Dynamics Profile Runtime Generation。
/// Runtime 唯一擁有本 generation 的 <see cref="OfficialWorkerProfileExecutor"/> 集合、Process／Pipe graph、
/// retirement CTS 與 admission registration；不同 alias、CE version、credential profile 或 ControlPlane generation
/// 不會共用任何 Worker executor、request gate、pipe、SDK/WCF state 或 caller Session。
/// </summary>
/// <remarks>
/// 所有 State 與 active execution count 都在 <see cref="_lifecycleGate"/> 內變更。
/// <see cref="BeginDrain"/> 先封閉新租約，最後一個租約歸還後才依 Worker→Admission Registration 順序清理；
/// 若 caller cancellation 或 timeout 中斷 drain，Runtime 保持 Draining 並清除 faulted drain task，
/// 讓下一個 replacement/shutdown owner 可重試同一套資源，而不是配置第三個 generation 或留下孤兒 process。
/// </remarks>
public sealed class DynamicsProfileRuntime : IDynamicsProfileRuntime
{
    private readonly object _lifecycleGate = new();
    private readonly DynamicsProfileDefinition _definition;
    private readonly IOrganizationAdmissionRegistration _admissionRegistration;
    private readonly OfficialWorkerProfileExecutor[] _workers;
    private readonly WorkerPoolOperationExecutor _executor;
    private readonly ControlledOperationExecutor _warmUpExecutor;
    private readonly CancellationTokenSource _retirementCts = new();

    private TaskCompletionSource _zeroExecutions = CreateCompletedSignal();
    private Task? _drainTask;
    private DynamicsProfileRuntimeState _state = DynamicsProfileRuntimeState.Active;
    private int _activeExecutionCount;

    /// <summary>
    /// 建立一個已完成 Worker READY handshake、但尚未由 Manager 發布的 Active Runtime。
    /// Factory 轉移 Workers 與 Registration 的唯一 ownership；constructor 不建立新 Process、不解析 Credential，
    /// 也不保存 IConfiguration、Request、User、LINE ID、JWT、Token、Cookie 或 Session。
    /// </summary>
    /// <param name="key">不含秘密的 ControlPlane runtime generation key。</param>
    /// <param name="definition">建立此 generation 的 immutable deployment definition。</param>
    /// <param name="admissionRegistration">此 generation 對共享 Organization admission manager 的唯一 registration。</param>
    /// <param name="workers">已 READY 且完全隔離的 bounded Worker executor 集合。</param>
    internal DynamicsProfileRuntime(
        ProfileRuntimeKey key,
        DynamicsProfileDefinition definition,
        IOrganizationAdmissionRegistration admissionRegistration,
        IReadOnlyCollection<OfficialWorkerProfileExecutor> workers)
    {
        if (key.Generation < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "Runtime generation must be at least one.");
        }

        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(admissionRegistration);
        ArgumentNullException.ThrowIfNull(workers);
        if (workers.Count != definition.WorkerCount || workers.Count == 0)
        {
            throw new ArgumentException(
                "The runtime must own exactly the configured number of ready official workers.",
                nameof(workers));
        }

        Key = key;
        _definition = definition;
        _admissionRegistration = admissionRegistration;
        _workers = workers.ToArray();
        if (_workers.Any(static worker => worker is null))
        {
            throw new ArgumentException("Worker collection cannot contain null.", nameof(workers));
        }

        _executor = new WorkerPoolOperationExecutor(_workers);
        _warmUpExecutor = new ControlledOperationExecutor(_executor, admissionRegistration.Manager);
    }

    /// <summary>
    /// 取得此不可變 generation 的非秘密結構化 key；它不包含 worker-profile.xml 內容、Credential、
    /// Token、User、LINE ID、JWT、Session 或 request correlation。
    /// </summary>
    public ProfileRuntimeKey Key { get; }

    /// <summary>取得 Active→Draining→Disposed 的單向狀態快照。</summary>
    public DynamicsProfileRuntimeState State
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// 取得目前尚未釋放的 generation execution lease 數量；它不包含 admission queue waiter，
    /// 也不以 user/session 為維度，因此只可用於 drain 與 bounded lifecycle telemetry。
    /// </summary>
    public int ActiveExecutionCount
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _activeExecutionCount;
            }
        }
    }

    /// <summary>
    /// 取得 registration 所屬的共享 Organization admission manager。
    /// Runtime 只擁有 registration，不直接 Dispose manager；最後一個 registration 由 registry 統一回收 manager 與 host slot。
    /// </summary>
    public IOrganizationAdmissionManager AdmissionManager => _admissionRegistration.Manager;

    /// <summary>取得不含秘密、Session 或 Worker object reference 的即時 admission 指標。</summary>
    public AdmissionMetricsSnapshot AdmissionSnapshot => _admissionRegistration.Manager.GetSnapshot();

    /// <summary>
    /// 只在 State 仍為 Active 時原子增加執行引用並建立 lease。
    /// BeginDrain 之後立即 fail closed，避免新要求在 Worker termination 或 Pipe disposal 期間取得 use-after-dispose 參考。
    /// </summary>
    public bool TryAcquireExecution(out IDynamicsProfileExecutionLease? lease)
    {
        lock (_lifecycleGate)
        {
            if (_state != DynamicsProfileRuntimeState.Active)
            {
                lease = null;
                return false;
            }

            if (_activeExecutionCount == 0)
            {
                _zeroExecutions = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            checked
            {
                _activeExecutionCount++;
            }

            lease = new ExecutionLease(this);
            return true;
        }
    }

    /// <summary>
    /// 以固定服務 workload 執行 registry-owned WhoAmI warm-up。
    /// Warm-up 仍先取得 Organization admission permit，並觀察 caller、host lease-loss 與 bounded operation timeout；
    /// 它不接收登入使用者、LINE ID、browser session 或 caller credential，也不建立 user-keyed connection/session cache。
    /// </summary>
    public Task<OperationExecutionResult> WarmUpAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleGate)
        {
            if (_state != DynamicsProfileRuntimeState.Active)
            {
                return Task.FromResult(OperationExecutionResult.Failure(
                    DynamicsErrorCodes.NotReady,
                    "Profile runtime is not active for warm-up."));
            }
        }

        return _warmUpExecutor.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = Key.ProfileAlias,
                CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                WorkloadSubjectId = "dynamics-runtime-warmup"
            },
            cancellationToken);
    }

    /// <summary>
    /// 原子把 Active 切換為 Draining 並永久封閉新 lease。
    /// 方法可重複呼叫，不會取消仍在自然 drain 期限內的工作，也不會提前釋放 admission capacity。
    /// </summary>
    public void BeginDrain()
    {
        lock (_lifecycleGate)
        {
            if (_state == DynamicsProfileRuntimeState.Active)
            {
                _state = DynamicsProfileRuntimeState.Draining;
            }
        }
    }

    /// <summary>
    /// 啟動或共用目前唯一 drain 嘗試：先等待 active lease 自然歸零，逾時才取消 retirement token，
    /// 再等待有限 cleanup grace。只有 active count 為零時才終止 Workers 並釋放 admission registration。
    /// caller cancellation／timeout 會清除 cached task 但保留 Draining ownership，使後續呼叫可重試。
    /// </summary>
    public Task DrainAndDisposeAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            if (_state == DynamicsProfileRuntimeState.Disposed)
            {
                return Task.CompletedTask;
            }

            if (_drainTask is not null)
            {
                return _drainTask;
            }

            _drainTask = DrainAttemptAsync(cancellationToken);
            return _drainTask;
        }
    }

    /// <summary>
    /// 同步 Dispose 仍完整等待非同步 Worker process 與 registration cleanup，不使用 fire-and-forget。
    /// Task.Run 只隔離舊同步 context；所有失敗仍由 caller 觀察。
    /// </summary>
    public void Dispose()
        => Task.Run(async () => await DrainAndDisposeAsync().ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();

    /// <summary>非同步 Dispose 委派到同一個可重入、可重試 drain state machine。</summary>
    public ValueTask DisposeAsync()
        => new(DrainAndDisposeAsync());

    /// <summary>
    /// 回傳每個 Worker 的 bounded lifecycle counter snapshot，供 readiness、soak 與 no-leak 驗證。
    /// 回傳值不暴露 Process、Pipe、request、Credential、Token、CRM SDK object 或 mutable worker state。
    /// </summary>
    internal IReadOnlyList<OfficialWorkerLifecycleSnapshot> GetWorkerLifecycleSnapshots()
        => _workers.Select(static worker => worker.GetLifecycleSnapshot()).ToArray();

    /// <summary>
    /// 執行一次 drain 嘗試。自然期限超過才發出 retirement cancellation；若 caller cancellation、
    /// timeout 或 cleanup failure 發生而 Runtime 尚未 terminal，必須清除 cached task 讓同一資源可再次被接管。
    /// </summary>
    private async Task DrainAttemptAsync(CancellationToken cancellationToken)
    {
        try
        {
            BeginDrain();
            var zeroTask = GetZeroExecutionTask();
            try
            {
                await zeroTask.WaitAsync(_definition.DrainTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // retirement CTS 只在自然 drain 超時後取消；過早取消會讓仍在核准期限內的讀取無故失敗。
                _retirementCts.Cancel();
                await zeroTask.WaitAsync(
                    _definition.CancellationGracePeriod,
                    cancellationToken).ConfigureAwait(false);
            }

            await DisposeOwnedResourcesAsync().ConfigureAwait(false);
        }
        catch
        {
            lock (_lifecycleGate)
            {
                if (_state != DynamicsProfileRuntimeState.Disposed)
                {
                    _drainTask = null;
                }
            }

            throw;
        }
    }

    /// <summary>取得目前 active execution 歸零訊號；Task 不保存 request 或 Worker result。</summary>
    private Task GetZeroExecutionTask()
    {
        lock (_lifecycleGate)
        {
            return _activeExecutionCount == 0
                ? Task.CompletedTask
                : _zeroExecutions.Task;
        }
    }

    /// <summary>
    /// 在 active count 歸零後確定性回收全部 generation-owned 資源。
    /// Workers 先停止並關閉 Process／Pipe／background drains，registration 最後釋放 host slot 與 admission manager；
    /// 此順序避免舊 process 尚可能接觸 Dynamics 時，新 host 已拿回完整 capacity。每一項 cleanup 都會被嘗試並彙整失敗。
    /// </summary>
    private async Task DisposeOwnedResourcesAsync()
    {
        lock (_lifecycleGate)
        {
            if (_state == DynamicsProfileRuntimeState.Disposed)
            {
                return;
            }

            if (_activeExecutionCount != 0)
            {
                throw new InvalidOperationException(
                    "Profile runtime resources cannot be disposed while execution leases remain active.");
            }
        }

        List<Exception>? failures = null;
        foreach (var worker in _workers)
        {
            await CaptureFailureAsync(
                async () => await worker.DisposeAsync().ConfigureAwait(false),
                exception => (failures ??= []).Add(exception)).ConfigureAwait(false);
        }

        await CaptureFailureAsync(
            async () => await _admissionRegistration.DisposeAsync().ConfigureAwait(false),
            exception => (failures ??= []).Add(exception)).ConfigureAwait(false);

        _retirementCts.Dispose();
        lock (_lifecycleGate)
        {
            _state = DynamicsProfileRuntimeState.Disposed;
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException(
                "One or more official worker profile runtime resources failed to dispose.",
                failures);
        }
    }

    /// <summary>執行單一 cleanup 並收集例外，使後續 Worker 與 registration 仍可繼續回收。</summary>
    private static async Task CaptureFailureAsync(
        Func<Task> action,
        Action<Exception> capture)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            capture(exception);
        }
    }

    /// <summary>建立初始已完成的 active-count 歸零訊號。</summary>
    private static TaskCompletionSource CreateCompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult();
        return signal;
    }

    /// <summary>
    /// 釋放一個 execution reference；最後一個 lease 只喚醒 drain，不在 caller finally 內重入 Worker disposal。
    /// </summary>
    private void ReleaseExecution()
    {
        TaskCompletionSource? zeroSignal = null;
        lock (_lifecycleGate)
        {
            if (_activeExecutionCount <= 0)
            {
                throw new InvalidOperationException("Profile runtime execution reference count underflow.");
            }

            _activeExecutionCount--;
            if (_activeExecutionCount == 0)
            {
                zeroSignal = _zeroExecutions;
            }
        }

        zeroSignal?.TrySetResult();
    }

    /// <summary>
    /// 把多個單 Worker executor 投影成一個 generation-local SDK-free executor。
    /// Round-robin index 是唯一共享 scalar；每個實際 Worker 仍由自己的單一 operation gate 保證
    /// MaxInFlightPerWorker=1，且失敗要求不會重送到另一個 Worker、版本、profile 或 transport。
    /// </summary>
    private sealed class WorkerPoolOperationExecutor : IDynamicsOperationExecutor
    {
        private readonly OfficialWorkerProfileExecutor[] _workers;
        private int _nextWorker = -1;

        /// <summary>建立不擁有 Worker disposal 的 dispatch view；唯一 owner 仍是外層 Runtime。</summary>
        public WorkerPoolOperationExecutor(OfficialWorkerProfileExecutor[] workers)
        {
            _workers = workers;
        }

        /// <summary>
        /// 為每次新要求選取一個固定 Worker 並只執行一次；Worker 失敗或取消不會在 pool 內 replay。
        /// uint modulo 可在長時間執行造成 int rollover 時維持 bounded index，不建立 queue 或 collection。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            var index = (int)((uint)Interlocked.Increment(ref _nextWorker) % (uint)_workers.Length);
            return _workers[index].ExecuteAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Runtime-owned execution lease。Lease 只保存 Runtime 強引用與 executor view，不複製 request、
    /// result、Credential、Token 或 Session；同步/非同步 Dispose 競速時只遞減一次 active count。
    /// </summary>
    private sealed class ExecutionLease : IDynamicsProfileExecutionLease
    {
        private readonly DynamicsProfileRuntime _owner;
        private int _disposed;

        /// <summary>建立已由 Runtime 原子增加 active count 的 lease。</summary>
        public ExecutionLease(DynamicsProfileRuntime owner)
        {
            _owner = owner;
        }

        /// <summary>取得本 lease 固定的 ControlPlane runtime generation key。</summary>
        public ProfileRuntimeKey RuntimeKey => _owner.Key;

        /// <summary>取得本 generation 唯一的 SDK-free worker-pool executor view。</summary>
        public IDynamicsOperationExecutor Executor => _owner._executor;

        /// <summary>取得自然 drain 超時後用來取消尚未完成外呼的 retirement token。</summary>
        public CancellationToken RetirementToken => _owner._retirementCts.Token;

        /// <summary>同步釋放 active reference；沒有 I/O 或背景工作。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.ReleaseExecution();
            }
        }

        /// <summary>非同步相容路徑與同步 Dispose 共用同一個 idempotent reference release。</summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
