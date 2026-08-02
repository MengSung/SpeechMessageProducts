// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/OperationDispatchQueueLifecycleTests.cs
// 目的：驗證 queue wait 只保留 prepared bounded state，並鎖定 lease 與 buffer 的 cleanup 順序。
//
// 信任邊界與生命週期：
// - NoInlining helper 建立 request/dictionary/JsonDocument graph，確保測試 frame 不會意外延長其壽命。
// - Blocking provider 只保留 DispatchEnvelope，不保留 request、JsonElement、client、runtime、principal 或 session。
// - 取消與例外不得略過 prepared.Dispose；但 prepared buffer 只能在 async lease cleanup 完整被 await 後清零歸還。
// ============================================================================

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 <see cref="ControlledOperationExecutor"/> 的 public 入口本身不是 async state machine，
/// 原始 request graph 在同步 prepare 結束後即可回收；排隊、成功、失敗、取消與 cleanup 均只由 prepared owner 承擔。
/// </summary>
public sealed class OperationDispatchQueueLifecycleTests
{
    /// <summary>
    /// 當 admission 確實阻塞時，原始 request、dictionary 與 JsonElement 底層 document 必須可回收；
    /// pending Task 只能保留有界 prepared dispatch。取消後 pooled buffer 只可清零並歸還一次。
    /// </summary>
    [Fact]
    public async Task Blocked_queue_does_not_retain_original_request_graph()
    {
        var pool = new RecordingArrayPool();
        var provider = new BlockingLeaseProvider(CreatePlan());
        var executor = new ControlledOperationExecutor(
            provider,
            new OperationDispatchPreparer(pool));
        using var cancellation = new CancellationTokenSource();

        var pending = StartBlockedExecution(executor, cancellation.Token);
        await provider.AcquireEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pending.Execution.IsCompleted.Should().BeFalse("the retention assertion is meaningful only while admission is blocked");

        ForceCollection(pending.RequestReference, pending.DictionaryReference, pending.DocumentReference);
        pending.RequestReference.IsAlive.Should().BeFalse();
        pending.DictionaryReference.IsAlive.Should().BeFalse();
        pending.DocumentReference.IsAlive.Should().BeFalse();

        cancellation.Cancel();
        await FluentActions.Awaiting(() => pending.Execution)
            .Should().ThrowAsync<OperationCanceledException>();
        pool.ReturnCalls.Should().Be(1);
        pool.ReturnedSnapshot.Should().OnlyContain(value => value == 0);
    }

    /// <summary>
    /// client 完成後 executor 必須先 await lease.DisposeAsync；在 lease owner 尚未完成 runtime/admission cleanup 前，
    /// prepared buffer 不可清零或歸還，否則並行 cleanup 可能觀察到已重用記憶體。
    /// </summary>
    [Fact]
    public async Task Lease_cleanup_completes_before_prepared_buffer_return()
    {
        var pool = new RecordingArrayPool();
        var lease = new BlockingDisposeLease(CreatePlan(), new SuccessfulClient());
        var provider = new ImmediateLeaseProvider(lease);
        var executor = new ControlledOperationExecutor(
            provider,
            new OperationDispatchPreparer(pool));

        var execution = executor.ExecuteAsync(CreateWhoAmIRequest());
        await lease.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pool.ReturnCalls.Should().Be(0, "prepared ownership ends only after lease cleanup is fully observed");

        lease.AllowDispose();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        result.Succeeded.Should().BeTrue();
        lease.DisposeCompleted.Should().BeTrue();
        pool.ReturnCalls.Should().Be(1);
        pool.ReturnedSnapshot.Should().OnlyContain(value => value == 0);
    }

