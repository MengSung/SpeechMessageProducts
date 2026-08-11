namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 集中定義目前 Worker IPC wire contract 版本；Supervisor 與 Worker 必須精確相等，
/// 不支援的版本在 payload 執行或資源配置前 fail closed，禁止以猜測方式相容。
/// </summary>
public static class WorkerProtocolVersion
{
    /// <summary>目前唯一支援的 wire protocol 版本。</summary>
    public const int Current = 1;
}
