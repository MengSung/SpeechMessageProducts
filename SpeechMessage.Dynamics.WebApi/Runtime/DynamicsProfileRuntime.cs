// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntime.cs
// 目的：實作單一 Profile Generation 的執行引用計數、Active→Draining→Disposed 單向狀態、
//       有界取消與 Token／Transport／Admission Registration 的確定性回收。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 一個不可變 Profile Generation 的 Runtime 實作。
/// Runtime 唯一擁有 Generation-local Client、Transport、Token Provider、Retirement CTS 與 Admission Registration；
/// Manager 只能透過 Execution Lease 暫時使用 Client，不得把 Client／Token／Handler 快取到 Runtime 外。
/// 所有狀態與 active count 都在 <see cref="_lifecycleGate"/> 內變更，確保 Generation 發布、停止新 Lease、
/// 最後 Lease 釋放與資源 Dispose 之間沒有 use-after-dispose race。
/// </summary>
public sealed class DynamicsProfileRuntime : IDynamicsProfileRuntime
{
    private readonly object _lifecycleGate = new();
    private readonly DynamicsProfileDefinition _definition;
    private readonly IOrganizationAdmissionRegistration _admissionRegistration;
    private readonly ControlledOperationExecutor _warmUpExecutor;
    private readonly CancellationTokenSource _retirementCts = new();
    private TaskCompletionSource _zeroExecutions = CreateCompletedSignal();
    private Task? _drainTask;
    private DynamicsProfileRuntimeState _state = DynamicsProfileRuntimeState.Active;
    private int _activeExecutionCount;

    // 這三個欄位名稱同時是 ownership 測試的觀測 seam；它們不可改成 static 或跨 Generation 共用。
    private readonly IDynamicsWebApiClient _client;
    private readonly DynamicsHttpTransport _transport;
    private readonly AdfsOAuthTokenProvider _tokenProvider;

    /// <summary>
    /// 建立一個已完整配置但尚未由 Manager 發布的 Active Runtime。
    /// 所有參數都必須由 Factory 建立並轉移唯一 ownership；Constructor 不執行網路、不啟動背景 Task，
    /// 也不複製或解析終端使用者 Session／JWT／Token。
    /// </summary>
    public DynamicsProfileRuntime(
        ProfileRuntimeKey key,
        DynamicsProfileDefinition definition,
        IOrganizationAdmissionRegistration admissionRegistration,
        DynamicsHttpTransport transport,
        AdfsOAuthTokenProvider tokenProvider,
        IDynamicsWebApiClient client,
        ControlledOperationExecutor warmUpExecutor)
    {
        if (key.Generation < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "Runtime generation must be at least one.");
        }

