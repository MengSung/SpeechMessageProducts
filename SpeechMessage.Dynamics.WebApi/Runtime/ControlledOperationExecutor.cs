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
    private readonly IDynamicsWebApiClient _webApiClient;
    private readonly IOrganizationAdmissionManager _admissionManager;

    public ControlledOperationExecutor(
        IDynamicsWebApiClient webApiClient,
        IOrganizationAdmissionManager admissionManager)
    {
        _webApiClient = webApiClient ?? throw new ArgumentNullException(nameof(webApiClient));
        _admissionManager = admissionManager ?? throw new ArgumentNullException(nameof(admissionManager));
    }

    /// <inheritdoc />
    public async Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ProfileAlias))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                "ProfileAlias is required.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkloadSubjectId))
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                "WorkloadSubjectId is required.");
        }

        if (!Package01OperationRegistry.TryGet(request.CapabilityOperationId, out var definition) ||
            definition is null)
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.UnknownOperation,
                $"Operation '{request.CapabilityOperationId}' is not registered in Package 0/1.");
        }

        // 參數名稱白名單：不在 registry 的參數直接拒絕。
        var allowed = definition.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = request.Parameters.Keys.Where(k => !allowed.Contains(k)).ToArray();
        if (unknown.Length > 0)
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                $"Unknown parameters: {string.Join(", ", unknown)}");
        }

        var envelope = new DispatchEnvelope
        {
            ProfileAlias = request.ProfileAlias.Trim(),
            CapabilityOperationId = definition.CapabilityOperationId,
            WorkloadSubjectId = request.WorkloadSubjectId.Trim(),
            TemplateId = definition.TemplateId,
            TemplateHash = definition.TemplateHash,
            IdempotencyKey = request.IdempotencyKey,
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(
                Math.Max(1, _admissionManager.Plan.QueueAdmissionTimeoutSeconds + 30)),
            EstimatedEnvelopeBytes = EstimateEnvelopeBytes(request)
        };

        var admission = await _admissionManager.AcquireAsync(envelope, cancellationToken).ConfigureAwait(false);
        if (!admission.Succeeded || admission.Permit is null)
        {
            return admission.Error ?? OperationExecutionResult.Failure(
                DynamicsErrorCodes.CapacityRejected,
                "Admission was rejected.");
        }

        await using (admission.Permit.ConfigureAwait(false))
        {
            using var outboundCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                admission.Permit.LeaseLostToken);
            var remainingToDeadline = envelope.DeadlineUtc - DateTimeOffset.UtcNow;
            var maximumLifetime = remainingToDeadline < _admissionManager.Plan.MaximumOutboundWorkLifetime
                ? remainingToDeadline
                : _admissionManager.Plan.MaximumOutboundWorkLifetime;
            if (maximumLifetime <= TimeSpan.Zero)
            {
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.AdmissionTimeout,
                    "Outbound operation deadline expired before dispatch.");
            }

            outboundCts.CancelAfter(maximumLifetime);
            return await _webApiClient.ExecuteRegisteredOperationAsync(
                definition,
                request.Parameters,
                outboundCts.Token).ConfigureAwait(false);
        }
    }

    private static int EstimateEnvelopeBytes(OperationExecutionRequest request)
    {
        // 粗估：固定標頭 + 每個參數名/值的字元長度。這只用於 queue 防護，不是序列化真相。
        var total = 256;
        total += (request.ProfileAlias?.Length ?? 0) * 2;
        total += (request.CapabilityOperationId?.Length ?? 0) * 2;
        total += (request.WorkloadSubjectId?.Length ?? 0) * 2;
        total += (request.IdempotencyKey?.Length ?? 0) * 2;

        foreach (var pair in request.Parameters)
        {
            total += (pair.Key?.Length ?? 0) * 2;
            total += pair.Value switch
            {
                null => 0,
                string s => s.Length * 2,
                _ => 64
            };
        }

        return total;
    }
}
