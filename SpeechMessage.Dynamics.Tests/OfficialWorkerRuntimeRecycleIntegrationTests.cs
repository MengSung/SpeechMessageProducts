// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/OfficialWorkerRuntimeRecycleIntegrationTests.cs
// 目的：驗證官方 Worker sticky recycle reason 會在 admission 前觸發整代 Runtime 替換，
//       並固定單一 replacement owner、候選 rollback、Active+Draining 上限與 caller cancellation 隔離。
//
// 所有 Runtime、Admission 與 Executor 都是程序內 bounded fake，不建立真實 CRM、Credential、Token、
// Process、Pipe、Timer 或背景續租工作；測試只保留部署識別與有限生命週期計數。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Runtime;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 固定 Runtime Manager 對官方 Worker 回收原因的跨層整合契約。
/// 每次 admission 前都必須先評估目前 Active Generation；需要回收時由 Manager 建立一個不受個別 caller
/// cancellation 擁有的共享替換工作，候選完整通過 Host Slot 與 warm-up 後才發布，舊代則確定性 drain。
/// </summary>
public sealed class OfficialWorkerRuntimeRecycleIntegrationTests
{
    private const string ProfileAlias = "crm91";

    /// <summary>
    /// 證明多個同時看見 sticky recycle reason 的 caller 只會共用一個 Manager-owned replacement task。
    /// 候選 warm-up 尚未放行時 Factory 只能存在初始代與單一候選代，放行後所有 caller 都取得第二代。
    /// </summary>
    [Fact]
    public async Task Concurrent_callers_share_one_recycle_replacement_generation()
    {
        await using var factory = new RecycleRuntimeFactory();
        var warmUpGate = factory.BlockWarmUp(generation: 2);
        await using var manager = CreateManager(factory);
        await manager.InitializeAsync(CancellationToken.None);
        factory.GetRuntime(1).MarkRecycle(OfficialWorkerRecycleReason.MaximumCompletedOperations);

        var firstAcquire = manager.AcquireAsync(
            CreateEnvelope("concurrent-first"),
            CancellationToken.None);
        Task<ProfileExecutionLeaseAcquireResult>? secondAcquire = null;
        try
        {
            await WaitForWarmUpAsync(factory, generation: 2);
            secondAcquire = manager.AcquireAsync(
                CreateEnvelope("concurrent-second"),
                CancellationToken.None);

            await Task.Delay(TimeSpan.FromMilliseconds(50));
            factory.CreateCount.Should().Be(2);

            warmUpGate.TrySetResult(SuccessfulWarmUp());
            var first = await firstAcquire;
            var second = await secondAcquire;
            try
            {
                first.Succeeded.Should().BeTrue();
                second.Succeeded.Should().BeTrue();
                first.Lease!.RuntimeKey!.Value.Generation.Should().Be(2);
                second.Lease!.RuntimeKey!.Value.Generation.Should().Be(2);
                factory.CreateCount.Should().Be(2);
            }
            finally
            {
                await DisposeLeaseAsync(first);
                await DisposeLeaseAsync(second);
            }
        }
        finally
        {
            warmUpGate.TrySetResult(SuccessfulWarmUp());
            await DisposeCompletedLeaseAsync(firstAcquire);
            if (secondAcquire is not null)
            {
                await DisposeCompletedLeaseAsync(secondAcquire);
            }
        }
    }