    /// <summary>
    /// 成功、client 回傳受控失敗與 client throw 都必須先釋放 admission permit，再清除 prepared owner。
    /// 本測試只斷言即時 gauges 回到 baseline；Accepted/Rejected/Timeout 是累計 counter，發生活動後不會歸零。
    /// </summary>
    [Theory]
    [InlineData(ClientOutcome.Success)]
    [InlineData(ClientOutcome.ControlledFailure)]
    [InlineData(ClientOutcome.Throw)]
    public async Task Completed_client_paths_restore_live_admission_gauges(ClientOutcome outcome)
    {
        var pool = new RecordingArrayPool();
        await using var manager = CreateAdmissionManager();
        var client = new OutcomeClient(outcome);
        var executor = new ControlledOperationExecutor(
            new FixedProfileExecutionLeaseProvider(client, manager),
            new OperationDispatchPreparer(pool));

        if (outcome == ClientOutcome.Throw)
        {
            await FluentActions.Awaiting(() => executor.ExecuteAsync(CreateWhoAmIRequest()))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            var result = await executor.ExecuteAsync(CreateWhoAmIRequest());
            result.Succeeded.Should().Be(outcome == ClientOutcome.Success);
        }

        AssertLiveGaugesAtBaseline(manager.GetSnapshot());
        pool.ReturnCalls.Should().Be(1);
        pool.ReturnedSnapshot.Should().OnlyContain(value => value == 0);
    }

    /// <summary>
    /// 真實 manager 的唯一 in-flight permit 被 holder 佔用時，第二個 prepared dispatch 必須確實進入 queue。
    /// caller cancellation 或 queue timeout 完成後，釋放 holder 必須使所有 live gauges 回到零且 buffer 只歸還一次。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Queued_cancellation_and_timeout_restore_live_admission_gauges(bool cancelCaller)
    {
        var pool = new RecordingArrayPool();
        await using var manager = CreateAdmissionManager(queueAdmissionTimeoutSeconds: 1);
        var holderResult = await manager.AcquireAsync(
            CreateEnvelope("holder-workload"),
            CancellationToken.None);
        holderResult.Succeeded.Should().BeTrue();
        var holder = holderResult.Permit!;
        using var cancellation = new CancellationTokenSource();
        var executor = new ControlledOperationExecutor(
            new FixedProfileExecutionLeaseProvider(new SuccessfulClient(), manager),
            new OperationDispatchPreparer(pool));

        var execution = executor.ExecuteAsync(CreateWhoAmIRequest(), cancellation.Token);
        await WaitForSnapshotAsync(manager, snapshot => snapshot.Queued == 1);
        if (cancelCaller)
        {
            cancellation.Cancel();
        }

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.AdmissionTimeout);
        await holder.DisposeAsync();

