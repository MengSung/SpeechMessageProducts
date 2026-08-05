// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Runtime/DynamicsProfileRuntimeManager.cs
// 目的：實作 Alias Catalog、Admission→Queue→Current Active Runtime 的正確取得順序、
//       原子 replace-and-drain、Active+最多一個 Draining 與確定性 Shutdown。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// Local Gateway 與 Central Gateway 共用的 Multi-Profile Runtime Manager。
/// Manager 只擁有 Alias Catalog、Generation 發布狀態與 Runtime 生命週期；每個 Runtime 唯一擁有自己的
/// Worker Executor／Process／Pipe，Admission Registry 才是同一實體 Organization 容量的唯一共享邊界。
/// </summary>
/// <remarks>
/// 所有 Catalog 狀態都在 <see cref="_gate"/> 內短暫變更，Factory、Warm-up、Queue wait、drain 與 dispose
/// 一律在鎖外等待。Request 在 Admission Queue 等待期間只保存非秘密 Dispatch Envelope 與 Admission Manager，
/// 不取得或保存 Runtime Lease；排隊完成後才在 Catalog 鎖內取得當下 Active Generation，避免 Queue 強引用舊 Runtime。
/// </remarks>
public sealed class DynamicsProfileRuntimeManager :
    IDynamicsProfileRuntimeManager,
    IActiveProfileGenerationResolver
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProfileSlot> _slots;
    private readonly IDynamicsProfileRuntimeFactory _runtimeFactory;
    private readonly ProfileRoutedOperationExecutor _executor;
    private readonly CancellationTokenSource _shutdownCts = new();

    private TaskCompletionSource _lifecycleOperationsDrained = CreateCompletedSignal();
    private Task? _initializationTask;
    private Task? _disposeTask;
    private int _activeLifecycleOperations;
    private bool _ready;
    private bool _disposeStarted;

    /// <summary>
    /// 建立尚未初始化的 Runtime Manager。
    /// Constructor 只建立不含秘密的 Alias Catalog，並以大小寫不敏感規則拒絕重複 Alias；
    /// 它不呼叫 Factory、不解析 Secret、不取得 Admission Host Slot，也不啟動背景 Task。
    /// </summary>
    /// <param name="definitions">部署核准的初始 Profile Definitions；至少需要一筆且 Alias 不可碰撞。</param>
    /// <param name="runtimeFactory">建立每個隔離 Runtime Generation 的 Factory；其生命週期由外層 DI/Host 擁有。</param>
    public DynamicsProfileRuntimeManager(
        IEnumerable<DynamicsProfileDefinition> definitions,
        IDynamicsProfileRuntimeFactory runtimeFactory)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));

        _slots = new Dictionary<string, ProfileSlot>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!_slots.TryAdd(definition.ProfileAlias, new ProfileSlot(definition)))
            {
                throw new ArgumentException(
                    "Profile aliases must be unique using case-insensitive comparison.",
                    nameof(definitions));
            }
        }

        if (_slots.Count == 0)
        {
            throw new ArgumentException(
                "At least one Dynamics profile definition is required.",
                nameof(definitions));
        }

        _executor = new ProfileRoutedOperationExecutor(this);
    }

    /// <summary>
    /// 建立並驗證全部初始 Profile Generation，只有所有 Factory、Host Slot 與可選 Warm-up 都成功後才原子標記 Ready。
    /// 同時間多個呼叫會共用同一 Manager-owned Task；任一失敗會清理所有候選 Runtime、重設初始化狀態並允許後續重試。
    /// 呼叫者取消只影響這次初始化流程，不會留下未觀察的 Factory、Handler、Token Provider 或 Admission Registration。
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposeStarted();
            if (_ready)
            {
                return Task.CompletedTask;
            }

            if (_initializationTask is not null)
            {
                return _initializationTask;
            }

            BeginLifecycleOperationLocked();
            _initializationTask = InitializeCoreAsync(cancellationToken);
            return _initializationTask;
        }
    }

    /// <summary>
    /// 為既有 Alias 建立不可變新 Generation，完成 Host Slot／Warm-up 驗證後原子發布並 drain 舊 Generation。
    /// 同一 Alias 僅容許一個 Active 加一個 Draining；若另一個 replacement owner 仍在執行會於 Factory 前拒絕。
    /// 若只剩前次失敗留下的 Draining Runtime，本次呼叫會先接管並重試其 cleanup，完成前不配置第三套
    /// Worker、Process、Pipe、CTS 或 Admission Registration；已 Disposed 的 cleanup failure 仍向上回報但不永久卡住 Slot。
    /// </summary>
    public Task ReplaceAsync(
        DynamicsProfileDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        ProfileSlot slot;
        lock (_gate)
        {
            ThrowIfDisposeStarted();
            if (!_ready)
            {
                throw new InvalidOperationException("Dynamics profile runtime manager is not ready.");
            }

            if (!_slots.TryGetValue(definition.ProfileAlias, out slot!))
            {
                throw new KeyNotFoundException("The requested Dynamics profile alias is not registered.");
            }

            // ReplacementInProgress 是同一 Alias 唯一的非同步生命週期 owner；平行呼叫必須在 Factory 前拒絕。
            // 單獨存在的 Draining 代表前一次 owner 已因取消或 cleanup failure 離開，後續呼叫必須能接手重試，
            // 否則暫時性 drain failure 會永久凍結設定更新。真正的第三套資源防線位於 ReplaceCoreAsync：
            // pending Draining 未完成並從 Slot 精確移除前，Generation 不遞增且 Factory 絕不執行。
            if (slot.ReplacementInProgress)
            {
                throw new InvalidOperationException(
                    "The Dynamics profile already has a replacement generation in progress.");
            }

            slot.ReplacementInProgress = true;
            BeginLifecycleOperationLocked();
        }

        return ReplaceCoreAsync(slot, definition, cancellationToken);
    }

    /// <summary>
    /// 執行一個 Operation Registry 已核准的受控操作。此入口不接受 CRM Endpoint、Credential 或 Transport 選擇；
    /// Alias 會先通過 Admission Queue，再由 Manager 取得排隊完成當下的 Active Runtime Generation。
    /// </summary>
    public Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default)
        => _executor.ExecuteAsync(request, cancellationToken);

    /// <summary>
    /// 在不觸發 Secret、Factory、Admission I/O 或 Worker process 的前提下，解析目前 Active Alias 的不可變容量計畫。
    /// 未知 Alias、NotReady、Draining-only 或 Shutdown 狀態一律回傳 false，且不洩漏內部 Organization、Namespace 或 Endpoint。
    /// </summary>
    public bool TryGetAdmissionPlan(
        string profileAlias,
        out OrganizationAdmissionPlan? admissionPlan)
    {
        admissionPlan = null;
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposeStarted || !_ready ||
                !_slots.TryGetValue(profileAlias.Trim(), out var slot) ||
                slot.Active is null ||
                slot.Active.State != DynamicsProfileRuntimeState.Active)
            {
                return false;
            }

            admissionPlan = slot.Active.AdmissionManager.Plan;
            return true;
        }
    }

    /// <summary>
    /// 只讀取目前 Active Runtime 的非秘密 generation key，供 Official Worker Profile resolver 更新
    /// Router 的 generation snapshot。此方法不建立或保存 Runtime、Worker、Pipe、Permit、Credential、
    /// Timer 或 Request；Draining／NotReady／Shutdown 狀態一律 fail closed。
    /// </summary>
    public bool TryGetActiveRuntimeKey(
        string profileAlias,
        out ProfileRuntimeKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposeStarted || !_ready ||
                !_slots.TryGetValue(profileAlias.Trim(), out var slot) ||
                slot.Active is null ||
                slot.Active.State != DynamicsProfileRuntimeState.Active)
            {
                return false;
            }

            key = slot.Active.Key;
            return true;
        }
    }

    /// <summary>
    /// 先取得實體 Organization 的 Admission Permit，排隊完成後才在 Catalog 鎖內取得當下 Active Runtime Lease。
    /// Queue 等待期間不持有 Executor／Process／Pipe／Runtime 強引用；若 Runtime 交換、不相容或取得失敗，
    /// 會依反向 ownership 順序嘗試釋放 Runtime Lease 與 Permit，並彙整所有 cleanup failure 後再回報。
    /// </summary>
    public async Task<ProfileExecutionLeaseAcquireResult> AcquireAsync(
        DispatchEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var binding = await ResolveAdmissionBindingForAcquireAsync(
            envelope.ProfileAlias,
            cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            return NotReadyLeaseResult();
        }

        var admissionManager = binding.AdmissionManager;
        var expectedPlan = binding.AdmissionPlan;

        // 這裡刻意不持有 Runtime Execution Lease：Queue 可能等待數秒，若提前取得舊 Runtime，
        // replace-and-drain 會被未 dispatch 的工作阻塞，且 Queue 會長期保留舊 Worker／Pipe Generation。
        var admission = await admissionManager
            .AcquireAsync(envelope, cancellationToken)
            .ConfigureAwait(false);
        if (!admission.Succeeded || admission.Permit is null)
        {
            return ProfileExecutionLeaseAcquireResult.Failure(
                admission.Error ?? OperationExecutionResult.Failure(
                    DynamicsErrorCodes.CapacityRejected,
                    "Admission was rejected."));
        }

        var permit = admission.Permit!;
        IDynamicsProfileExecutionLease? runtimeLease = null;
        try
        {
            lock (_gate)
            {
                if (!TryResolveActiveRuntimeLocked(envelope.ProfileAlias, out var currentActive) ||
                    !ReferenceEquals(currentActive.AdmissionManager, admissionManager) ||
                    currentActive.Key.CanonicalOrganizationKey != expectedPlan.CanonicalKey ||
                    !string.Equals(
                        currentActive.AdmissionManager.Plan.ConfigurationDigest,
                        expectedPlan.ConfigurationDigest,
                        StringComparison.Ordinal) ||
                    !currentActive.TryAcquireExecution(out runtimeLease) ||
                    runtimeLease is null)
                {
                    runtimeLease = null;
                }
            }

            if (runtimeLease is null)
            {
                // Admission 已成功但 Runtime 取得失敗時，Permit 必須在回傳前釋放；
                // 否則 ActivePermits／Semaphore 名額會永久保留，最終讓整個 Organization 無法再接受工作。
                await permit.DisposeAsync().ConfigureAwait(false);
                return NotReadyLeaseResult();
            }

            return ProfileExecutionLeaseAcquireResult.Success(
                new CombinedExecutionLease(runtimeLease, permit, expectedPlan));
        }
        catch (Exception acquisitionFailure)
        {
            // 若在組合租約完成 ownership 轉移前發生例外，依反向順序釋放所有部分資源；
            // 每個 cleanup 都必須獨立嘗試，不能讓 Runtime Lease 的釋放錯誤阻止 Permit 歸還。
            // 原始取得錯誤永遠放在第一項；若清理也失敗則彙整回報，避免錯誤被覆蓋或容量被沉默遺留。
            var failures = new List<Exception> { acquisitionFailure };
            await CaptureCleanupFailureAsync(
                runtimeLease,
                static lease => lease.DisposeAsync().AsTask(),
                failures).ConfigureAwait(false);
            await CaptureCleanupFailureAsync(
                permit,
                static ownedPermit => ownedPermit.DisposeAsync().AsTask(),
                failures).ConfigureAwait(false);

            if (failures.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(acquisitionFailure)
                    .Throw();
            }

            throw new AggregateException(
                "Dynamics profile execution lease acquisition and one or more rollback operations failed.",
                failures);
        }
    }

    /// <summary>
    /// 建立目前 Active／Draining Generation 的非秘密 bounded 快照，供 Readiness 與生命週期測試使用。
    /// 快照只複製 Key、狀態、引用數與 Admission 指標，不把 Executor、Process、Pipe、Credential、Request 或 Runtime 物件交給呼叫端。
    /// </summary>
    public DynamicsProfileRuntimeManagerSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var profiles = _slots.Values
                .SelectMany(static slot => EnumerateOwnedRuntimes(slot))
                .Select(static runtime => new DynamicsProfileRuntimeSnapshot
                {
                    Key = runtime.Key,
                    State = runtime.State,
                    ActiveExecutionCount = runtime.ActiveExecutionCount,
                    Admission = runtime.AdmissionSnapshot
                })
                .OrderBy(static profile => profile.Key.ProfileAlias, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static profile => profile.Key.Generation)
                .ToArray();

            return new DynamicsProfileRuntimeManagerSnapshot
            {
                IsReady = _ready && !_disposeStarted,
                Profiles = profiles
            };
        }
    }

    /// <summary>
    /// 同步關閉 Manager，完整等待所有建立／替換作業、Execution Lease drain 與 Runtime resource disposal。
    /// Task.Run 隔離呼叫端 SynchronizationContext，但仍同步觀察所有例外，不留下背景清理工作。
    /// </summary>
    public void Dispose()
        => Task.Run(async () => await DisposeAsync().ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// 原子停止新路由與新生命週期操作、取消尚未完成的 Factory／Warm-up，等待它們離開後再 drain Catalog。
    /// 多次呼叫共用同一 Dispose Task；最後清空 Alias Catalog 與 CTS，確保 Manager 不保留 Runtime 強引用。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposeStarted = true;
            _ready = false;
            _shutdownCts.Cancel();
            var lifecycleDrain = _activeLifecycleOperations == 0
                ? Task.CompletedTask
                : _lifecycleOperationsDrained.Task;
            _disposeTask = DisposeCoreAsync(lifecycleDrain);
            return new ValueTask(_disposeTask);
        }
    }

    /// <summary>
    /// 建立所有初始 Generation，完成可選 Warm-up 後一次發布整個 Catalog。
    /// 任一項失敗會清理本次所有候選 Runtime，讓 Manager 保持 NotReady，避免部分 Alias 可用、部分 Alias 未驗證。
    /// </summary>
    private async Task InitializeCoreAsync(CancellationToken callerCancellationToken)
    {
        // 先建立一次明確的非同步邊界，讓 InitializeAsync 能在任何同步完成的 Factory／Fake 失敗前，
        // 把本次 Task 發布到 _initializationTask。否則 Core 可能先在 catch 清成 null，外層賦值隨後又把
        // 已失敗 Task 寫回欄位，造成暫時性故障排除後仍永遠無法重試初始化。
        await Task.Yield();

        var candidates = new List<(ProfileSlot Slot, IDynamicsProfileRuntime Runtime)>();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            _shutdownCts.Token);
        try
        {
            ProfileSlot[] slots;
            lock (_gate)
            {
                slots = _slots.Values
                    .OrderBy(static slot => slot.Definition.ProfileAlias, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            foreach (var slot in slots)
            {
                long generation;
                lock (_gate)
                {
                    ThrowIfDisposeStarted();
                    generation = checked(++slot.LastGeneration);
                }

                var runtime = await CreateValidatedRuntimeAsync(
                    slot.Definition,
                    generation,
                    linkedCts.Token).ConfigureAwait(false);
                candidates.Add((slot, runtime));
            }

            lock (_gate)
            {
                ThrowIfDisposeStarted();
                foreach (var candidate in candidates)
                {
                    candidate.Slot.Active = candidate.Runtime;
                }

                _ready = true;
            }
        }
        catch (Exception initializationFailure)
        {
            // 候選 Runtime cleanup failure 不得跳過 Catalog 狀態回滾；否則 _initializationTask 會永久指向失敗 Task，
            // Manager 也可能保留候選 Runtime。先嘗試清理全部候選並收集錯誤，再無條件重設 Ready／Task 狀態。
            var failures = new List<Exception> { initializationFailure };
            await CaptureCleanupFailureAsync(
                candidates,
                static ownedCandidates => DisposeRuntimesAsync(
                    ownedCandidates.Select(static candidate => candidate.Runtime)),
                failures).ConfigureAwait(false);
            lock (_gate)
            {
                foreach (var candidate in candidates)
                {
                    if (ReferenceEquals(candidate.Slot.Active, candidate.Runtime))
                    {
                        candidate.Slot.Active = null;
                    }
                }

                _ready = false;
                _initializationTask = null;
            }

            if (failures.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(initializationFailure)
                    .Throw();
            }

            throw new AggregateException(
                "Dynamics profile runtime initialization and one or more rollback operations failed.",
                failures);
        }
        finally
        {
            EndLifecycleOperation();
        }
    }

    /// <summary>
    /// 在取得 Admission Manager 或 Permit 前評估目前 Active Runtime 的 sticky recycle reason。
    /// 健康 Runtime 只回傳 Admission Manager 與 immutable Plan，不把 Runtime 帶進後續 Queue wait；需要回收時
    /// 所有 caller 共用同一個 Manager-owned replacement task，而 caller cancellation 只取消自己的等待。
    /// 單一 Acquire 最多觸發一次 replacement；若候選 warm-up 已立即要求回收，會 fail closed 而不建立第三代。
    /// </summary>
    private async Task<AdmissionBinding?> ResolveAdmissionBindingForAcquireAsync(
        string profileAlias,
        CancellationToken callerCancellationToken)
    {
        var replacementAttempted = false;
        for (var observationAttempt = 0; observationAttempt < 3; observationAttempt++)
        {
            ProfileSlot slot;
            IDynamicsProfileRuntime observedActive;
            lock (_gate)
            {
                if (!TryResolveActiveRuntimeLocked(profileAlias, out observedActive!) ||
                    !_slots.TryGetValue(profileAlias.Trim(), out slot!))
                {
                    return null;
                }
            }

            OfficialWorkerRecycleReason recycleReason;
            try
            {
                recycleReason = observedActive.EvaluateRecycleForNextAdmission();
                if (!Enum.IsDefined(recycleReason))
                {
                    recycleReason = OfficialWorkerRecycleReason.ResourceObservationFailure;
                }
            }
            catch
            {
                // Runtime 應自行把不可讀資源映射為固定 reason；此防線確保任一非預期觀測例外仍 fail closed，
                // 且不把 Process、Pipe、Profile、Exception message 或 caller state 帶入產品錯誤。
                recycleReason = OfficialWorkerRecycleReason.ResourceObservationFailure;
            }

            if (recycleReason == OfficialWorkerRecycleReason.None)
            {
                lock (_gate)
                {
                    if (!TryResolveActiveRuntimeLocked(profileAlias, out var currentActive))
                    {
                        return null;
                    }

                    if (!ReferenceEquals(currentActive, observedActive))
                    {
                        continue;
                    }

                    try
                    {
                        recycleReason = observedActive.RecycleReason;
                        if (!Enum.IsDefined(recycleReason))
                        {
                            recycleReason = OfficialWorkerRecycleReason.ResourceObservationFailure;
                        }
                    }
                    catch
                    {
                        recycleReason = OfficialWorkerRecycleReason.ResourceObservationFailure;
                    }

                    if (recycleReason == OfficialWorkerRecycleReason.None)
                    {
                        if (!TryResolveAdmissionBindingLocked(
                                profileAlias,
                                out var admissionManager,
                                out var admissionPlan))
                        {
                            return null;
                        }

                        return new AdmissionBinding(admissionManager, admissionPlan);
                    }

                    // Evaluation 在 Catalog 鎖外執行以避免 Process resource observation 阻塞其他 Alias；
                    // 取得 AdmissionManager 前再讀一次 sticky reason，縮小平行 worker completion 的競態窗。
                    // 離開鎖後加入或建立同一個 replacement task。
                }
            }

            if (replacementAttempted)
            {
                return null;
            }

            Task<bool>? replacementTask;
            lock (_gate)
            {
                if (!TryResolveActiveRuntimeLocked(profileAlias, out var currentActive))
                {
                    return null;
                }

                if (!ReferenceEquals(currentActive, observedActive))
                {
                    continue;
                }

                replacementTask = GetOrStartRecycleReplacementLocked(slot, observedActive);
            }

            if (replacementTask is null)
            {
                return null;
            }

            var replaced = await replacementTask
                .WaitAsync(callerCancellationToken)
                .ConfigureAwait(false);
            if (!replaced)
            {
                return null;
            }

            replacementAttempted = true;
        }

        return null;
    }

    /// <summary>
    /// 在 Catalog 鎖內取得既有自動替換 task，或為精確的 Active Runtime 建立唯一 owner。
    /// Manual Replace 已進行、仍有 Draining，或 Active 已改變時一律拒絕配置候選，避免第三代資源。
    /// </summary>
    private Task<bool>? GetOrStartRecycleReplacementLocked(
        ProfileSlot slot,
        IDynamicsProfileRuntime expectedActive)
    {
        if (slot.RecycleReplacementTask is not null)
        {
            return slot.RecycleReplacementTask;
        }

        if (slot.ReplacementInProgress ||
            slot.Draining is not null ||
            !ReferenceEquals(slot.Active, expectedActive))
        {
            return null;
        }

        slot.ReplacementInProgress = true;
        BeginLifecycleOperationLocked();
        var replacementTask = ReplaceRecycledRuntimeCoreAsync(slot, expectedActive);
        slot.RecycleReplacementTask = replacementTask;
        return replacementTask;
    }

    /// <summary>
    /// 使用 Manager shutdown token 建立、驗證、發布並 drain 一個 recycle replacement。
    /// 方法先建立非同步邊界，確保共享 task 已發布到 Slot 後才可能同步完成；候選失敗只回傳 false，
    /// 呼叫端統一得到 sanitized NotReady，原本 sticky Active 不會被 dispatch 或改寫。
    /// </summary>
    private async Task<bool> ReplaceRecycledRuntimeCoreAsync(
        ProfileSlot slot,
        IDynamicsProfileRuntime expectedActive)
    {
        await Task.Yield();

        IDynamicsProfileRuntime? candidate = null;
        IDynamicsProfileRuntime? previous = null;
        var published = false;
        try
        {
            DynamicsProfileDefinition definition;
            long generation;
            lock (_gate)
            {
                if (_disposeStarted ||
                    !_ready ||
                    slot.Draining is not null ||
                    !ReferenceEquals(slot.Active, expectedActive))
                {
                    return false;
                }

                definition = slot.Definition;
                generation = checked(++slot.LastGeneration);
            }

            candidate = await CreateValidatedRuntimeAsync(
                definition,
                generation,
                _shutdownCts.Token).ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposeStarted ||
                    !_ready ||
                    slot.Draining is not null ||
                    !ReferenceEquals(slot.Active, expectedActive))
                {
                    throw new InvalidOperationException(
                        "The Dynamics profile slot changed while the recycle replacement was being built.");
                }

                previous = slot.Active;
                slot.Active = candidate;
                slot.Draining = previous;
                previous.BeginDrain();
                published = true;
            }

            await DrainOwnedRuntimeAsync(
                slot,
                previous,
                _shutdownCts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            if (!published && candidate is not null)
            {
                await DisposeRejectedRecycleCandidateAsync(slot, candidate).ConfigureAwait(false);
            }

            return false;
        }
        finally
        {
            lock (_gate)
            {
                slot.RecycleReplacementTask = null;
                slot.ReplacementInProgress = false;
            }

            EndLifecycleOperation();
        }
    }

    /// <summary>
    /// 清理未發布候選。若 cleanup failure 後 Runtime 仍未 Disposed，Slot 會把它保留為唯一 Draining owner，
    /// 讓 shutdown 或後續 manual replacement 能重試，而不是遺失 Process、Pipe、CTS 或 Admission Registration。
    /// </summary>
    private async Task DisposeRejectedRecycleCandidateAsync(
        ProfileSlot slot,
        IDynamicsProfileRuntime candidate)
    {
        try
        {
            await candidate.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                if (candidate.State != DynamicsProfileRuntimeState.Disposed &&
                    slot.Draining is null &&
                    !ReferenceEquals(slot.Active, candidate))
                {
                    slot.Draining = candidate;
                }
            }
        }
    }

    /// <summary>
    /// 建立、驗證並可選 Warm-up 候選 Generation；Warm-up 失敗視為候選不可發布，
    /// 並在方法內確定性 Dispose 候選 Runtime，避免未發布 Process／Pipe／Registration 洩漏。
    /// </summary>
    private async Task<IDynamicsProfileRuntime> CreateValidatedRuntimeAsync(
        DynamicsProfileDefinition definition,
        long generation,
        CancellationToken cancellationToken)
    {
        var runtime = await _runtimeFactory
            .CreateAsync(definition, generation, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            // Host Slot 是 Runtime 可發布的前置條件，而不是第一個產品 Request 才補做的延遲初始化。
            // 只有 coordinator 已確認目前 Admission Epoch 與安全 expiry fence，Gateway 才能宣告此 Profile Ready。
            await runtime.AdmissionManager
                .EnsureHostSlotAsync(cancellationToken)
                .ConfigureAwait(false);

            if (definition.WarmUpOnActivation)
            {
                var warmUp = await runtime.WarmUpAsync(cancellationToken).ConfigureAwait(false);
                if (!warmUp.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Dynamics profile warm-up failed; the candidate generation was not published.");
                }
            }

            return runtime;
        }
        catch
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 先接手並重試前次失敗留下的 Draining Runtime，只有確認 Slot 不再擁有舊 Draining 後才配置新 Generation。
    /// 新 Runtime 的發布與舊 Active 的 BeginDrain 在同一 Catalog 鎖內完成，因此新 Request 不可能在兩者之間
    /// 再取得舊 Lease。所有 drain 都使用 caller 與 Manager shutdown 的 linked token，並以 Runtime 實際 State
    /// 決定是否移除強引用：cleanup failure 不會被吞掉，尚未 Disposed 的資源也不會成為 Manager 無法再接管的孤兒。
    /// </summary>
    private async Task ReplaceCoreAsync(
        ProfileSlot slot,
        DynamicsProfileDefinition definition,
        CancellationToken callerCancellationToken)
    {
        IDynamicsProfileRuntime? candidate = null;
        IDynamicsProfileRuntime? previous = null;
        var published = false;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            _shutdownCts.Token);
        try
        {
            IDynamicsProfileRuntime? pendingDraining;
            lock (_gate)
            {
                pendingDraining = slot.Draining;
            }

            if (pendingDraining is not null)
            {
                // 前次 Replace 若因 caller cancellation 或 drain timeout 離開，舊 Runtime 仍可能持有 Lease／Handler。
                // 本次 owner 必須先重試同一物件的 bounded cleanup；完成前不可遞增 Generation 或呼叫 Factory，
                // 否則同一 Alias 會同時存在 Active 加兩個 Draining，突破 Worker／Process／Pipe 資源上限。
                await DrainOwnedRuntimeAsync(
                    slot,
                    pendingDraining,
                    linkedCts.Token).ConfigureAwait(false);
            }

            long generation;
            lock (_gate)
            {
                ThrowIfDisposeStarted();
                if (slot.Draining is not null || slot.Active is null)
                {
                    throw new InvalidOperationException(
                        "The Dynamics profile cannot allocate a replacement while a draining generation remains owned.");
                }

                // 只有既有 Draining 已確定收斂後才消耗下一個 Generation 編號；
                // 重複取消或 cleanup retry 不會製造沒有對應 Runtime 的流水號缺口。
                generation = checked(++slot.LastGeneration);
            }

            candidate = await CreateValidatedRuntimeAsync(
                definition,
                generation,
                linkedCts.Token).ConfigureAwait(false);

            lock (_gate)
            {
                ThrowIfDisposeStarted();
                if (slot.Draining is not null || slot.Active is null)
                {
                    throw new InvalidOperationException(
                        "The Dynamics profile slot changed while the replacement generation was being built.");
                }

                previous = slot.Active;
                slot.Definition = definition;
                slot.Active = candidate;
                slot.Draining = previous;
                previous.BeginDrain();
                published = true;
            }

            await DrainOwnedRuntimeAsync(
                slot,
                previous,
                linkedCts.Token).ConfigureAwait(false);
        }
        catch
        {
            if (!published && candidate is not null)
            {
                await candidate.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            lock (_gate)
            {
                slot.ReplacementInProgress = false;
            }

            EndLifecycleOperation();
        }
    }

    /// <summary>
    /// 等待 Manager 仍明確擁有的 Draining Runtime 完成 bounded drain 與所有 Generation-owned cleanup。
    /// 方法永遠在 Catalog 鎖外等待；完成或失敗後只在 Runtime 已確實進入 <see cref="DynamicsProfileRuntimeState.Disposed"/>
    /// 且 Slot 仍指向同一物件時移除強引用。如此 Worker／Pipe／Admission cleanup failure 仍可被 caller 觀察，
    /// 但已 Disposed 的幽靈 Runtime 不會永久阻塞替換；caller cancellation 或 timeout 留下的未完成 Runtime 則繼續由 Slot 擁有，
    /// 讓後續 Replace 或 Manager Shutdown 能重新接管，而不是遺留不可回收的 Process、Pipe、CTS、Lease 或背景工作。
    /// </summary>
    /// <param name="slot">目前唯一擁有該 Alias Active／Draining reference 的 Catalog Slot。</param>
    /// <param name="runtime">必須完成或重試 cleanup 的精確 Draining Runtime 物件。</param>
    /// <param name="cancellationToken">同時受 caller 與 Manager shutdown 控制的有界取消訊號。</param>
    private async Task DrainOwnedRuntimeAsync(
        ProfileSlot slot,
        IDynamicsProfileRuntime runtime,
        CancellationToken cancellationToken)
    {
        try
        {
            await runtime.DrainAndDisposeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // 是否拋出例外不能代表資源是否已收斂：DisposeOwnedResourcesAsync 可能先把 State 設為 Disposed，
            // 再彙整 Token Provider／Transport／Registration failure。只依實際 State 與 reference identity 清除，
            // 可避免「catch 一律清除」遺失未完成 Runtime，也避免「成功才清除」永久保留已 Disposed 物件。
            if (runtime.State == DynamicsProfileRuntimeState.Disposed)
            {
                lock (_gate)
                {
                    if (ReferenceEquals(slot.Draining, runtime))
                    {
                        slot.Draining = null;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 等待所有 Factory／Warm-up／Replace 操作離開，再取得最終 Catalog ownership 並 drain 每個 Runtime。
    /// Runtime 清理全部完成後才清空 Slot Dictionary 與 Dispose Shutdown CTS，讓晚到的 Lease release 仍有有效 owner。
    /// </summary>
    private async Task DisposeCoreAsync(Task lifecycleDrain)
    {
        await lifecycleDrain.ConfigureAwait(false);

        IDynamicsProfileRuntime[] runtimes;
        lock (_gate)
        {
            runtimes = _slots.Values
                .SelectMany(static slot => EnumerateOwnedRuntimes(slot))
                .Distinct(RuntimeReferenceComparer.Instance)
                .ToArray();

            foreach (var slot in _slots.Values)
            {
                slot.Active = null;
                slot.Draining = null;
                slot.ReplacementInProgress = false;
                slot.RecycleReplacementTask = null;
            }

            _slots.Clear();
        }

        try
        {
            await DisposeRuntimesAsync(runtimes).ConfigureAwait(false);
        }
        finally
        {
            _shutdownCts.Dispose();
        }
    }

    /// <summary>
    /// 嘗試在 Catalog 鎖內解析目前 Active Runtime。未知 Alias、NotReady、Shutdown、Draining 或 Disposed
    /// 一律 fail closed；方法不呼叫 Secret、Factory、Admission、Token 或 Transport。
    /// </summary>
    private bool TryResolveActiveRuntimeLocked(
        string profileAlias,
        out IDynamicsProfileRuntime runtime)
    {
        runtime = null!;
        if (_disposeStarted || !_ready || string.IsNullOrWhiteSpace(profileAlias) ||
            !_slots.TryGetValue(profileAlias.Trim(), out var slot) ||
            slot.Active is null ||
            slot.Active.State != DynamicsProfileRuntimeState.Active)
        {
            return false;
        }

        runtime = slot.Active;
        return true;
    }

    /// <summary>
    /// 在 Catalog 鎖內只擷取排隊所需的 Admission Manager 與不可變 Plan，不把 Runtime、Executor、Process 或 Pipe
    /// 帶到 Queue wait 的非同步狀態機。如此 queued-undispatched Request 不會形成舊 Generation 的強引用。
    /// </summary>
    private bool TryResolveAdmissionBindingLocked(
        string profileAlias,
        out IOrganizationAdmissionManager admissionManager,
        out OrganizationAdmissionPlan admissionPlan)
    {
        admissionManager = null!;
        admissionPlan = null!;
        if (!TryResolveActiveRuntimeLocked(profileAlias, out var runtime))
        {
            return false;
        }

        admissionManager = runtime.AdmissionManager;
        admissionPlan = admissionManager.Plan;
        return true;
    }

    /// <summary>
    /// 建立一致的 NotReady 租約失敗，避免把 Alias、Endpoint、Organization GUID、Namespace 或內部例外洩漏給產品呼叫端。
    /// </summary>
    private static ProfileExecutionLeaseAcquireResult NotReadyLeaseResult()
        => ProfileExecutionLeaseAcquireResult.Failure(
            OperationExecutionResult.Failure(
                DynamicsErrorCodes.NotReady,
                "The requested Dynamics profile is not ready."));

    /// <summary>
    /// 在 Catalog 鎖內登記一個 Manager-owned 生命週期操作；從零變一時建立新的 drain 訊號。
    /// Counter 只追蹤 Factory／Warm-up／Replace 工作數，不保存 Task、Request、Token 或 Runtime 強引用。
    /// </summary>
    private void BeginLifecycleOperationLocked()
    {
        if (_activeLifecycleOperations == 0)
        {
            _lifecycleOperationsDrained = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        checked
        {
            _activeLifecycleOperations++;
        }
    }

    /// <summary>
    /// 結束一個 Manager-owned 生命週期操作；最後一個操作離開時喚醒 Shutdown。
    /// 訊號在鎖外完成，避免 continuation 於 Catalog 鎖內重入 Manager。
    /// </summary>
    private void EndLifecycleOperation()
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            if (_activeLifecycleOperations <= 0)
            {
                throw new InvalidOperationException(
                    "Dynamics profile lifecycle operation count underflow.");
            }

            _activeLifecycleOperations--;
            if (_activeLifecycleOperations == 0)
            {
                drained = _lifecycleOperationsDrained;
            }
        }

        drained?.TrySetResult();
    }

    /// <summary>
    /// Dispose 多個 Runtime 並等待全部完成；每個 Runtime 都會被嘗試清理，最後以 AggregateException
    /// 回報所有失敗，避免第一個錯誤阻止其他 Worker、Pipe、Registration 或 Host Slot 回收。
    /// </summary>
    private static async Task DisposeRuntimesAsync(IEnumerable<IDynamicsProfileRuntime> runtimes)
    {
        List<Exception>? failures = null;
        foreach (var runtime in runtimes.Distinct(RuntimeReferenceComparer.Instance))
        {
            try
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException(
                "One or more Dynamics profile runtimes failed to dispose.",
                failures);
        }
    }

    /// <summary>
    /// 對一項已取得 ownership 的部分資源執行非同步 rollback，並把 cleanup failure 收集到既有錯誤集合。
    /// AggregateException 會展開成個別根因，讓呼叫端同時保留原始操作失敗及每一個資源釋放失敗；
    /// 方法本身不重新拋出，因此後續 Runtime Lease、Admission Permit 或候選 Runtime 仍有機會確定性回收。
    /// </summary>
    /// <typeparam name="TResource">具有明確單一清理 owner 的參考型別。</typeparam>
    /// <param name="resource">已取得的資源；null 表示 ownership 尚未建立，不需執行清理。</param>
    /// <param name="cleanupAsync">完成時代表該資源 rollback 已結束的非同步清理函式。</param>
    /// <param name="failures">先包含原始操作錯誤、並接收所有 cleanup 根因的有界集合。</param>
    private static async Task CaptureCleanupFailureAsync<TResource>(
        TResource? resource,
        Func<TResource, Task> cleanupAsync,
        ICollection<Exception> failures)
        where TResource : class
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            await cleanupAsync(resource).ConfigureAwait(false);
        }
        catch (AggregateException aggregateException)
        {
            foreach (var cleanupFailure in aggregateException.Flatten().InnerExceptions)
            {
                failures.Add(cleanupFailure);
            }
        }
        catch (Exception cleanupFailure)
        {
            failures.Add(cleanupFailure);
        }
    }

    /// <summary>
    /// 列舉 Slot 目前擁有的 Active 與 Draining Runtime；只供持有 Manager 鎖時建立快照，
    /// 不可把回傳的 Runtime 列舉保存到 Request、Queue 或長生命週期 Cache。
    /// </summary>
    private static IEnumerable<IDynamicsProfileRuntime> EnumerateOwnedRuntimes(ProfileSlot slot)
    {
        if (slot.Active is not null)
        {
            yield return slot.Active;
        }

        if (slot.Draining is not null)
        {
            yield return slot.Draining;
        }
    }

    /// <summary>
    /// 若 Shutdown 已開始則拒絕新的初始化或替換；必須在 Catalog 鎖內呼叫，確保檢查與後續狀態變更原子化。
    /// </summary>
    private void ThrowIfDisposeStarted()
        => ObjectDisposedException.ThrowIf(_disposeStarted, this);

    /// <summary>建立初始已完成的生命週期歸零訊號。</summary>
    private static TaskCompletionSource CreateCompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult();
        return signal;
    }

    /// <summary>
    /// 單一 Alias 的 Catalog Slot。所有欄位只可在 Manager 鎖內讀寫；Slot 不保存 Secret、Token、User、Session 或 Request。
    /// </summary>
    private sealed class ProfileSlot
    {
        /// <summary>建立尚未發布 Runtime 的 Slot。</summary>
        public ProfileSlot(DynamicsProfileDefinition definition)
        {
            Definition = definition;
        }

        /// <summary>取得或設定下一個 Generation 使用的單調遞增基準。</summary>
        public long LastGeneration { get; set; }

        /// <summary>取得或設定目前部署核准的不可變 Profile Definition。</summary>
        public DynamicsProfileDefinition Definition { get; set; }

        /// <summary>取得或設定接受新 Lease 的唯一 Active Runtime。</summary>
        public IDynamicsProfileRuntime? Active { get; set; }

        /// <summary>取得或設定最多一個、只等待既有 Lease 排空的 Draining Runtime。</summary>
        public IDynamicsProfileRuntime? Draining { get; set; }

        /// <summary>取得或設定是否已有 Factory／Warm-up／發布／drain 替換流程進行中。</summary>
        public bool ReplacementInProgress { get; set; }

        /// <summary>
        /// 取得或設定由 Manager shutdown token 唯一擁有的自動回收替換 task。
        /// Task 不保存 caller cancellation、Request、Credential、Token 或 Session；平行 caller 只等待同一參考。
        /// </summary>
        public Task<bool>? RecycleReplacementTask { get; set; }
    }

    /// <summary>
    /// Queue wait 唯一需要保存的 admission binding；只包含 manager 與 immutable plan，不持有 Runtime、Executor、
    /// Process、Pipe、Request、Credential、Token 或 Session，因此不會讓 queued-undispatched 工作保留舊 Generation。
    /// </summary>
    private sealed record AdmissionBinding(
        IOrganizationAdmissionManager AdmissionManager,
        OrganizationAdmissionPlan AdmissionPlan);

    /// <summary>
    /// 以物件身分比較 Runtime，避免同一個 Runtime 因 Catalog 異常或未來 Slot 擴充而被重複 Dispose。
    /// Comparer 不讀取 Runtime Key、Client、Token 或其他可變狀態，因此不會在比較期間觸發 I/O 或延長 Request 資料生命週期。
    /// </summary>
    private sealed class RuntimeReferenceComparer : IEqualityComparer<IDynamicsProfileRuntime>
    {
        /// <summary>取得無狀態、程序內唯一的 comparer 實例。</summary>
        public static RuntimeReferenceComparer Instance { get; } = new();

        /// <summary>只有兩個參考指向完全相同的 Runtime 物件時才視為相等。</summary>
        public bool Equals(IDynamicsProfileRuntime? x, IDynamicsProfileRuntime? y)
            => ReferenceEquals(x, y);

        /// <summary>取得不會呼叫 Runtime 自訂邏輯的物件身分雜湊值。</summary>
        public int GetHashCode(IDynamicsProfileRuntime obj)
            => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>
    /// Runtime Execution Lease 與 Admission Permit 的唯一合併 owner。
    /// Dispose 必須先釋放 Runtime 引用，再釋放 Permit；即使第一步失敗仍會嘗試第二步並彙整例外，
    /// 避免 runtime active count 或 organization capacity 任一側形成永久洩漏。
    /// </summary>
    private sealed class CombinedExecutionLease : IProfileExecutionLease
    {
        private readonly IDynamicsProfileExecutionLease _runtimeLease;
        private readonly IAdmissionPermit _permit;
        private int _disposed;

        /// <summary>建立合併租約並接管 Runtime Lease 與 Permit 的唯一 ownership。</summary>
        public CombinedExecutionLease(
            IDynamicsProfileExecutionLease runtimeLease,
            IAdmissionPermit permit,
            OrganizationAdmissionPlan admissionPlan)
        {
            _runtimeLease = runtimeLease;
            _permit = permit;
            AdmissionPlan = admissionPlan;
        }

        /// <summary>取得本次呼叫固定使用的 Runtime Generation Key；只供診斷，不能作為 User／Session Cache Key。</summary>
        public ProfileRuntimeKey? RuntimeKey => _runtimeLease.RuntimeKey;

        /// <summary>取得租約生命週期內唯一允許使用的 Executor；租約釋放後呼叫端不得保存或再次使用此參考。</summary>
        public IDynamicsOperationExecutor Executor => _runtimeLease.Executor;

        /// <summary>取得本次 Permit 所屬的不可變 Organization 容量計畫，不包含秘密或呼叫者狀態。</summary>
        public OrganizationAdmissionPlan AdmissionPlan { get; }

        /// <summary>取得 Host Slot 遭 fencing、revocation 或 expiry 時用來停止外呼與 retry 的取消訊號。</summary>
        public CancellationToken LeaseLostToken => _permit.LeaseLostToken;

        /// <summary>取得 Generation drain 超過期限時的強制退休訊號，確保工作不會越過有界清理期限。</summary>
        public CancellationToken RetirementToken => _runtimeLease.RetirementToken;

        /// <summary>
        /// 同步執行 Runtime Lease→Admission Permit 的安全釋放順序，並完整觀察非同步清理錯誤。
        /// </summary>
        public void Dispose()
            => Task.Run(async () => await DisposeAsync().ConfigureAwait(false))
                .GetAwaiter()
                .GetResult();

        /// <summary>
        /// 先釋放 Runtime active reference，再歸還 Organization capacity；兩個步驟都會被等待與觀察。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            List<Exception>? failures = null;
            try
            {
                await _runtimeLease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            try
            {
                await _permit.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            if (failures is { Count: > 0 })
            {
                throw new AggregateException(
                    "The Dynamics profile execution lease failed to release all owned resources.",
                    failures);
            }
        }
    }
}