    /// <summary>
    /// 證明最先觸發替換的 caller 取消時只取消自己的等待，不會把取消 token 傳給共享候選 owner。
    /// 另一個 caller 仍可在相同候選 warm-up 完成後取得第二代，且候選不會被提早 Dispose 或重建。
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_does_not_cancel_shared_recycle_replacement_owner()
    {
        await using var factory = new RecycleRuntimeFactory();
        var warmUpGate = factory.BlockWarmUp(generation: 2);
        await using var manager = CreateManager(factory);
        await manager.InitializeAsync(CancellationToken.None);
        factory.GetRuntime(1).MarkRecycle(OfficialWorkerRecycleReason.MaximumWorkerAge);
        using var callerCancellation = new CancellationTokenSource();

        var cancelledAcquire = manager.AcquireAsync(
            CreateEnvelope("cancelled-owner-caller"),
            callerCancellation.Token);
        Task<ProfileExecutionLeaseAcquireResult>? survivingAcquire = null;
        try
        {
            var candidate = await WaitForWarmUpAsync(factory, generation: 2);
            survivingAcquire = manager.AcquireAsync(
                CreateEnvelope("surviving-caller"),
                CancellationToken.None);

            callerCancellation.Cancel();
            var cancelledAct = async () => await cancelledAcquire;
            await cancelledAct.Should().ThrowAsync<OperationCanceledException>();
            candidate.DisposeCount.Should().Be(0);
            factory.CreateCount.Should().Be(2);

            warmUpGate.TrySetResult(SuccessfulWarmUp());
            var surviving = await survivingAcquire;
            try
            {
                surviving.Succeeded.Should().BeTrue();
                surviving.Lease!.RuntimeKey!.Value.Generation.Should().Be(2);
                factory.CreateCount.Should().Be(2);
            }
            finally
            {
                await DisposeLeaseAsync(surviving);
            }
        }
        finally
        {
            warmUpGate.TrySetResult(SuccessfulWarmUp());
            await DisposeCompletedLeaseAsync(cancelledAcquire);
            if (survivingAcquire is not null)
            {
                await DisposeCompletedLeaseAsync(survivingAcquire);
            }
        }
    }

    /// <summary>
    /// 證明候選 warm-up 失敗會只 Dispose 候選一次，並讓原本帶 sticky reason 的 Active Runtime 保持不變。
    /// 失敗 caller 只能收到固定 NotReady，且舊 Runtime 的 admission、execution lease 與 executor 都不可被 dispatch。
    /// </summary>
    [Fact]
    public async Task Candidate_failure_rolls_back_and_does_not_dispatch_sticky_old_runtime()
    {
        await using var factory = new RecycleRuntimeFactory();
        factory.FailWarmUp(
            generation: 2,
            OperationExecutionResult.Failure(
                "InjectedSecretFailure",
                "https://secret.example.test/?token=must-not-escape"));
        await using var manager = CreateManager(factory);
        await manager.InitializeAsync(CancellationToken.None);
        var original = factory.GetRuntime(1);
        original.ResetObservationCounters();
        original.MarkRecycle(OfficialWorkerRecycleReason.ProtocolViolation);

        var result = await manager.AcquireAsync(
            CreateEnvelope("candidate-failure"),
            CancellationToken.None);
        await DisposeLeaseAsync(result);

        result.Succeeded.Should().BeFalse();
        result.Error!.ErrorCode.Should().Be(DynamicsErrorCodes.NotReady);
        result.Error.ErrorMessage.Should().Be("The requested Dynamics profile is not ready.");
        result.Error.ErrorMessage.Should().NotContain("secret");
        original.State.Should().Be(DynamicsProfileRuntimeState.Active);
        original.RecycleReason.Should().Be(OfficialWorkerRecycleReason.ProtocolViolation);
        original.AdmissionManagerGetterCount.Should().Be(0);
        original.Admission.AcquireCount.Should().Be(0);
        original.TryAcquireExecutionCount.Should().Be(0);
        original.Executor.ExecutionCount.Should().Be(0);
        factory.CreateCount.Should().Be(2);
        factory.GetRuntime(2).DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 證明舊代仍有 execution lease 時，候選發布後 Catalog 只保留第二代 Active 與第一代 Draining。
    /// 新 caller 可使用已發布的健康第二代而不建立第三代；舊 lease 歸還後第一代只 Dispose 一次。
    /// </summary>
    [Fact]
    public async Task Published_replacement_keeps_at_most_active_plus_one_draining_generation()
    {
        await using var factory = new RecycleRuntimeFactory();
        await using var manager = CreateManager(factory);
        await manager.InitializeAsync(CancellationToken.None);
        var original = factory.GetRuntime(1);
        original.TryAcquireExecution(out var heldOldLease).Should().BeTrue();
        original.MarkRecycle(OfficialWorkerRecycleReason.MaximumPrivateBytes);

        var firstAcquire = manager.AcquireAsync(
            CreateEnvelope("replacement-owner"),
            CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                () =>
                {
                    var profiles = manager.GetSnapshot().Profiles;
                    return profiles.Count == 2 &&
                           profiles.Count(static profile => profile.State == DynamicsProfileRuntimeState.Active) == 1 &&
                           profiles.Count(static profile => profile.State == DynamicsProfileRuntimeState.Draining) == 1;
                },
                "one active plus one draining generation after publication");

            var second = await manager.AcquireAsync(
                CreateEnvelope("post-publication-caller"),
                CancellationToken.None);
            try
            {
                second.Succeeded.Should().BeTrue();
                second.Lease!.RuntimeKey!.Value.Generation.Should().Be(2);
                factory.CreateCount.Should().Be(2);
            }
            finally
            {
                await DisposeLeaseAsync(second);
            }

            await heldOldLease!.DisposeAsync();
            heldOldLease = null;
            var first = await firstAcquire;
            try
            {
                first.Succeeded.Should().BeTrue();
                first.Lease!.RuntimeKey!.Value.Generation.Should().Be(2);
            }
            finally
            {
                await DisposeLeaseAsync(first);
            }

            original.State.Should().Be(DynamicsProfileRuntimeState.Disposed);
            original.DisposeCount.Should().Be(1);
            manager.GetSnapshot().Profiles.Should().ContainSingle(profile =>
                profile.Key.Generation == 2 &&
                profile.State == DynamicsProfileRuntimeState.Active);
        }
        finally
        {
            if (heldOldLease is not null)
            {
                await heldOldLease.DisposeAsync();
            }

            await DisposeCompletedLeaseAsync(firstAcquire);
        }
    }

