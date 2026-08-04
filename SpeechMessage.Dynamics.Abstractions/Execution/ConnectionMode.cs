// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Execution/ConnectionMode.cs
// 用途：宣告產品在部署時固定選擇的 Dynamics 連線主機模式。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Execution;

/// <summary>
/// 定義產品在啟動時由部署設定固定的 Dynamics 連線模式。
/// 此值不是 HTTP、使用者、Session 或工作負載可覆寫的參數；模式變更必須透過設定替換與
/// 主機重新啟動（或 replace-and-drain）生效，避免既有 Pool、Credential 或 Worker
/// generation 在同一個處理程序內被不同租戶重新解讀。
/// </summary>
public enum ConnectionMode
{
    /// <summary>
    /// 在產品處理程序內承載同一套 Guard、Profile Resolver、Admission 與 Connector Pool。
    /// 此模式供 Visual Studio F5 與明確選擇的獨立產品部署使用；實作必須維持 Profile
    /// generation 的獨立所有權，且產品停止時由 DI container 決定性釋放所有資源。
    /// </summary>
    Embedded = 0,

    /// <summary>
    /// 產品透過 HTTPS 呼叫與產品共同部署的 Dedicated Gateway，例如 localhost 的開發主機。
    /// Gateway 是 Pool、連線、Worker 與憑證的唯一資源所有者；產品端僅保存 Endpoint、
    /// Alias 與受限 capability operation，不能保存 CRM Session 或 Connector 狀態。
    /// </summary>
    DedicatedGateway = 1,

    /// <summary>
    /// 產品透過 HTTPS 呼叫多產品共用的 Central Gateway。多產品可共用同一個 Organization
    /// 容量預算，但每個 Profile/Generation 的可變資源仍由 Gateway 以不可變快照隔離，
    /// 不得以使用者或瀏覽器 Session 做為 Pool key。
    /// </summary>
    CentralGateway = 2
}