        Key = key;
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _admissionRegistration = admissionRegistration ?? throw new ArgumentNullException(nameof(admissionRegistration));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _warmUpExecutor = warmUpExecutor ?? throw new ArgumentNullException(nameof(warmUpExecutor));
    }

    /// <summary>
    /// 取得此不可變 Generation 的非秘密結構化 Key；Key 固定 Alias、Generation、CE Version 與 Canonical Organization，
    /// 不包含 Credential、Token、User、LINE ID、JWT 或 Session，也不得被用來跨 Generation 共用可變 Client 狀態。
    /// </summary>
    public ProfileRuntimeKey Key { get; }

    /// <summary>
    /// 取得 Active、Draining 或 Disposed 的單向生命週期狀態。讀取與轉換都受同一鎖保護，
    /// 因此呼叫端不會看到已停止新 Lease 卻仍標示 Active 的不一致狀態。
    /// </summary>
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
    /// 取得尚未釋放的 Runtime Execution Lease 數量；此數值只表示 Generation-local active reference，
    /// 不等同於 Organization Admission Permit 數量，也不包含 Queue 中尚未 dispatch 的工作。
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
    /// 取得此 Runtime Registration 綁定的共享 Organization Admission Manager。
    /// Runtime 不擁有 Manager 本體，只擁有 Registration；最後一個 Registration 釋放時才由 Registry 回收 Manager。
    /// </summary>
    public IOrganizationAdmissionManager AdmissionManager => _admissionRegistration.Manager;

    /// <summary>
    /// 取得不含 Request、Credential、Token 或 Session 的 bounded Admission 快照，供 Readiness 與測試觀測容量生命週期。
    /// </summary>
    public AdmissionMetricsSnapshot AdmissionSnapshot => _admissionRegistration.Manager.GetSnapshot();

    /// <summary>
    /// 只在 Runtime 仍為 Active 時原子增加 execution reference 並建立 Lease；BeginDrain 之後立即 fail closed。
    /// Lease 唯一擁有這次 active reference，呼叫端必須確定性 Dispose，否則 drain 會等待到有界取消期限。
    /// 方法不執行 I/O，也不複製 Request、Token、Credential 或終端使用者狀態。
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
    /// 以固定服務工作負載執行受控 WhoAmI warm-up，預先驗證 Admission、Token 與 Transport 路徑。
    /// Warm-up 不接受登入使用者、LINE ID、JWT、Cookie 或自訂 CRM 操作；Runtime 非 Active 時回傳受控 NotReady，
    /// 呼叫者取消會沿既有受控執行鏈傳遞，不會建立使用者專屬 Connection／Token Cache。
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

        // Warm-up 使用固定伺服器端 Operation ID 與服務工作負載，不接收登入使用者、LINE ID、JWT 或 Request Session。
        return _warmUpExecutor.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = Key.ProfileAlias,
                CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                WorkloadSubjectId = "dynamics-runtime-warmup",
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            },
            cancellationToken);
    }

    /// <summary>
    /// 在生命週期鎖內把 Active 單向切換為 Draining，立即停止所有新 Execution Lease。
    /// 方法可重複呼叫且不 Dispose 使用中的資源；既有 Lease 仍可在 drain 期限內完成並自行釋放。
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
    /// 啟動或共用唯一的 bounded drain Task：先停止新 Lease、等待 active reference 歸零，逾時後發出 Retirement Token，
    /// 再等待有界 grace period，最後依 Token Provider→Transport→Admission Registration 順序回收所有 Generation 資源。
    /// 多次呼叫共用同一 Task，確保 Handler、CTS、Host Slot 與 Registration 只釋放一次，且任何 cleanup failure 都可被觀察。
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
    /// 同步 Dispose 仍完整等待 bounded drain 與非同步 Registration release，
    /// 不使用 fire-and-forget，也不捕捉呼叫端 SynchronizationContext。
    /// </summary>
    public void Dispose()
        => Task.Run(async () => await DrainAndDisposeAsync().ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// 非同步 Dispose 直接委派至同一個可重入 Drain Task；重複呼叫不會重複釋放 Handler、Semaphore 或 Host Slot。
    /// </summary>
    public ValueTask DisposeAsync()
        => new(DrainAndDisposeAsync());

    /// <summary>
    /// 執行一次 drain 嘗試。自然排空逾時才取消 Retirement Token；強制取消後仍給 finally／stream disposal 有界餘裕。
    /// 若租約在兩個期限後仍未歸零，方法會保留所有使用中資源並拋出 TimeoutException，讓後續再次呼叫可繼續清理，
    /// 絕不為了讓 shutdown 看似成功而 Dispose 仍被執行緒使用的 Client／Handler／CTS。
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
                // 只有自然 drain 超過上限才發出 Generation retirement cancellation；已 dispatch 工作會在共用 linked token 收到訊號。
                _retirementCts.Cancel();
                await zeroTask.WaitAsync(
                    _definition.CancellationGracePeriod,
                    cancellationToken).ConfigureAwait(false);
            }

            await DisposeOwnedResourcesAsync().ConfigureAwait(false);
        }
        catch
        {
            // 失敗嘗試不可永久快取；例如 Lease 稍後歸零或呼叫端取消解除後，Shutdown 可再次呼叫完成剩餘清理。
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

    /// <summary>
    /// 取得目前 active execution 歸零訊號。Task 本身不保存 Request 或 Client，只代表引用計數狀態。
    /// </summary>
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
    /// 在 active count 已歸零後，依安全順序回收所有 Generation-owned 資源。
    /// Token Provider 與 Transport 先停止並清除 Credential／Socket 狀態，Admission Registration 最後釋放，
    /// 確保 Runtime 完全清理前仍持有 Host Slot，不讓替代 Host 與舊資源重疊超過 Aggregate Budget。
    /// 所有清理都會被嘗試；若任一項失敗，彙整後向上拋出，不能靜默遺失 release failure。
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

            _state = DynamicsProfileRuntimeState.Disposed;
        }

        List<Exception>? failures = null;
        await CaptureFailureAsync(
            async () => await _tokenProvider.DisposeAsync().ConfigureAwait(false),
            exception => (failures ??= []).Add(exception)).ConfigureAwait(false);
        await CaptureFailureAsync(
            async () => await _transport.DisposeAsync().ConfigureAwait(false),
            exception => (failures ??= []).Add(exception)).ConfigureAwait(false);
        await CaptureFailureAsync(
            async () => await _admissionRegistration.DisposeAsync().ConfigureAwait(false),
            exception => (failures ??= []).Add(exception)).ConfigureAwait(false);

        _retirementCts.Dispose();
        if (failures is { Count: > 0 })
        {
            throw new AggregateException("One or more profile runtime resources failed to dispose.", failures);
        }
    }

    /// <summary>
    /// 執行單一非同步清理並保存例外，使後續資源仍能繼續回收；
    /// 這可避免第一個 Dispose failure 阻止其他 Handler、Registration 或 CTS 清理而形成連鎖洩漏。
    /// </summary>
    private static async Task CaptureFailureAsync(
        Func<Task> action,
        Action<Exception> capture)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            capture(ex);
        }
    }

    /// <summary>
    /// 建立已完成的 zero-execution 訊號，供尚未有任何 Lease 的新 Runtime 使用。
    /// </summary>
    private static TaskCompletionSource CreateCompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult();
        return signal;
    }

    /// <summary>
    /// 釋放一個 Execution Lease 的引用。Interlocked 由 Lease 保證每個 Lease 只會進入一次；
    /// active count 轉為零時喚醒 drain，而不在此處直接 Dispose 資源，避免呼叫端 finally 內發生重入清理。
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
    /// Runtime-owned Execution Lease。Lease 只保留 Runtime 引用，不複製 Request、Token、Credential 或 Session；
    /// Dispose 使用 Interlocked 確保同步／非同步競速時只遞減一次 active count。
    /// </summary>
    private sealed class ExecutionLease : IDynamicsProfileExecutionLease
    {
        private readonly DynamicsProfileRuntime _owner;
        private int _disposed;

        /// <summary>建立已由 Runtime 增加 active count 的 Lease。</summary>
        public ExecutionLease(DynamicsProfileRuntime owner)
        {
            _owner = owner;
        }

        /// <summary>取得此 Lease 固定的 Generation Key；Lease 釋放後不得再以此 Key 保存 Client 參考。</summary>
        public ProfileRuntimeKey RuntimeKey => _owner.Key;

        /// <summary>取得 Runtime 唯一擁有的 Client；只允許在本 Lease 仍有效時執行受控操作。</summary>
        public IDynamicsWebApiClient Client => _owner._client;

        /// <summary>取得 drain timeout 後的強制退休訊號，用來停止仍未完成的外呼、Stream 與 Retry。</summary>
        public CancellationToken RetirementToken => _owner._retirementCts.Token;

        /// <summary>同步釋放執行引用；沒有 I/O 或背景工作，因此不需阻塞等待。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.ReleaseExecution();
            }
        }

        /// <summary>非同步相容路徑與同步 Dispose 共用相同 idempotent 引用釋放。</summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