        AssertLiveGaugesAtBaseline(manager.GetSnapshot());
        pool.ReturnCalls.Should().Be(1);
        pool.ReturnedSnapshot.Should().OnlyContain(value => value == 0);
    }

    /// <summary>
    /// Manager shutdown 必須先停止 admission 並取消 queued prepared work，再等待已取得的 holder permit 釋放；
    /// shutdown Task 完成即代表 reservation、renewal、host slot 與 semaphore ownership 都已結束。
    /// Prepared buffer 不得等到 holder 釋放才回收，因為該 queued request 已沒有 lease ownership。
    /// </summary>
    [Fact]
    public async Task Manager_shutdown_cancels_queued_dispatch_and_drains_live_gauges()
    {
        var pool = new RecordingArrayPool();
        var manager = CreateAdmissionManager(queueAdmissionTimeoutSeconds: 10);
        var holderResult = await manager.AcquireAsync(
            CreateEnvelope("shutdown-holder"),
            CancellationToken.None);
        holderResult.Succeeded.Should().BeTrue();
        var holder = holderResult.Permit!;
        var executor = new ControlledOperationExecutor(
            new FixedProfileExecutionLeaseProvider(new SuccessfulClient(), manager),
            new OperationDispatchPreparer(pool));

        var execution = executor.ExecuteAsync(CreateWhoAmIRequest());
        await WaitForSnapshotAsync(manager, snapshot => snapshot.Queued == 1);
        var shutdown = manager.DisposeAsync().AsTask();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.HostSlotUnavailable);
        pool.ReturnCalls.Should().Be(1);
        pool.ReturnedSnapshot.Should().OnlyContain(value => value == 0);

        await holder.DisposeAsync();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        AssertLiveGaugesAtBaseline(manager.GetSnapshot());
    }

    /// <summary>
    /// capacity rejection、admission timeout 與 shutdown/not-ready 均可在沒有 lease 的情況下終止。Executor 仍是 prepared 的
    /// 唯一 owner，必須在回傳受控錯誤前清除全 buffer，且 client 絕不可被呼叫。
    /// </summary>
    [Theory]
    [InlineData(DynamicsErrorCodes.CapacityRejected)]
    [InlineData(DynamicsErrorCodes.AdmissionTimeout)]
    [InlineData(DynamicsErrorCodes.NotReady)]
    public async Task Acquisition_rejection_paths_return_prepared_buffer_once(string errorCode)
    {
        var pool = new RecordingArrayPool();
        var provider = new RejectingLeaseProvider(CreatePlan(), errorCode);
        var executor = new ControlledOperationExecutor(
            provider,
            new OperationDispatchPreparer(pool));

        var result = await executor.ExecuteAsync(CreateWhoAmIRequest());

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(errorCode);
        provider.AcquireCalls.Should().Be(1);
        pool.ReturnCalls.Should().Be(1);
        pool.ReturnedSnapshot.Should().OnlyContain(value => value == 0);
    }

    /// <summary>
    /// 在獨立 stack frame 建立原始 graph，並以 <c>using</c> 讓 helper 成為 JsonDocument 的明確 owner。
    /// <c>ExecuteAsync</c> 返回時同步 prepare 已完成，helper 立即 Dispose document；若舊 async state machine
    /// 仍保留 request/JsonElement，已 Dispose document 物件的弱參考仍會繼續存活，因此不需要製造資源漏洞也可驗證 retention。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PendingExecution StartBlockedExecution(
        ControlledOperationExecutor executor,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse("\"bounded-值🙂\"");
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["logicalProfileId"] = document.RootElement
        };
        var request = new OperationExecutionRequest
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.RuntimePoolValidateConnection,
            WorkloadSubjectId = "queue-retention-workload",
            Parameters = parameters
        };
        var execution = executor.ExecuteAsync(request, cancellationToken);
        return new PendingExecution(
            execution,
            new WeakReference(request),
            new WeakReference(parameters),
            new WeakReference(document));
    }

    /// <summary>重複執行 bounded full GC，降低 JIT stack lifetime 與分代 GC 時機對 retention 斷言的干擾。</summary>
    private static void ForceCollection(params WeakReference[] references)
    {
        for (var attempt = 0; attempt < 10 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>建立無參數 WhoAmI request，作為 cleanup ordering 的最小 dispatch。</summary>
    private static OperationExecutionRequest CreateWhoAmIRequest()
        => new()
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "queue-lifecycle-workload"
        };

    /// <summary>建立有界 queue/outbound timeout 的不可變 plan，不含 credential、token 或 request 資料。</summary>
    private static OrganizationAdmissionPlan CreatePlan(int queueAdmissionTimeoutSeconds = 2)
    {
        var admissionOptions = new OrganizationAdmissionOptions
        {
            ExpectedOrganizationId = Guid.Parse("67676767-6767-6767-6767-676767676767"),
            AggregateMaxInFlight = 1,
            MaximumRuntimeHosts = 1,
            LocalQueueCapacity = 1,
            MaxInFlightAndQueuedPerWorkload = 2,
            MaxDispatchEnvelopeBytes = 4096,
            QueueAdmissionTimeoutSeconds = queueAdmissionTimeoutSeconds,
            MaximumOutboundWorkLifetimeSeconds = 5,
            RuntimeHostSlotLeaseTtlSeconds = 15,
            RuntimeHostSlotRenewalIntervalSeconds = 2,
            RuntimeHostSlotExpiryFenceSeconds = 2,
            AdmissionNamespaceId = "dispatch-queue-tests",
            LeaseNamespaceId = "dispatch-queue-tests"
        };
        OrganizationAdmissionPlan.TryCreate(
                "https://crm.example.local/DispatchQueue/",
                workerCount: 1,
                maxInFlightPerWorker: 1,
                admissionOptions,
                out var plan,
                out var error)
            .Should().BeTrue(error?.ErrorMessage);
        return plan!;
    }

    /// <summary>
    /// 建立測試獨佔的真實 admission manager。Manager 是所有 semaphore、renewal task、CTS 與 permit 的唯一 owner，
    /// 測試以 <c>await using</c> 等待確定性 shutdown，不遺留 background renewal 或 host-slot lease。
    /// </summary>
    private static OrganizationAdmissionManager CreateAdmissionManager(int queueAdmissionTimeoutSeconds = 2)
        => new(
            CreatePlan(queueAdmissionTimeoutSeconds),
            new InMemoryRuntimeHostSlotCoordinator(),
            NullLogger<OrganizationAdmissionManager>.Instance);

    /// <summary>
    /// 建立供 holder 使用的有界 envelope；與 production prepared envelope 相同，不包含 parameter graph、
    /// user/session identity、token、client 或 runtime。
    /// </summary>
    private static DispatchEnvelope CreateEnvelope(string workloadSubjectId)
        => new()
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = workloadSubjectId,
            TemplateId = "WhoAmI",
            TemplateHash = new string('a', 64),
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
            CanonicalEnvelopeBytes = 256
        };

    /// <summary>斷言所有會保留 queue/permit/workload ownership 的即時 gauges 已回零，不檢查累計 counter。</summary>
    private static void AssertLiveGaugesAtBaseline(AdmissionMetricsSnapshot snapshot)
    {
        snapshot.Queued.Should().Be(0);
        snapshot.InFlight.Should().Be(0);
        snapshot.ActivePermits.Should().Be(0);
        snapshot.TrackedWorkloadCount.Should().Be(0);
    }

    /// <summary>
    /// 以 condition polling 等待真實 queue gauge，不以固定 sleep 猜測時序。超時會連同最後快照失敗，
    /// 避免測試在 provider 尚未排隊時做無意義的 retention/cancellation 斷言。
    /// </summary>
    private static async Task WaitForSnapshotAsync(
        OrganizationAdmissionManager manager,
        Func<AdmissionMetricsSnapshot, bool> predicate)
    {
        var timeout = Stopwatch.StartNew();
        AdmissionMetricsSnapshot snapshot;
        do
        {
            snapshot = manager.GetSnapshot();
            if (predicate(snapshot))
            {
                return;
            }

            await Task.Delay(10);
        }
        while (timeout.Elapsed < TimeSpan.FromSeconds(5));

        throw new TimeoutException(
            $"Admission snapshot did not reach the expected state. Queued={snapshot.Queued}, " +
            $"InFlight={snapshot.InFlight}, ActivePermits={snapshot.ActivePermits}.");
    }

    /// <summary>封裝 pending Task 與必須可回收的三個原始 graph owner，不保留實體強參考。</summary>
    private sealed record PendingExecution(
        Task<OperationExecutionResult> Execution,
        WeakReference RequestReference,
        WeakReference DictionaryReference,
        WeakReference DocumentReference);

    /// <summary>
    /// 模擬真實 queue wait；只儲存 bounded envelope 供斷言，等待期間觀察 caller cancellation，
    /// 不儲存 request、parameter dictionary、JsonElement、runtime 或 client。
    /// </summary>
    private sealed class BlockingLeaseProvider : IProfileExecutionLeaseProvider
    {
        /// <summary>建立綁定一個 immutable plan 的阻塞 provider。</summary>
        public BlockingLeaseProvider(OrganizationAdmissionPlan plan)
        {
            Plan = plan;
        }

        /// <summary>取得排隊已實際開始的訊號，continuation 在非同步執行以避免重入。</summary>
        public TaskCompletionSource AcquireEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>取得 provider 不可變的 admission plan。</summary>
        public OrganizationAdmissionPlan Plan { get; }

        /// <summary>同步解析 plan，不觸發 I/O、runtime 或秘密資料。</summary>
        public bool TryGetAdmissionPlan(string profileAlias, out OrganizationAdmissionPlan? admissionPlan)
        {
            admissionPlan = Plan;
            return true;
        }

        /// <summary>通知測試已進入 wait，並無限期等待 caller cancellation；不產生半完成 lease。</summary>
        public async Task<ProfileExecutionLeaseAcquireResult> AcquireAsync(
            DispatchEnvelope envelope,
            CancellationToken cancellationToken)
        {
            AcquireEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking admission test unexpectedly resumed without cancellation.");
        }
    }

    /// <summary>立即返回指定 lease，用來單獨驗證 executor 的反向 cleanup 順序。</summary>
    private sealed class ImmediateLeaseProvider : IProfileExecutionLeaseProvider
    {
        private readonly IProfileExecutionLease _lease;

        /// <summary>建立只轉移一次測試 lease ownership 的 provider。</summary>
        public ImmediateLeaseProvider(IProfileExecutionLease lease)
        {
            _lease = lease;
        }

        /// <summary>以 lease 的 immutable plan 回覆同步 lookup。</summary>
        public bool TryGetAdmissionPlan(string profileAlias, out OrganizationAdmissionPlan? admissionPlan)
        {
            admissionPlan = _lease.AdmissionPlan;
            return true;
        }

        /// <summary>立即轉移 lease ownership，不建立額外背景工作或 cancellation registration。</summary>
        public Task<ProfileExecutionLeaseAcquireResult> AcquireAsync(
            DispatchEnvelope envelope,
            CancellationToken cancellationToken)
            => Task.FromResult(ProfileExecutionLeaseAcquireResult.Success(_lease));
    }

    /// <summary>
    /// 立即回傳受控取得失敗的 provider。它不建立 permit/runtime/client ownership，
    /// 用來證明「沒有 lease」不代表可以略過 prepared cleanup。
    /// </summary>
    private sealed class RejectingLeaseProvider : IProfileExecutionLeaseProvider
    {
        private readonly string _errorCode;
        private int _acquireCalls;

        /// <summary>建立固定回傳一種 rejection 的 provider。</summary>
        public RejectingLeaseProvider(OrganizationAdmissionPlan plan, string errorCode)
        {
            Plan = plan;
            _errorCode = errorCode;
        }

        /// <summary>取得 Acquire 實際呼叫次數。</summary>
        public int AcquireCalls => Volatile.Read(ref _acquireCalls);

        /// <summary>取得不含 request/runtime 的 immutable plan。</summary>
        public OrganizationAdmissionPlan Plan { get; }

        /// <summary>同步回傳 plan，不觸發 I/O。</summary>
        public bool TryGetAdmissionPlan(string profileAlias, out OrganizationAdmissionPlan? admissionPlan)
        {
            admissionPlan = Plan;
            return true;
        }

        /// <summary>記錄一次呼叫並回傳不包含半完成資源的受控錯誤。</summary>
        public Task<ProfileExecutionLeaseAcquireResult> AcquireAsync(
            DispatchEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _acquireCalls);
            return Task.FromResult(ProfileExecutionLeaseAcquireResult.Failure(
                OperationExecutionResult.Failure(_errorCode, "rejected-for-lifecycle-test")));
        }
    }

    /// <summary>
    /// 具有可控非同步 Dispose 的 lease。Dispose owner 保留 cleanup 完成訊號，不保留 request 或 prepared buffer。
    /// </summary>
    private sealed class BlockingDisposeLease : IProfileExecutionLease
    {
        private readonly TaskCompletionSource _allowDispose =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        /// <summary>建立綁定 immutable plan 與無狀態 client 的 lease。</summary>
        public BlockingDisposeLease(OrganizationAdmissionPlan admissionPlan, IDynamicsOperationExecutor executor)
        {
            AdmissionPlan = admissionPlan;
            Executor = executor;
        }

        /// <summary>取得 cleanup 已進入但尚未完成的訊號。</summary>
        public TaskCompletionSource DisposeEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>取得 async cleanup 是否已完整離開。</summary>
        public bool DisposeCompleted { get; private set; }

        /// <summary>測試路徑沒有 runtime catalog generation。</summary>
        public ProfileRuntimeKey? RuntimeKey => null;

        /// <summary>取得只在 lease 生命週期內使用的 client。</summary>
        public IDynamicsOperationExecutor Executor { get; }

        /// <summary>取得計算 outbound 期限的 immutable plan。</summary>
        public OrganizationAdmissionPlan AdmissionPlan { get; }

        /// <summary>本測試不模擬 host fencing。</summary>
        public CancellationToken LeaseLostToken => CancellationToken.None;

        /// <summary>本測試不模擬 runtime retirement。</summary>
        public CancellationToken RetirementToken => CancellationToken.None;

        /// <summary>
        /// 同步 Dispose 先放行測試 gate 再等待同一 idempotent cleanup，避免介面誤用形成死鎖；
        /// 測試主路徑仍必須使用 async cleanup 驗證 ordering。
        /// </summary>
        public void Dispose()
        {
            _allowDispose.TrySetResult();
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <summary>以 Interlocked 確保單一 cleanup owner，並在測試放行前保持未完成狀態。</summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            DisposeEntered.TrySetResult();
            await _allowDispose.Task;
            DisposeCompleted = true;
        }

        /// <summary>允許測試 lease 完成 cleanup，訊號可重複呼叫。</summary>
        public void AllowDispose() => _allowDispose.TrySetResult();
    }

    /// <summary>回傳最小成功結果的無狀態 client，不儲存 parameters 或 cancellation registration。</summary>
    private sealed class SuccessfulClient : IDynamicsOperationExecutor
    {
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperationExecutionResult.Success(data: null));

        /// <summary>健康檢查路徑本測試不使用。</summary>
        public Task<OperationExecutionResult> WhoAmIAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>同步建立成功 Task，不延長 definition 或 parameter container 壽命。</summary>
        public Task<OperationExecutionResult> ExecuteRegisteredOperationAsync(
            OperationDefinition definition,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperationExecutionResult.Success(data: null));
    }

    /// <summary>列舉 client 終端行為，用一套真實 admission 測試成功、受控失敗與 throw cleanup。</summary>
    public enum ClientOutcome
    {
        /// <summary>回傳成功結果。</summary>
        Success,

        /// <summary>回傳受控失敗結果。</summary>
        ControlledFailure,

        /// <summary>拋出未受控例外，驗證 finally cleanup。</summary>
        Throw
    }

    /// <summary>
    /// 依測試設定完成、失敗或拋出的 client；不儲存 parameters、definition 或 cancellation registration，
    /// 因此呼叫完成後唯一剩餘 owner 是 executor 的 cleanup frame。
    /// </summary>
    private sealed class OutcomeClient : IDynamicsOperationExecutor
    {
        private readonly ClientOutcome _outcome;

        /// <summary>建立固定終端行為的無狀態 client。</summary>
        public OutcomeClient(ClientOutcome outcome)
        {
            _outcome = outcome;
        }

        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
            => _outcome switch
            {
                ClientOutcome.Success => Task.FromResult(OperationExecutionResult.Success(data: null)),
                ClientOutcome.ControlledFailure => Task.FromResult(OperationExecutionResult.Failure(
                    DynamicsErrorCodes.NotReady,
                    "controlled-client-failure")),
                ClientOutcome.Throw => Task.FromException<OperationExecutionResult>(
                    new InvalidOperationException("client-threw-for-lifecycle-test")),
                _ => throw new ArgumentOutOfRangeException()
            };

        /// <summary>健康檢查路徑本測試不使用。</summary>
        public Task<OperationExecutionResult> WhoAmIAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>立即產生指定結果，不轉移 parameters ownership。</summary>
        public Task<OperationExecutionResult> ExecuteRegisteredOperationAsync(
            OperationDefinition definition,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
            => _outcome switch
            {
                ClientOutcome.Success => Task.FromResult(OperationExecutionResult.Success(data: null)),
                ClientOutcome.ControlledFailure => Task.FromResult(OperationExecutionResult.Failure(
                    DynamicsErrorCodes.NotReady,
                    "controlled-client-failure")),
                ClientOutcome.Throw => Task.FromException<OperationExecutionResult>(
                    new InvalidOperationException("client-threw-for-lifecycle-test")),
                _ => throw new ArgumentOutOfRangeException()
            };
    }

    /// <summary>記錄 prepared buffer 歸還順序的 pool；陣列 tail 預填非零值以證明整段清零。</summary>
    private sealed class RecordingArrayPool : ArrayPool<byte>
    {
        private int _returnCalls;

        /// <summary>取得 Return 次數。</summary>
        public int ReturnCalls => Volatile.Read(ref _returnCalls);

        /// <summary>取得 Return 當下的整個 buffer 快照。</summary>
        public byte[] ReturnedSnapshot { get; private set; } = [];

        /// <summary>租借含額外 tail 的非零陣列。</summary>
        public override byte[] Rent(int minimumLength)
        {
            var buffer = new byte[checked(minimumLength + 17)];
            Array.Fill(buffer, (byte)0x5A);
            return buffer;
        }

        /// <summary>在歸還當下複製快照，測試不重用此陣列。</summary>
        public override void Return(byte[] array, bool clearArray = false)
        {
            ReturnedSnapshot = array.ToArray();
            Interlocked.Increment(ref _returnCalls);
        }
    }
}
