namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 定義可被 IPC 啟動參數選取的版本固定 Official Worker 種類。
/// 每個值對應獨立 process、SDK package graph 與 credential/runtime，禁止在要求失敗時互相 fallback 或共享 Session。
/// </summary>
public enum OfficialWorkerKind
{
    /// <summary>僅載入已鎖定 CE 8.2 SDK 套件與 Profile 的 Worker。</summary>
    OfficialCrm82Worker = 1,

    /// <summary>僅載入已鎖定 CE 9.1 SDK 套件與 Profile 的 Worker。</summary>
    OfficialCrm91Worker = 2
}
