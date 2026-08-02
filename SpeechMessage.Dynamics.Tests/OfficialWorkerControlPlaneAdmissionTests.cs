using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.DependencyInjection;
using SpeechMessage.Dynamics.ControlPlane.Runtime;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證官方 NuGet Worker 路徑的中立 ControlPlane 定義、容量、派送與確定性生命週期。
/// 測試只使用 SDK-free test worker，不載入 CRM SDK、Credential、Token、Session 或產品要求狀態；
/// 每個案例都明確檢查 immutable generation identity、單 Worker 單一在途限制與 drain 後資源歸零。
/// </summary>
public sealed class OfficialWorkerControlPlaneAdmissionTests
{
    /// <summary>
    /// 驗證容量計畫以 Organization root 建立 canonical identity，且 WorkerCount 只能在
    /// 每個 Worker 一個在途作業的前提下占用既有 host-local admission 預算。
    /// </summary>
    [Fact]
    public void Admission_plan_uses_the_organization_root_and_bounded_worker_concurrency()
    {
        var admission = CreateAdmissionOptions();

        var created = OrganizationAdmissionPlan.TryCreate(
            "https://crm.example.local/Church/",
            workerCount: 2,
            maxInFlightPerWorker: 1,
            admission,
            out var plan,
            out var error);

        created.Should().BeTrue(error?.ErrorMessage);
        plan.Should().NotBeNull();
        plan!.CanonicalKey.NormalizedOrganizationBaseUri
            .Should().Be("https://crm.example.local/Church/");
        plan.MaximumWorkerInFlightPerHost.Should().Be(2);
        plan.MaximumWorkerInFlightPerHost.Should().BeLessThanOrEqualTo(plan.LocalMaxInFlight);
    }

    /// <summary>
    /// 驗證 Profile Definition 在建構當下即凍結所有非秘密部署欄位；呼叫端後續修改
    /// mutable admission options 不得改變既有 generation 的 Organization、容量或 Worker bootstrap identity。
    /// </summary>
    [Fact]
    public void Profile_definition_is_immutable_and_preserves_the_worker_xml_generation_identity()
    {
        var admission = CreateAdmissionOptions();
        var originalOrganizationId = admission.ExpectedOrganizationId;
        var definition = CreateDefinition(
            admission,
            workerProfileGenerationId: "profile-generation-0001",
            workerCount: 2);

        admission.ExpectedOrganizationId = Guid.NewGuid();
        admission.AggregateMaxInFlight = 100;
        admission.AdmissionNamespaceId = "mutated-after-construction";

        definition.ProfileAlias.Should().Be("crm91-test");
        definition.WorkerProfileGenerationId.Should().Be("profile-generation-0001");
        definition.WorkerVersion.Should().Be(OfficialWorkerVersion.Ce91);
        definition.CeVersion.Should().Be("9.1");
        definition.OrganizationBaseUri.Should().Be("https://crm.example.local/Church/");
        definition.ExpectedOrganizationId.Should().Be(originalOrganizationId);
        definition.WorkerCount.Should().Be(2);
        definition.MaxInFlightPerWorker.Should().Be(1);
        definition.AdmissionPlan.AggregateMaxInFlight.Should().Be(8);
        definition.AdmissionPlan.AdmissionKey.AdmissionNamespaceId
            .Should().Be("official-workers-test");

        var workerOptions = definition.CreateWorkerOptions();
        workerOptions.ProfileGenerationId.Should().Be("profile-generation-0001",
            because: "worker-profile.xml uses an exact deployment-owned generationId unrelated to the manager's numeric runtime generation");
    }

    /// <summary>
    /// 驗證未具精確壓力與 soak 證據前，任何大於一的 per-worker concurrency 都在
    /// Process、Pipe、Admission Registration 或背景工作建立前 fail closed。
    /// </summary>
    [Fact]
    public void Profile_definition_rejects_more_than_one_in_flight_operation_per_worker()
    {
        var action = () => CreateDefinition(
            CreateAdmissionOptions(),
            maxInFlightPerWorker: 2);

        action.Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithMessage("*exactly 1*");
    }

    /// <summary>
    /// 驗證官方 Worker DI 擴充只註冊中立 ControlPlane seam；它不依賴 IConfiguration，
    /// 也不建立 Web API client、transport 或 token provider 的跨 generation singleton。
    /// </summary>
    [Fact]
    public void Official_worker_dependency_injection_registers_only_the_neutral_runtime_graph()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSpeechMessageDynamicsOfficialWorkers(
            [CreateDefinition(CreateAdmissionOptions())]);

        services.Should().NotContain(descriptor =>
            descriptor.ServiceType.FullName != null &&
            descriptor.ServiceType.FullName.Contains("WebApi", StringComparison.OrdinalIgnoreCase));

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        var manager = provider.GetRequiredService<IDynamicsProfileRuntimeManager>();

