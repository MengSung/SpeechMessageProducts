using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Connectors;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Official Worker 到 Connector Pool/Lease 的離線 adapter。所有替身均為測試擁有的純記憶體
/// scalar，沒有 CE、CRM SDK、process、pipe、secret 或 network I/O；測試因此能聚焦於 P6 的唯一
/// admission/runtime lease transfer、generation match 與 deterministic disposal 契約。
/// </summary>
public sealed class OfficialWorkerConnectorPoolTests
{
    /// <summary>
    /// 驗證一個 Official 9.1 Connector operation 只向既有 ProfileExecution provider 取得一次合併 lease，
    /// 並把 bounded OperationResponseData 原樣交回 Connector result。離開 await using 後必須剛好釋放一次
    /// underlying runtime/admission owner，避免 adapter 另行取得 permit 或保留 Worker generation reference。
    /// </summary>
    [Fact]
    public async Task Acquire_and_execute_transfers_one_existing_profile_lease_and_releases_it_on_dispose()
    {
        var profile = CreateProfile();
        var executionLease = new TrackingProfileExecutionLease(
            CreateRuntimeKey(profile),
            CreateAdmissionPlan(),
            OperationExecutionResult.Success(OperationResponseData.ForWhoAmI(
                OperationIds.RuntimeHealthWhoAmI,
                "9.1",
                new WhoAmIResponseData
                {
                    UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    BusinessUnitId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    OrganizationId = profile.OrganizationId
                })));
        var provider = new TrackingProfileExecutionLeaseProvider(executionLease);
        await using var pool = new OfficialWorkerConnectorPool(profile, provider);
        var operation = CreateOperation();

        await using (var connectorLease = await pool.AcquireAsync(operation, CancellationToken.None))
        {
            var result = await connectorLease.ExecuteAsync(operation, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.ResponseKind.Should().Be(OperationResponseKind.WhoAmI);
        }

        provider.AcquireCount.Should().Be(1);
        executionLease.Executor.ExecutionCount.Should().Be(1);
        executionLease.DisposeCount.Should().Be(1);
        pool.ActiveLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證已過期的 Connector operation 在建立 DispatchEnvelope、取得 admission permit、Worker runtime、
    /// pipe 或 cancellation registration 前即被拒絕。決定性斷言是 provider 零次 Acquire，避免 deadline
    /// 已失效的 request 把任何資源帶入 drain／dispose 路徑。
    /// </summary>
    [Fact]
    public async Task Expired_operation_is_rejected_before_profile_lease_acquisition()
    {
        var profile = CreateProfile();
        var executionLease = new TrackingProfileExecutionLease(
            CreateRuntimeKey(profile),
            CreateAdmissionPlan(),
            OperationExecutionResult.Failure("unused", "unused"));
        var provider = new TrackingProfileExecutionLeaseProvider(executionLease);
        await using var pool = new OfficialWorkerConnectorPool(profile, provider);
        var expired = CreateOperation() with { DeadlineUtc = DateTimeOffset.UtcNow.AddMilliseconds(-1) };

        var action = () => pool.AcquireAsync(expired, CancellationToken.None);

        await action.Should().ThrowAsync<OperationCanceledException>();
        provider.AcquireCount.Should().Be(0);
        executionLease.DisposeCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 Provider 在 Router snapshot 與 Active Runtime generation 競速交換後，錯誤世代 lease 會先被
    /// 完整釋放再回報 fail-closed。這避免 adapter 把新 generation 的 Worker／Permit 誤掛到舊 Pool key。
    /// </summary>
    [Fact]
    public async Task Runtime_generation_mismatch_is_rejected_and_underlying_lease_is_released()
    {
        var profile = CreateProfile();
        var mismatchedLease = new TrackingProfileExecutionLease(
            CreateRuntimeKey(profile) with { Generation = profile.GenerationId + 1 },
            CreateAdmissionPlan(),
            OperationExecutionResult.Failure("unused", "unused"));
        var provider = new TrackingProfileExecutionLeaseProvider(mismatchedLease);
        await using var pool = new OfficialWorkerConnectorPool(profile, provider);

        var action = () => pool.AcquireAsync(CreateOperation(), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        mismatchedLease.DisposeCount.Should().Be(1);
        pool.ActiveLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 Pool drain 會先關閉新 acquisition，再等待既有 lease；釋放既有 lease 後 drain 才完成，
    /// 避免 replacement 期間有新的 Worker runtime reference 或 admission permit 進入舊世代。
    /// </summary>
    [Fact]
    public async Task Drain_rejects_new_acquisition_and_waits_for_existing_lease()
    {
        var profile = CreateProfile();
        var executionLease = new TrackingProfileExecutionLease(
            CreateRuntimeKey(profile),
            CreateAdmissionPlan(),
            OperationExecutionResult.Failure("unused", "unused"));
        var provider = new TrackingProfileExecutionLeaseProvider(executionLease);
        await using var pool = new OfficialWorkerConnectorPool(profile, provider);
        var heldLease = await pool.AcquireAsync(CreateOperation(), CancellationToken.None);

        var drain = pool.DrainAsync();
        drain.IsCompleted.Should().BeFalse();
        var newAcquire = () => pool.AcquireAsync(CreateOperation(), CancellationToken.None);
        await newAcquire.Should().ThrowAsync<ObjectDisposedException>();

        await heldLease.DisposeAsync();
        await drain;
        pool.ActiveLeaseCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 Official Worker Pool Registry 在同一 Alias 只發布一個 Active 與最多一個 Draining generation。
    /// 新 generation 發布時舊 Pool 先同步封閉 acquisition；只有明確等待 drain 完成後才可再替換，避免
    /// registry reference、Worker runtime lease 或 permit owner 無界累積。
    /// </summary>
    [Fact]
    public async Task Registry_publishes_one_active_and_one_draining_generation()
    {
        var first = CreateProfile();
        var second = first with { GenerationId = first.GenerationId + 1 };
        var provider = new TrackingProfileExecutionLeaseProvider(
            new TrackingProfileExecutionLease(
                CreateRuntimeKey(first),
                CreateAdmissionPlan(),
                OperationExecutionResult.Failure("unused", "unused")));
        await using var registry = new OfficialWorkerConnectorPoolRegistry(provider);

        var firstPool = registry.Resolve(first);
        var secondPool = registry.Resolve(second);

        firstPool.Should().NotBeSameAs(secondPool);
        firstPool.IsDraining.Should().BeTrue();
        registry.Resolve(second).Should().BeSameAs(secondPool);

        await registry.DrainCompletedGenerationsAsync();
        await registry.DisposeAsync();
    }

    /// <summary>建立已通過 CE/Connector 相容性驗證的 Official 9.1 Profile snapshot。</summary>
    private static ResolvedProfile CreateProfile()
        => new(
            "crm91",
            "crm91",
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CeVersion.Ce91,
            ConnectorKind.OfficialCrm91Worker,
            "test-credential-reference",
            new ResolvedPoolPolicy(0, 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(2), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(5), 0, TimeSpan.Zero),
            7);

    /// <summary>建立只含 identity operation 的 bounded Connector request。</summary>
    private static ConnectorOperation CreateOperation()
        => new()
        {
            OperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "official-worker-pool-test",
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5)
        };

    /// <summary>建立與 Profile 完全相符的 non-secret active runtime key。</summary>
    private static ProfileRuntimeKey CreateRuntimeKey(ResolvedProfile profile)
        => new(
            profile.ProfileAlias,
            profile.GenerationId,
            "9.1",
            new CanonicalOrganizationCapacityKey(
                profile.OrganizationId,
                "https://crm.example.test/"));

    /// <summary>
    /// 建立可供 preparer/admission 讀取的最小 immutable plan。測試不執行 manager 的 I/O；Plan 只讓
    /// Official Worker Pool 驗證 canonical envelope 上限和 operation lifetime。
    /// </summary>
    private static OrganizationAdmissionPlan CreateAdmissionPlan()
    {
        var options = new OrganizationAdmissionOptions
        {
            ExpectedOrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            AggregateMaxInFlight = 1,
            MaximumRuntimeHosts = 1,
            LocalQueueCapacity = 1,
            MaxDispatchEnvelopeBytes = 4_096,
            QueueAdmissionTimeoutSeconds = 5,
            MaxInFlightAndQueuedPerWorkload = 1,
            AdmissionNamespaceId = "official-worker-pool-test",
            LeaseNamespaceId = "official-worker-pool-test",
            AdmissionEpoch = 1,
            RuntimeHostSlotLeaseTtlSeconds = 120,
            RuntimeHostSlotRenewalIntervalSeconds = 15,
            RuntimeHostSlotExpiryFenceSeconds = 10,
            MaximumOutboundWorkLifetimeSeconds = 30,
            ShutdownDrainTimeoutSeconds = 30,
            RequireDurableHostCoordinator = false
        };
        OrganizationAdmissionPlan.TryCreate(
                "https://crm.example.test/",
                workerCount: 1,
                maxInFlightPerWorker: 1,
                options,
                out var plan,
                out var error)
            .Should().BeTrue(error?.ErrorMessage);
        return plan!;
    }

    /// <summary>
    /// 追蹤 ProfileExecution provider 的最小替身；它只回傳 constructor 已轉移給它的 single lease，
    /// 不建立第二個 permit、runtime、process 或 background owner。
    /// </summary>
    private sealed class TrackingProfileExecutionLeaseProvider : IProfileExecutionLeaseProvider
    {
        private readonly TrackingProfileExecutionLease _lease;

        /// <summary>建立固定回傳單一 test-owned lease 的 provider。</summary>
        public TrackingProfileExecutionLeaseProvider(TrackingProfileExecutionLease lease) => _lease = lease;

        /// <summary>取得 provider Acquire 呼叫次數。</summary>
        public int AcquireCount { get; private set; }

        /// <summary>回傳測試 lease 的 immutable admission plan。</summary>
        public bool TryGetAdmissionPlan(string profileAlias, out OrganizationAdmissionPlan? admissionPlan)
        {
            admissionPlan = string.Equals(profileAlias, _lease.RuntimeKey!.Value.ProfileAlias, StringComparison.Ordinal)
                ? _lease.AdmissionPlan
                : null;
            return admissionPlan is not null;
        }

        /// <summary>記錄 acquire 並把唯一 lease ownership 交給 Connector Pool。</summary>
        public Task<ProfileExecutionLeaseAcquireResult> AcquireAsync(
            DispatchEnvelope envelope,
            CancellationToken cancellationToken)
        {
            AcquireCount++;
            return Task.FromResult(ProfileExecutionLeaseAcquireResult.Success(_lease));
        }
    }

    /// <summary>
    /// 代表既有 Manager 已取得的 runtime/admission composite lease。它由 Connector Lease 唯一 Dispose；
    /// 計數能偵測 adapter 若重複釋放或沒有釋放既有 owner 的資源洩漏。
    /// </summary>
    private sealed class TrackingProfileExecutionLease : IProfileExecutionLease
    {
        private int _disposed;

        /// <summary>建立含固定 runtime key、plan 與無資源 fake executor 的 composite lease。</summary>
        public TrackingProfileExecutionLease(
            ProfileRuntimeKey runtimeKey,
            OrganizationAdmissionPlan admissionPlan,
            OperationExecutionResult result)
        {
            RuntimeKey = runtimeKey;
            AdmissionPlan = admissionPlan;
            Executor = new TrackingExecutor(result);
        }

        /// <summary>取得 runtime generation key。</summary>
        public ProfileRuntimeKey? RuntimeKey { get; }

        /// <summary>取得 Connector Lease 可在單次 operation 中使用的 executor。</summary>
        public TrackingExecutor Executor { get; }

        /// <summary>以介面型別公開同一 executor。</summary>
        IDynamicsOperationExecutor IProfileExecutionLease.Executor => Executor;

        /// <summary>取得 admission plan。</summary>
        public OrganizationAdmissionPlan AdmissionPlan { get; }

        /// <summary>測試不模擬 host fencing，因此不會取消。</summary>
        public CancellationToken LeaseLostToken => CancellationToken.None;

        /// <summary>測試不模擬 drain timeout，因此不會取消。</summary>
        public CancellationToken RetirementToken => CancellationToken.None;

        /// <summary>取得 exactly-once Dispose 次數。</summary>
        public int DisposeCount => Volatile.Read(ref _disposed);

        /// <summary>同步 Dispose 導向同一個 idempotent 釋放計數。</summary>
        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

        /// <summary>非同步 Dispose 不建立背景工作，並與同步路徑共用同一個 ownership guard。</summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 回傳固定受控結果的 executor。它不保存 request、credential、SDK、session 或 transport；呼叫計數
    /// 只驗證 Connector Lease 是否確實將 allowlisted operation 交給已取得的 Runtime lease。
    /// </summary>
    private sealed class TrackingExecutor : IDynamicsOperationExecutor
    {
        private readonly OperationExecutionResult _result;

        /// <summary>建立固定結果的無資源 executor。</summary>
        public TrackingExecutor(OperationExecutionResult result) => _result = result;

        /// <summary>取得 Execute 呼叫次數。</summary>
        public int ExecutionCount { get; private set; }

        /// <summary>記錄一次 bounded execution，沒有外部 I/O 或 retained state。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(_result);
        }
    }
}
