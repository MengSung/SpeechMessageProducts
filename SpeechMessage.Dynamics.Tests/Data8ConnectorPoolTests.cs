using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Connectors.Data8;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.Abstractions.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 P3 Data8 Pool 的世代隔離、Permit 生命週期與故障淘汰契約。
/// 每個 fake client 都是測試擁有的可計數資源；測試結束前必須回到零，
/// 以便捕捉連線、Permit 或 Lease 遺留造成的 Memory／Resource Leakage。
/// </summary>
/// <remarks>
/// 每個測試都以 <c>await using</c> 釋放 Pool、Lease、Registry 與 Registration，並使用不含真實端點、
/// Credential、Token 或 Session 的替身。測試聚焦於 Client、Permit、Cancellation、Drain 與 Generation 的
/// 確定性所有權，避免把 P3 的隔離與資源回收契約交給整合環境才驗證。
/// </remarks>
public sealed class Data8ConnectorPoolTests
{
    /// <summary>
    /// 驗證健康 Lease 只歸還建立它的 Profile Generation，並在結束時釋放一次 Permit；
    /// Drain 後 idle Client 也必須被確定 Dispose，避免閒置連線永久保留。
    /// </summary>
    [Fact]
    public async Task Healthy_lease_returns_to_original_generation_and_releases_permit()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new TrackingClientFactory();
        await using var pool = CreatePool("sunnyvalechback", 7, admission, factory);

        await using (var lease = await pool.AcquireAsync(CreateOperation(), CancellationToken.None))
        {
            Assert.Equal("sunnyvalechback", lease.ProfileAlias);
            Assert.Equal(7, lease.GenerationId);
        }

        Assert.Equal(1, factory.CreatedCount);
        Assert.Equal(0, factory.DisposedCount);
        Assert.Equal(1, admission.AcquireCount);
        Assert.Equal(1, admission.ReleaseCount);

