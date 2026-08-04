using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.ControlPlane.Guard;

/// <summary>
/// 定義 Connector 之前的請求邊界。實作必須同步、無 I/O 且不保留 request 參考；只有先通過
/// 此檢查的 operation 才能進入 Profile Resolver、Admission Permit 或 Pool，避免拒絕路徑
/// 因意外配置 Worker、連線、Timer 或背景工作而發生資源洩漏。
/// </summary>
public interface IRequestGuard
{
    /// <summary>
    /// 檢查受限 contract 是否含保留路由參數、合法 Alias 與已登錄 operation。來源用於確認
    /// Embedded/Dedicated/Central 使用同一規則；回傳結果不持有 request、Session 或 Credential。
    /// </summary>
    RequestGuardResult Inspect(OperationExecutionRequest request, RequestOrigin origin);
}
