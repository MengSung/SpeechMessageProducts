using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Configuration;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Data8 的同程序 operation executor 仍以 ProfileResolver、Organization Admission 與 generation-owned Pool
/// 執行，而不是讓產品 request 直接取得 client。測試替身不連線 D365；它只保護跨 Profile 隔離、permit 歸還與
/// client dispose 的生命週期契約，避免 Embedded 成為繞過 ControlPlane 的後門。
/// </summary>
public sealed class Data8ProfileOperationExecutorTests
{
    /// <summary>
    /// 保護已解析的 Data8 Profile 必須先經 Router，再由 Data8 pool 取得 admission permit 與 client，最後在
    /// await using 退出時全部歸還。故障模型是 executor 漏掉其中一層；決定性斷言是成功 WhoAmI 結果、一次 client
    /// 建立，以及 permit 在回傳後立即回到零，沒有 timer、background task 或 session retained state。
    /// </summary>
    [Fact]
    public async Task Execute_async_routes_resolved_profile_through_data8_pool_and_returns_admission_permit()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new WhoAmIFactory();
        await using var pool = new Data8ConnectorPool(
            CreateResolvedProfile(), admission, factory, minSize: 0, maxSize: 1);
        var executor = new Data8ProfileOperationExecutor(CreateResolver(), new Data8ConnectorRouter(pool));

        var result = await executor.ExecuteAsync(CreateWhoAmIRequest(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.WhoAmI);
        result.Data.WhoAmI!.OrganizationId.Should().Be(OrganizationId);
        factory.CreatedCount.Should().Be(1);
        admission.AcquireCount.Should().Be(1);
        admission.ReleaseCount.Should().Be(1);

        await pool.DrainAsync();
        factory.DisposedCount.Should().Be(1);
    }

    /// <summary>
    /// 保護未知 Profile 在 resolver fail closed 後立即停止；故障注入為格式正確但未登錄的 alias。決定性斷言是
    /// profile.not-found，且 Router、admission、client factory 均未被觸及，故不存在跨 Organization permit 或
    /// connection/session 泄漏。
    /// </summary>
    [Fact]
    public async Task Execute_async_rejects_unknown_profile_before_admission_or_client_creation()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new WhoAmIFactory();
        await using var pool = new Data8ConnectorPool(
            CreateResolvedProfile(), admission, factory, minSize: 0, maxSize: 1);
        var executor = new Data8ProfileOperationExecutor(CreateResolver(), new Data8ConnectorRouter(pool));

        var result = await executor.ExecuteAsync(
            CreateWhoAmIRequest("elijah"),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("profile.not-found");
        admission.AcquireCount.Should().Be(0);
        admission.ReleaseCount.Should().Be(0);
        factory.CreatedCount.Should().Be(0);
    }

    /// <summary>
    /// 保護產品 request 即使透過非序列化呼叫端將 Parameters 指定為 null，仍必須在取得 permit 或 client 前
    /// fail closed。故障注入為刻意破壞 required collection 的異常 request；決定性斷言是回傳
    /// operation.not-supported 且 admission、factory 計數維持零，避免 NullReferenceException 使日後
    /// 呼叫端誤以為已部分取得 Session 或連線資源。
    /// </summary>
    [Fact]
    public async Task Execute_async_rejects_null_parameters_before_admission_or_client_creation()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new WhoAmIFactory();
        await using var pool = new Data8ConnectorPool(
            CreateResolvedProfile(), admission, factory, minSize: 0, maxSize: 1);
        var executor = new Data8ProfileOperationExecutor(CreateResolver(), new Data8ConnectorRouter(pool));
        var malformedRequest = new OperationExecutionRequest
        {
            ProfileAlias = "sunnyvalechback",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "embedded-test",
            Parameters = null!
        };

