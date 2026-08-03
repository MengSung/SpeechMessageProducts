using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Runtime;
using SpeechMessage.Dynamics.WorkerSupervisor;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證官方 Worker 的純記憶體回收決策與部署設定邊界。
/// 測試只傳入單調時間戳、有限純量與不含秘密的資源觀測，不建立 Process、Timer、背景工作、
/// Session、Credential、Token 或任何跨 Profile 共用的 mutable state。
/// </summary>
public sealed class OfficialWorkerRecyclePolicyTests
{
    private const long StartedTimestamp = 10_000;

    /// <summary>
    /// 每一個回收門檻都必須是有限正值且受部署硬上限保護；無效值在 Policy 或 Worker 資源建立前失敗。
    /// </summary>
    [Fact]
    public void Options_reject_non_positive_and_deployment_exceeding_thresholds()
    {
        var invalidFactories = new Func<OfficialWorkerRecyclePolicyOptions>[]
        {
            () => CreateOptions(maximumWorkerAge: TimeSpan.Zero),
            () => CreateOptions(maximumWorkerAge: TimeSpan.FromTicks(-1)),
            () => CreateOptions(
                maximumWorkerAge:
                    OfficialWorkerRecyclePolicyOptions.DeploymentMaximumWorkerAge +
                    TimeSpan.FromTicks(1)),
            () => CreateOptions(maximumCompletedOperations: 0),
            () => CreateOptions(maximumCompletedOperations: -1),
            () => CreateOptions(
                maximumCompletedOperations:
                    OfficialWorkerRecyclePolicyOptions.DeploymentMaximumCompletedOperations + 1),
            () => CreateOptions(maximumPrivateBytes: 0),
            () => CreateOptions(maximumPrivateBytes: -1),
            () => CreateOptions(
                maximumPrivateBytes:
                    OfficialWorkerRecyclePolicyOptions.DeploymentMaximumResourceBytes + 1),
            () => CreateOptions(maximumWorkingSet: 0),
            () => CreateOptions(maximumWorkingSet: -1),
            () => CreateOptions(
                maximumWorkingSet:
                    OfficialWorkerRecyclePolicyOptions.DeploymentMaximumResourceBytes + 1),
            () => CreateOptions(maximumConsecutiveCompleteWorkerTimeouts: 0),
            () => CreateOptions(maximumConsecutiveCompleteWorkerTimeouts: -1),
            () => CreateOptions(
                maximumConsecutiveCompleteWorkerTimeouts:
                    OfficialWorkerRecyclePolicyOptions
                        .DeploymentMaximumConsecutiveCompleteWorkerTimeouts + 1)
        };

        foreach (var invalidFactory in invalidFactories)
        {
            invalidFactory.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    /// <summary>
    /// Profile Definition 必須保存自己的已驗證 immutable recycle snapshot；呼叫端不能在建構後改寫門檻，
    /// 也不能透過同一 options reference 讓既有 generation 的回收政策漂移。
    /// </summary>
    [Fact]
    public void Profile_definition_carries_a_validated_immutable_recycle_snapshot()
    {
        var source = CreateOptions(
            maximumWorkerAge: TimeSpan.FromMinutes(17),
            maximumCompletedOperations: 1234,
            maximumPrivateBytes: 4567,
            maximumWorkingSet: 3456,
            maximumConsecutiveCompleteWorkerTimeouts: 4);

        var definition = CreateDefinition(source);

        definition.RecyclePolicyOptions.Should().NotBeSameAs(source);
        definition.RecyclePolicyOptions.MaximumWorkerAge.Should().Be(TimeSpan.FromMinutes(17));
        definition.RecyclePolicyOptions.MaximumCompletedOperations.Should().Be(1234);
        definition.RecyclePolicyOptions.MaximumPrivateBytes.Should().Be(4567);
        definition.RecyclePolicyOptions.MaximumWorkingSet.Should().Be(3456);
        definition.RecyclePolicyOptions.MaximumConsecutiveCompleteWorkerTimeouts.Should().Be(4);
        typeof(OfficialWorkerRecyclePolicyOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Should().OnlyContain(property => property.SetMethod == null);
    }

    /// <summary>
    /// 驗證從不可變 Profile Definition 複製出的每一份 Worker bootstrap options，
    /// 都必須保留完全相同的 recycle 門檻，且取得自己的 immutable policy snapshot。
    /// 若遺漏此複製，internal 呼叫端會安靜地回退至預設值，導致同一部署 generation
    /// 的 Worker 在 age、完成作業數、記憶體或 timeout 門檻上產生不一致的回收行為；
    /// 那會破壞 replace-and-drain 的可預測性，並提高資源滯留或不必要重啟的風險。
    /// </summary>
    [Fact]
    public void Worker_options_preserve_the_profile_recycle_policy_snapshot()
    {
        var definition = CreateDefinition(CreateOptions(
            maximumWorkerAge: TimeSpan.FromMinutes(17),
            maximumCompletedOperations: 1234,
            maximumPrivateBytes: 4567,
            maximumWorkingSet: 3456,
            maximumConsecutiveCompleteWorkerTimeouts: 4));

        var workerOptions = definition.CreateWorkerOptions();

        workerOptions.RecyclePolicyOptions.Should().NotBeSameAs(definition.RecyclePolicyOptions);
        workerOptions.RecyclePolicyOptions.MaximumWorkerAge.Should().Be(TimeSpan.FromMinutes(17));
        workerOptions.RecyclePolicyOptions.MaximumCompletedOperations.Should().Be(1234);
        workerOptions.RecyclePolicyOptions.MaximumPrivateBytes.Should().Be(4567);
        workerOptions.RecyclePolicyOptions.MaximumWorkingSet.Should().Be(3456);
        workerOptions.RecyclePolicyOptions.MaximumConsecutiveCompleteWorkerTimeouts.Should().Be(4);
    }

    /// <summary>Worker age 在單調時間差精確等於門檻時即停止下一次 admission。</summary>
    [Fact]
    public void Maximum_worker_age_boundary_equality_requires_recycle()
    {
        var options = CreateOptions(maximumWorkerAge: TimeSpan.FromSeconds(1));
        var policy = new OfficialWorkerRecyclePolicy(options, StartedTimestamp);

        var reason = policy.EvaluateForNextAdmission(
            StartedTimestamp + Stopwatch.Frequency,
            isReady: true,
            isHealthy: true,
            protocolViolation: false,
            HealthyObservation());

        reason.Should().Be(OfficialWorkerRecycleReason.MaximumWorkerAge);
    }

    /// <summary>完成作業數在本次完整 response 後精確到達門檻時，該作業已完成且下一次 admission 被拒絕。</summary>
    [Fact]
    public void Maximum_completed_operation_boundary_equality_requires_recycle_after_completion()
    {
        var policy = new OfficialWorkerRecyclePolicy(
            CreateOptions(maximumCompletedOperations: 1),
            StartedTimestamp);

        var completionReason = policy.RecordCompletedResponse(
            StartedTimestamp,
            isCompleteWorkerTimeout: false,
            HealthyObservation());
        var nextAdmissionReason = policy.EvaluateForNextAdmission(
            StartedTimestamp,
            isReady: true,
            isHealthy: true,
            protocolViolation: false,
            HealthyObservation());

        completionReason.Should().Be(OfficialWorkerRecycleReason.MaximumCompletedOperations);
        policy.CompletedOperationCount.Should().Be(1);
        policy.IsRecycleRequired.Should().BeTrue();
        nextAdmissionReason.Should().Be(OfficialWorkerRecycleReason.MaximumCompletedOperations);
    }

    /// <summary>Private Bytes 精確等於部署門檻時即回收，不使用大於比較而漏掉邊界值。</summary>
    [Fact]
    public void Maximum_private_bytes_boundary_equality_requires_recycle()
    {
        var policy = new OfficialWorkerRecyclePolicy(
            CreateOptions(maximumPrivateBytes: 100, maximumWorkingSet: 200),
            StartedTimestamp);

        var reason = policy.EvaluateForNextAdmission(
            StartedTimestamp,
            isReady: true,
            isHealthy: true,
            protocolViolation: false,
            new OfficialWorkerResourceObservation(true, 100, 99));

        reason.Should().Be(OfficialWorkerRecycleReason.MaximumPrivateBytes);
    }

    /// <summary>Working Set 精確等於部署門檻時即回收，不使用大於比較而漏掉邊界值。</summary>
    [Fact]
    public void Maximum_working_set_boundary_equality_requires_recycle()
    {
        var policy = new OfficialWorkerRecyclePolicy(
            CreateOptions(maximumPrivateBytes: 200, maximumWorkingSet: 100),
            StartedTimestamp);

        var reason = policy.EvaluateForNextAdmission(
            StartedTimestamp,
            isReady: true,
            isHealthy: true,
            protocolViolation: false,
            new OfficialWorkerResourceObservation(true, 99, 100));

        reason.Should().Be(OfficialWorkerRecycleReason.MaximumWorkingSet);
    }

    /// <summary>完整 Worker timeout response 的連續次數精確等於門檻時，該 response 完成後才要求回收。</summary>
    [Fact]
    public void Complete_worker_timeout_streak_boundary_equality_requires_recycle()
    {
        var policy = new OfficialWorkerRecyclePolicy(
            CreateOptions(maximumConsecutiveCompleteWorkerTimeouts: 2),
            StartedTimestamp);

        policy.RecordCompletedResponse(
                StartedTimestamp,
                isCompleteWorkerTimeout: true,
                HealthyObservation())
            .Should().Be(OfficialWorkerRecycleReason.None);

        policy.RecordCompletedResponse(
                StartedTimestamp,
                isCompleteWorkerTimeout: true,
                HealthyObservation())
            .Should().Be(OfficialWorkerRecycleReason.MaximumConsecutiveCompleteWorkerTimeouts);
        policy.ConsecutiveCompleteWorkerTimeouts.Should().Be(2);
    }

    /// <summary>
    /// 缺少、無法讀取、負值或超出支援上限的資源觀測都代表健康證據不完整；必須 fail closed，
    /// 不能把它解讀為低於 Private Bytes／Working Set 門檻。
    /// </summary>
    [Fact]
    public void Missing_unreadable_negative_and_overflow_resource_observations_fail_closed()
    {
        OfficialWorkerResourceObservation?[] invalidObservations =
        [
            null,
            OfficialWorkerResourceObservation.Unreadable,
            new OfficialWorkerResourceObservation(true, -1, 0),
            new OfficialWorkerResourceObservation(true, 0, -1),
            new OfficialWorkerResourceObservation(
                true,
                OfficialWorkerResourceObservation.MaximumSupportedObservedBytes + 1,
                0),
            new OfficialWorkerResourceObservation(
                true,
                0,
                OfficialWorkerResourceObservation.MaximumSupportedObservedBytes + 1)
        ];

        foreach (var observation in invalidObservations)
        {
            var policy = new OfficialWorkerRecyclePolicy(CreateOptions(), StartedTimestamp);

            policy.EvaluateForNextAdmission(
                    StartedTimestamp,
                    isReady: true,
                    isHealthy: true,
                    protocolViolation: false,
                    observation)
                .Should().Be(OfficialWorkerRecycleReason.ResourceObservationFailure);
        }
    }

    /// <summary>
    /// 多個條件同時成立時，優先序固定為 NotReady、Health、Protocol、Resource Observation、Age、
    /// Completed Count、Private Bytes、Working Set、Complete Timeout Streak；不能受 if 順序漂移或資料值影響。
    /// </summary>
    [Fact]
    public void Simultaneous_triggers_use_the_documented_deterministic_priority()
    {
        var directOptions = CreateOptions(
            maximumWorkerAge: TimeSpan.FromSeconds(1),
            maximumPrivateBytes: 1,
            maximumWorkingSet: 1);

        new OfficialWorkerRecyclePolicy(directOptions, StartedTimestamp)
            .EvaluateForNextAdmission(
                StartedTimestamp + Stopwatch.Frequency,
                isReady: false,
                isHealthy: false,
                protocolViolation: true,
                OfficialWorkerResourceObservation.Unreadable)
            .Should().Be(OfficialWorkerRecycleReason.NotReady);

        new OfficialWorkerRecyclePolicy(directOptions, StartedTimestamp)
            .EvaluateForNextAdmission(
                StartedTimestamp + Stopwatch.Frequency,
                isReady: true,
                isHealthy: false,
                protocolViolation: true,
                OfficialWorkerResourceObservation.Unreadable)
            .Should().Be(OfficialWorkerRecycleReason.HealthFailure);

        new OfficialWorkerRecyclePolicy(directOptions, StartedTimestamp)
            .EvaluateForNextAdmission(
                StartedTimestamp + Stopwatch.Frequency,
                isReady: true,
                isHealthy: true,
                protocolViolation: true,
                OfficialWorkerResourceObservation.Unreadable)
            .Should().Be(OfficialWorkerRecycleReason.ProtocolViolation);

        new OfficialWorkerRecyclePolicy(directOptions, StartedTimestamp)
            .EvaluateForNextAdmission(
                StartedTimestamp + Stopwatch.Frequency,
                isReady: true,
                isHealthy: true,
                protocolViolation: false,
                OfficialWorkerResourceObservation.Unreadable)
            .Should().Be(OfficialWorkerRecycleReason.ResourceObservationFailure);

        new OfficialWorkerRecyclePolicy(directOptions, StartedTimestamp)
            .EvaluateForNextAdmission(
                StartedTimestamp + Stopwatch.Frequency,
                isReady: true,
                isHealthy: true,
                protocolViolation: false,
                new OfficialWorkerResourceObservation(true, 1, 1))
            .Should().Be(OfficialWorkerRecycleReason.MaximumWorkerAge);

        var countPolicy = new OfficialWorkerRecyclePolicy(
            CreateOptions(
                maximumCompletedOperations: 1,
                maximumPrivateBytes: 1,
                maximumWorkingSet: 1,
                maximumConsecutiveCompleteWorkerTimeouts: 1),
            StartedTimestamp);
        countPolicy.RecordCompletedResponse(
                StartedTimestamp,
                isCompleteWorkerTimeout: true,
                new OfficialWorkerResourceObservation(true, 1, 1))
            .Should().Be(OfficialWorkerRecycleReason.MaximumCompletedOperations);

        var privateBytesPolicy = new OfficialWorkerRecyclePolicy(
            CreateOptions(
                maximumPrivateBytes: 1,
                maximumWorkingSet: 1,
                maximumConsecutiveCompleteWorkerTimeouts: 1),
            StartedTimestamp);
        privateBytesPolicy.RecordCompletedResponse(
                StartedTimestamp,
                isCompleteWorkerTimeout: true,
                new OfficialWorkerResourceObservation(true, 1, 1))
            .Should().Be(OfficialWorkerRecycleReason.MaximumPrivateBytes);

        var workingSetPolicy = new OfficialWorkerRecyclePolicy(
            CreateOptions(
                maximumPrivateBytes: 2,
                maximumWorkingSet: 1,
                maximumConsecutiveCompleteWorkerTimeouts: 1),
            StartedTimestamp);
        workingSetPolicy.RecordCompletedResponse(
                StartedTimestamp,
                isCompleteWorkerTimeout: true,
                new OfficialWorkerResourceObservation(true, 1, 1))
            .Should().Be(OfficialWorkerRecycleReason.MaximumWorkingSet);

        var timeoutPolicy = new OfficialWorkerRecyclePolicy(
            CreateOptions(maximumConsecutiveCompleteWorkerTimeouts: 1),
            StartedTimestamp);
        timeoutPolicy.RecordCompletedResponse(
                StartedTimestamp,
                isCompleteWorkerTimeout: true,
                HealthyObservation())
            .Should().Be(OfficialWorkerRecycleReason.MaximumConsecutiveCompleteWorkerTimeouts);
    }

    /// <summary>任一完整且不是 Worker timeout 的 response 都會把連續 timeout streak 歸零。</summary>
    [Fact]
    public void Complete_non_timeout_response_resets_the_complete_timeout_streak()
    {
        var policy = new OfficialWorkerRecyclePolicy(
            CreateOptions(maximumConsecutiveCompleteWorkerTimeouts: 3),
            StartedTimestamp);

        policy.RecordCompletedResponse(StartedTimestamp, true, HealthyObservation());
        policy.RecordCompletedResponse(StartedTimestamp, true, HealthyObservation());
        policy.ConsecutiveCompleteWorkerTimeouts.Should().Be(2);

        policy.RecordCompletedResponse(StartedTimestamp, false, HealthyObservation());
        policy.ConsecutiveCompleteWorkerTimeouts.Should().Be(0);

        policy.RecordCompletedResponse(StartedTimestamp, true, HealthyObservation());
        policy.RecordCompletedResponse(StartedTimestamp, true, HealthyObservation());
        policy.RecycleReason.Should().Be(OfficialWorkerRecycleReason.None);
    }

    /// <summary>
    /// Supervisor 自己的 timeout、cancellation 或 frame interruption 代表 IPC 狀態不再可信，
    /// 必須立即標記 fatal retirement，不得等待完整 Worker timeout streak。
    /// </summary>
    [Fact]
    public void Supervisor_interruptions_require_immediate_fatal_retirement()
    {
        var timeoutPolicy = new OfficialWorkerRecyclePolicy(CreateOptions(), StartedTimestamp);
        var cancellationPolicy = new OfficialWorkerRecyclePolicy(CreateOptions(), StartedTimestamp);
        var framePolicy = new OfficialWorkerRecyclePolicy(CreateOptions(), StartedTimestamp);

        timeoutPolicy.RecordSupervisorTimeout()
            .Should().Be(OfficialWorkerRecycleReason.SupervisorTimeout);
        cancellationPolicy.RecordSupervisorCancellation()
            .Should().Be(OfficialWorkerRecycleReason.SupervisorCancellation);
        framePolicy.RecordFatalFrameInterruption()
            .Should().Be(OfficialWorkerRecycleReason.FatalFrameInterruption);

        timeoutPolicy.ConsecutiveCompleteWorkerTimeouts.Should().Be(0);
        cancellationPolicy.ConsecutiveCompleteWorkerTimeouts.Should().Be(0);
        framePolicy.ConsecutiveCompleteWorkerTimeouts.Should().Be(0);
    }

    /// <summary>
    /// 第一個成功記錄的 sanitized reason 是不可變 retirement identity；並行較晚原因不得覆寫，
    /// 也不得配置 lock-owned collection、Task 或 Timer 才能維持一致性。
    /// </summary>
    [Fact]
    public void First_recycle_reason_is_thread_safe_and_immutable()
    {
        var policy = new OfficialWorkerRecyclePolicy(CreateOptions(), StartedTimestamp);
        var racingResults = new OfficialWorkerRecycleReason[3];
        Parallel.Invoke(
            () => racingResults[0] = policy.RecordSupervisorTimeout(),
            () => racingResults[1] = policy.RecordSupervisorCancellation(),
            () => racingResults[2] = policy.RecordFatalFrameInterruption());
        var firstReason = policy.RecycleReason;

        firstReason.Should().BeOneOf(
            OfficialWorkerRecycleReason.SupervisorTimeout,
            OfficialWorkerRecycleReason.SupervisorCancellation,
            OfficialWorkerRecycleReason.FatalFrameInterruption);
        racingResults.Should().OnlyContain(reason => reason == firstReason);

        Parallel.For(
            0,
            1024,
            index =>
            {
                _ = (index % 3) switch
                {
                    0 => policy.RecordSupervisorCancellation(),
                    1 => policy.RecordFatalFrameInterruption(),
                    _ => policy.EvaluateForNextAdmission(
                        StartedTimestamp,
                        isReady: false,
                        isHealthy: false,
                        protocolViolation: true,
                        OfficialWorkerResourceObservation.Unreadable)
                };
            });

        policy.RecycleReason.Should().Be(firstReason);
        policy.IsRecycleRequired.Should().BeTrue();
    }

    /// <summary>單調時間戳倒退代表時間觀測不可信，Policy 以 sanitized health reason fail closed。</summary>
    [Fact]
    public void Backward_monotonic_timestamp_fails_closed_as_health_failure()
    {
        var policy = new OfficialWorkerRecyclePolicy(CreateOptions(), StartedTimestamp);

        policy.EvaluateForNextAdmission(
                StartedTimestamp - 1,
                isReady: true,
                isHealthy: true,
                protocolViolation: false,
                HealthyObservation())
            .Should().Be(OfficialWorkerRecycleReason.HealthFailure);
    }

    /// <summary>
    /// 熱路徑在 warm-up 後反覆評估健康且低於門檻的 Worker 時不配置 managed object；
    /// Policy 只讀取 bounded scalar state，不建立集合、例外、字串、Task、Timer 或背景工作。
    /// </summary>
    [Fact]
    public void Healthy_hot_path_evaluation_has_no_steady_state_managed_allocation()
    {
        var policy = new OfficialWorkerRecyclePolicy(CreateOptions(), StartedTimestamp);
        var observation = HealthyObservation();
        for (var index = 0; index < 1_000; index++)
        {
            _ = policy.EvaluateForNextAdmission(
                StartedTimestamp,
                isReady: true,
                isHealthy: true,
                protocolViolation: false,
                observation);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100_000; index++)
        {
            if (policy.EvaluateForNextAdmission(
                    StartedTimestamp,
                    isReady: true,
                    isHealthy: true,
                    protocolViolation: false,
                    observation) != OfficialWorkerRecycleReason.None)
            {
                throw new InvalidOperationException("Healthy recycle evaluation unexpectedly failed.");
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.Should().Be(0);
    }

    /// <summary>建立具明確有限門檻的測試 options；未覆寫欄位保持遠離各案例的觸發值。</summary>
    private static OfficialWorkerRecyclePolicyOptions CreateOptions(
        TimeSpan? maximumWorkerAge = null,
        long maximumCompletedOperations = 10_000,
        long maximumPrivateBytes = 10_000,
        long maximumWorkingSet = 10_000,
        int maximumConsecutiveCompleteWorkerTimeouts = 10)
        => new(
            maximumWorkerAge ?? TimeSpan.FromMinutes(10),
            maximumCompletedOperations,
            maximumPrivateBytes,
            maximumWorkingSet,
            maximumConsecutiveCompleteWorkerTimeouts);

    /// <summary>建立可讀且遠低於測試門檻的 bounded 資源觀測。</summary>
    private static OfficialWorkerResourceObservation HealthyObservation()
        => new(isReadable: true, privateBytes: 10, workingSetBytes: 10);

    /// <summary>建立只供 immutable recycle options 傳遞測試使用的 SDK-free Profile Definition。</summary>
    private static DynamicsProfileDefinition CreateDefinition(
        OfficialWorkerRecyclePolicyOptions recyclePolicyOptions)
        => new(
            profileAlias: "crm91-recycle-test",
            workerProfileGenerationId: "profile-generation-recycle-0001",
            OfficialWorkerVersion.Ce91,
            organizationBaseUri: "https://crm.example.local/Church/",
            workerExecutablePath: Path.Combine(Path.GetTempPath(), "official-worker-recycle-test.exe"),
            workerExecutableSha256: new string('a', 64),
            packageLockId: "test-worker-package-lock-recycle-0001",
            admissionOptions: new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                AggregateMaxInFlight = 2,
                MaximumRuntimeHosts = 1,
                LocalQueueCapacity = 2,
                MaxDispatchEnvelopeBytes = 65_536,
                QueueAdmissionTimeoutSeconds = 5,
                MaxInFlightAndQueuedPerWorkload = 2,
                AdmissionNamespaceId = "recycle-policy-test",
                LeaseNamespaceId = "recycle-policy-test",
                AdmissionEpoch = 1,
                RuntimeHostSlotLeaseTtlSeconds = 120,
                RuntimeHostSlotRenewalIntervalSeconds = 15,
                RuntimeHostSlotExpiryFenceSeconds = 10,
                MaximumOutboundWorkLifetimeSeconds = 30,
                ShutdownDrainTimeoutSeconds = 30,
                RequireDurableHostCoordinator = false
            },
            recyclePolicyOptions: recyclePolicyOptions);
}