    /// <summary>
    /// 證明 sticky recycle 評估發生在讀取舊 Runtime AdmissionManager 與取得 Permit 之前。
    /// 成功替換後 caller 只能碰觸第二代 admission 與 lease；舊代 getter、Acquire、lease 與 executor 計數都維持零。
    /// </summary>
    [Fact]
    public async Task Sticky_recycle_is_checked_before_old_runtime_admission_binding()
    {
        await using var factory = new RecycleRuntimeFactory();
        await using var manager = CreateManager(factory);
        await manager.InitializeAsync(CancellationToken.None);
        var original = factory.GetRuntime(1);
        original.ResetObservationCounters();
        original.MarkRecycle(OfficialWorkerRecycleReason.ResourceObservationFailure);

        var result = await manager.AcquireAsync(
            CreateEnvelope("pre-admission-check"),
            CancellationToken.None);
        try
        {
            result.Succeeded.Should().BeTrue();
            result.Lease!.RuntimeKey!.Value.Generation.Should().Be(2);
            original.AdmissionManagerGetterCount.Should().Be(0);
            original.Admission.AcquireCount.Should().Be(0);
            original.TryAcquireExecutionCount.Should().Be(0);
            original.Executor.ExecutionCount.Should().Be(0);
        }
        finally
        {
            await DisposeLeaseAsync(result);
        }
    }

    /// <summary>
    /// 證明候選 warm-up 本身若已達 MaximumCompletedOperations，單次 request 最多只建立一個替換代。
    /// Manager 在發布後重新評估一次並回傳 NotReady，不可遞迴建立第三代或形成無限 replacement loop。
    /// </summary>
    [Fact]
    public async Task Warm_up_triggered_recycle_stops_after_one_replacement_attempt_per_request()
    {
        await using var factory = new RecycleRuntimeFactory();
        factory.MarkRecycleAfterSuccessfulWarmUp(
            generation: 2,
            OfficialWorkerRecycleReason.MaximumCompletedOperations);
        await using var manager = CreateManager(factory);
        await manager.InitializeAsync(CancellationToken.None);
        factory.GetRuntime(1).MarkRecycle(OfficialWorkerRecycleReason.MaximumCompletedOperations);

        var result = await manager.AcquireAsync(
            CreateEnvelope("warmup-consumed-limit"),
            CancellationToken.None);
        await DisposeLeaseAsync(result);

        result.Succeeded.Should().BeFalse();
        result.Error!.ErrorCode.Should().Be(DynamicsErrorCodes.NotReady);
        factory.CreateCount.Should().Be(2);
        factory.GetRuntime(2).State.Should().Be(DynamicsProfileRuntimeState.Active);
        factory.GetRuntime(2).RecycleReason.Should().Be(
            OfficialWorkerRecycleReason.MaximumCompletedOperations);
    }

