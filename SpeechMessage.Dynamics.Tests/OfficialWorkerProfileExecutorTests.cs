using System.Collections;
using System.Diagnostics;
using System.Security.Cryptography;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WorkerSupervisor;
using SpeechMessage.Testing;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 以真實子程序與 Windows named pipe 驗證 .NET 10 Supervisor 的最小官方 Worker 路徑。
/// 測試不載入 CRM SDK、不讀 Credential，也不連線到 Dynamics；只證明 process／pipe／
/// discard task／operation lease 在完成與重複 Dispose 後回到零基線。
/// </summary>
[Collection(WorkerTestHostProcessBoundaryCollection.Name)]
public sealed class OfficialWorkerProfileExecutorTests
{
    private static readonly string[] SensitiveChildEnvironmentSentinelNames =
    [
        "DYNAMICS_TEST_SENTINEL",
        "CRM_TEST_SENTINEL",
        "WORKER_CREDENTIAL_SENTINEL",
        "WORKER_PASSWORD_SENTINEL",
        "WORKER_SECRET_SENTINEL",
        "WORKER_TOKEN_SENTINEL",
        "WORKER_AUTH_SENTINEL",
        "WORKER_CONNECTION_SENTINEL",
        "WORKER_SQL_SENTINEL",
        "WORKER_KEY_SENTINEL",
        "WORKER_SESSION_SENTINEL",
        "WORKER_COOKIE_SENTINEL",
        "SPEECHMESSAGE_ARBITRARY_PARENT_SENTINEL"
    ];

