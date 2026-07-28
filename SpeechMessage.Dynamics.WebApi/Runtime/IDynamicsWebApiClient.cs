// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsWebApiClient.cs
// 目的：私有 Web API client 介面。
//
// 保母教學：
// - 這一層真正打 OData function / FetchXML template。
// - 產品程式不應直接注入這個介面；請走 IDynamicsOperationExecutor。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 私有 Dynamics Web API client。
/// </summary>
public interface IDynamicsWebApiClient
{
    /// <summary>
    /// 執行 WhoAmI 健康/身分檢查。
    /// </summary>
    Task<OperationExecutionResult> WhoAmIAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 執行已驗證的 OperationDefinition。
    /// </summary>
    Task<OperationExecutionResult> ExecuteRegisteredOperationAsync(
        OperationDefinition definition,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}