    /// <summary>建立固定啟用 warm-up 的單一 Profile Manager。</summary>
    private static DynamicsProfileRuntimeManager CreateManager(IDynamicsProfileRuntimeFactory factory)
        => new([CreateDefinition()], factory);

    /// <summary>
    /// 建立完全由部署擁有的測試 Profile；設定只包含非秘密識別與有限 timeout，並明確啟用發布前 warm-up。
    /// </summary>
    private static DynamicsProfileDefinition CreateDefinition()
        => new(
            ProfileAlias,
            "profile-generation-runtime-recycle",
            OfficialWorkerVersion.Ce91,
            "https://crm91.example.test/Org/",
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "speechmessage-runtime-recycle", "worker.exe")),
            new string('a', 64),
            "package-lock-runtime-recycle",
            new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("91919191-9191-9191-9191-919191919191"),
                AggregateMaxInFlight = 6,
                MaximumRuntimeHosts = 6,
                LocalQueueCapacity = 4,
                MaxInFlightAndQueuedPerWorkload = 4,
                QueueAdmissionTimeoutSeconds = 5,
                AdmissionNamespaceId = "admission-runtime-recycle",
                LeaseNamespaceId = "lease-runtime-recycle",
                RequireDurableHostCoordinator = false
            },
            warmUpOnActivation: true,
            drainTimeout: TimeSpan.FromSeconds(5),
            cancellationGracePeriod: TimeSpan.FromSeconds(5));

    /// <summary>建立不含 Request、Credential、Token 或 Session 的固定 bounded dispatch envelope。</summary>
    private static DispatchEnvelope CreateEnvelope(string workload)
        => new()
        {
            ProfileAlias = ProfileAlias,
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = workload,
            TemplateId = "WhoAmI",
            TemplateHash = new string('a', 64),
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
            EstimatedEnvelopeBytes = 512
        };

    /// <summary>建立不保存任何 upstream payload 的成功 warm-up 結果。</summary>
    private static OperationExecutionResult SuccessfulWarmUp()
        => OperationExecutionResult.Success(null);

    /// <summary>等待指定候選確實進入 warm-up；等待具有五秒硬上限，失敗時不留下無限輪詢工作。</summary>
    private static async Task<RecycleRuntime> WaitForWarmUpAsync(
        RecycleRuntimeFactory factory,
        long generation)
    {
        RecycleRuntime? runtime = null;
        await WaitUntilAsync(
            () => factory.TryGetRuntime(generation, out runtime) && runtime.WarmUpCount > 0,
            $"generation {generation} entering warm-up");
        return runtime!;
    }

    /// <summary>以可取消短輪詢等待非同步生命週期狀態，避免測試故障時永久佔用執行緒。</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        condition().Should().BeTrue(description);
    }

    /// <summary>若取得結果含租約則確定性釋放；失敗結果不持有任何部分 ownership。</summary>
    private static async Task DisposeLeaseAsync(ProfileExecutionLeaseAcquireResult result)
    {
        if (result.Lease is not null)
        {
            await result.Lease.DisposeAsync();
        }
    }

    /// <summary>
    /// 測試清理專用：在兩秒內觀察已啟動的 Acquire 並釋放可能取得的租約；原始測試失敗不會被清理例外覆蓋。
    /// </summary>
    private static async Task DisposeCompletedLeaseAsync(
        Task<ProfileExecutionLeaseAcquireResult> acquireTask)
    {
        try
        {
            var result = await acquireTask.WaitAsync(TimeSpan.FromSeconds(2));
            await DisposeLeaseAsync(result);
        }
        catch
        {
            // 測試主體負責斷言原始錯誤；清理只避免失敗路徑保留 Runtime Lease。
        }
    }

    /// <summary>
    /// 建立每代完全隔離的 Runtime fake，並提供受控 warm-up gate／failure／完成後 sticky reason 注入。
    /// Factory 只保留有限 generation 對照，不保存 request、caller token、credential 或 session。
    /// </summary>
    private sealed class RecycleRuntimeFactory : IDynamicsProfileRuntimeFactory, IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<long, RecycleRuntime> _runtimes = [];
        private readonly Dictionary<long, RuntimeBehavior> _behaviors = [];
        private int _disposed;

        /// <summary>取得實際建立的 generation 數量。</summary>
        public int CreateCount
        {
            get
            {
                lock (_gate)
                {
                    return _runtimes.Count;
                }
            }
        }

        /// <summary>讓指定 generation 的 warm-up 等待測試擁有的 bounded completion signal。</summary>
        public TaskCompletionSource<OperationExecutionResult> BlockWarmUp(long generation)
        {
            var signal = new TaskCompletionSource<OperationExecutionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _behaviors[generation] = new RuntimeBehavior(signal.Task, OfficialWorkerRecycleReason.None);
            }

            return signal;
        }

        /// <summary>讓指定 generation 回傳已清洗形狀的 warm-up failure。</summary>
        public void FailWarmUp(long generation, OperationExecutionResult failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            failure.Succeeded.Should().BeFalse();
            lock (_gate)
            {
                _behaviors[generation] = new RuntimeBehavior(
                    Task.FromResult(failure),
                    OfficialWorkerRecycleReason.None);
            }
        }

        /// <summary>讓指定 generation 在成功 warm-up 完成後立即記錄 sticky recycle reason。</summary>
        public void MarkRecycleAfterSuccessfulWarmUp(
            long generation,
            OfficialWorkerRecycleReason recycleReason)
        {
            recycleReason.Should().NotBe(OfficialWorkerRecycleReason.None);
            lock (_gate)
            {
                _behaviors[generation] = new RuntimeBehavior(
                    Task.FromResult(SuccessfulWarmUp()),
                    recycleReason);
            }
        }

        /// <summary>建立新的 Runtime、Admission Manager、Executor、CTS 與生命週期狀態，不跨代共用 mutable owner。</summary>
        public Task<IDynamicsProfileRuntime> CreateAsync(
            DynamicsProfileDefinition definition,
            long generation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                _behaviors.TryGetValue(generation, out var behavior);
                var runtime = new RecycleRuntime(
                    definition,
                    generation,
                    behavior ?? RuntimeBehavior.Success);
                _runtimes.Add(generation, runtime);
                return Task.FromResult<IDynamicsProfileRuntime>(runtime);
            }
        }

        /// <summary>取得已建立的精確 generation；不存在代表 Manager 尚未呼叫 Factory。</summary>
        public RecycleRuntime GetRuntime(long generation)
        {
            lock (_gate)
            {
                return _runtimes[generation];
            }
        }

        /// <summary>以不拋例外方式查詢 generation，供 bounded 非同步輪詢。</summary>
        public bool TryGetRuntime(long generation, out RecycleRuntime runtime)
        {
            lock (_gate)
            {
                return _runtimes.TryGetValue(generation, out runtime!);
            }
        }

        /// <summary>
        /// Factory 不擁有已交給 Manager 的 Runtime；Dispose 只清除自身有限對照，避免重複釋放 generation 資源。
        /// </summary>
        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return ValueTask.CompletedTask;
                }

                _behaviors.Clear();
                _runtimes.Clear();
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>單一 generation 的 immutable warm-up 行為，不保存 caller cancellation token。</summary>
    private sealed record RuntimeBehavior(
        Task<OperationExecutionResult> WarmUpResult,
        OfficialWorkerRecycleReason RecycleAfterWarmUp)
    {
        /// <summary>取得立即成功且不要求回收的預設行為。</summary>
        public static RuntimeBehavior Success { get; } = new(
            Task.FromResult(SuccessfulWarmUp()),
            OfficialWorkerRecycleReason.None);
    }

    /// <summary>
    /// 完整模擬 Active／Draining／Disposed、execution reference count 與 sticky recycle reason 的測試 Runtime。
    /// 每代唯一擁有自己的 Admission、Executor 與 retirement CTS，不跨 generation 或 caller 共用 mutable 狀態。
    /// </summary>
    private sealed class RecycleRuntime : IDynamicsProfileRuntime
    {
        private readonly object _gate = new();
        private readonly RuntimeBehavior _behavior;
        private readonly CancellationTokenSource _retirementCts = new();
        private TaskCompletionSource _zeroExecutions = CreateCompletedSignal();
        private Task? _drainTask;
        private DynamicsProfileRuntimeState _state = DynamicsProfileRuntimeState.Active;
        private int _activeExecutionCount;
        private int _admissionManagerGetterCount;
        private int _disposeCount;
        private int _recycleEvaluationCount;
        private int _recycleReason;
        private int _tryAcquireExecutionCount;
        private int _warmUpCount;

        /// <summary>建立一個完全隔離的 fake generation。</summary>
        public RecycleRuntime(
            DynamicsProfileDefinition definition,
            long generation,
            RuntimeBehavior behavior)
        {
            _behavior = behavior;
            Key = new ProfileRuntimeKey(
                definition.ProfileAlias,
                generation,
                definition.CeVersion,
                definition.AdmissionPlan.CanonicalKey);
            Admission = new RecycleAdmissionManager(definition.AdmissionPlan);
            Executor = new RecycleExecutor();
        }

        /// <summary>取得 generation key。</summary>
        public ProfileRuntimeKey Key { get; }

        /// <summary>取得 bounded fake admission owner。</summary>
        public RecycleAdmissionManager Admission { get; }

        /// <summary>取得不保存 request 的 fake executor。</summary>
        public RecycleExecutor Executor { get; }

        /// <summary>取得 Runtime 狀態。</summary>
        public DynamicsProfileRuntimeState State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        /// <summary>取得尚未歸還的 execution reference 數量。</summary>
        public int ActiveExecutionCount
        {
            get
            {
                lock (_gate)
                {
                    return _activeExecutionCount;
                }
            }
        }

        /// <summary>取得 AdmissionManager getter 被讀取的次數，以固定 recycle-before-admission 順序。</summary>
        public int AdmissionManagerGetterCount => Volatile.Read(ref _admissionManagerGetterCount);

        /// <summary>取得 TryAcquireExecution 呼叫次數。</summary>
        public int TryAcquireExecutionCount => Volatile.Read(ref _tryAcquireExecutionCount);

        /// <summary>取得 warm-up 呼叫次數。</summary>
        public int WarmUpCount => Volatile.Read(ref _warmUpCount);

        /// <summary>取得實際完成資源釋放的次數。</summary>
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        /// <summary>取得已記錄的第一個 sticky recycle reason。</summary>
        public OfficialWorkerRecycleReason RecycleReason
            => (OfficialWorkerRecycleReason)Volatile.Read(ref _recycleReason);

        /// <summary>讀取本代 admission owner 並記錄 getter 次數。</summary>
        public IOrganizationAdmissionManager AdmissionManager
        {
            get
            {
                Interlocked.Increment(ref _admissionManagerGetterCount);
                return Admission;
            }
        }

        /// <summary>取得不含 request 或秘密的 admission 快照。</summary>
        public AdmissionMetricsSnapshot AdmissionSnapshot => Admission.GetSnapshot();

        /// <summary>記錄並回傳目前 sticky reason；方法不建立 Task、Timer、Token 或長生命週期集合。</summary>
        public OfficialWorkerRecycleReason EvaluateRecycleForNextAdmission()
        {
            Interlocked.Increment(ref _recycleEvaluationCount);
            return RecycleReason;
        }

        /// <summary>以 first-writer-wins 規則記錄非 None sticky reason。</summary>
        public void MarkRecycle(OfficialWorkerRecycleReason recycleReason)
        {
            recycleReason.Should().NotBe(OfficialWorkerRecycleReason.None);
            Interlocked.CompareExchange(
                ref _recycleReason,
                (int)recycleReason,
                (int)OfficialWorkerRecycleReason.None);
        }

        /// <summary>重設初始化造成的觀測計數，但保留 Runtime ownership 與 sticky reason。</summary>
        public void ResetObservationCounters()
        {
            Volatile.Write(ref _admissionManagerGetterCount, 0);
            Volatile.Write(ref _recycleEvaluationCount, 0);
            Volatile.Write(ref _tryAcquireExecutionCount, 0);
            Executor.Reset();
            Admission.ResetObservationCounters();
        }

        /// <summary>僅在 Active 狀態增加 execution reference 並建立唯一 fake lease。</summary>
        public bool TryAcquireExecution(out IDynamicsProfileExecutionLease? lease)
        {
            Interlocked.Increment(ref _tryAcquireExecutionCount);
            lock (_gate)
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

                lease = new RecycleExecutionLease(this);
                return true;
            }
        }

        /// <summary>
        /// 執行受控 warm-up 行為；caller token 只取消本次等待，不會取消 Factory 擁有的共享測試 completion signal。
        /// 成功後可依測試設定記錄 sticky reason，模擬低 completed-operation 門檻已被 warm-up 消耗。
        /// </summary>
        public async Task<OperationExecutionResult> WarmUpAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _warmUpCount);
            var result = await _behavior.WarmUpResult
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (result.Succeeded &&
                _behavior.RecycleAfterWarmUp != OfficialWorkerRecycleReason.None)
            {
                MarkRecycle(_behavior.RecycleAfterWarmUp);
            }

            return result;
        }

        /// <summary>單向切換為 Draining，之後拒絕新 execution lease。</summary>
        public void BeginDrain()
        {
            lock (_gate)
            {
                if (_state == DynamicsProfileRuntimeState.Active)
                {
                    _state = DynamicsProfileRuntimeState.Draining;
                }
            }
        }

        /// <summary>建立或重用本代唯一 drain task；取消後允許 Manager shutdown 重新接管同一 Runtime。</summary>
        public Task DrainAndDisposeAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_state == DynamicsProfileRuntimeState.Disposed)
                {
                    return Task.CompletedTask;
                }

                _drainTask ??= DrainCoreAsync(cancellationToken);
                return _drainTask;
            }
        }

        /// <summary>同步等待相同 drain owner，不建立未觀察的 fire-and-forget 工作。</summary>
        public void Dispose()
            => Task.Run(async () => await DrainAndDisposeAsync()).GetAwaiter().GetResult();

        /// <summary>非同步 Dispose 委派到相同 drain task。</summary>
        public ValueTask DisposeAsync()
            => new(DrainAndDisposeAsync());

        /// <summary>等待 active references 歸零後依序 Dispose admission 與 retirement CTS。</summary>
        private async Task DrainCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                BeginDrain();
                Task zeroExecutions;
                lock (_gate)
                {
                    zeroExecutions = _activeExecutionCount == 0
                        ? Task.CompletedTask
                        : _zeroExecutions.Task;
                }

                await zeroExecutions.WaitAsync(cancellationToken).ConfigureAwait(false);
                await Admission.DisposeAsync().ConfigureAwait(false);
                lock (_gate)
                {
                    if (_state != DynamicsProfileRuntimeState.Disposed)
                    {
                        _state = DynamicsProfileRuntimeState.Disposed;
                        Interlocked.Increment(ref _disposeCount);
                    }
                }

                _retirementCts.Dispose();
            }
            catch
            {
                lock (_gate)
                {
                    if (_state != DynamicsProfileRuntimeState.Disposed)
                    {
                        _drainTask = null;
                    }
                }

                throw;
            }
        }

        /// <summary>歸還一個 execution reference，最後一個離開時喚醒 drain owner。</summary>
        private void ReleaseExecution()
        {
            TaskCompletionSource? zero = null;
            lock (_gate)
            {
                _activeExecutionCount--;
                if (_activeExecutionCount == 0)
                {
                    zero = _zeroExecutions;
                }
            }

            zero?.TrySetResult();
        }

        /// <summary>建立初始已完成的 execution 歸零訊號。</summary>
        private static TaskCompletionSource CreateCompletedSignal()
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            signal.TrySetResult();
            return signal;
        }

        /// <summary>只持有 owner reference 的 fake execution lease，使用 Interlocked 確保只釋放一次。</summary>
        private sealed class RecycleExecutionLease : IDynamicsProfileExecutionLease
        {
            private readonly RecycleRuntime _owner;
            private int _disposed;

            /// <summary>接管已由 Runtime 增加的 execution reference。</summary>
            public RecycleExecutionLease(RecycleRuntime owner)
            {
                _owner = owner;
            }

            /// <summary>取得固定 generation key。</summary>
            public ProfileRuntimeKey RuntimeKey => _owner.Key;

            /// <summary>取得本代唯一 executor。</summary>
            public IDynamicsOperationExecutor Executor => _owner.Executor;

            /// <summary>取得本代 retirement token。</summary>
            public CancellationToken RetirementToken => _owner._retirementCts.Token;

            /// <summary>同步歸還 reference。</summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.ReleaseExecution();
                }
            }

            /// <summary>非同步相容路徑。</summary>
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>每代獨立的 bounded admission fake，不跨 generation 共用 Permit、CTS 或 mutable counter。</summary>
    private sealed class RecycleAdmissionManager : IOrganizationAdmissionManager
    {
        private int _acquireCount;
        private int _activePermits;
        private int _disposed;
        private int _ensureHostSlotCount;

        /// <summary>建立持有 immutable plan 的 admission fake。</summary>
        public RecycleAdmissionManager(OrganizationAdmissionPlan plan)
        {
            Plan = plan;
        }

        /// <summary>取得 immutable organization plan。</summary>
        public OrganizationAdmissionPlan Plan { get; }

        /// <summary>取得 AcquireAsync 呼叫次數。</summary>
        public int AcquireCount => Volatile.Read(ref _acquireCount);

        /// <summary>驗證 host slot 前置條件並記錄次數，不啟動 renewal loop。</summary>
        public Task EnsureHostSlotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            Interlocked.Increment(ref _ensureHostSlotCount);
            return Task.CompletedTask;
        }

        /// <summary>立即建立不含 request state 的 Permit，讓測試聚焦於 recycle-before-admission 順序。</summary>
        public Task<AdmissionAcquireResult> AcquireAsync(
            DispatchEnvelope envelope,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            Interlocked.Increment(ref _acquireCount);
            Interlocked.Increment(ref _activePermits);
            return Task.FromResult(AdmissionAcquireResult.Success(new RecyclePermit(this)));
        }

        /// <summary>取得只含有限計數的快照。</summary>
        public AdmissionMetricsSnapshot GetSnapshot()
            => new()
            {
                LocalMaxInFlight = Plan.LocalMaxInFlight,
                InFlight = Volatile.Read(ref _activePermits),
                Queued = 0,
                LocalQueueCapacity = Plan.LocalQueueCapacity,
                AcceptedCount = Volatile.Read(ref _acquireCount),
                RejectedCount = 0,
                TimeoutCount = 0,
                HostSlotReady = _disposed == 0 && Volatile.Read(ref _ensureHostSlotCount) > 0,
                HostFencingToken = 1,
                HostLeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
                ActivePermits = Volatile.Read(ref _activePermits),
                RenewalLoopActive = false,
                TrackedWorkloadCount = 0
            };

        /// <summary>重設初始化觀測計數；只供 recycle-before-admission 斷言。</summary>
        public void ResetObservationCounters()
        {
            Volatile.Write(ref _acquireCount, 0);
            Volatile.Write(ref _ensureHostSlotCount, 0);
        }

        /// <summary>同步標記 Dispose；所有 Permit 必須已由 Combined Lease 歸還。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Volatile.Read(ref _activePermits).Should().Be(0);
            }
        }

        /// <summary>非同步相容路徑。</summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>歸還一個 Permit。</summary>
        private void ReleasePermit()
            => Interlocked.Decrement(ref _activePermits);

        /// <summary>只持有 admission owner 的 fake Permit。</summary>
        private sealed class RecyclePermit : IAdmissionPermit
        {
            private readonly RecycleAdmissionManager _owner;
            private int _disposed;

            /// <summary>接管已增加的 active permit 計數。</summary>
            public RecyclePermit(RecycleAdmissionManager owner)
            {
                _owner = owner;
            }

            /// <summary>取得不含 workload identity 的測試 correlation id。</summary>
            public Guid CorrelationId { get; } = Guid.NewGuid();

            /// <summary>取得固定有效 fencing token。</summary>
            public long HostFencingToken => 1;

            /// <summary>Fake 不模擬中途 lease loss。</summary>
            public CancellationToken LeaseLostToken => CancellationToken.None;

            /// <summary>同步歸還 Permit。</summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.ReleasePermit();
                }
            }

            /// <summary>非同步相容路徑。</summary>
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>只記錄 execution 次數的 bounded executor，不保留 request 或 response payload。</summary>
    private sealed class RecycleExecutor : IDynamicsOperationExecutor
    {
        private int _executionCount;

        /// <summary>取得已 dispatch 次數。</summary>
        public int ExecutionCount => Volatile.Read(ref _executionCount);

        /// <summary>回傳封閉成功結果，不保存 request。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _executionCount);
            return Task.FromResult(OperationExecutionResult.Success(null));
        }

        /// <summary>重設觀測計數。</summary>
        public void Reset()
            => Volatile.Write(ref _executionCount, 0);
    }
}