    /// <summary>
    /// 驗證連線池驗證操作必須由 Supervisor 依 registry 產生精確 revision，並把已正規化的
    /// logicalProfileId 轉成 bounded WorkerValue。測試 Worker 只接受該 revision 與該參數，
    /// 因此硬編碼 WhoAmI revision、丟棄參數或 request-time fallback 都會使此測試失敗。
    /// </summary>
    [Fact]
    public async Task Validate_connection_preserves_registry_revision_and_typed_parameter()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                "profile-generation-validate-connection",
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        var result = await executor.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = "crm91-test",
                CapabilityOperationId = OperationIds.RuntimePoolValidateConnection,
                WorkloadSubjectId = "worker-supervisor-validate-connection-test",
                Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["logicalProfileId"] = "crm91-test"
                }
            },
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.WhoAmI);
        result.Data.WhoAmI!.OrganizationId.Should().Be(
            Guid.Parse("33333333-3333-3333-3333-333333333333"));

        await executor.DisposeAsync();
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 驗證不合法的 identity-operation parameter count 必須在列舉或複製 caller dictionary 前
    /// fail closed。如此即使非標準呼叫端繞過 ControlPlane preparer，也不能用大型或具副作用的
    /// collection 迫使 Supervisor 配置無界 snapshot；拒絕路徑不會建立 request frame 或寫入 pipe。
    /// </summary>
    [Fact]
    public async Task Invalid_identity_parameter_count_is_rejected_before_parameter_enumeration()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                "profile-generation-prevalidation",
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        var act = () => executor.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = "crm91-test",
                CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                WorkloadSubjectId = "worker-supervisor-prevalidation-test",
                Parameters = new EnumerationForbiddenParameters()
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The official Dynamics worker operation is not permitted.");

        await executor.DisposeAsync();
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 驗證 official Worker child process 只繼承最小 Windows/runtime allowlist，父行程中的
    /// Dynamics、CRM、Credential、Password、Secret、Token、Auth、Connection、SQL、Key、
    /// Session、Cookie 與任意應用程式狀態皆不可見。finally 逐項還原父行程原值，避免測試本身
    /// 造成跨測試 Session／環境污染；成功啟動也同時證明必要 OS runtime 變數仍被保留。
    /// </summary>
    [Fact]
    public async Task Worker_process_environment_is_allowlisted_and_parent_sensitive_state_is_not_inherited()
    {
        var originals = SensitiveChildEnvironmentSentinelNames.ToDictionary(
            name => name,
            name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var name in SensitiveChildEnvironmentSentinelNames)
            {
                Environment.SetEnvironmentVariable(
                    name,
                    "synthetic-sentinel",
                    EnvironmentVariableTarget.Process);
            }

            var executablePath = FindTestWorkerExecutable();
            await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
                CreateOptions(
                    executablePath,
                    "profile-generation-environment-scrub",
                    operationTimeout: TimeSpan.FromSeconds(5),
                    drainTimeout: TimeSpan.FromSeconds(2)),
                CancellationToken.None);

            executor.GetLifecycleSnapshot().IsReady.Should().BeTrue();
            await executor.DisposeAsync();
            AssertFullyRetired(executor.GetLifecycleSnapshot());
        }
        finally
        {
            foreach (var pair in originals)
            {
                Environment.SetEnvironmentVariable(
                    pair.Key,
                    pair.Value,
                    EnvironmentVariableTarget.Process);
            }
        }
    }

    /// <summary>
    /// 驗證 Worker 在 startup deadline 內完全不連線時，失敗結果仍攜帶明確可 Dispose 的 startup
    /// lifecycle owner。Supervisor 必須終止 test-owned PID、等候 stdout/stderr reader terminal，並以
    /// 零 snapshot 證明沒有因 timeout 例外而遺失 Process、Pipe 或背景工作 ownership。
    /// </summary>
    [Fact]
    public async Task Startup_timeout_returns_explicit_owner_after_pid_and_readers_are_retired()
    {
        const string generation = "profile-generation-startup-timeout";
        var evidencePath = GetProcessEvidencePath(generation);
        DeleteEvidenceFile(evidencePath);
        try
        {
            var executablePath = FindTestWorkerExecutable();
            var act = () => OfficialWorkerProfileExecutor.StartAsync(
                CreateOptions(
                    executablePath,
                    generation,
                    operationTimeout: TimeSpan.FromSeconds(5),
                    drainTimeout: TimeSpan.FromSeconds(1),
                    startupTimeout: TimeSpan.FromMilliseconds(150)),
                CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<Exception>();
            thrown.Which.GetType().Name.Should().Be("OfficialWorkerStartupException");
            thrown.Which.InnerException.Should().BeAssignableTo<OperationCanceledException>();
            thrown.Which.Should().BeAssignableTo<IAsyncDisposable>();

            var processId = await ReadCapturedProcessIdAsync(evidencePath);
            await WaitForProcessExitAsync(processId);
            AssertFullyRetired(GetStartupFailureLifecycleSnapshot(thrown.Which));
            await ((IAsyncDisposable)thrown.Which).DisposeAsync();
        }
        finally
        {
            DeleteEvidenceFile(evidencePath);
        }
    }

    /// <summary>
    /// 驗證 READY identity 無效時，原始協定失敗保留在 startup owner 的 InnerException；同一次 bounded
    /// cleanup 會關閉 local readers、等待 reader tasks 並釋放 Process／Pipe，重複 Dispose 仍安全。
    /// </summary>
    [Fact]
    public async Task Invalid_ready_preserves_protocol_failure_and_retires_all_owned_resources()
    {
        var generation = CreateRunUniqueGeneration("profile-generation-invalid-ready-");
        var evidencePath = GetProcessEvidencePath(generation);
        int? processId = null;
        DeleteEvidenceFile(evidencePath);
        try
        {
            var executablePath = FindTestWorkerExecutable();
            var act = () => OfficialWorkerProfileExecutor.StartAsync(
                CreateOptions(
                    executablePath,
                    generation,
                    operationTimeout: TimeSpan.FromSeconds(5),
                    drainTimeout: TimeSpan.FromMilliseconds(150)),
                CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<Exception>();
            thrown.Which.GetType().Name.Should().Be("OfficialWorkerStartupException");
            thrown.Which.InnerException.Should().NotBeNull();
            thrown.Which.InnerException!.GetType().Name.Should().Be("WorkerProtocolException");
            thrown.Which.InnerException.Message.Should().Be(
                "The official Dynamics worker readiness identity is invalid.");
            thrown.Which.Should().BeAssignableTo<IAsyncDisposable>();

            processId = await ReadCapturedProcessIdAsync(evidencePath);
            await WaitForProcessExitAsync(processId.Value);
            AssertFullyRetired(GetStartupFailureLifecycleSnapshot(thrown.Which));

            var owner = (IAsyncDisposable)thrown.Which;
            await owner.DisposeAsync();
            await owner.DisposeAsync();
            AssertFullyRetired(GetStartupFailureLifecycleSnapshot(thrown.Which));
        }
        finally
        {
            if (processId.HasValue)
            {
                await TerminateTestOwnedProcessAsync(processId.Value);
            }

            DeleteEvidenceFile(evidencePath);
        }
    }

    /// <summary>
    /// 驗證正常 worker 的並行 Dispose caller 共用同一 cleanup attempt；完成後 Process、Pipe、reader tasks
    /// 與 operation entrant 均回到零基線。永久 descendant 的 forced-reader-close 行為由獨立 regression 覆蓋。
    /// </summary>
    [Fact]
    public async Task Concurrent_successful_dispose_shares_one_attempt_and_retires_all_resources()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                CreateRunUniqueGeneration("profile-generation-concurrent-dispose-"),
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        var result = await executor.ExecuteAsync(
            CreateWhoAmIRequest("worker-supervisor-output-handle-test"),
            CancellationToken.None);
        result.Succeeded.Should().BeTrue();

        var firstDisposeTask = executor.DisposeAsync().AsTask();
        var concurrentDisposeTask = executor.DisposeAsync().AsTask();
        concurrentDisposeTask.Should().BeSameAs(
            firstDisposeTask,
            because: "並行 cleanup caller 必須共享唯一 lifecycle owner，不能同時操作 Process 與 reader reference");
        await firstDisposeTask;
        await concurrentDisposeTask;
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 證明 Package01 日期區間要求會沿用 registry revision 與 typed scalar，經真實 named-pipe
    /// Worker session 回到 Supervisor 後投影成封閉 fee DTO；任何實作若仍把成功結果硬解成
    /// WhoAmI object，或只允許 identity parameter shape，都會使此測試失敗。
    /// </summary>
    [Fact]
    public async Task Package01_fee_date_range_is_projected_through_supervisor()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                CreateRunUniqueGeneration("profile-generation-package01-success-"),
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        var result = await executor.ExecuteAsync(
            CreatePackage01Request("worker-supervisor-package01-success"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Data!.ResponseKind.Should().Be(OperationResponseKind.Package01FeeRecords);
        result.Data.WhoAmI.Should().BeNull();
        result.Data.FeeRecords.Should().ContainSingle();
        result.Data.FeeRecords![0].Should().BeEquivalentTo(new Package01FeeRecord
        {
            FeeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CreatedOn = new DateTimeOffset(2026, 8, 1, 1, 2, 3, TimeSpan.Zero),
            PayDate = new DateTimeOffset(2026, 8, 2, 4, 5, 6, TimeSpan.Zero),
            Amount = 123.45m,
            PayWayOption = 100000001,
            PayWayLabel = "Credit card",
            CategoryLabel = "Dedication",
            Others = "bounded-note",
            PaidPeriod = "2026-08",
            Name = "FEE-0001"
        });

        await executor.DisposeAsync();
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 證明 Worker 的固定 <c>crm.operation.result-too-large</c> 不會退化成 generic upstream failure，
    /// 也不會把 Worker 原始錯誤字串帶到產品邊界；完成回覆後仍可確定性 drain／dispose。
    /// </summary>
    [Fact]
    public async Task Package01_result_too_large_is_mapped_to_supervisor_error()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                CreateRunUniqueGeneration("profile-generation-package01-result-too-large-"),
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        var result = await executor.ExecuteAsync(
            CreatePackage01Request("worker-supervisor-package01-result-too-large"),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("worker.operation.result-too-large");
        result.ErrorMessage.Should().Be("The official Dynamics worker operation failed.");

        await executor.DisposeAsync();
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 證明合法的 Package01 結果可以超過通用 identity codec 的 256 個 array-item 預設值，
    /// 並仍以 Package01 的 17,604 item operation-specific limit 穿越真實 Worker process boundary。
    /// 這能防止小型 fixture 全綠、實際三十筆以上資料卻在 IPC serialize／deserialize 時失敗。
    /// </summary>
    [Fact]
    public async Task Legal_package01_result_above_default_array_limit_crosses_process_boundary()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                CreateRunUniqueGeneration("profile-generation-package01-large-valid-"),
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        var result = await executor.ExecuteAsync(
            CreatePackage01Request("worker-supervisor-package01-large-valid"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Data!.FeeRecords.Should().HaveCount(30);

        await executor.DisposeAsync();
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// A completed response makes the configured count reason sticky, and the final pre-write guard
    /// rejects the next frame with a sanitized failure before deterministically retiring the executor.
    /// </summary>
    [Fact]
    public async Task Sticky_recycle_reason_rejects_final_frame_write_and_retires_generation()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                CreateRunUniqueGeneration("profile-generation-recycle-count-"),
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2),
                recyclePolicyOptions: new OfficialWorkerRecyclePolicyOptions(
                    maximumWorkerAge: TimeSpan.FromMinutes(10),
                    maximumCompletedOperations: 1,
                    maximumPrivateBytes: 1L << 40,
                    maximumWorkingSet: 1L << 40,
                    maximumConsecutiveCompleteWorkerTimeouts: 10)),
            CancellationToken.None);

        var first = await executor.ExecuteAsync(
            CreateWhoAmIRequest("worker-supervisor-recycle-count-first"),
            CancellationToken.None);

        first.Succeeded.Should().BeTrue();
        executor.RecycleReason.Should().Be(
            OfficialWorkerRecycleReason.MaximumCompletedOperations);
        executor.EvaluateRecycleForNextAdmission().Should().Be(
            OfficialWorkerRecycleReason.MaximumCompletedOperations);

        var rejected = await executor.ExecuteAsync(
            CreateWhoAmIRequest("worker-supervisor-recycle-count-rejected"),
            CancellationToken.None);

        rejected.Succeeded.Should().BeFalse();
        rejected.ErrorCode.Should().Be("worker.operation.recycle-required");
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// A complete worker failure response counts as a completed operation before the next admission.
    /// </summary>
    [Fact]
    public async Task Complete_failure_response_records_completion_before_next_admission()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                CreateRunUniqueGeneration("profile-generation-complete-failure-"),
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2),
                recyclePolicyOptions: new OfficialWorkerRecyclePolicyOptions(
                    maximumWorkerAge: TimeSpan.FromMinutes(10),
                    maximumCompletedOperations: 1,
                    maximumPrivateBytes: 1L << 40,
                    maximumWorkingSet: 1L << 40,
                    maximumConsecutiveCompleteWorkerTimeouts: 10)),
            CancellationToken.None);

        var result = await executor.ExecuteAsync(
            CreateWhoAmIRequest("worker-supervisor-complete-failure-response"),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("worker.operation.upstream-failure");
        executor.RecycleReason.Should().Be(
            OfficialWorkerRecycleReason.MaximumCompletedOperations);
    }

    /// <summary>
    /// Admission evaluation reads only executor-owned process counters and records a sticky threshold reason.
    /// </summary>
    [Fact]
    public async Task Next_admission_evaluation_reads_owned_process_resources_and_sticks_reason()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                CreateRunUniqueGeneration("profile-generation-recycle-memory-"),
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2),
                recyclePolicyOptions: new OfficialWorkerRecyclePolicyOptions(
                    maximumWorkerAge: TimeSpan.FromMinutes(10),
                    maximumCompletedOperations: 10_000,
                    maximumPrivateBytes: 1,
                    maximumWorkingSet: 1L << 40,
                    maximumConsecutiveCompleteWorkerTimeouts: 10)),
            CancellationToken.None);

        executor.EvaluateRecycleForNextAdmission().Should().Be(
            OfficialWorkerRecycleReason.MaximumPrivateBytes);
        executor.RecycleReason.Should().Be(
            OfficialWorkerRecycleReason.MaximumPrivateBytes);
    }

    /// <summary>
    /// 驗證 Worker parent 已退出但 detached descendant 仍持有 inherited stdout/stderr write-end 時，
    /// 並行 Dispose caller 只能共用同一 cleanup attempt，且都收到固定 sanitized failure。Supervisor
    /// 必須維持 NotReady，保留 Process 與未完成 reader task owner，不能以關閉本機 reader 假裝 OS handle
    /// 已回收；測試終止唯一 descendant owner 後，下一次 serialized retry 才可成功歸零。
    /// </summary>
    [Fact]
    public async Task Dispose_retains_process_and_output_owners_until_detached_descendant_closes_handles()
    {
        var generation = CreateRunUniqueGeneration(
            "profile-generation-never-exit-descendant-");
        var evidencePath = GetDescendantEvidencePath(generation);
        OfficialWorkerProfileExecutor? executor = null;
        int? descendantProcessId = null;
        DeleteEvidenceFile(evidencePath);

        try
        {
            var executablePath = FindTestWorkerExecutable();
            var drainTimeout = TimeSpan.FromMilliseconds(300);
            executor = await OfficialWorkerProfileExecutor.StartAsync(
                CreateOptions(
                    executablePath,
                    generation,
                    operationTimeout: TimeSpan.FromSeconds(5),
                    drainTimeout: drainTimeout),
                CancellationToken.None);
            descendantProcessId = await ReadCapturedProcessIdAsync(evidencePath);
            IsProcessRunning(descendantProcessId.Value).Should().BeTrue();

            var result = await executor.ExecuteAsync(
                CreateWhoAmIRequest("worker-supervisor-never-exit-descendant-test"),
                CancellationToken.None);
            result.Succeeded.Should().BeTrue();

            var firstDisposeTask = executor.DisposeAsync().AsTask();
            var concurrentDisposeTask = executor.DisposeAsync().AsTask();
            concurrentDisposeTask.Should().BeSameAs(firstDisposeTask);
            var firstFailure = await Record.ExceptionAsync(async () => await firstDisposeTask);
            var concurrentFailure = await Record.ExceptionAsync(
                async () => await concurrentDisposeTask);

            AssertCleanupFailure(firstFailure);
            AssertCleanupFailure(concurrentFailure);
            var retained = executor.GetLifecycleSnapshot();
            retained.IsReady.Should().BeFalse();
            retained.OwnedProcessCount.Should().Be(1);
            retained.OwnedBackgroundTaskCount.Should().BeGreaterThan(0);
            retained.OwnedOutputReaderCount.Should().BeGreaterThan(0);
            retained.OwnedOutputTaskCount.Should().BeGreaterThan(0);
            retained.OwnedProcessExitWaitCount.Should().Be(0,
                because: "a failed attempt must release its process-exit wait registration before returning");
            IsProcessRunning(descendantProcessId.Value).Should().BeTrue(
                because: "the first failed cleanup must not terminate the detached test-owned descendant");

            await TerminateTestOwnedProcessAsync(descendantProcessId.Value);
            descendantProcessId = null;
            await executor.DisposeAsync();
            AssertFullyRetired(executor.GetLifecycleSnapshot());
        }
        finally
        {
            if (descendantProcessId.HasValue)
            {
                await TerminateTestOwnedProcessAsync(descendantProcessId.Value);
            }

            if (executor is not null)
            {
                await RetryDisposeUntilRetiredAsync(executor);
            }

            DeleteEvidenceFile(evidencePath);
        }
    }

    /// <summary>
    /// 驗證 descendant 持續占用 inherited output handle 時，每次 cleanup timeout 都完整結束自己的
    /// process-exit wait timer／registration，且下一次 retry 建立新的 serialized attempt。每輪並行 caller
    /// 仍共用同一 task；返回後明確 wait-owner counter 必須為零，Process 與 reader owner 則維持可重試，
    /// 不得隨 retry 次數累積 hidden task 或提早假報退休。
    /// </summary>
    [Fact]
    public async Task Repeated_cleanup_timeouts_do_not_accumulate_process_exit_wait_owners()
    {
        var generation = CreateRunUniqueGeneration(
            "profile-generation-never-exit-descendant-");
        var evidencePath = GetDescendantEvidencePath(generation);
        OfficialWorkerProfileExecutor? executor = null;
        int? descendantProcessId = null;
        DeleteEvidenceFile(evidencePath);

        try
        {
            var executablePath = FindTestWorkerExecutable();
            executor = await OfficialWorkerProfileExecutor.StartAsync(
                CreateOptions(
                    executablePath,
                    generation,
                    operationTimeout: TimeSpan.FromSeconds(5),
                    drainTimeout: TimeSpan.FromMilliseconds(150)),
                CancellationToken.None);
            descendantProcessId = await ReadCapturedProcessIdAsync(evidencePath);
            Task? previousAttempt = null;

            for (var attemptIndex = 0; attemptIndex < 3; attemptIndex++)
            {
                var attempt = executor.DisposeAsync().AsTask();
                var concurrentAttempt = executor.DisposeAsync().AsTask();
                concurrentAttempt.Should().BeSameAs(attempt);
                if (previousAttempt is not null)
                {
                    attempt.Should().NotBeSameAs(previousAttempt,
                        because: "a completed failed attempt must be cleared before the next retry");
                }

                var failure = await Record.ExceptionAsync(async () => await attempt);
                AssertCleanupFailure(failure);
                var snapshot = executor.GetLifecycleSnapshot();
                snapshot.IsReady.Should().BeFalse();
                snapshot.OwnedProcessCount.Should().Be(1);
                snapshot.OwnedBackgroundTaskCount.Should().BeGreaterThan(0);
                snapshot.OwnedProcessExitWaitCount.Should().Be(0,
                    because: "each failed attempt must release its exit-wait owner before returning");
                previousAttempt = attempt;
            }

            await TerminateTestOwnedProcessAsync(descendantProcessId.Value);
            descendantProcessId = null;
            await executor.DisposeAsync();
            AssertFullyRetired(executor.GetLifecycleSnapshot());
        }
        finally
        {
            if (descendantProcessId.HasValue)
            {
                await TerminateTestOwnedProcessAsync(descendantProcessId.Value);
            }

            if (executor is not null)
            {
                await RetryDisposeUntilRetiredAsync(executor);
            }

            DeleteEvidenceFile(evidencePath);
        }
    }

    /// <summary>
    /// 驗證單一 Dispose attempt 只消耗一個 monotonic absolute deadline；gate wait、graceful drain、
    /// process-exit confirmation、reader terminal 與 entrant drain 共用剩餘時間，並行 caller 共用同一 task。
    /// </summary>
    [Fact]
    public async Task Dispose_attempt_uses_one_absolute_deadline_and_concurrent_callers_share_it()
    {
        var executablePath = FindTestWorkerExecutable();
        var drainTimeout = TimeSpan.FromMilliseconds(200);
        var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                "profile-generation-hang",
                operationTimeout: TimeSpan.FromSeconds(10),
                drainTimeout: drainTimeout),
            CancellationToken.None);
        var execution = executor.ExecuteAsync(
            CreateWhoAmIRequest("worker-supervisor-absolute-cleanup-deadline-test"),
            CancellationToken.None);

        try
        {
            await WaitForActiveOperationAsync(executor);
            AssertActiveExecutionOwnership(executor.GetLifecycleSnapshot());
            var elapsed = Stopwatch.StartNew();
            var firstDisposeTask = executor.DisposeAsync().AsTask();
            var concurrentDisposeTask = executor.DisposeAsync().AsTask();
            concurrentDisposeTask.Should().BeSameAs(firstDisposeTask);

            var cleanupFailure = await Record.ExceptionAsync(async () => await firstDisposeTask);
            elapsed.Stop();

            if (cleanupFailure is not null)
            {
                cleanupFailure.Should().BeOfType<InvalidOperationException>()
                    .Which.Message.Should().Be(
                        "The official Dynamics worker cleanup did not complete.");
            }

            elapsed.Elapsed.Should().BeLessThan(
                drainTimeout + TimeSpan.FromMilliseconds(175),
                because: "all cleanup stages in one attempt must consume only the remaining absolute deadline");
        }
        finally
        {
            _ = await Record.ExceptionAsync(async () => await execution);
            await RetryDisposeUntilRetiredAsync(executor);
        }
    }

    /// <summary>
    /// 驗證 Worker 即使回傳格式正確且內容可解析，只要 RequestId 並非本次要求的識別碼，
    /// Supervisor 就必須拒絕結果、回傳固定協定錯誤並終止整個 generation，避免跨要求資料誤配。
    /// </summary>
    [Fact]
    public async Task Wrong_response_request_id_returns_protocol_failure_and_retires_generation()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                "profile-generation-wrong-request-id",
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        var result = await executor.ExecuteAsync(
            CreateWhoAmIRequest("worker-supervisor-request-id-test"),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("worker.operation.protocol-failure");
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 驗證 response identity protocol failure 決定後，caller 收到固定 typed result；automatic retirement
    /// 在同一次 bounded cleanup 內歸零，且 fatal frame recycle reason 保持 sticky、同一 request 永不 replay。
    /// </summary>
    [Fact]
    public async Task Protocol_failure_result_is_preserved_and_automatic_retirement_fully_releases_resources()
    {
        var executablePath = FindTestWorkerExecutable();
        var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                CreateRunUniqueGeneration("profile-generation-wrong-request-id-"),
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromMilliseconds(150)),
            CancellationToken.None);

        try
        {
            var result = await executor.ExecuteAsync(
                CreateWhoAmIRequest("worker-supervisor-protocol-outcome-test"),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.ErrorCode.Should().Be("worker.operation.protocol-failure");
            executor.RecycleReason.Should().Be(
                OfficialWorkerRecycleReason.FatalFrameInterruption);
            AssertFullyRetired(executor.GetLifecycleSnapshot());
        }
        finally
        {
            await RetryDisposeUntilRetiredAsync(executor);
        }
    }

    /// <summary>
    /// 驗證 internal operation timeout 保留 typed result 與 sticky supervisor-timeout reason；強制終止後
    /// automatic retirement 在 bounded cleanup 內關閉 readers、釋放 Process／Pipe，且不重送不確定要求。
    /// </summary>
    [Fact]
    public async Task Timeout_result_is_preserved_and_automatic_retirement_fully_releases_resources()
    {
        var executablePath = FindTestWorkerExecutable();
        var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                CreateRunUniqueGeneration("profile-generation-timeout-"),
                operationTimeout: TimeSpan.FromMilliseconds(150),
                drainTimeout: TimeSpan.FromMilliseconds(150)),
            CancellationToken.None);

        try
        {
            var result = await executor.ExecuteAsync(
                CreateWhoAmIRequest("worker-supervisor-timeout-outcome-test"),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.ErrorCode.Should().Be("worker.operation.timeout");
            executor.RecycleReason.Should().Be(
                OfficialWorkerRecycleReason.SupervisorTimeout);
            AssertFullyRetired(executor.GetLifecycleSnapshot());
        }
        finally
        {
            await RetryDisposeUntilRetiredAsync(executor);
        }
    }

    /// <summary>
    /// 驗證 caller cancellation 保持 OperationCanceledException 與原 token 語意，並記錄 sticky
    /// supervisor-cancellation reason；automatic retirement 在 bounded cleanup 內歸零，不產生未觀察 Task。
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_is_preserved_and_automatic_retirement_fully_releases_resources()
    {
        var executablePath = FindTestWorkerExecutable();
        var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                CreateRunUniqueGeneration("profile-generation-cancel-"),
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromMilliseconds(150)),
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        try
        {
            var act = () => executor.ExecuteAsync(
                CreateWhoAmIRequest("worker-supervisor-cancellation-outcome-test"),
                cancellation.Token);

            var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
            thrown.Which.CancellationToken.Should().Be(cancellation.Token);
            executor.RecycleReason.Should().Be(
                OfficialWorkerRecycleReason.SupervisorCancellation);
            AssertFullyRetired(executor.GetLifecycleSnapshot());
        }
        finally
        {
            await RetryDisposeUntilRetiredAsync(executor);
        }
    }

    /// <summary>
    /// 驗證 READY 發布後 Worker 自行退出時，next-admission evaluation fail closed 為 NotReady，而 caller
    /// 仍收到固定 process-exited typed result；automatic retirement 會完整釋放 readers、Pipe 與 Process。
    /// </summary>
    [Fact]
    public async Task Worker_exit_result_is_preserved_and_automatic_retirement_fully_releases_resources()
    {
        var generation = CreateRunUniqueGeneration("profile-generation-worker-exit-");
        var evidencePath = GetProcessEvidencePath(generation);
        int? processId = null;
        DeleteEvidenceFile(evidencePath);
        var executablePath = FindTestWorkerExecutable();
        var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                generation,
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromMilliseconds(150)),
            CancellationToken.None);

        try
        {
            processId = await ReadCapturedProcessIdAsync(evidencePath);
            await WaitForProcessExitAsync(processId.Value);
            executor.EvaluateRecycleForNextAdmission().Should().Be(
                OfficialWorkerRecycleReason.NotReady);
            var result = await executor.ExecuteAsync(
                CreateWhoAmIRequest("worker-supervisor-process-exit-outcome-test"),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.ErrorCode.Should().Be("worker.process.exited");
            AssertFullyRetired(executor.GetLifecycleSnapshot());
        }
        finally
        {
            if (processId.HasValue)
            {
                await TerminateTestOwnedProcessAsync(processId.Value);
            }

            await RetryDisposeUntilRetiredAsync(executor);
            DeleteEvidenceFile(evidencePath);
        }
    }

    /// <summary>
    /// 驗證 caller 取消不可中斷的 Worker 呼叫時，Supervisor 會立即以
    /// OperationCanceledException 回應、強制終止隔離程序，且所有生命週期計數歸零。
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_of_uninterruptible_call_retires_promptly_and_clears_resources()
    {
        var executablePath = FindTestWorkerExecutable();
        await using var executor = await OfficialWorkerProfileExecutor.StartAsync(
            CreateOptions(
                executablePath,
                "profile-generation-hang",
                operationTimeout: TimeSpan.FromSeconds(5),
                drainTimeout: TimeSpan.FromSeconds(2)),
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var elapsed = Stopwatch.StartNew();

        Func<Task> act = () => executor.ExecuteAsync(
            CreateWhoAmIRequest("worker-supervisor-cancellation-test"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        elapsed.Stop();
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 反覆建立、執行及 drain 多個獨立 generation，證明 process、pipe、背景輸出工作與
    /// operation gate 不會跨 iteration 留存；每輪都必須回到同一個零資源基準線。
    /// </summary>
    [Fact]
    public async Task Repeated_start_execute_and_drain_returns_to_zero_every_iteration()
    {
        var executablePath = FindTestWorkerExecutable();
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var executor = await OfficialWorkerProfileExecutor.StartAsync(
                CreateOptions(
                    executablePath,
                    $"profile-generation-loop-{iteration:D2}",
                    operationTimeout: TimeSpan.FromSeconds(5),
                    drainTimeout: TimeSpan.FromSeconds(2)),
                CancellationToken.None);
            try
            {
                var result = await executor.ExecuteAsync(
                    CreateWhoAmIRequest($"worker-supervisor-loop-{iteration:D2}"),
                    CancellationToken.None);
                result.Succeeded.Should().BeTrue();

                await executor.DisposeAsync();
                AssertFullyRetired(executor.GetLifecycleSnapshot());
            }
            finally
            {
                await executor.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// 模擬官方 SDK 呼叫永不返回時，Supervisor 不得留下半個 frame 或繼續使用已失同步的 Pipe；
    /// 它必須在有限 timeout 後淘汰整個 process generation，並把所有 owner counter 歸零。
    /// </summary>
    [Fact]
    public async Task Operation_timeout_forces_process_exit_and_returns_every_resource_to_zero()
    {
        var executablePath = FindTestWorkerExecutable();
        var options = new OfficialWorkerProfileOptions
        {
            ProfileAlias = "crm91-test",
            ProfileGenerationId = "profile-generation-hang",
            WorkerVersion = OfficialWorkerVersion.Ce91,
            WorkerExecutablePath = executablePath,
            WorkerExecutableSha256 = ComputeSha256(executablePath),
            PackageLockId = "test-worker-package-lock-0001",
            StartupTimeout = TimeSpan.FromSeconds(10),
            OperationTimeout = TimeSpan.FromMilliseconds(150),
            DrainTimeout = TimeSpan.FromSeconds(2)
        };
        var executor = await OfficialWorkerProfileExecutor.StartAsync(
            options,
            CancellationToken.None);

        var elapsed = Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = options.ProfileAlias,
                CapabilityOperationId = "runtime.health.whoami",
                WorkloadSubjectId = "worker-supervisor-timeout-test"
            },
            CancellationToken.None);
        elapsed.Stop();

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("worker.operation.timeout");
        elapsed.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(1),
            because: "a timed-out frame is already desynchronized and must force-retire without another drain wait");

        var retired = executor.GetLifecycleSnapshot();
        AssertFullyRetired(retired);

        await executor.DisposeAsync();
    }

    /// <summary>
    /// 啟動 READY、執行一個 SDK-free WhoAmI envelope，再以 Drain 結束子程序；所有生命週期
    /// 計數必須歸零，確保不留下 Session、Pipe、Process Handle、背景讀取或強參考。
    /// </summary>
    [Fact]
    public async Task Start_execute_and_dispose_return_every_owned_resource_to_zero()
    {
        var executablePath = FindTestWorkerExecutable();
        var options = new OfficialWorkerProfileOptions
        {
            ProfileAlias = "crm91-test",
            ProfileGenerationId = "profile-generation-0001",
            WorkerVersion = OfficialWorkerVersion.Ce91,
            WorkerExecutablePath = executablePath,
            WorkerExecutableSha256 = ComputeSha256(executablePath),
            PackageLockId = "test-worker-package-lock-0001",
            StartupTimeout = TimeSpan.FromSeconds(10),
            OperationTimeout = TimeSpan.FromSeconds(10),
            DrainTimeout = TimeSpan.FromSeconds(10)
        };
        var executor = await OfficialWorkerProfileExecutor.StartAsync(
            options,
            CancellationToken.None);

        var active = executor.GetLifecycleSnapshot();
        active.IsReady.Should().BeTrue();
        active.OwnedProcessCount.Should().Be(1);
        active.OwnedPipeCount.Should().Be(1);
        active.OwnedBackgroundTaskCount.Should().Be(2);
        active.OwnedOperationGateCount.Should().Be(1);
        active.OwnedOutputReaderCount.Should().Be(2);
        active.OwnedOutputTaskCount.Should().Be(2);
        active.OwnedOutputCancellationSourceCount.Should().Be(1);
        active.OwnedProcessExitWaitCount.Should().Be(0);
        active.OperationEntrantCount.Should().Be(0);
        active.ActiveOperationCount.Should().Be(0);

        var result = await executor.ExecuteAsync(
            new OperationExecutionRequest
            {
                ProfileAlias = options.ProfileAlias,
                CapabilityOperationId = "runtime.health.whoami",
                WorkloadSubjectId = "worker-supervisor-test"
            },
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Data!.WhoAmI!.UserId.Should().Be(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        result.Data.WhoAmI.BusinessUnitId.Should().Be(
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        result.Data.WhoAmI.OrganizationId.Should().Be(
            Guid.Parse("33333333-3333-3333-3333-333333333333"));

        await executor.DisposeAsync();
        await executor.DisposeAsync();

        var drained = executor.GetLifecycleSnapshot();
        drained.IsReady.Should().BeFalse();
        drained.OwnedProcessCount.Should().Be(0);
        drained.OwnedPipeCount.Should().Be(0);
        drained.OwnedBackgroundTaskCount.Should().Be(0);
        drained.OwnedOperationGateCount.Should().Be(0);
        drained.OwnedOutputReaderCount.Should().Be(0);
        drained.OwnedOutputTaskCount.Should().Be(0);
        drained.OwnedOutputCancellationSourceCount.Should().Be(0);
        drained.OwnedProcessExitWaitCount.Should().Be(0);
        drained.OperationEntrantCount.Should().Be(0);
        drained.ActiveOperationCount.Should().Be(0);
    }

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

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// 建立只含測試允許欄位的 Supervisor 選項；每個 generation 都使用相同已雜湊的
    /// SDK-free 執行檔，且 timeout 由個別案例明確限制。
    /// </summary>
    private static OfficialWorkerProfileOptions CreateOptions(
        string executablePath,
        string profileGenerationId,
        TimeSpan operationTimeout,
        TimeSpan drainTimeout,
        TimeSpan? startupTimeout = null,
        OfficialWorkerRecyclePolicyOptions? recyclePolicyOptions = null)
    {
        return new OfficialWorkerProfileOptions
        {
            ProfileAlias = "crm91-test",
            ProfileGenerationId = profileGenerationId,
            WorkerVersion = OfficialWorkerVersion.Ce91,
            WorkerExecutablePath = executablePath,
            WorkerExecutableSha256 = ComputeSha256(executablePath),
            PackageLockId = "test-worker-package-lock-0001",
            StartupTimeout = startupTimeout ?? TimeSpan.FromSeconds(10),
            OperationTimeout = operationTimeout,
            DrainTimeout = drainTimeout,
            RecyclePolicyOptions = recyclePolicyOptions ?? new OfficialWorkerRecyclePolicyOptions()
        };
    }

    /// <summary>
    /// 建立零參數且固定 operation/profile 的 WhoAmI 要求；workload subject 僅供單次呼叫，
    /// 不得成為 Worker 或 Supervisor 的 cache/session key。
    /// </summary>
    private static OperationExecutionRequest CreateWhoAmIRequest(string workloadSubjectId)
    {
        return new OperationExecutionRequest
        {
            ProfileAlias = "crm91-test",
            CapabilityOperationId = "runtime.health.whoami",
            WorkloadSubjectId = workloadSubjectId
        };
    }

    /// <summary>
    /// 建立固定、SDK-free 且有界的 Package01 日期區間要求。contactName 是相容性欄位，Worker
    /// 必須在 CRM adapter 前移除；其餘三欄才可成為 QueryExpression 的 typed 輸入。
    /// </summary>
    private static OperationExecutionRequest CreatePackage01Request(string workloadSubjectId)
    {
        return new OperationExecutionRequest
        {
            ProfileAlias = "crm91-test",
            CapabilityOperationId = OperationIds.FeeDedicationRetrieveByContactDateRange,
            WorkloadSubjectId = workloadSubjectId,
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                ["contactName"] = "compatibility-only",
                ["startDate"] = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                ["endDate"] = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero)
            }
        };
    }

    /// <summary>
    /// 驗證 cleanup attempt 只公開固定 sanitized failure，不洩漏 Process、路徑、exception detail 或
    /// descendant identity；null 或其他例外類型都代表 lifecycle contract 未被遵守。
    /// </summary>
    private static void AssertCleanupFailure(Exception? failure)
    {
        failure.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be(
                "The official Dynamics worker cleanup did not complete.");
    }

    /// <summary>
    /// 驗證已退休 executor 的所有明確 owner 計數皆回到零，且 readiness 不再公開。
    /// </summary>
    private static void AssertFullyRetired(OfficialWorkerLifecycleSnapshot snapshot)
    {
        snapshot.IsReady.Should().BeFalse();
        snapshot.OwnedProcessCount.Should().Be(0);
        snapshot.OwnedPipeCount.Should().Be(0);
        snapshot.OwnedBackgroundTaskCount.Should().Be(0);
        snapshot.OwnedOperationGateCount.Should().Be(0);
        snapshot.OwnedOutputReaderCount.Should().Be(0);
        snapshot.OwnedOutputTaskCount.Should().Be(0);
        snapshot.OwnedOutputCancellationSourceCount.Should().Be(0);
        snapshot.OwnedProcessExitWaitCount.Should().Be(0);
        snapshot.OperationEntrantCount.Should().Be(0);
        snapshot.ActiveOperationCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證要求正在占用 operation gate 時，Executor 仍明確保有 gate、兩個輸出 reader／task、輸出取消
    /// owner 與唯一 entrant；這些 bounded counter 不暴露 Process、Pipe、Task 或 caller Session reference。
    /// </summary>
    private static void AssertActiveExecutionOwnership(OfficialWorkerLifecycleSnapshot snapshot)
    {
        snapshot.IsReady.Should().BeTrue();
        snapshot.OwnedProcessCount.Should().Be(1);
        snapshot.OwnedPipeCount.Should().Be(1);
        snapshot.OwnedBackgroundTaskCount.Should().Be(2);
        snapshot.OwnedOperationGateCount.Should().Be(1);
        snapshot.OwnedOutputReaderCount.Should().Be(2);
        snapshot.OwnedOutputTaskCount.Should().Be(2);
        snapshot.OwnedOutputCancellationSourceCount.Should().Be(1);
        snapshot.OwnedProcessExitWaitCount.Should().Be(0);
        snapshot.OperationEntrantCount.Should().Be(1);
        snapshot.ActiveOperationCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證自動退休已關閉 readiness／pipe，但仍明確保留 parent Process handle 與至少一個未完成 reader。
    /// 此 snapshot 是 retry ownership 證據；不得以 cleanup failure 取代 caller outcome 或假裝已完全退休。
    /// </summary>
    /// <summary>
    /// 有限等待 hung request 真正取得 operation gate；只讀取 bounded counter，不依賴任意 sleep 推測時序。
    /// </summary>
    private static async Task WaitForActiveOperationAsync(OfficialWorkerProfileExecutor executor)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (executor.GetLifecycleSnapshot().ActiveOperationCount == 1)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        throw new InvalidOperationException("The test worker operation did not become active.");
    }

    /// <summary>
    /// 對仍由測試持有的 executor 執行有限 serialized retry。只接受固定 cleanup failure；每次 attempt
    /// 都由 production owner 完整 await，成功後以 snapshot 證明 Process、Pipe 與 reader 歸零。
    /// </summary>
    private static async Task RetryDisposeUntilRetiredAsync(OfficialWorkerProfileExecutor executor)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await executor.DisposeAsync();
            }
            catch (InvalidOperationException exception) when (
                string.Equals(
                    exception.Message,
                    "The official Dynamics worker cleanup did not complete.",
                    StringComparison.Ordinal))
            {
            }

            var snapshot = executor.GetLifecycleSnapshot();
            if (snapshot.OwnedProcessCount == 0 &&
                snapshot.OwnedPipeCount == 0 &&
                snapshot.OwnedBackgroundTaskCount == 0 &&
                snapshot.OwnedOperationGateCount == 0 &&
                snapshot.OwnedOutputReaderCount == 0 &&
                snapshot.OwnedOutputTaskCount == 0 &&
                snapshot.OwnedOutputCancellationSourceCount == 0 &&
                snapshot.OwnedProcessExitWaitCount == 0 &&
                snapshot.OperationEntrantCount == 0 &&
                snapshot.ActiveOperationCount == 0)
            {
                AssertFullyRetired(snapshot);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        AssertFullyRetired(executor.GetLifecycleSnapshot());
    }

    /// <summary>
    /// 從公開 startup failure contract 取得只含 bounded counter 的 snapshot。反射刻意不讀取
    /// Process、Pipe、Task 或任何 mutable owner；若例外沒有發布此固定 lifecycle seam，測試即 fail closed。
    /// </summary>
    private static OfficialWorkerLifecycleSnapshot GetStartupFailureLifecycleSnapshot(
        Exception startupFailure)
    {
        var method = startupFailure.GetType().GetMethod(
            "GetLifecycleSnapshot",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        method.Should().NotBeNull(
            because: "startup cleanup ownership must expose a bounded lifecycle snapshot without exposing resource handles");
        return method!.Invoke(startupFailure, null)
            .Should().BeOfType<OfficialWorkerLifecycleSnapshot>().Subject;
    }

    /// <summary>取得 test-owned PID evidence 路徑；generation 已受 production identifier 規則限制。</summary>
    private static string GetProcessEvidencePath(string profileGenerationId) =>
        Path.Combine(
            Path.GetTempPath(),
            $"speechmessage-dynamics-worker-{profileGenerationId}.pid");

    /// <summary>建立 run-unique generation，避免平行 test class 共用 Worker 行為或 TEMP evidence。</summary>
    private static string CreateRunUniqueGeneration(string prefix) =>
        prefix + Guid.NewGuid().ToString("N");

    /// <summary>取得只屬於本次 test run 的 detached descendant PID evidence 路徑。</summary>
    private static string GetDescendantEvidencePath(string profileGenerationId) =>
        Path.Combine(
            Path.GetTempPath(),
            $"speechmessage-dynamics-worker-{profileGenerationId}.descendant.pid");

    /// <summary>以 PID 即時確認 test-owned descendant 是否仍存在，不保留 Process handle。</summary>
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
    /// 終止並等待 test-owned descendant，確保 regression 不把永不退出的 Process 留給其他 test class；
    /// PID 只來自本次 run-unique evidence，不接受 caller、Session 或外部輸入。
    /// </summary>
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

    /// <summary>
    /// 有限等待 Worker 建立 PID evidence；內容只允許正整數 PID，避免把任意檔案內容當成 process identity。
    /// </summary>
    private static async Task<int> ReadCapturedProcessIdAsync(string evidencePath)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
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

    /// <summary>
    /// 有限等待 test-owned PID 離開；PID 已在本案例內由 Worker 寫入，方法不列舉或終止其他 process。
    /// </summary>
    private static async Task WaitForProcessExitAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
            // Process 在取得 handle 前已退出，仍是明確的 OS-level cleanup 證據。
        }
    }

    /// <summary>刪除本案例唯一的 PID evidence；不存在時不做任何事。</summary>
    private static void DeleteEvidenceFile(string evidencePath)
    {
        if (File.Exists(evidencePath))
        {
            File.Delete(evidencePath);
        }
    }

    /// <summary>
    /// 代表 Count 已知但禁止列舉的測試 dictionary。若 production 在完成 operation shape
    /// prevalidation 前嘗試建立 snapshot，測試會以固定例外揭露不必要的配置與信任邊界錯誤。
    /// </summary>
    private sealed class EnumerationForbiddenParameters : IReadOnlyDictionary<string, object?>
    {
        public int Count => 1;

        public IEnumerable<string> Keys => throw EnumerationFailure();

        public IEnumerable<object?> Values => throw EnumerationFailure();

        public object? this[string key] => throw EnumerationFailure();

        public bool ContainsKey(string key) => throw EnumerationFailure();

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            throw EnumerationFailure();

        public bool TryGetValue(string key, out object? value)
        {
            value = null;
            throw EnumerationFailure();
        }

        IEnumerator IEnumerable.GetEnumerator() => throw EnumerationFailure();

        private static InvalidOperationException EnumerationFailure() =>
            new("Parameter enumeration must not occur before shape validation.");
    }

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