        await pool.DrainAsync();
        Assert.Equal(1, factory.DisposedCount);
    }

    /// <summary>
    /// 注入故障標記並驗證 Client 不會重新進入 idle queue；下一次借用必須建立新 Client，
    /// 防止不明傳輸狀態、WCF Channel 或 Session 被後續請求重用。
    /// </summary>
    [Fact]
    public async Task Faulted_lease_is_evicted_and_never_reenters_idle_pool()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new TrackingClientFactory();
        await using var pool = CreatePool("sunnyvalechback", 8, admission, factory);

        await using (var lease = await pool.AcquireAsync(CreateOperation(), CancellationToken.None))
        {
            lease.MarkFaulted(new InvalidOperationException("test fault"));
        }

        await using (var secondLease = await pool.AcquireAsync(CreateOperation(), CancellationToken.None))
        {
            Assert.Equal(2, factory.CreatedCount);
        }

        Assert.Equal(1, factory.DisposedCount);
        await pool.DrainAsync();
        Assert.Equal(2, factory.DisposedCount);
    }

    /// <summary>
    /// 在 Factory 等待期間取消取得 Lease，驗證 rollback 只釋放已取得的 Permit，
    /// 且不會留下 Client、local slot、Timer 或取消註冊。
    /// </summary>
    [Fact]
    public async Task Cancellation_during_acquire_releases_permit_and_does_not_create_client()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new TrackingClientFactory { BlockCreation = true };
        await using var pool = CreatePool("sunnyvalechback", 9, admission, factory);
        using var cancellation = new CancellationTokenSource();

        var acquisition = pool.AcquireAsync(CreateOperation(), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquisition);
        Assert.Equal(1, admission.ReleaseCount);
        Assert.Equal(0, factory.CreatedCount);
    }

    /// <summary>
    /// 在取得流程超過作業截止時間時，驗證短生命週期 deadline CTS 會取消等待、釋放 Permit，
    /// 且 Factory 尚未產生可遺留或跨請求共用的 Client。
    /// </summary>
    [Fact]
    public async Task Expired_operation_deadline_releases_permit_and_does_not_leave_a_client()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new TrackingClientFactory { BlockCreation = true };
        await using var pool = CreatePool("sunnyvalechback", 91, admission, factory);
        var operation = CreateOperation() with { DeadlineUtc = DateTimeOffset.UtcNow.AddMilliseconds(25) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pool.AcquireAsync(operation, CancellationToken.None));

        Assert.Equal(1, admission.AcquireCount);
        Assert.Equal(1, admission.ReleaseCount);
        Assert.Equal(0, factory.CreatedCount);
    }

    /// <summary>
    /// 驗證已借出的 Lease 在實際 Connector 執行遇到呼叫端取消時，會將 Client 標記為不可重用，
    /// 並在 <c>await using</c> 的確定性釋放路徑中 Dispose Client 與歸還 Admission Permit。
    /// 此測試注入可取消的假 Client，以防止取消後殘留 WCF／認證工作階段被下一個 Profile 或請求重用。
    /// </summary>
    [Fact]
    public async Task Cancelled_execution_evicts_client_and_releases_permit_when_lease_is_disposed()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new TrackingClientFactory { BlockExecution = true };
        await using var pool = CreatePool("sunnyvalechback", 92, admission, factory);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var lease = await pool.AcquireAsync(CreateOperation(), CancellationToken.None);
            cancellation.Cancel();
            await lease.ExecuteAsync(CreateOperation(), cancellation.Token);
        });

        Assert.Equal(1, factory.CreatedCount);
        Assert.Equal(1, factory.DisposedCount);
        Assert.Equal(1, admission.ReleaseCount);
    }

    /// <summary>
    /// 驗證 Lease 執行時發現截止時間已過也會淘汰 Client，而不是把可能處於不明傳輸狀態的物件放回 idle pool。
    /// 假 Client 不需真的開始 I/O；關鍵斷言是 deadline gate 後的 fault 標記、Dispose 與 Permit 釋放都只發生一次。
    /// </summary>
    [Fact]
    public async Task Expired_execution_deadline_evicts_client_and_releases_permit_when_lease_is_disposed()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new TrackingClientFactory();
        await using var pool = CreatePool("sunnyvalechback", 93, admission, factory);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var lease = await pool.AcquireAsync(CreateOperation(), CancellationToken.None);
            await lease.ExecuteAsync(
                CreateOperation() with { DeadlineUtc = DateTimeOffset.UtcNow.AddMilliseconds(-1) },
                CancellationToken.None);
        });

        Assert.Equal(1, factory.CreatedCount);
        Assert.Equal(1, factory.DisposedCount);
        Assert.Equal(1, admission.ReleaseCount);
    }

    /// <summary>
    /// 啟動 Drain 後驗證新 Lease fail closed、既有 Lease 可完成，且最後一個 Lease 歸還時會 Dispose Client。
    /// 這保護 Profile replacement 不會接受舊世代的新工作或留下 idle 資源。
    /// </summary>
    [Fact]
    public async Task Drain_rejects_new_lease_and_waits_for_existing_lease()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new TrackingClientFactory();
        await using var pool = CreatePool("sunnyvalechback", 10, admission, factory);
        var lease = await pool.AcquireAsync(CreateOperation(), CancellationToken.None);

        var drain = pool.DrainAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            pool.AcquireAsync(CreateOperation(), CancellationToken.None));
        Assert.False(drain.IsCompleted);

        await lease.DisposeAsync();
        await drain;
        Assert.Equal(1, factory.DisposedCount);
    }

    /// <summary>
    /// 驗證 Router 僅接受 ResolvedProfile 指定的 Data8 ConnectorKind；Official Worker 不可自動 fallback 至 Data8。
    /// 這保護請求無法藉由路由失敗改變 Connector、Credential 或實際傳輸邊界。
    /// </summary>
    [Fact]
    public async Task Router_uses_profile_connector_kind_and_fails_closed_without_fallback()
    {
        var data8Pool = CreatePool("sunnyvalechback", 11, new TrackingAdmissionManager(), new TrackingClientFactory());
        var router = new Data8ConnectorRouter(data8Pool);

        Assert.Same(data8Pool, router.Resolve(CreateProfile(ConnectorKind.Data8, 11)));
        Assert.Throws<NotSupportedException>(() =>
            router.Resolve(CreateProfile(ConnectorKind.OfficialCrm91Worker, 11)));

        await data8Pool.DisposeAsync();
    }

    /// <summary>
    /// 驗證不同 Alias 即使注入同一 Admission Manager，也只可各自擁有與重用自身 Client，
    /// 不可因共用 Organization 容量而共用 Session、Credential 或 idle 連線。
    /// </summary>
    [Fact]
    public async Task Different_profiles_do_not_share_idle_clients_but_can_share_one_organization_admission_manager()
    {
        var sharedAdmission = new TrackingAdmissionManager();
        var firstFactory = new TrackingClientFactory();
        var secondFactory = new TrackingClientFactory();
        await using var firstPool = CreatePool("sunnyvalechback", 12, sharedAdmission, firstFactory);
        await using var secondPool = CreatePool("sunnyvalechback-report", 12, sharedAdmission, secondFactory);

        await using (var first = await firstPool.AcquireAsync(CreateOperation(), CancellationToken.None)) { }
        await using (var second = await secondPool.AcquireAsync(CreateOperation(), CancellationToken.None)) { }

        Assert.Equal(1, firstFactory.CreatedCount);
        Assert.Equal(1, secondFactory.CreatedCount);
        Assert.Equal(2, sharedAdmission.AcquireCount);
        Assert.Equal(2, sharedAdmission.ReleaseCount);
    }

    /// <summary>
    /// 驗證兩個不同 ProfileAlias 透過既有 OrganizationAdmissionRegistry 登錄同一個實體 Organization 時，
    /// 會取得同一個 Admission Manager，並共同受一份 AggregateMaxInFlight=1 預算限制。
    /// 此測試故意讓第一個 Pool 持有 Lease，再斷言第二個 Pool 被拒絕；釋放第一個 Lease 後第二個 Pool 才能取得
    /// Permit。這證明 Pool 的隔離鍵不會錯誤地把同一 Organization 的容量切成兩份。
    /// </summary>
    [Fact]
    public async Task Different_profiles_of_same_organization_share_one_actual_admission_budget()
    {
        await using var registry = new OrganizationAdmissionRegistry(
            new InMemoryRuntimeHostSlotCoordinator(),
            NullLogger<OrganizationAdmissionRegistry>.Instance,
            NullLogger<OrganizationAdmissionManager>.Instance);
        var plan = CreateSinglePermitPlan();
        await using var firstRegistration = registry.Acquire(plan);
        await using var secondRegistration = registry.Acquire(plan);
        Assert.Same(firstRegistration.Manager, secondRegistration.Manager);

        await using var firstPool = new Data8ConnectorPool(
            CreateProfile(ConnectorKind.Data8, 121, "sunnyvalechback"),
            firstRegistration.Manager,
            new TrackingClientFactory(),
            minSize: 0,
            maxSize: 2);
        await using var secondPool = new Data8ConnectorPool(
            CreateProfile(ConnectorKind.Data8, 121, "sunnyvalechback-report"),
            secondRegistration.Manager,
            new TrackingClientFactory(),
            minSize: 0,
            maxSize: 2);

        var firstLease = await firstPool.AcquireAsync(CreateOperation(), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            secondPool.AcquireAsync(CreateOperation(), CancellationToken.None));

        await firstLease.DisposeAsync();
        await using var secondLease = await secondPool.AcquireAsync(CreateOperation(), CancellationToken.None);
    }

    /// <summary>
    /// 連續 128 次借用、執行與歸還後，驗證只建立一個可重用 Client，所有 Permit 均回到基線；
    /// 最後 Drain 後 Client 也必須完全 Dispose，以偵測單調的連線或記憶體保留趨勢。
    /// </summary>
    [Fact]
    public async Task Repeated_acquire_release_reuses_bounded_client_and_returns_permits_to_baseline()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new TrackingClientFactory();
        await using var pool = CreatePool("sunnyvalechback", 13, admission, factory);

        for (var index = 0; index < 128; index++)
        {
            await using var lease = await pool.AcquireAsync(CreateOperation(), CancellationToken.None);
            await lease.ExecuteAsync(CreateOperation(), CancellationToken.None);
        }

        Assert.Equal(1, factory.CreatedCount);
        Assert.Equal(128, admission.AcquireCount);
        Assert.Equal(128, admission.ReleaseCount);
        await pool.DrainAsync();
        Assert.Equal(1, factory.DisposedCount);
    }

    /// <summary>
    /// 更換同一 Alias 的 Generation 後，驗證舊 Pool 進入 Drain、舊 Profile 不再可路由，
    /// 並在持有的 Lease 歸還後清理舊 Client；新世代是唯一可取得新 Lease 的 Pool。
    /// </summary>
    [Fact]
    public async Task Registry_replaces_active_generation_drains_old_pool_and_routes_only_current_generation()
    {
        var admission = new TrackingAdmissionManager();
        var firstFactory = new TrackingClientFactory();
        var secondFactory = new TrackingClientFactory();
        await using var registry = new Data8ConnectorPoolRegistry();
        var firstProfile = CreateProfile(ConnectorKind.Data8, 14);
        var secondProfile = CreateProfile(ConnectorKind.Data8, 15);

        var firstPool = registry.Register(firstProfile, admission, firstFactory, 0, 2);
        var heldLease = await firstPool.AcquireAsync(CreateOperation(), CancellationToken.None);
        var secondPool = registry.Register(secondProfile, admission, secondFactory, 0, 2);

        Assert.Throws<KeyNotFoundException>(() => registry.Resolve(firstProfile));
        Assert.Same(secondPool, registry.Resolve(secondProfile));
        Assert.True(firstPool.IsDraining);

        await heldLease.DisposeAsync();
        await registry.DrainCompletedGenerationsAsync();
        Assert.Equal(1, firstFactory.DisposedCount);
    }

    /// <summary>
    /// 建立小型測試 Pool。假 Admission Manager 與 Factory 只保留原子計數，不保存 Credential、Session 或請求，
    /// 使每個測試能精確觀察 P3 的借還與釋放責任。
    /// </summary>
    private static Data8ConnectorPool CreatePool(
        string profileAlias,
        long generationId,
        TrackingAdmissionManager admission,
        TrackingClientFactory factory)
        => new(
            CreateProfile(ConnectorKind.Data8, generationId, profileAlias),
            admission,
            factory,
            minSize: 0,
            maxSize: 2);

    /// <summary>
    /// 建立不含真實端點或 Credential 的不可變 ResolvedProfile，僅用於驗證 Alias／Generation 隔離。
    /// </summary>
    private static ResolvedProfile CreateProfile(ConnectorKind kind, long generationId, string alias = "sunnyvalechback")
        => new(alias, alias, Guid.NewGuid(), CeVersion.Ce91, kind, "test-reference",
            new ResolvedPoolPolicy(0, 2, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(2), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(5), 0, TimeSpan.Zero), generationId);

    /// <summary>
    /// 建立具有短而有限截止時間的已核准作業模型；不含 OrganizationId、Connector、端點或 Credential。
    /// </summary>
    private static ConnectorOperation CreateOperation()
        => new() { OperationId = "test.operation", DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(5), WorkloadSubjectId = "test-workload" };

    /// <summary>
    /// 建立只允許一個同時作業的真實 InMemory Admission Plan。測試使用假的固定 Organization GUID 與不可路由 URI，
    /// 不會連線至 D365；其用途是驗證 Registry 註冊與 Pool 對同一 Capacity Manager 的實際整合行為。
    /// </summary>
    private static OrganizationAdmissionPlan CreateSinglePermitPlan()
    {
        var options = new OrganizationAdmissionOptions
        {
            ExpectedOrganizationId = Guid.Parse("bfb92ead-3705-f011-8143-00155d006608"),
            AggregateMaxInFlight = 1,
            MaximumRuntimeHosts = 1,
            LocalQueueCapacity = 0,
            MaxDispatchEnvelopeBytes = 4096,
            QueueAdmissionTimeoutSeconds = 1,
            MaxInFlightAndQueuedPerWorkload = 2,
            AdmissionNamespaceId = "data8-pool-shared-test",
            LeaseNamespaceId = "data8-pool-shared-test",
            AdmissionEpoch = 1,
            RuntimeHostSlotLeaseTtlSeconds = 60,
            RuntimeHostSlotRenewalIntervalSeconds = 5,
            RuntimeHostSlotExpiryFenceSeconds = 5,
            MaximumOutboundWorkLifetimeSeconds = 10,
            ShutdownDrainTimeoutSeconds = 10,
            RequireDurableHostCoordinator = false
        };

        Assert.True(OrganizationAdmissionPlan.TryCreate(
            "https://example.invalid/",
            workerCount: 1,
            maxInFlightPerWorker: 1,
            options,
            out var plan,
            out _));
        return plan!;
    }

    /// <summary>
    /// 以原子計數觀察 Client 建立與 Dispose 的測試 Factory。可選擇性無限等待建立或執行，
    /// 並完全依賴傳入的取消權杖解除，藉此驗證 Pool 不會遺留工作、Timer 或 Client。
    /// </summary>
    private sealed class TrackingClientFactory : IData8ConnectorClientFactory
    {
        private int _created;
        private int _disposed;

        /// <summary>指出建立是否必須等待取消，以模擬可取消的 Factory I/O。</summary>
        public bool BlockCreation { get; init; }

        /// <summary>指出 Client 執行是否必須等待取消，以模擬不確定的傳輸狀態。</summary>
        public bool BlockExecution { get; init; }

        /// <summary>取得已建立 Client 數量；只用於測試斷言，不保存 Client 實體。</summary>
        public int CreatedCount => Volatile.Read(ref _created);

        /// <summary>取得已完成 Dispose 的 Client 數量，用以確認資源回到基線。</summary>
        public int DisposedCount => Volatile.Read(ref _disposed);

        /// <summary>建立不含真實 Credential 或網路資源的假 Client，並把唯一 Dispose 計數回呼交給 Client。</summary>
        public async Task<IConnectorClient> CreateAsync(ResolvedProfile profile, CancellationToken cancellationToken)
        {
            if (BlockCreation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            Interlocked.Increment(ref _created);
            return new TrackingClient(
                () => Interlocked.Increment(ref _disposed),
                BlockExecution);
        }
    }

    /// <summary>
    /// 模擬可取消 Connector 執行與 exactly-once Dispose 的 Client。它不保存 Profile、作業、Credential 或 Session，
    /// 因此測試只觀察 Pool 的生命週期責任，而非模擬任何真實 D365 傳輸。
    /// </summary>
    private sealed class TrackingClient : IConnectorClient
    {
        private readonly Action _dispose;
        private readonly bool _blockExecution;
        private int _disposed;

        /// <summary>建立假 Client 並接收一次性的 Dispose 通知回呼。</summary>
        public TrackingClient(Action dispose, bool blockExecution)
        {
            _dispose = dispose;
            _blockExecution = blockExecution;
        }

        /// <summary>依設定等待取消或回傳成功，讓 Lease 的取消淘汰流程可被決定性驗證。</summary>
        public async Task<ConnectorOperationResult> ExecuteAsync(
            ConnectorOperation operation,
            CancellationToken cancellationToken)
        {
            if (_blockExecution)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new ConnectorOperationResult(true);
        }

        /// <summary>只執行一次回呼，模擬真實 Client 的 deterministic cleanup。</summary>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 為單元測試提供立即成功的 Admission Manager 與 exactly-once Permit 計數。
    /// 此替身不建立 Host Slot、背景續租或佇列；需要實際共用容量時，測試會改用 OrganizationAdmissionRegistry。
    /// </summary>
    private sealed class TrackingAdmissionManager : IOrganizationAdmissionManager
    {
        /// <summary>取得 Permit 的次數。</summary>
        public int AcquireCount;

        /// <summary>Permit 釋放的次數。</summary>
        public int ReleaseCount;

        /// <summary>取得符合測試前置條件的固定 Admission Plan。</summary>
        public OrganizationAdmissionPlan Plan { get; } = CreatePlan();

        /// <summary>測試替身沒有實體 Host Slot；此方法不保留任何資源。</summary>
        public Task EnsureHostSlotAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>立即核發僅由返回 Permit 擁有的假 Permit，以驗證 Pool 的釋放順序。</summary>
        public Task<AdmissionAcquireResult> AcquireAsync(DispatchEnvelope envelope, CancellationToken cancellationToken)
        {
            AcquireCount++;
            return Task.FromResult(AdmissionAcquireResult.Success(new Permit(() => Interlocked.Increment(ref ReleaseCount))));
        }

        /// <summary>回傳無背景狀態的最小計量快照。</summary>
        public AdmissionMetricsSnapshot GetSnapshot() => new()
        {
            LocalMaxInFlight = 2, InFlight = 0, Queued = 0, LocalQueueCapacity = 2,
            AcceptedCount = AcquireCount, RejectedCount = 0, TimeoutCount = 0,
            HostSlotReady = true, HostFencingToken = 1, HostLeaseExpiresAtUtc = null,
            ActivePermits = 0, RenewalLoopActive = false
        };

        /// <summary>替身不擁有非受控資源或背景工作，因此非同步釋放為空操作。</summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>替身不擁有資源；同步釋放為空操作。</summary>
        public void Dispose() { }

        /// <summary>建立只用於假 Permit 計數的有效 Plan，不含真實端點或機密。</summary>
        private static OrganizationAdmissionPlan CreatePlan()
        {
            var options = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("bfb92ead-3705-f011-8143-00155d006608"),
                AggregateMaxInFlight = 2,
                MaximumRuntimeHosts = 1,
                LocalQueueCapacity = 2,
                MaxDispatchEnvelopeBytes = 4096,
                QueueAdmissionTimeoutSeconds = 1,
                MaxInFlightAndQueuedPerWorkload = 2,
                AdmissionNamespaceId = "data8-test",
                LeaseNamespaceId = "data8-test",
                AdmissionEpoch = 1,
                RuntimeHostSlotLeaseTtlSeconds = 60,
                RuntimeHostSlotRenewalIntervalSeconds = 5,
                RuntimeHostSlotExpiryFenceSeconds = 5,
                MaximumOutboundWorkLifetimeSeconds = 10,
                ShutdownDrainTimeoutSeconds = 10
            };
            Assert.True(OrganizationAdmissionPlan.TryCreate("https://example.invalid/", 1, 1, options, out var plan, out _));
            return plan!;
        }
    }

    /// <summary>
    /// 以 Interlocked 確保釋放回呼最多執行一次的假 Permit。它不保存作業、使用者或 Client，
    /// 用來證明 Lease 即使遇到取消與 Dispose 重入，也不會重複或遺漏容量釋放。
    /// </summary>
    private sealed class Permit : IAdmissionPermit
    {
        private readonly Action _release;
        private int _released;

        /// <summary>建立由此 Permit 唯一擁有的釋放回呼。</summary>
        public Permit(Action release) => _release = release;

        /// <summary>取得測試用相關識別碼；不含任何真實請求識別資料。</summary>
        public Guid CorrelationId { get; } = Guid.NewGuid();

        /// <summary>取得固定的測試 fencing token。</summary>
        public long HostFencingToken => 1;

        /// <summary>測試替身沒有 lease-loss 來源，因此永不取消。</summary>
        public CancellationToken LeaseLostToken => CancellationToken.None;

        /// <summary>以 exactly-once 規則釋放 Permit 計數，不建立背景資源。</summary>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) _release();
            return ValueTask.CompletedTask;
        }

        /// <summary>同步等待與 <see cref="DisposeAsync"/> 相同的唯一釋放流程。</summary>
        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();
    }
}
