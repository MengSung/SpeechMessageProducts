// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/IDynamicsOperationExecutor.cs
// 目的：Gateway 與 Embedded 共用的受控操作執行介面。
//
// 保母教學：
// - 產品業務碼應依賴這個介面（或 Gateway HTTP），不要依賴 WebApi 細節。
// - Gateway 實作 = HTTP 進入點後面的 executor。
// - Embedded 實作 = 同程序 executor。
// - 兩者都只能執行 registry 內的操作。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 受控 Dynamics 操作執行器。
/// </summary>
public interface IDynamicsOperationExecutor
{
    /// <summary>
    /// 執行一個已註冊操作。
    /// </summary>
    Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken = default);
}