        var result = await executor.ExecuteAsync(malformedRequest, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("operation.not-supported");
        admission.AcquireCount.Should().Be(0);
        admission.ReleaseCount.Should().Be(0);
        factory.CreatedCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 Data8 client 即使因部署錯置而連到另一個有效的 Organization，仍不得把對方的 WhoAmI 視為成功。
    /// 故障注入為三個 GUID 均合法、但 organizationId 與不可變 resolver snapshot 不同的回應；決定性斷言是
    /// executor fail closed、lease 將 client 標成 faulted 而非回收入 idle pool，且 permit 在同一個 await using
    /// 結束時歸還。這防止跨 Organization session／連線在下一個 Embedded request 被重用。
    /// </summary>
    [Fact]
    public async Task Execute_async_rejects_a_whoami_response_from_a_different_organization_and_evicts_its_client()
    {
        var admission = new TrackingAdmissionManager();
        var otherOrganizationId = Guid.Parse("80e1da32-96c8-4678-be37-9cf2cd0a8697");
        var factory = new WhoAmIFactory(otherOrganizationId);
        await using var pool = new Data8ConnectorPool(
            CreateResolvedProfile(), admission, factory, minSize: 0, maxSize: 1);
        var executor = new Data8ProfileOperationExecutor(CreateResolver(), new Data8ConnectorRouter(pool));

        var result = await executor.ExecuteAsync(CreateWhoAmIRequest(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("connector.invalid-response");
        admission.AcquireCount.Should().Be(1);
        admission.ReleaseCount.Should().Be(1);
        factory.CreatedCount.Should().Be(1);
        factory.DisposedCount.Should().Be(1);
    }

    private static readonly Guid OrganizationId = Guid.Parse("bfb92ead-3705-f011-8143-00155d006608");

    /// <summary>
    /// 建立與 ChurchReport mapper 輸出同形狀的 immutable resolver；URL 使用不可路由測試位址，保證測試沒有網路 I/O。
    /// </summary>
    private static ConfigurationProfileResolver CreateResolver()
    {
        var profile = new DynamicsProfileOptions
        {
            OrganizationAlias = "sunnyvalechback",
            CeVersion = CeVersion.Ce91,
            ConnectorKind = ConnectorKind.Data8,
            CredentialReference = "churchreport.crmconnection",
            Pool = new PoolPolicy { MinSize = 0, MaxSize = 1, IdleTimeoutMinutes = 1, AcquireTimeoutSeconds = 1 },
            Operation = new OperationPolicy { TimeoutSeconds = 5, MaxRetries = 0, RetryBaseDelayMs = 1 }
        };
        var organization = new OrganizationCatalogEntry
        {
            FriendlyName = "測試組織",
            UniqueName = "sunnyvalechback",
            OrganizationId = OrganizationId,
            State = OrganizationState.Enabled,
            ServiceUri = "https://example.invalid/XRMServices/2011/Organization.svc"
        };
        return new ConfigurationProfileResolver(
            new Dictionary<string, DynamicsProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["sunnyvalechback"] = profile
            },
            new Dictionary<string, OrganizationCatalogEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["sunnyvalechback"] = organization
            },
            generationId: 1);
    }

    /// <summary>
    /// 建立已被 resolver 固定到同一組織與 generation 的 Profile；factory 不可從 request 讀 endpoint 或 credential。
    /// </summary>
    private static ResolvedProfile CreateResolvedProfile()
        => new(
            "sunnyvalechback",
            "sunnyvalechback",
            OrganizationId,
            CeVersion.Ce91,
            ConnectorKind.Data8,
            "churchreport.crmconnection",
            new ResolvedPoolPolicy(0, 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(5), 0, TimeSpan.FromMilliseconds(1)),
            GenerationId: 1);

    /// <summary>
    /// 建立不含 endpoint、connector、credential 或組織識別的產品 operation request；它正是 Embedded adapter 可接受的邊界。
    /// </summary>
    private static OperationExecutionRequest CreateWhoAmIRequest(string profileAlias = "sunnyvalechback")
        => new()
        {
            ProfileAlias = profileAlias,
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "embedded-test"
        };

    /// <summary>
    /// 以真實 admission contract 模擬單一 permit，不建立 host slot、timer 或 coordinator worker，並以計數驗證
    /// lease Dispose 的唯一釋放責任。
    /// </summary>
    private sealed class TrackingAdmissionManager : IOrganizationAdmissionManager
    {
        private int _acquires;
        private int _releases;

        public int AcquireCount => Volatile.Read(ref _acquires);

        public int ReleaseCount => Volatile.Read(ref _releases);

        public OrganizationAdmissionPlan Plan { get; } = CreatePlan();

        public Task EnsureHostSlotAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AdmissionAcquireResult> AcquireAsync(DispatchEnvelope envelope, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _acquires);
            return Task.FromResult(AdmissionAcquireResult.Success(
                new TrackingPermit(() => Interlocked.Increment(ref _releases))));
        }

        public AdmissionMetricsSnapshot GetSnapshot() => new()
        {
            LocalMaxInFlight = 1,
            InFlight = 0,
            Queued = 0,
            LocalQueueCapacity = 0,
            AcceptedCount = AcquireCount,
            RejectedCount = 0,
            TimeoutCount = 0,
            HostSlotReady = true,
            HostFencingToken = 1,
            HostLeaseExpiresAtUtc = null,
            ActivePermits = AcquireCount - ReleaseCount,
            RenewalLoopActive = false
        };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose() { }

        private static OrganizationAdmissionPlan CreatePlan()
        {
            var options = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = OrganizationId,
                AggregateMaxInFlight = 1,
                MaximumRuntimeHosts = 1,
                LocalQueueCapacity = 0,
                MaxDispatchEnvelopeBytes = 4096,
                QueueAdmissionTimeoutSeconds = 1,
                MaxInFlightAndQueuedPerWorkload = 1,
                AdmissionNamespaceId = "data8-profile-executor-test",
                LeaseNamespaceId = "data8-profile-executor-test",
                AdmissionEpoch = 1,
                RuntimeHostSlotLeaseTtlSeconds = 60,
                RuntimeHostSlotRenewalIntervalSeconds = 5,
                RuntimeHostSlotExpiryFenceSeconds = 5,
                MaximumOutboundWorkLifetimeSeconds = 5,
                ShutdownDrainTimeoutSeconds = 5
            };
            OrganizationAdmissionPlan.TryCreate(
                "https://example.invalid/",
                workerCount: 1,
                maxInFlightPerWorker: 1,
                options,
                out var plan,
                out _).Should().BeTrue();
            return plan!;
        }
    }

    /// <summary>
    /// 以 exactly-once callback 記錄 permit 釋放；沒有 CancellationTokenSource、handle 或背景工作可供測試遺留。
    /// </summary>
    private sealed class TrackingPermit : IAdmissionPermit
    {
        private readonly Action _onDispose;
        private int _disposed;

        public TrackingPermit(Action onDispose) => _onDispose = onDispose;

        public Guid CorrelationId { get; } = Guid.NewGuid();

        public long HostFencingToken => 1;

        public CancellationToken LeaseLostToken => CancellationToken.None;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _onDispose();
            }

            return ValueTask.CompletedTask;
        }

        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Factory 僅建立可釋放的測試 client，並統計 ownership；它不保存 profile、credential、session 或已完成 operation。
    /// </summary>
    private sealed class WhoAmIFactory : IData8ConnectorClientFactory
    {
        private readonly Guid _organizationId;
        private int _created;
        private int _disposed;

        /// <summary>
        /// 建立可指定回應 Organization 的 factory。預設為 resolver 預期組織；測試指定其他 GUID 時只模擬
        /// deployment mismatch，不建立網路、WCF channel、credential、token 或跨測試共享狀態。
        /// </summary>
        /// <param name="organizationId">測試 client 回傳的非秘密 WhoAmI Organization GUID。</param>
        public WhoAmIFactory(Guid? organizationId = null) => _organizationId = organizationId ?? OrganizationId;

        public int CreatedCount => Volatile.Read(ref _created);

        public int DisposedCount => Volatile.Read(ref _disposed);

        public Task<IConnectorClient> CreateAsync(ResolvedProfile profile, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _created);
            return Task.FromResult<IConnectorClient>(new WhoAmIClient(
                _organizationId,
                () => Interlocked.Increment(ref _disposed)));
        }
    }

    /// <summary>
    /// 回傳固定但非秘密的 WhoAmI scalar 值；Dispose 以 Interlocked 保證 client 不會被 pool 釋放兩次。
    /// </summary>
    private sealed class WhoAmIClient : IConnectorClient
    {
        private readonly Guid _organizationId;
        private readonly Action _onDispose;
        private int _disposed;

        /// <summary>
        /// 建立只回傳固定純值的測試 client。Organization GUID 只存活在這個 lease-owned client，Dispose 後沒有
        /// 靜態集合、timer、subscription 或 Session 可以保留它。
        /// </summary>
        /// <param name="organizationId">要投影到 WhoAmI 回應的測試 Organization GUID。</param>
        /// <param name="onDispose">只計數一次 client release 的測試 callback。</param>
        public WhoAmIClient(Guid organizationId, Action onDispose)
        {
            _organizationId = organizationId;
            _onDispose = onDispose;
        }

        public Task<ConnectorOperationResult> ExecuteAsync(ConnectorOperation operation, CancellationToken cancellationToken)
            => Task.FromResult(new ConnectorOperationResult(true)
            {
                Values = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["userId"] = "11111111-1111-1111-1111-111111111111",
                    ["businessUnitId"] = "22222222-2222-2222-2222-222222222222",
                    ["organizationId"] = _organizationId.ToString("D")
                }
            });

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _onDispose();
            }

            return ValueTask.CompletedTask;
        }
    }
}
