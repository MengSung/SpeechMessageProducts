// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/ControlledOperationExecutor.cs
// 目的：把「查 registry -> 驗證參數 -> 呼叫私有 WebApi client」串起來。
//
// 保母教學：
// - Gateway 與 Embedded 都應透過這個 executor，避免兩邊驗證邏輯分叉。
// - 未知操作一律拒絕。
// - 這裡不做業務規則（例如奉獻計算），只做受控 CRM 操作邊界。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 受控操作執行器（Gateway / Embedded 共用核心）。
/// </summary>
public sealed class ControlledOperationExecutor : IDynamicsOperationExecutor
{
    private readonly IDynamicsWebApiClient _webApiClient;

    public ControlledOperationExecutor(IDynamicsWebApiClient webApiClient)
    {
        _webApiClient = webApiClient;
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

        // 參數名稱白名單：不在 registry 的參數直接拒絕，避免偷偷塞 filter/FetchXML。
        var allowed = definition.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = request.Parameters.Keys.Where(k => !allowed.Contains(k)).ToArray();
        if (unknown.Length > 0)
        {
            return OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                $"Unknown parameters: {string.Join(", ", unknown)}");
        }

        return await _webApiClient.ExecuteRegisteredOperationAsync(
            definition,
            request.Parameters,
            cancellationToken).ConfigureAwait(false);
    }
}