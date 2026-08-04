// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Execution/ConnectorKind.cs
// 用途：宣告部署端 Profile 固定使用的 Dynamics Connector 類型。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Execution;

/// <summary>
/// 定義一個不可變 Profile generation 可使用的 Connector。Connector 是部署端設定，絕對
/// 不可由產品請求、HTTP body、Session、Cookie 或使用者輸入選擇；失敗時也不得自動切換。
/// 這個限制讓每個 Pool、Worker 與 Credential 都能有單一且可釋放的生命週期所有者。
/// </summary>
public enum ConnectorKind
{
    /// <summary>
    /// 使用既有 Data8 Organization Service 實作。這是長期受支援的選項之一，會在後續
    /// Phase 以 Profile generation 為界建立獨立、可歸還且具 deterministic disposal 的 Pool。
    /// </summary>
    Data8 = 0,

    /// <summary>
    /// 使用獨立 net48、Microsoft 官方 NuGet 組件固定版本的 CE 8.2 Worker。
    /// 它只能搭配 CE 8.2 Profile，避免 SDK/WCF 組件與其他版本共用記憶體狀態。
    /// </summary>
    OfficialCrm82Worker = 1,

    /// <summary>
    /// 使用獨立 net48、Microsoft 官方 NuGet 組件固定版本的 CE 9.1 Worker。
    /// 它只能搭配 CE 9.1 Profile，Worker 處理程序終止與 IPC dispose 是其資源邊界的一部分。
    /// </summary>
    OfficialCrm91Worker = 2
}