        provider.GetRequiredService<IProfileExecutionLeaseProvider>().Should().BeSameAs(manager);
        provider.GetRequiredService<IDynamicsOperationExecutor>()
            .Should().BeSameAs(provider.GetRequiredService<ProfileRoutedOperationExecutor>());
        provider.GetRequiredService<IDynamicsProfileRuntimeFactory>()
            .Should().BeOfType<DynamicsProfileRuntimeFactory>();
    }

    /// <summary>
    /// 以真實 SDK-free 子程序驗證完整資料流：受控要求先取得 Organization admission permit，
    /// 再取得當下 active runtime lease，最後透過 IDynamicsOperationExecutor 派送到官方 Worker supervisor。
    /// 完成後 runtime active count 與 admission permit 都必須歸零，且不得保留 caller Session 或 Credential。
    /// </summary>
    [Fact]
    public async Task Runtime_manager_dispatches_through_the_official_worker_executor_seam()
    {
        await using var registry = CreateAdmissionRegistry();
        var definition = CreateDefinition(
            CreateAdmissionOptions(
                Guid.Parse("33333333-3333-3333-3333-333333333333")),
            executablePath: FindTestWorkerExecutable(),
            workerExecutableSha256: ComputeSha256(FindTestWorkerExecutable()),
            workerCount: 2,
            warmUpOnActivation: false);
        var factory = new DynamicsProfileRuntimeFactory(registry);
        await using var manager = new DynamicsProfileRuntimeManager([definition], factory);

        await manager.InitializeAsync();
        var result = await manager.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = definition.ProfileAlias,
                CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                WorkloadSubjectId = "official-control-plane-dispatch-test"
            });

        result.Succeeded.Should().BeTrue();
        result.Data!.WhoAmI!.OrganizationId.Should().Be(definition.ExpectedOrganizationId);
        var snapshot = manager.GetSnapshot();
        snapshot.IsReady.Should().BeTrue();
        snapshot.Profiles.Should().ContainSingle();
        snapshot.Profiles[0].ActiveExecutionCount.Should().Be(0);
        snapshot.Profiles[0].Admission.ActivePermits.Should().Be(0);
    }

    /// <summary>
    /// 故障注入 caller cancellation 中止第一次 drain；Runtime 必須保持 Draining 並繼續擁有
    /// Worker、Retirement CTS 與 Admission Registration。最後一個 lease 歸還後，第二次 drain 必須建立
    /// 新嘗試並確定性關閉所有 Process／Pipe／背景輸出工作，證明取消不會永久快取 faulted cleanup task。
    /// </summary>
    [Fact]
    public async Task Cancelled_drain_is_retryable_and_releases_workers_and_admission_registration()
    {
        await using var registry = CreateAdmissionRegistry();
        var executablePath = FindTestWorkerExecutable();
        var definition = CreateDefinition(
            CreateAdmissionOptions(),
            executablePath: executablePath,
            workerExecutableSha256: ComputeSha256(executablePath),
            workerCount: 2,
            warmUpOnActivation: false,
            drainTimeout: TimeSpan.FromSeconds(5));
        var factory = new DynamicsProfileRuntimeFactory(registry);
        var runtime = (DynamicsProfileRuntime)await factory.CreateAsync(
            definition,
            generation: 1,
            CancellationToken.None);

        runtime.TryAcquireExecution(out var lease).Should().BeTrue();
        lease.Should().NotBeNull();
        runtime.BeginDrain();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        Func<Task> firstDrain = () => runtime.DrainAndDisposeAsync(cancellation.Token);
        await firstDrain.Should().ThrowAsync<OperationCanceledException>();
        runtime.State.Should().Be(DynamicsProfileRuntimeState.Draining);
        runtime.ActiveExecutionCount.Should().Be(1);
        registry.EntryCount.Should().Be(1);

        await lease!.DisposeAsync();
        await runtime.DrainAndDisposeAsync();

        runtime.State.Should().Be(DynamicsProfileRuntimeState.Disposed);
        runtime.ActiveExecutionCount.Should().Be(0);
        registry.EntryCount.Should().Be(0);
        runtime.GetWorkerLifecycleSnapshots().Should().OnlyContain(snapshot =>
            !snapshot.IsReady &&
            snapshot.OwnedProcessCount == 0 &&
            snapshot.OwnedPipeCount == 0 &&
            snapshot.OwnedBackgroundTaskCount == 0 &&
            snapshot.ActiveOperationCount == 0);
    }

    /// <summary>
    /// 驗證 Organization root 僅接受明確 HTTPS 組織基底；Direct Web API、Organization Service、
    /// user-info、query 與 fragment 皆不可進入中立容量或 Worker runtime key。
    /// </summary>
    [Theory]
    [InlineData("http://crm.example.local/Church/")]
    [InlineData("https://user:password@crm.example.local/Church/")]
    [InlineData("https://crm.example.local/Church/?query=forbidden")]
    [InlineData("https://crm.example.local/Church/#fragment")]
    [InlineData("https://crm.example.local/Church/api/data/v9.1/")]
    [InlineData("https://crm.example.local/Church/XRMServices/2011/Organization.svc")]
    public void Admission_plan_rejects_non_organization_or_direct_transport_roots(string root)
    {
        var created = OrganizationAdmissionPlan.TryCreate(
            root,
            workerCount: 1,
            maxInFlightPerWorker: 1,
            CreateAdmissionOptions(),
            out var plan,
            out var error);

        created.Should().BeFalse();
        plan.Should().BeNull();
        error.Should().NotBeNull();
    }

    /// <summary>
    /// 建立不含秘密的測試 Profile Definition。未執行 Worker 的案例使用不存在但完整限定的路徑；
    /// 真實 lifecycle 案例則明確傳入 SDK-free test worker 與其當下 SHA-256。
    /// </summary>
    private static DynamicsProfileDefinition CreateDefinition(
        OrganizationAdmissionOptions admissionOptions,
        string workerProfileGenerationId = "profile-generation-0001",
        string? executablePath = null,
        string? workerExecutableSha256 = null,
        int workerCount = 1,
        int maxInFlightPerWorker = 1,
        bool warmUpOnActivation = false,
        TimeSpan? drainTimeout = null)
        => new(
            profileAlias: "crm91-test",
            workerProfileGenerationId,
            OfficialWorkerVersion.Ce91,
            organizationBaseUri: "https://crm.example.local/Church/",
            workerExecutablePath: executablePath ?? Path.Combine(Path.GetTempPath(), "official-worker-test.exe"),
            workerExecutableSha256: workerExecutableSha256 ?? new string('a', 64),
            packageLockId: "test-worker-package-lock-0001",
            admissionOptions,
            workerCount,
            maxInFlightPerWorker,
            warmUpOnActivation,
            startupTimeout: TimeSpan.FromSeconds(10),
            operationTimeout: TimeSpan.FromSeconds(5),
            drainTimeout);

    /// <summary>
    /// 建立互不共用的 in-memory admission registry；每個測試各自擁有 coordinator、manager 與 registration，
    /// Dispose 後不得把 Host Slot、Queue、Permit 或 runtime reference 留給下一個測試。
    /// </summary>
    private static OrganizationAdmissionRegistry CreateAdmissionRegistry()
        => new(
            new InMemoryRuntimeHostSlotCoordinator(),
            NullLogger<OrganizationAdmissionRegistry>.Instance,
            NullLogger<OrganizationAdmissionManager>.Instance);

    /// <summary>
    /// 建立符合單機測試與兩個 Worker 的 bounded admission 選項；方法回傳的新物件只供單一
    /// Definition 建構使用，測試可在建構後修改它以驗證 immutable snapshot 邊界。
    /// </summary>
    private static OrganizationAdmissionOptions CreateAdmissionOptions(Guid? organizationId = null) => new()
    {
        ExpectedOrganizationId = organizationId ??
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
        AggregateMaxInFlight = 8,
        MaximumRuntimeHosts = 2,
        LocalQueueCapacity = 8,
        MaxDispatchEnvelopeBytes = 65_536,
        QueueAdmissionTimeoutSeconds = 5,
        MaxInFlightAndQueuedPerWorkload = 4,
        AdmissionNamespaceId = "official-workers-test",
        LeaseNamespaceId = "official-workers-test",
        AdmissionEpoch = 1,
        RuntimeHostSlotLeaseTtlSeconds = 120,
        RuntimeHostSlotRenewalIntervalSeconds = 15,
        RuntimeHostSlotExpiryFenceSeconds = 10,
        MaximumOutboundWorkLifetimeSeconds = 30,
        ShutdownDrainTimeoutSeconds = 30,
        RequireDurableHostCoordinator = false
    };

    /// <summary>取得由 test project reference 預先建置的 SDK-free WorkerTestHost。</summary>
    private static string FindTestWorkerExecutable()
    {
        var root = FindRepositoryRoot();
        var executablePath = Path.Combine(
            root,
            "SpeechMessage.Dynamics.WorkerTestHost",
            "bin",
            "Debug",
            "net10.0",
            "SpeechMessage.Dynamics.WorkerTestHost.exe");
        File.Exists(executablePath).Should().BeTrue(
            because: "the test project reference must build the SDK-free worker test host");
        return executablePath;
    }

    /// <summary>計算 test-owned executable 的當下 SHA-256，不保存 Stream、Handle 或 secret。</summary>
    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>從測試輸出目錄向上尋找 repository root，不使用環境變數或 caller-controlled path。</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
