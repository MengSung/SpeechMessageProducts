// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs
// 目的：registry 驗證 -> admission permit -> 私有 WebApi client。
//
// 保母教學：
// - Gateway 與 Embedded 都應透過這個 executor。
// - 未知操作 / 非法參數一律拒絕。
// - 外呼 CRM 前必須取得 admission permit，並在 finally 釋放。
// - 這裡不做 per-user CRM session 快取。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 受控操作執行器（Gateway / Embedded 共用核心）。
/// </summary>
public sealed class ControlledOperationExecutor : IDynamicsOperationExecutor
{
    private readonly IProfileExecutionLeaseProvider _leaseProvider;
    private readonly OperationDispatchPreparer _preparer;

    /// <summary>
    /// 保留既有單一 Profile 建構方式。此建構式把固定 Client 與 Admission Manager 包裝成一個合併租約 Provider，
    /// 因此既有呼叫仍只取得一次 Admission Permit，並沿用與 Multi-Profile 相同的取消與確定性釋放路徑。
    /// </summary>
    public ControlledOperationExecutor(
        IDynamicsWebApiClient webApiClient,
        IOrganizationAdmissionManager admissionManager)
        : this(new FixedProfileExecutionLeaseProvider(webApiClient, admissionManager))
    {
    }

    /// <summary>
    /// 建立 Profile-aware 受控操作執行器。Provider 負責 Alias、Admission Queue 與當下 Active Runtime；
    /// Executor 不擁有或 Dispose Provider，避免 Runtime Manager／DI Container 的生命週期被重複終止。
    /// </summary>
    public ControlledOperationExecutor(IProfileExecutionLeaseProvider leaseProvider)
        : this(leaseProvider, OperationDispatchPreparer.Shared)
    {
    }

    /// <summary>
    /// 建立可注入 preparer 的受控 executor；此內部建構式只供測試驗證 pooled buffer 與 cleanup 順序。
    /// Executor 與 preparer 都不擁有 Provider/ArrayPool，只擁有每次呼叫產生的 PreparedOperationDispatch。
    /// </summary>
    internal ControlledOperationExecutor(
        IProfileExecutionLeaseProvider leaseProvider,
        OperationDispatchPreparer preparer)
    {
        _leaseProvider = leaseProvider ?? throw new ArgumentNullException(nameof(leaseProvider));
        _preparer = preparer ?? throw new ArgumentNullException(nameof(preparer));
    }

    /// <summary>
    /// 同步消耗 caller request，在建立任何 async state machine 前完成 registry lookup、plan lookup 與 canonical prepare。
    /// 方法故意不使用 <c>async</c>；若只把 prepare 移到 async method 的第一個 await 前，compiler/JIT
    /// 仍可把 <paramref name="request"/> 保留在 state machine，使 queue wait 延長 JSON/body graph 壽命。
    /// </summary>
    public Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ProfileAlias))
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                "ProfileAlias is required."));
        }

        if (string.IsNullOrWhiteSpace(request.WorkloadSubjectId))
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                "WorkloadSubjectId is required."));
        }

        if (!Package01OperationRegistry.TryGet(request.CapabilityOperationId, out var definition) ||
            definition is null)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                DynamicsErrorCodes.UnknownOperation,
                $"Operation '{request.CapabilityOperationId}' is not registered in Package 0/1."));
        }

        var normalizedAlias = request.ProfileAlias.Trim();
        if (!_leaseProvider.TryGetAdmissionPlan(normalizedAlias, out var admissionPlan) ||
            admissionPlan is null)
        {
            return Task.FromResult(OperationExecutionResult.Failure(
                DynamicsErrorCodes.NotReady,
                "The requested Dynamics profile is not ready."));
        }

        // 安全邊界：必須在 public 非 async frame 內完成準備，之後返回的 Task 不得再捕捉 request。
        if (!_preparer.TryPrepare(
                request,
                definition,
                admissionPlan,
                out var prepared,
                out var preparationError) ||
            prepared is null)
        {
            return Task.FromResult(preparationError ?? OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                "The operation dispatch could not be prepared."));
        }

        return ExecutePreparedAsync(prepared, definition, cancellationToken);
    }

    /// <summary>
    /// 只捕捉 prepared owner、registry definition 與 caller cancellation。方法完全不知道原始 request、
    /// JSON document、HttpContext、principal、session 或 token，因此 admission wait 不會把這些 graph 提升為 queued state。
    /// </summary>
    private async Task<OperationExecutionResult> ExecutePreparedAsync(
        PreparedOperationDispatch prepared,
        OperationDefinition definition,
        CancellationToken cancellationToken)
    {
        try
        {
            var acquisition = await _leaseProvider
                .AcquireAsync(prepared.Envelope, cancellationToken)
                .ConfigureAwait(false);
            if (!acquisition.Succeeded || acquisition.Lease is null)
            {
                return acquisition.Error ?? OperationExecutionResult.Failure(
                    DynamicsErrorCodes.CapacityRejected,
                    "Dynamics profile execution lease was rejected.");
            }

            // await using 區塊必須完全離開後才能進入外層 finally 清除 prepared buffer。
            // 如此 runtime/admission cleanup 若需診斷 envelope 或 correlation，不會觀察到已歸還陣列。
            await using (acquisition.Lease.ConfigureAwait(false))
            {
                using var outboundCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    acquisition.Lease.LeaseLostToken,
                    acquisition.Lease.RetirementToken);
                var remainingToDeadline = prepared.Envelope.DeadlineUtc - DateTimeOffset.UtcNow;
                var maximumLifetime = remainingToDeadline < acquisition.Lease.AdmissionPlan.MaximumOutboundWorkLifetime
                    ? remainingToDeadline
                    : acquisition.Lease.AdmissionPlan.MaximumOutboundWorkLifetime;
                if (maximumLifetime <= TimeSpan.Zero)
                {
                    return OperationExecutionResult.Failure(
                        DynamicsErrorCodes.AdmissionTimeout,
                        "Outbound operation deadline expired before dispatch.");
                }

                outboundCts.CancelAfter(maximumLifetime);
                return await acquisition.Lease.Client.ExecuteRegisteredOperationAsync(
                    definition,
                    prepared.Parameters,
                    outboundCts.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            // 成功、受控失敗、admission 拒絕、caller/lease/retirement 取消、timeout 與 client throw
            // 全部收旂至此；Dispose 是並行 idempotent，且一定晚於 lease cleanup。
            prepared.Dispose();
        }
    }
}
