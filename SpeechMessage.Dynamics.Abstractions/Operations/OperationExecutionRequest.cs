// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionRequest.cs
// 目的：產品/Gateway 呼叫受控操作時的請求信封。
//
// 保母教學：
// - 這裡沒有 Connection string、沒有 Entity、沒有 FetchXML。
// - WorkloadSubjectId 應由 Gateway/Embedded 從已驗證身份推導，不要盲信客戶端字串。
// - Parameters 只能是命名參數，且必須符合 registry。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 執行受控 Dynamics 操作的請求。
/// </summary>
public sealed class OperationExecutionRequest
{
    /// <summary>
    /// 目標 profile 別名，例如 jesus-prod。
    /// </summary>
    public required string ProfileAlias { get; init; }

    /// <summary>
    /// 已註冊的 capabilityOperationId。
    /// </summary>
    public required string CapabilityOperationId { get; init; }

    /// <summary>
    /// 工作負載主體（伺服器推導）。例如產品服務帳戶/應用識別。
    /// 不可直接等於 LINE user id 當 CRM session key。
    /// </summary>
    public required string WorkloadSubjectId { get; init; }

    /// <summary>
    /// 命名參數字典。值應可 JSON 序列化。
    /// </summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// 寫入類操作需要的冪等鍵。Package 1 目前全是 read，可為 null。
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
