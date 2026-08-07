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
    /// operation.invalid-parameters 且 admission、factory 計數維持零，避免 NullReferenceException 使日後
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
        result.ErrorCode.Should().Be("operation.invalid-parameters");
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

    /// <summary>
    /// 保護 P7.1 的既有 Package01 fee capability 在解析到 Data8 Profile 後，必須以 server-owned 的最小型別
    /// 參數通過 Pool/Lease，再只回傳封閉 fee branch。故障模型是 executor 仍沿用 WhoAmI-only allowlist，或把
    /// legacy contactName、原始 request dictionary、CRM endpoint/credential 帶入 connector；決定性斷言是成功
    /// 結果、只含 contactId 的獨立 scalar copy、permit exactly-once release 與 drain 後 client dispose。
    /// 此測試不連線 D365，所有 mutable request 與替身只活在單一測試 scope，避免測試本身形成跨 Profile state。
    /// </summary>
    [Fact]
    public async Task Execute_async_projects_registered_fee_read_through_a_lease_with_only_server_owned_parameters()
    {
        var contactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var feeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var admission = new TrackingAdmissionManager();
        var factory = new FixedResultFactory(operation => new ConnectorOperationResult(true)
        {
            Data = OperationResponseData.ForPackage01FeeRecords(
                operation.OperationId,
                "9.1",
                [new Package01FeeRecord { FeeId = feeId, Amount = 123.45m, Name = "測試奉獻" }])
        })
        {
            ExpectedOperationId = OperationIds.FeeDedicationRetrieveByContact
        };
        await using var pool = new Data8ConnectorPool(
            CreateResolvedProfile(), admission, factory, minSize: 0, maxSize: 1);
        var executor = new Data8ProfileOperationExecutor(CreateResolver(), new Data8ConnectorRouter(pool));

        var result = await executor.ExecuteAsync(
            CreatePackage01Request(
                OperationIds.FeeDedicationRetrieveByContact,
                new Dictionary<string, object?>
                {
                    ["contactId"] = contactId,
                    ["contactName"] = "只供舊版顯示相容，絕不可成為 CRM 查詢權威"
                }),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.Package01FeeRecords);
        result.Data.FeeRecords.Should().ContainSingle().Which.FeeId.Should().Be(feeId);
        factory.LastOperation!.Parameters.Should().ContainSingle();
        factory.LastOperation.Parameters["contactId"].Should().Be(contactId);
        admission.AcquireCount.Should().Be(1);
        admission.ReleaseCount.Should().Be(1);

        await pool.DrainAsync();
        factory.DisposedCount.Should().Be(1);
    }

    /// <summary>
    /// 保護 P7.2 contact basic-info capability 只有在 CE 9.1 Data8 profile 下，才會以固定三個 scalar 取得一次
    /// connector lease；故障注入是尚未加入 executor allowlist 的 operation，決定性斷言是 typed response、copied
    /// parameters 與 permit exactly-once release。測試 factory 不建立 CRM、WCF、credential、token、session 或
    /// background resource，client 在 pool drain 時必須確定釋放。
    /// </summary>
    [Fact]
    public async Task Execute_async_routes_contact_basic_info_update_through_a_ce91_data8_lease()
    {
        var contactId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var admission = new TrackingAdmissionManager();
        var factory = new FixedResultFactory(operation => new ConnectorOperationResult(true)
        {
            Data = OperationResponseData.ForContactBasicInfoUpdate(
                operation.OperationId,
                "9.1",
                ContactBasicInfoUpdateDisposition.Changed,
                ContactBasicInfoUpdateCorrelationCategory.ReadBackConfirmed)
        })
        {
            ExpectedOperationId = OperationIds.MemberInfoContactUpdateBasicInfo
        };
        await using var pool = new Data8ConnectorPool(
            CreateResolvedProfile(), admission, factory, minSize: 0, maxSize: 1);
        var executor = new Data8ProfileOperationExecutor(CreateResolver(), new Data8ConnectorRouter(pool));

        var result = await executor.ExecuteAsync(
            CreatePackage01Request(
                OperationIds.MemberInfoContactUpdateBasicInfo,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["contactId"] = contactId,
                    ["phone"] = "0900-000-001",
                    ["address"] = "P7.2 fixture address"
                }),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.ContactBasicInfoUpdate);
        result.Data.ContactBasicInfoUpdate!.Disposition.Should().Be(ContactBasicInfoUpdateDisposition.Changed);
        factory.LastOperation!.Parameters.Should().BeEquivalentTo(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = contactId,
                ["phone"] = "0900-000-001",
                ["address"] = "P7.2 fixture address"
            });
        admission.AcquireCount.Should().Be(1);
        admission.ReleaseCount.Should().Be(1);

        await pool.DrainAsync();
        factory.DisposedCount.Should().Be(1);
    }

    /// <summary>
    /// 保護空白字串沿用 legacy 的「不覆寫」語意：executor 應在取得 connector lease 前直接產生
    /// NoChange/NoDispatch。故障注入是目前 generic string normalizer 對空白值的拒絕；決定性斷言是 success
    /// envelope 與 admission/factory 均為零，證明 no-change 路徑沒有建立 client、session、timer 或 outbound CRM。
    /// </summary>
    [Fact]
    public async Task Execute_async_returns_contact_basic_info_no_change_before_admission_when_only_blank_values_are_supplied()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new FixedResultFactory(_ => throw new InvalidOperationException("no-change must not create a client."));
        await using var pool = new Data8ConnectorPool(
            CreateResolvedProfile(), admission, factory, minSize: 0, maxSize: 1);
        var executor = new Data8ProfileOperationExecutor(CreateResolver(), new Data8ConnectorRouter(pool));

        var result = await executor.ExecuteAsync(
            CreatePackage01Request(
                OperationIds.MemberInfoContactUpdateBasicInfo,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["contactId"] = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
                    ["phone"] = "   ",
                    ["address"] = ""
                }),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Data!.ContactBasicInfoUpdate!.Disposition.Should().Be(ContactBasicInfoUpdateDisposition.NoChange);
        result.Data.ContactBasicInfoUpdate.CorrelationCategory
            .Should().Be(ContactBasicInfoUpdateCorrelationCategory.NoDispatch);
        admission.AcquireCount.Should().Be(0);
        admission.ReleaseCount.Should().Be(0);
        factory.CreatedCount.Should().Be(0);
    }

    /// <summary>
    /// 保護同一 capability 不會因為將 CE 版本切到 8.2 就偷偷 fallback 或取得 Data8 lease；故障注入是
    /// CE 8.2 resolved profile，決定性斷言是 operation.not-supported 且 admission、factory 都為零。Official
    /// Worker 與 CE 8.2 live evidence 不在此 Data8-first slice 內，也不能由 connector 自動切換。
    /// </summary>
    [Fact]
    public async Task Execute_async_rejects_contact_basic_info_update_for_ce82_before_admission()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new FixedResultFactory(_ => throw new InvalidOperationException("CE 8.2 must fail closed."));
        await using var pool = new Data8ConnectorPool(
            CreateResolvedProfile() with { CeVersion = CeVersion.Ce82 },
            admission,
            factory,
            minSize: 0,
            maxSize: 1);
        var executor = new Data8ProfileOperationExecutor(
            CreateResolver(CeVersion.Ce82),
            new Data8ConnectorRouter(pool));

        var result = await executor.ExecuteAsync(
            CreatePackage01Request(
                OperationIds.MemberInfoContactUpdateBasicInfo,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["contactId"] = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
                    ["phone"] = "0900-000-001"
                }),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("operation.not-supported");
        admission.AcquireCount.Should().Be(0);
        admission.ReleaseCount.Should().Be(0);
        factory.CreatedCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 Package01 型別化參數若違反 registry 的 Guid contract，必須在取得 admission permit 或建立 Data8 client
    /// 前 fail closed。故障注入是將 contactId 改成不可解析字串；決定性斷言是專屬 invalid-parameters 分類與
    /// 所有資源計數為零，避免無效或 caller-controlled 值進入 Pool、WCF session 或後續 request reuse。
    /// </summary>
    [Fact]
    public async Task Execute_async_rejects_invalid_package01_parameters_before_admission_or_client_creation()
    {
        var admission = new TrackingAdmissionManager();
        var factory = new FixedResultFactory(_ => new ConnectorOperationResult(true)
        {
            Data = OperationResponseData.ForPackage01FeeRecords(
                OperationIds.FeeDedicationRetrieveByContact,
                "9.1",
                Array.Empty<Package01FeeRecord>())
        });
        await using var pool = new Data8ConnectorPool(
            CreateResolvedProfile(), admission, factory, minSize: 0, maxSize: 1);
        var executor = new Data8ProfileOperationExecutor(CreateResolver(), new Data8ConnectorRouter(pool));

        var result = await executor.ExecuteAsync(
            CreatePackage01Request(
                OperationIds.FeeDedicationRetrieveByContact,
                new Dictionary<string, object?> { ["contactId"] = "not-a-guid" }),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("operation.invalid-parameters");
        admission.AcquireCount.Should().Be(0);
        admission.ReleaseCount.Should().Be(0);
        factory.CreatedCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 connector 即使宣告成功，也不能把另一個 capability 的合法封閉 branch 重標成目前 fee read 成功。
    /// 故障注入是回傳 stor-lesson branch；決定性斷言是 executor 在 lease scope 內標記 faulted、回傳固定的
    /// invalid-response 分類，並在退出時淘汰 client 與歸還 permit，避免錯誤資料或未知 session 重用。
    /// </summary>
    [Fact]
    public async Task Execute_async_evicts_client_when_package01_response_does_not_match_the_requested_operation()
    {
        var contactId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var admission = new TrackingAdmissionManager();
        var factory = new FixedResultFactory(operation => new ConnectorOperationResult(true)
        {
            Data = OperationResponseData.ForPackage01StorLessonRecords(
                operation.OperationId,
                "9.1",
                Array.Empty<Package01StorLessonRecord>())
        });
        await using var pool = new Data8ConnectorPool(
            CreateResolvedProfile(), admission, factory, minSize: 0, maxSize: 1);
        var executor = new Data8ProfileOperationExecutor(CreateResolver(), new Data8ConnectorRouter(pool));

        var result = await executor.ExecuteAsync(
            CreatePackage01Request(
                OperationIds.FeeDedicationRetrieveByContact,
                new Dictionary<string, object?> { ["contactId"] = contactId }),
            CancellationToken.None);

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
    private static ConfigurationProfileResolver CreateResolver(CeVersion ceVersion = CeVersion.Ce91)
    {
        var profile = new DynamicsProfileOptions
        {
            OrganizationAlias = "sunnyvalechback",
            CeVersion = ceVersion,
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
    /// 建立只含 Package01 registry operation 與 caller-owned scalar 的測試 request。真正 executor 必須在首次
    /// 非同步等待前驗證、正規化並複製必要值；此 helper 不提供 endpoint、credential、connector 或 Organization
    /// 選擇，以維持產品邊界與 Embedded/Dedicated route 的相同信任模型。
    /// </summary>
    /// <param name="operationId">已登錄的 Package01 capability operation ID。</param>
    /// <param name="parameters">故意以可變字典模擬產品/Gateway 輸入，供 executor 驗證其不會跨 await 保留。</param>
    /// <returns>只在目前測試呼叫生命週期內使用的受控 operation request。</returns>
    private static OperationExecutionRequest CreatePackage01Request(
        string operationId,
        IReadOnlyDictionary<string, object?> parameters)
        => new()
        {
            ProfileAlias = "sunnyvalechback",
            CapabilityOperationId = operationId,
            WorkloadSubjectId = "embedded-test",
            Parameters = parameters
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
    /// 建立可回傳預先封閉資料的離線 connector factory。它只在單一測試 scope 保存 callback、計數與 defensive
    /// copied operation snapshot，用來確認 executor 是否正確移除 legacy-only 參數；它不保存真實 request、
    /// credential、token、session、stream 或任何跨測試資源，client 的唯一 Dispose owner 仍是 Pool/Lease。
    /// </summary>
    private sealed class FixedResultFactory : IData8ConnectorClientFactory
    {
        private readonly Func<ConnectorOperation, ConnectorOperationResult> _createResult;
        private int _created;
        private int _disposed;

        /// <summary>
        /// 建立固定結果 factory。callback 僅接收 executor 已建立的 SDK-free operation，不能接觸 Profile 的端點、
        /// credential 或底層 client；它讓測試能把 response branch mismatch 當成可重現故障注入。
        /// </summary>
        /// <param name="createResult">依目前 connector operation 建立單一封閉 result 的測試 callback。</param>
        public FixedResultFactory(Func<ConnectorOperation, ConnectorOperationResult> createResult)
            => _createResult = createResult ?? throw new ArgumentNullException(nameof(createResult));

        /// <summary>取得測試期間實際建立的 lease-owned client 次數，僅供精確 ownership assertion。</summary>
        public int CreatedCount => Volatile.Read(ref _created);

        /// <summary>取得 Pool fault/drain 後實際呼叫 client Dispose 的次數，必須與建立次數一致。</summary>
        public int DisposedCount => Volatile.Read(ref _disposed);

        /// <summary>
        /// 取得 defensive copied 的最後 operation snapshot。此值只由目前測試保存，複製參數字典後不會保留
        /// executor caller dictionary；production factory 絕不可模仿此診斷用途的測試 state。
        /// </summary>
        public ConnectorOperation? LastOperation { get; private set; }

        /// <summary>
        /// 設定測試預期 operation ID。若 executor 對固定 factory 傳入其他能力即立即失敗，防止測試意外把
        /// generic connector routing 當作成功；null 表示本測試只關心 response projection。
        /// </summary>
        public string? ExpectedOperationId { get; init; }

        /// <summary>
        /// 建立一個由 Pool/Lease 唯一擁有的離線 client。profile 不被保存，取消在建立前檢查；每個 client 只
        /// 持有 callback 到 Dispose，Dispose 後不留下可被下一個 test/request 重用的 connector state。
        /// </summary>
        public Task<IConnectorClient> CreateAsync(ResolvedProfile profile, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(profile);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _created);
            return Task.FromResult<IConnectorClient>(new FixedResultClient(
                operation =>
                {
                    if (ExpectedOperationId is not null)
                    {
                        operation.OperationId.Should().Be(ExpectedOperationId);
                    }

                    LastOperation = operation with
                    {
                        Parameters = new Dictionary<string, object?>(operation.Parameters, StringComparer.Ordinal)
                    };
                    return _createResult(operation);
                },
                () => Interlocked.Increment(ref _disposed)));
        }
    }

    /// <summary>
    /// 只在測試中同步回傳 factory 指定結果的 lease-owned client。它不建立網路、WCF、timer 或背景工作；
    /// Interlocked Dispose callback 證明 executor 的成功與 faulted 路徑都由 Pool/Lease 一次性收回資源。
    /// </summary>
    private sealed class FixedResultClient : IConnectorClient
    {
        private readonly Func<ConnectorOperation, ConnectorOperationResult> _execute;
        private readonly Action _onDispose;
        private int _disposed;

        /// <summary>
        /// 建立固定結果 client。兩個 delegate 僅屬於此 client，Pool dispose 後不再可達，不會保存 Profile、
        /// Organization、credential 或先前使用者輸入。
        /// </summary>
        /// <param name="execute">建立一次封閉 operation result 的同步 callback。</param>
        /// <param name="onDispose">記錄唯一資源釋放的測試 callback。</param>
        public FixedResultClient(
            Func<ConnectorOperation, ConnectorOperationResult> execute,
            Action onDispose)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
        }

        /// <summary>
        /// 執行 single-use test callback。取消會在 callback 前 fail closed；若已 Dispose 則拒絕，確保測試不會
        /// 掩蓋 lease 對已淘汰 client 的錯誤重用。此方法沒有 await，因此不會建立未受控 continuation。
        /// </summary>
        public Task<ConnectorOperationResult> ExecuteAsync(
            ConnectorOperation operation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(FixedResultClient));
            }

            return Task.FromResult(_execute(operation));
        }

        /// <summary>
        /// 以 exactly-once 方式完成 client 釋放計數。callback 不拋出且沒有後續資源，因此 Data8 pool 可在
        /// fault、cancel、drain 或正常回收後安全回到測試所宣告的 baseline。
        /// </summary>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _onDispose();
            }

            return ValueTask.CompletedTask;
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
