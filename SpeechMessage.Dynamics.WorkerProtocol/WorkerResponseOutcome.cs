namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 定義 Worker 可回傳給 Supervisor 的封閉結果語意。
/// 分類不含 raw exception、CRM response 或身分資料；Supervisor 只能依固定政策處理，不能據此切換 Profile／Connector 或盲目重試寫入。
/// </summary>
public enum WorkerResponseOutcome
{
    /// <summary>Operation 完成且 Result 通過 bounded typed validation。</summary>
    Success = 0,
    /// <summary>要求 shape、參數或授權前置條件不合法。</summary>
    InvalidRequest = 1,
    /// <summary>Worker 或其 CRM client 尚未通過就緒驗證。</summary>
    NotReady = 2,
    /// <summary>要求於 bounded deadline 內未完成，結果不可被視為可安全重試。</summary>
    Timeout = 3,
    /// <summary>CRM／SDK 上游失敗，僅提供 sanitized error code。</summary>
    UpstreamFailure = 4,
    /// <summary>IPC frame、nonce、版本或 envelope contract 失敗。</summary>
    ProtocolFailure = 5
}
