// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ControlledOperationExecutorTests.cs
// 目的：驗證 worker-neutral executor 在 operation registry、admission 與 lease-loss 邊界的行為。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 <see cref="ControlledOperationExecutor"/> 不依賴 HTTP、ADFS 或 Web API transport，
/// 並且在任何 worker 執行前完成 registry/parameter 驗證、取得 admission permit，
/// lease 遺失時取消既有工作並確定釋放 permit。
/// </summary>
public sealed class ControlledOperationExecutorTests
{
    [Fact]
    public async Task Unknown_operation_is_rejected_before_worker_execution()
    {
        var worker = new RecordingExecutor();
        using var admission = new TestAdmissionManager();
        var executor = new ControlledOperationExecutor(worker, admission);

        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = "entity.generic.retrieve.blocked",
            WorkloadSubjectId = "test-workload",
            Parameters = new Dictionary<string, object?>()
        });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UnknownOperation);
        worker.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_parameter_is_rejected_before_worker_execution()
    {
        var worker = new RecordingExecutor();
        using var admission = new TestAdmissionManager();
        var executor = new ControlledOperationExecutor(worker, admission);

        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.FeeDedicationRetrieveByContact,
            WorkloadSubjectId = "test-workload",
            Parameters = new Dictionary<string, object?>
            {
                ["contactId"] = Guid.NewGuid().ToString(),
                ["rawFetchXml"] = "<fetch/>"
            }
        });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.InvalidParameter);
        result.ErrorMessage.Should().Contain("rawFetchXml");
        worker.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Registered_operation_dispatches_through_neutral_worker_executor()
    {
        var worker = new RecordingExecutor();
        using var admission = new TestAdmissionManager();
        var executor = new ControlledOperationExecutor(worker, admission);

        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "test-workload"
        });

        result.Succeeded.Should().BeTrue();
        worker.CallCount.Should().Be(1);
        worker.LastRequest.Should().NotBeNull();
        worker.LastRequest!.ProfileAlias.Should().Be("jesus-dev");
        worker.LastRequest.CapabilityOperationId.Should().Be(OperationIds.RuntimeHealthWhoAmI);
        worker.LastRequest.WorkloadSubjectId.Should().Be("test-workload");
        admission.PermitDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Lease_loss_cancels_in_flight_worker_work_and_releases_permit()
    {
        using var admission = new TestAdmissionManager();
        var worker = new CancellationObservingExecutor();
        var executor = new ControlledOperationExecutor(worker, admission);
        using var callerCts = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "test-workload"
        }, callerCts.Token);

        await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            admission.CancelLease();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));

            execution.IsCompleted.Should().BeTrue(
                "lease loss must cancel worker traffic without waiting for caller cancellation");
            (await execution).Succeeded.Should().BeFalse();
            admission.PermitDisposed.Should().BeTrue();
        }
        finally
        {
            callerCts.Cancel();
        }
    }

    /// <summary>
    /// 建立不含 endpoint、credential 或 transport state 的 admission plan；測試 manager 是 lease token
    /// 與 permit 的唯一 owner，Dispose 會取消並釋放 CTS，避免測試留下 registration 或背景生命週期。
    /// </summary>
    private sealed class TestAdmissionManager : IOrganizationAdmissionManager
    {
        private readonly CancellationTokenSource _leaseLost = new();

        public TestAdmissionManager()
        {
            var options = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("23232323-2323-2323-2323-232323232323"),
                AggregateMaxInFlight = 1,
                MaximumRuntimeHosts = 1,
                AdmissionNamespaceId = "executor-cancel",
                LeaseNamespaceId = "executor-cancel"
            };
            OrganizationAdmissionPlan.TryCreate(
                "https://crm.example.local/Contoso/",
                workerCount: 1,
                maxInFlightPerWorker: 1,
                options,
                out var plan,
                out var error).Should().BeTrue(error?.ErrorMessage);
            Plan = plan!;
        }

        public OrganizationAdmissionPlan Plan { get; }

        public bool PermitDisposed { get; private set; }

        public void CancelLease() => _leaseLost.Cancel();

        public Task EnsureHostSlotAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AdmissionAcquireResult> AcquireAsync(
            DispatchEnvelope envelope,
            CancellationToken cancellationToken)
            => Task.FromResult(AdmissionAcquireResult.Success(new Permit(this, _leaseLost.Token)));

        public AdmissionMetricsSnapshot GetSnapshot() => throw new NotSupportedException();

        public void Dispose() => _leaseLost.Dispose();

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private sealed class Permit : IAdmissionPermit
        {
            private readonly TestAdmissionManager _owner;
            private int _disposed;

            public Permit(TestAdmissionManager owner, CancellationToken leaseLostToken)
            {
                _owner = owner;
                LeaseLostToken = leaseLostToken;
            }

            public Guid CorrelationId { get; } = Guid.NewGuid();

            public long HostFencingToken => 1;

            public CancellationToken LeaseLostToken { get; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.PermitDisposed = true;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// 記錄 ControlPlane 交付的 bounded request，不持有 process、pipe、credential 或 response stream。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        public int CallCount { get; private set; }

        public OperationExecutionRequest? LastRequest { get; private set; }

        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(OperationExecutionResult.Success(data: null));
        }
    }

    /// <summary>
    /// 等待 generation-owned cancellation 的 worker seam；lease-loss token 是唯一終止來源，
    /// 完成後不保留 request、token registration 或背景 task。
    /// </summary>
    private sealed class CancellationObservingExecutor : IDynamicsOperationExecutor
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The cancellation test unexpectedly completed without cancellation.");
            }
            catch (OperationCanceledException)
            {
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.HostSlotUnavailable,
                    "Lease lost.");
            }
        }
    }
}
