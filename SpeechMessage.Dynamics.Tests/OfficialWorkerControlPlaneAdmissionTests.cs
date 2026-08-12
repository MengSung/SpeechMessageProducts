using System.Diagnostics;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.DependencyInjection;
using SpeechMessage.Dynamics.ControlPlane.Runtime;
using SpeechMessage.Dynamics.WorkerSupervisor;
using SpeechMessage.Testing;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證官方 NuGet Worker 路徑的中立 ControlPlane 定義、容量、派送與確定性生命週期。
/// 測試只使用 SDK-free test worker，不載入 CRM SDK、Credential、Token、Session 或產品要求狀態；
/// 每個案例都明確檢查 immutable generation identity、單 Worker 單一在途限制與 drain 後資源歸零。
/// </summary>
[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
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
    /// Factory creation copies the definition-owned recycle thresholds into each independently owned
    /// worker options snapshot; the definition or mutable caller state is never retained by the executor.
    /// </summary>
    [Fact]
    public async Task Factory_copies_definition_recycle_policy_into_worker_generation()
    {
        await using var registry = CreateAdmissionRegistry();
        var executablePath = FindTestWorkerExecutable();
        var definition = CreateDefinition(
            CreateAdmissionOptions(),
            workerProfileGenerationId:
                "profile-generation-factory-recycle-" + Guid.NewGuid().ToString("N"),
            executablePath: executablePath,
            workerExecutableSha256: ComputeSha256(executablePath),
            warmUpOnActivation: false,
            recyclePolicyOptions: new OfficialWorkerRecyclePolicyOptions(
                maximumWorkerAge: TimeSpan.FromMinutes(10),
                maximumCompletedOperations: 1,
                maximumPrivateBytes: 1L << 40,
                maximumWorkingSet: 1L << 40,
                maximumConsecutiveCompleteWorkerTimeouts: 10));
        var factory = new DynamicsProfileRuntimeFactory(registry);
        var runtime = (DynamicsProfileRuntime)await factory.CreateAsync(
            definition,
            generation: 1,
            CancellationToken.None);

        try
        {
            runtime.TryAcquireExecution(out var lease).Should().BeTrue();
            var acquiredLease = lease!;
            await using (acquiredLease)
            {
                var first = await acquiredLease.Executor.ExecuteAsync(
                    CreateWhoAmIRequest(
                        definition.ProfileAlias,
                        "factory-recycle-first-response"),
                    CancellationToken.None);
                var rejected = await acquiredLease.Executor.ExecuteAsync(
                    CreateWhoAmIRequest(
                        definition.ProfileAlias,
                        "factory-recycle-rejected-response"),
                    CancellationToken.None);

                first.Succeeded.Should().BeTrue();
                rejected.Succeeded.Should().BeFalse();
                rejected.ErrorCode.Should().Be("worker.operation.recycle-required");
            }
        }
        finally
        {
            await runtime.DrainAndDisposeAsync();
            await factory.DisposeAsync();
        }
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
            snapshot.OwnedOperationGateCount == 0 &&
            snapshot.OwnedOutputReaderCount == 0 &&
            snapshot.OwnedOutputTaskCount == 0 &&
            snapshot.OwnedOutputCancellationSourceCount == 0 &&
            snapshot.OperationEntrantCount == 0 &&
            snapshot.ActiveOperationCount == 0);
    }

    /// <summary>
    /// 驗證 singleton Factory 在某個 Worker 的 READY 無效時保留原始 protocol failure，同時在單一
    /// bounded rollback 內釋放 Process、Pipe、reader tasks 與 admission registration，不留下 retained owner。
    /// </summary>
    [Fact]
    public async Task Factory_preserves_invalid_ready_failure_after_bounded_rollback_releases_every_owner()
    {
        await using var registry = CreateAdmissionRegistry();
        var executablePath = FindTestWorkerExecutable();
        var generation = CreateRunUniqueGeneration("profile-generation-invalid-ready-");
        var evidencePath = GetWorkerProcessEvidencePath(generation);
        var definition = CreateDefinition(
            CreateAdmissionOptions(),
            workerProfileGenerationId: generation,
            executablePath: executablePath,
            workerExecutableSha256: ComputeSha256(executablePath),
            warmUpOnActivation: false,
            drainTimeout: TimeSpan.FromMilliseconds(150));
        var factory = new DynamicsProfileRuntimeFactory(registry);
        int? processId = null;
        File.Delete(evidencePath);

        try
        {
            var act = () => factory.CreateAsync(definition, generation: 1, CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<Exception>();
            thrown.Which.GetType().Name.Should().Be("WorkerProtocolException");
            thrown.Which.Message.Should().Be(
                "The official Dynamics worker readiness identity is invalid.");
            processId = await ReadCapturedProcessIdAsync(evidencePath);
            await WaitForProcessExitAsync(processId.Value);
            registry.EntryCount.Should().Be(0);
            GetRetainedStartupLifecycleSnapshot(factory).Should().BeNull();
            GetRetainedPartialCreationSnapshot(factory).Should().BeNull();

            await factory.DisposeAsync();
            await factory.DisposeAsync();
        }
        finally
        {
            if (processId.HasValue)
            {
                await TerminateTestOwnedProcessAsync(processId.Value);
            }

            await DisposeIgnoringFixedCleanupFailureAsync(factory);
            File.Delete(evidencePath);
        }
    }

    /// <summary>
    /// 驗證第二個 Worker 在 READY 失敗時，Factory 將已成功 Worker、startup owner 與 admission
    /// registration 放入同一 composite partial-creation owner。每項只有在 cleanup 被明確確認後才移除；
    /// registration cleanup 持續失敗時，不得建立下一個 registration 或 Process。解除 test-owned failure
    /// 後，同一 retained owner 先歸零，才允許後續 Create，且原始 READY protocol failure 始終是第一原因。
    /// </summary>
    [Fact]
    public async Task Factory_retains_failed_registration_in_composite_partial_creation_owner()
    {
        var failingGeneration = CreateRunUniqueGeneration(
            "profile-generation-second-start-invalid-ready-");
        var markerPath = GetFirstWorkerStartMarkerPath(failingGeneration);
        File.Delete(markerPath);
        await using var registry = new RetryableCleanupAdmissionRegistry(
            CreateAdmissionRegistry());
        var executablePath = FindTestWorkerExecutable();
        var definition = CreateDefinition(
            CreateAdmissionOptions(),
            workerProfileGenerationId: failingGeneration,
            executablePath: executablePath,
            workerExecutableSha256: ComputeSha256(executablePath),
            workerCount: 2,
            warmUpOnActivation: false,
            drainTimeout: TimeSpan.FromMilliseconds(500));
        var factory = new DynamicsProfileRuntimeFactory(registry);
        IDynamicsProfileRuntime? unexpectedRuntime = null;
        IDynamicsProfileRuntime? recoveredRuntime = null;

        try
        {
            var firstCreate = () => factory.CreateAsync(
                definition,
                generation: 1,
                CancellationToken.None);
            var thrown = await firstCreate.Should().ThrowAsync<AggregateException>();
            thrown.Which.InnerExceptions[0].GetType().Name.Should().Be(
                "WorkerProtocolException",
                because: "rollback cleanup failure must not replace the startup/protocol first cause");

            var retained = GetRetainedPartialCreationSnapshot(factory);
            ReadSnapshotCount(retained, "OwnedWorkerCount").Should().Be(0,
                because: "successfully retired workers must be removed from the composite owner");
            ReadSnapshotCount(retained, "OwnedStartupOwnerCount").Should().Be(0,
                because: "a fully retired startup owner must be removed from the composite owner");
            ReadSnapshotCount(retained, "OwnedRegistrationCount").Should().Be(1);
            ReadSnapshotCount(retained, "OwnedOperationGateCount").Should().Be(0);
            ReadSnapshotCount(retained, "OwnedOutputReaderCount").Should().Be(0);
            ReadSnapshotCount(retained, "OwnedOutputTaskCount").Should().Be(0);
            ReadSnapshotCount(retained, "OwnedOutputCancellationSourceCount").Should().Be(0);
            ReadSnapshotCount(retained, "OwnedProcessExitWaitCount").Should().Be(0);
            ReadSnapshotCount(retained, "OperationEntrantCount").Should().Be(0);
            ReadSnapshotCount(retained, "ActiveOperationCount").Should().Be(0);
            registry.AcquireCount.Should().Be(1);
            registry.EntryCount.Should().Be(1);

            var blockedDefinition = CreateDefinition(
                CreateAdmissionOptions(),
                workerProfileGenerationId: CreateRunUniqueGeneration("profile-generation-recovery-blocked-"),
                executablePath: executablePath,
                workerExecutableSha256: ComputeSha256(executablePath),
                warmUpOnActivation: false,
                drainTimeout: TimeSpan.FromMilliseconds(250));
            Func<Task> blockedCreate = async () =>
            {
                unexpectedRuntime = await factory.CreateAsync(
                    blockedDefinition,
                    generation: 2,
                    CancellationToken.None);
            };
            await blockedCreate.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The official Dynamics worker cleanup did not complete.");
            unexpectedRuntime.Should().BeNull();
            registry.AcquireCount.Should().Be(1,
                because: "retained cleanup must complete before another registration or Process is allocated");

            registry.AllowCleanup();
            recoveredRuntime = await factory.CreateAsync(
                blockedDefinition,
                generation: 3,
                CancellationToken.None);
            GetRetainedPartialCreationSnapshot(factory).Should().BeNull();
            registry.AcquireCount.Should().Be(2);

            recoveredRuntime.BeginDrain();
            await recoveredRuntime.DrainAndDisposeAsync();
            recoveredRuntime = null;
            registry.EntryCount.Should().Be(0);
        }
        finally
        {
            if (unexpectedRuntime is not null)
            {
                unexpectedRuntime.BeginDrain();
                await unexpectedRuntime.DrainAndDisposeAsync();
            }

            if (recoveredRuntime is not null)
            {
                recoveredRuntime.BeginDrain();
                await recoveredRuntime.DrainAndDisposeAsync();
            }

            registry.AllowCleanup();
            await DisposeIgnoringFixedCleanupFailureAsync(factory);
            File.Delete(markerPath);
        }
    }

    /// <summary>
    /// 驗證 partial-creation rollback 嚴格遵守反向所有權：第二個 Worker 的 READY 失敗後，
    /// 第一個 Worker 若仍被 descendant 繼承的 stdout/stderr handle 阻擋，Factory 必須保留
    /// Worker、Process 與 reader task，且不得提早呼叫 admission registration cleanup。
    /// 第一次及後續 bounded retry 都只能重試最上層未完成 owner；待測試擁有的 descendant
    /// 關閉 handles 後，才可依序完成 Worker cleanup，再釋放 registration，避免容量先歸還但
    /// 上游行程仍存活的跨 generation 資源與 admission 重疊。
    /// </summary>
    [Fact]
    public async Task Factory_does_not_release_registration_before_blocked_worker_cleanup_finishes()
    {
        var generation = CreateRunUniqueGeneration(
            "profile-generation-second-start-invalid-ready-never-exit-descendant-");
        var markerPath = GetFirstWorkerStartMarkerPath(generation);
        var descendantEvidencePath = GetDescendantEvidencePath(generation);
        File.Delete(markerPath);
        File.Delete(descendantEvidencePath);
        await using var registry = new RetryableCleanupAdmissionRegistry(
            CreateAdmissionRegistry());
        var executablePath = FindTestWorkerExecutable();
        var definition = CreateDefinition(
            CreateAdmissionOptions(),
            workerProfileGenerationId: generation,
            executablePath: executablePath,
            workerExecutableSha256: ComputeSha256(executablePath),
            workerCount: 2,
            warmUpOnActivation: false,
            drainTimeout: TimeSpan.FromMilliseconds(350));
        var factory = new DynamicsProfileRuntimeFactory(registry);
        int? descendantProcessId = null;

        try
        {
            var firstCreate = () => factory.CreateAsync(
                definition,
                generation: 1,
                CancellationToken.None);

            var thrown = await firstCreate.Should().ThrowAsync<AggregateException>();
            thrown.Which.InnerExceptions[0].GetType().Name.Should().Be(
                "WorkerProtocolException",
                because: "rollback must retain the original READY identity failure as the first cause");
            descendantProcessId = await ReadCapturedProcessIdAsync(descendantEvidencePath);
            IsProcessRunning(descendantProcessId.Value).Should().BeTrue();

            var retained = GetRetainedPartialCreationSnapshot(factory);
            ReadSnapshotCount(retained, "OwnedWorkerCount").Should().Be(1);
            ReadSnapshotCount(retained, "OwnedRegistrationCount").Should().Be(1);
            ReadSnapshotCount(retained, "OwnedProcessCount").Should().BeGreaterThan(0);
            ReadSnapshotCount(retained, "OwnedBackgroundTaskCount").Should().BeGreaterThan(0);
            ReadSnapshotCount(retained, "OwnedProcessExitWaitCount").Should().Be(0,
                because: "each bounded Worker cleanup attempt must release its exit-wait registration before returning");
            registry.RegistrationDisposeAttemptCount.Should().Be(0,
                because: "a lower admission owner cannot be released while the higher Worker owner is incomplete");

            Func<Task> blockedDispose = async () => await factory.DisposeAsync();
            await blockedDispose.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The official Dynamics worker cleanup did not complete.");
            registry.RegistrationDisposeAttemptCount.Should().Be(0,
                because: "bounded retries must resume at the same incomplete Worker owner");

            await TerminateTestOwnedProcessAsync(descendantProcessId.Value);
            descendantProcessId = null;
            registry.AllowCleanup();
            await RetryFactoryDisposeUntilRetiredAsync(factory);

            registry.RegistrationDisposeAttemptCount.Should().Be(1);
            registry.EntryCount.Should().Be(0);
            GetRetainedPartialCreationSnapshot(factory).Should().BeNull();
        }
        finally
        {
            if (descendantProcessId.HasValue)
            {
                await TerminateTestOwnedProcessAsync(descendantProcessId.Value);
            }

            registry.AllowCleanup();
            await DisposeIgnoringFixedCleanupFailureAsync(factory);
            File.Delete(markerPath);
            File.Delete(descendantEvidencePath);
        }
    }

    /// <summary>
    /// 驗證 registration 的 DisposeAsync 若尚未完成，Factory 只保留並重用同一個 cleanup task。
    /// 每次 bounded retry 不得建立新的 dispose owner、Task.WhenAll graph 或 continuation chain；
    /// snapshot 必須持續顯示一個明確 cleanup attempt，直到測試允許該 owner 完成後才歸零。
    /// </summary>
    [Fact]
    public async Task Factory_reuses_one_pending_cleanup_attempt_across_bounded_retries()
    {
        await using var registry = new PendingCleanupAdmissionRegistry(
            CreateAdmissionRegistry());
        var executablePath = FindTestWorkerExecutable();
        var definition = CreateDefinition(
            CreateAdmissionOptions(),
            workerProfileGenerationId: CreateRunUniqueGeneration(
                "profile-generation-pending-registration-cleanup-"),
            executablePath: executablePath,
            workerExecutableSha256: new string('0', 64),
            warmUpOnActivation: false,
            drainTimeout: TimeSpan.FromMilliseconds(120));
        var factory = new DynamicsProfileRuntimeFactory(registry);

        try
        {
            Func<Task> firstCreate = async () =>
                await factory.CreateAsync(definition, generation: 1, CancellationToken.None);
            await firstCreate.Should().ThrowAsync<AggregateException>();

            var retained = GetRetainedPartialCreationSnapshot(factory);
            ReadSnapshotCount(retained, "OwnedRegistrationCount").Should().Be(1);
            ReadSnapshotCount(retained, "OwnedCleanupAttemptCount").Should().Be(1);
            ReadSnapshotCount(retained, "OwnedProcessExitWaitCount").Should().Be(0);
            registry.RegistrationDisposeAttemptCount.Should().Be(1);

            Func<Task> blockedCreate = async () =>
                await factory.CreateAsync(definition, generation: 2, CancellationToken.None);
            await blockedCreate.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The official Dynamics worker cleanup did not complete.");

            retained = GetRetainedPartialCreationSnapshot(factory);
            ReadSnapshotCount(retained, "OwnedCleanupAttemptCount").Should().Be(1,
                because: "the same incomplete registration cleanup task must be reused");
            registry.RegistrationDisposeAttemptCount.Should().Be(1,
                because: "a bounded retry must not create another cleanup owner");

            registry.AllowCleanup();
            await factory.DisposeAsync();

            registry.EntryCount.Should().Be(0);
            GetRetainedPartialCreationSnapshot(factory).Should().BeNull();
        }
        finally
        {
            registry.AllowCleanup();
            await DisposeIgnoringFixedCleanupFailureAsync(factory);
        }
    }

    /// <summary>
    /// 驗證 Create 在另一個 startup owner 持有 creation gate 時，不會以 caller cancellation 為唯一界限
    /// 無界等待。gate acquisition、retained retry 與可能的 rollback 必須共用 definition DrainTimeout 的
    /// monotonic remaining budget；deadline 到期只回固定 cleanup failure，且不得啟動第二個 Worker。
    /// </summary>
    [Fact]
    public async Task Factory_create_gate_wait_consumes_one_absolute_deadline()
    {
        var firstGeneration = CreateRunUniqueGeneration("profile-generation-startup-timeout-");
        var blockedGeneration = CreateRunUniqueGeneration("profile-generation-startup-timeout-");
        var firstEvidencePath = GetWorkerProcessEvidencePath(firstGeneration);
        var blockedEvidencePath = GetWorkerProcessEvidencePath(blockedGeneration);
        File.Delete(firstEvidencePath);
        File.Delete(blockedEvidencePath);
        await using var registry = CreateAdmissionRegistry();
        var executablePath = FindTestWorkerExecutable();
        var factory = new DynamicsProfileRuntimeFactory(registry);
        var firstDefinition = CreateDefinition(
            CreateAdmissionOptions(),
            workerProfileGenerationId: firstGeneration,
            executablePath: executablePath,
            workerExecutableSha256: ComputeSha256(executablePath),
            warmUpOnActivation: false,
            drainTimeout: TimeSpan.FromMilliseconds(150),
            startupTimeout: TimeSpan.FromMilliseconds(750));
        var blockedDefinition = CreateDefinition(
            CreateAdmissionOptions(),
            workerProfileGenerationId: blockedGeneration,
            executablePath: executablePath,
            workerExecutableSha256: ComputeSha256(executablePath),
            warmUpOnActivation: false,
            drainTimeout: TimeSpan.FromMilliseconds(150),
            startupTimeout: TimeSpan.FromMilliseconds(150));
        var firstCreateTask = factory.CreateAsync(
            firstDefinition,
            generation: 1,
            CancellationToken.None);

        try
        {
            _ = await ReadCapturedProcessIdAsync(firstEvidencePath);
            var elapsed = Stopwatch.StartNew();
            var blockedCreate = () => factory.CreateAsync(
                blockedDefinition,
                generation: 2,
                CancellationToken.None);
            await blockedCreate.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The official Dynamics worker cleanup did not complete.");
            elapsed.Stop();

            elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(450));
            File.Exists(blockedEvidencePath).Should().BeFalse();
        }
        finally
        {
            _ = await Record.ExceptionAsync(async () => await firstCreateTask);
            await DisposeIgnoringFixedCleanupFailureAsync(factory);
            File.Delete(firstEvidencePath);
            File.Delete(blockedEvidencePath);
        }
    }

    /// <summary>
    /// 驗證 Factory Dispose 的 creation-gate wait、retained cleanup 與所有 Create entrant drain 共用一次
    /// monotonic deadline；並行 Dispose caller 必須取得同一 attempt task。deadline 失敗後不 Dispose
    /// semaphore，也不清除未完成 owner；持有 gate 的 Create 結束後，下一次 bounded retry 才能清零。
    /// </summary>
    [Fact]
    public async Task Factory_dispose_gate_and_entrant_drain_share_one_absolute_deadline()
    {
        var generation = CreateRunUniqueGeneration("profile-generation-startup-timeout-");
        var evidencePath = GetWorkerProcessEvidencePath(generation);
        File.Delete(evidencePath);
        await using var registry = CreateAdmissionRegistry();
        var executablePath = FindTestWorkerExecutable();
        var factory = new DynamicsProfileRuntimeFactory(registry);
        var definition = CreateDefinition(
            CreateAdmissionOptions(),
            workerProfileGenerationId: generation,
            executablePath: executablePath,
            workerExecutableSha256: ComputeSha256(executablePath),
            warmUpOnActivation: false,
            drainTimeout: TimeSpan.FromMilliseconds(150),
            startupTimeout: TimeSpan.FromMilliseconds(750));
        var createTask = factory.CreateAsync(
            definition,
            generation: 1,
            CancellationToken.None);

        try
        {
            _ = await ReadCapturedProcessIdAsync(evidencePath);
            var elapsed = Stopwatch.StartNew();
            var firstDisposeTask = factory.DisposeAsync().AsTask();
            var concurrentDisposeTask = factory.DisposeAsync().AsTask();
            concurrentDisposeTask.Should().BeSameAs(firstDisposeTask);
            var failure = await Record.ExceptionAsync(async () => await firstDisposeTask);
            elapsed.Stop();

            failure.Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Be(
                    "The official Dynamics worker cleanup did not complete.");
            elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(450));

            _ = await Record.ExceptionAsync(async () => await createTask);
            await factory.DisposeAsync();
        }
        finally
        {
            _ = await Record.ExceptionAsync(async () => await createTask);
            await DisposeIgnoringFixedCleanupFailureAsync(factory);
            File.Delete(evidencePath);
        }
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
        TimeSpan? drainTimeout = null,
        TimeSpan? startupTimeout = null,
        OfficialWorkerRecyclePolicyOptions? recyclePolicyOptions = null)
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
            startupTimeout: startupTimeout ?? TimeSpan.FromSeconds(10),
            operationTimeout: TimeSpan.FromSeconds(5),
            drainTimeout,
            recyclePolicyOptions: recyclePolicyOptions);

    private static OperationExecutionRequest CreateWhoAmIRequest(
        string profileAlias,
        string workloadSubjectId)
        => new()
        {
            ProfileAlias = profileAlias,
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = workloadSubjectId
        };

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
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var executablePath = Path.Combine(
            root,
            "SpeechMessage.Dynamics.WorkerTestHost",
            "bin",
            configuration,
            "net10.0",
            "SpeechMessage.Dynamics.WorkerTestHost.exe");
        File.Exists(executablePath).Should().BeTrue(
            because: "the test project reference must build the SDK-free worker test host");
        return executablePath;
    }

    /// <summary>
    /// 從 Factory 的 internal lifecycle seam 取得目前唯一 retained startup owner snapshot。反射只讀取
    /// bounded counters；若 Factory 未提供此 seam 或嘗試暴露實際 Process／Pipe owner，測試會 fail closed。
    /// </summary>
    private static OfficialWorkerLifecycleSnapshot? GetRetainedStartupLifecycleSnapshot(
        DynamicsProfileRuntimeFactory factory)
    {
        var method = factory.GetType().GetMethod(
            "GetRetainedStartupLifecycleSnapshot",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        method.Should().NotBeNull();
        return (OfficialWorkerLifecycleSnapshot?)method!.Invoke(factory, null);
    }

    /// <summary>
    /// 透過 internal diagnostic seam 取得 composite partial-creation owner 的 bounded counter snapshot；
    /// seam 不得暴露實際 Process、Pipe、Task、registration、Credential、Token 或 Session reference。
    /// </summary>
    private static object? GetRetainedPartialCreationSnapshot(
        DynamicsProfileRuntimeFactory factory)
    {
        var method = factory.GetType().GetMethod(
            "GetRetainedPartialCreationSnapshot",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        method.Should().NotBeNull();
        return method!.Invoke(factory, null);
    }

    /// <summary>從 bounded diagnostic snapshot 讀取指定 ownership counter，找不到欄位時 fail closed。</summary>
    private static int ReadSnapshotCount(object? snapshot, string propertyName)
    {
        snapshot.Should().NotBeNull();
        var property = snapshot!.GetType().GetProperty(propertyName);
        property.Should().NotBeNull();
        return property!.GetValue(snapshot).Should().BeOfType<int>().Subject;
    }

    /// <summary>建立符合 128 字元 bootstrap 上限的 run-unique generation，隔離平行 test class。</summary>
    private static string CreateRunUniqueGeneration(string prefix) =>
        prefix + Guid.NewGuid().ToString("N");

    /// <summary>取得第二個 Worker failure 模式使用的 run-unique atomic marker 路徑。</summary>
    private static string GetFirstWorkerStartMarkerPath(string profileGenerationId) =>
        Path.Combine(
            Path.GetTempPath(),
            $"speechmessage-dynamics-worker-{profileGenerationId}.first-start");

    /// <summary>取得 startup-timeout Worker 寫入的 run-unique PID evidence 路徑。</summary>
    private static string GetWorkerProcessEvidencePath(string profileGenerationId) =>
        Path.Combine(
            Path.GetTempPath(),
            $"speechmessage-dynamics-worker-{profileGenerationId}.pid");

    /// <summary>取得由 run-unique generation 綁定的測試 descendant PID evidence 路徑。</summary>
    private static string GetDescendantEvidencePath(string profileGenerationId) =>
        Path.Combine(
            Path.GetTempPath(),
            $"speechmessage-dynamics-worker-{profileGenerationId}.descendant.pid");

    /// <summary>
    /// 以 bounded condition polling 等待 test-owned Worker PID evidence，不使用固定 sleep；內容只接受正整數
    /// PID，路徑由 run-unique generation 產生，不含秘密或外部輸入。
    /// </summary>
    private static async Task<int> ReadCapturedProcessIdAsync(string evidencePath)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (File.Exists(evidencePath))
            {
                var text = await File.ReadAllTextAsync(evidencePath);
                if (int.TryParse(
                        text,
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var processId) &&
                    processId > 0)
                {
                    return processId;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new InvalidOperationException("The test worker process evidence was not captured.");
    }

    /// <summary>Waits for the run-unique test-owned worker process to exit without retaining its handle.</summary>
    private static async Task WaitForProcessExitAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
        }
    }

    /// <summary>Terminates only the exact run-unique test-owned PID during finally cleanup.</summary>
    private static async Task TerminateTestOwnedProcessAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
        }
    }

    /// <summary>只讀確認指定的測試 PID 是否仍存在且尚未退出，不保留 Process handle。</summary>
    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// 測試 finally 專用的完整 await cleanup；只忽略已知固定 cleanup failure，讓下一個 retry owner
    /// 仍保持可達，不會吞掉其他程式錯誤或建立未觀察 Task。
    /// </summary>
    private static async Task DisposeIgnoringFixedCleanupFailureAsync(IAsyncDisposable owner)
    {
        try
        {
            await owner.DisposeAsync();
        }
        catch (InvalidOperationException exception) when (
            string.Equals(
                exception.Message,
                "The official Dynamics worker cleanup did not complete.",
                StringComparison.Ordinal))
        {
        }
    }

    /// <summary>
    /// 將 production registry registration 包成可由 test-owned signal 控制的 retryable cleanup owner。
    /// cleanup 未獲准時同步失敗且不啟動背景 Task；獲准後才把 ownership 交回 inner registration。
    /// wrapper 不保存 caller identity、Session、Token、Credential 或 request data。
    /// </summary>
    private sealed class RetryableCleanupAdmissionRegistry : IOrganizationAdmissionRegistry
    {
        private readonly IOrganizationAdmissionRegistry _inner;
        private int _cleanupAllowed;
        private int _acquireCount;
        private int _registrationDisposeAttemptCount;

        internal RetryableCleanupAdmissionRegistry(IOrganizationAdmissionRegistry inner)
        {
            _inner = inner;
        }

        public int EntryCount => _inner.EntryCount;

        internal int AcquireCount => Volatile.Read(ref _acquireCount);

        internal int RegistrationDisposeAttemptCount =>
            Volatile.Read(ref _registrationDisposeAttemptCount);

        public IOrganizationAdmissionRegistration Acquire(OrganizationAdmissionPlan plan)
        {
            Interlocked.Increment(ref _acquireCount);
            return new RetryableCleanupAdmissionRegistration(this, _inner.Acquire(plan));
        }

        internal void AllowCleanup() => Volatile.Write(ref _cleanupAllowed, 1);

        public void Dispose() => _inner.Dispose();

        public ValueTask DisposeAsync() => _inner.DisposeAsync();

        private sealed class RetryableCleanupAdmissionRegistration :
            IOrganizationAdmissionRegistration
        {
            private readonly RetryableCleanupAdmissionRegistry _owner;
            private readonly IOrganizationAdmissionRegistration _inner;

            internal RetryableCleanupAdmissionRegistration(
                RetryableCleanupAdmissionRegistry owner,
                IOrganizationAdmissionRegistration inner)
            {
                _owner = owner;
                _inner = inner;
            }

            public OrganizationAdmissionPlan Plan => _inner.Plan;

            public IOrganizationAdmissionManager Manager => _inner.Manager;

            public void Dispose()
            {
                Interlocked.Increment(ref _owner._registrationDisposeAttemptCount);
                if (Volatile.Read(ref _owner._cleanupAllowed) == 0)
                {
                    throw new InvalidOperationException("Synthetic registration cleanup failure.");
                }

                _inner.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref _owner._registrationDisposeAttemptCount);
                if (Volatile.Read(ref _owner._cleanupAllowed) == 0)
                {
                    return ValueTask.FromException(
                        new InvalidOperationException("Synthetic registration cleanup failure."));
                }

                return _inner.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// 在測試擁有的 descendant 已終止後，以有限 serialized retry 等候 OS 關閉 inherited output
    /// handles。每次都完整 await production cleanup owner，只接受固定 fail-closed 結果；成功即返回，
    /// 不建立 background timer、fire-and-forget task 或第二個並行 Dispose owner。
    /// </summary>
    private static async Task RetryFactoryDisposeUntilRetiredAsync(
        DynamicsProfileRuntimeFactory factory)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await factory.DisposeAsync();
                return;
            }
            catch (InvalidOperationException exception) when (
                string.Equals(
                    exception.Message,
                    "The official Dynamics worker cleanup did not complete.",
                    StringComparison.Ordinal))
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        await factory.DisposeAsync();
    }

    /// <summary>
    /// 提供一個可由測試明確解除的 pending registration cleanup owner。第一次 DisposeAsync
    /// 只建立一個不含 credential、Session 或 request state 的 TaskCompletionSource；後續呼叫若
    /// production 錯誤地重建 cleanup attempt，計數會增加並使回歸測試失敗。
    /// </summary>
    private sealed class PendingCleanupAdmissionRegistry : IOrganizationAdmissionRegistry
    {
        private readonly IOrganizationAdmissionRegistry _inner;
        private readonly TaskCompletionSource _cleanupCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IOrganizationAdmissionRegistration? _pendingRegistration;
        private int _cleanupAllowed;
        private int _registrationDisposeAttemptCount;

        internal PendingCleanupAdmissionRegistry(IOrganizationAdmissionRegistry inner)
        {
            _inner = inner;
        }

        public int EntryCount => _inner.EntryCount;

        internal int RegistrationDisposeAttemptCount =>
            Volatile.Read(ref _registrationDisposeAttemptCount);

        public IOrganizationAdmissionRegistration Acquire(OrganizationAdmissionPlan plan)
        {
            var innerRegistration = _inner.Acquire(plan);
            var wrapper = new PendingCleanupAdmissionRegistration(this, innerRegistration);
            if (Interlocked.CompareExchange(ref _pendingRegistration, wrapper, null) is not null)
            {
                innerRegistration.Dispose();
                throw new InvalidOperationException();
            }

            return wrapper;
        }

        internal void AllowCleanup()
        {
            if (Interlocked.Exchange(ref _cleanupAllowed, 1) != 0)
            {
                return;
            }

            var registration = Interlocked.Exchange(ref _pendingRegistration, null);
            try
            {
                registration?.Dispose();
                _cleanupCompletion.TrySetResult();
            }
            catch (Exception failure)
            {
                _cleanupCompletion.TrySetException(failure);
            }
        }

        public void Dispose()
        {
            AllowCleanup();
            _inner.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            AllowCleanup();
            await _inner.DisposeAsync();
        }

        private sealed class PendingCleanupAdmissionRegistration :
            IOrganizationAdmissionRegistration
        {
            private readonly PendingCleanupAdmissionRegistry _owner;
            private readonly IOrganizationAdmissionRegistration _inner;

            internal PendingCleanupAdmissionRegistration(
                PendingCleanupAdmissionRegistry owner,
                IOrganizationAdmissionRegistration inner)
            {
                _owner = owner;
                _inner = inner;
            }

            public OrganizationAdmissionPlan Plan => _inner.Plan;

            public IOrganizationAdmissionManager Manager => _inner.Manager;

            public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref _owner._registrationDisposeAttemptCount);
                if (Volatile.Read(ref _owner._cleanupAllowed) != 0)
                {
                    _inner.Dispose();
                    return ValueTask.CompletedTask;
                }

                return new ValueTask(_owner._cleanupCompletion.Task);
            }
        }
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
