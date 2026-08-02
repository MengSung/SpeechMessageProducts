namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 定義官方 CRM Worker 的服務身分來源。身分來源在 Worker 啟動時固定，
/// 不得以產品要求、瀏覽器 Session、LINE 身分或單次 Organization operation 改寫。
/// </summary>
public enum OfficialCrmIdentityMode
{
    /// <summary>
    /// 使用 Worker 行程既有的 Windows 服務身分；設定檔不得同時包含 Credential Manager reference。
    /// </summary>
    HostIdentity = 1,

    /// <summary>
    /// 由 Worker 於本機 Windows Credential Manager 解析核准的非人類服務帳號 reference；
    /// reference 本身不是密碼，實際機密不得進入 XML、IPC、引數或記錄。
    /// </summary>
    WindowsCredentialReference = 2
}
