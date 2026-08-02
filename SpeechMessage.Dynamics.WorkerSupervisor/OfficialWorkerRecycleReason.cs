namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 官方 Worker generation 的 sanitized 回收原因。
/// 數值只描述 supervisor 可安全公開的固定類別，不包含 Profile、Organization、Credential、
/// Process、Pipe、Request、例外訊息或任何 caller/session 資料。
/// </summary>
public enum OfficialWorkerRecycleReason
{
    /// <summary>尚未要求回收，可繼續評估下一次 admission。</summary>
    None = 0,

    /// <summary>Worker 尚未通過或已失去 READY 狀態。</summary>
    NotReady = 1,

    /// <summary>Worker 健康證據失敗，包含不可信的單調時間觀測。</summary>
    HealthFailure = 2,

    /// <summary>Worker 違反已驗證的 IPC protocol contract。</summary>
    ProtocolViolation = 3,

    /// <summary>Private Bytes／Working Set 觀測缺失、無法讀取、為負值或超出可信範圍。</summary>
    ResourceObservationFailure = 4,

    /// <summary>Worker age 已到達部署設定的有限門檻。</summary>
    MaximumWorkerAge = 5,

    /// <summary>完整結束的作業數已到達部署設定的有限門檻。</summary>
    MaximumCompletedOperations = 6,

    /// <summary>Private Bytes 已到達部署設定的有限門檻。</summary>
    MaximumPrivateBytes = 7,

    /// <summary>Working Set 已到達部署設定的有限門檻。</summary>
    MaximumWorkingSet = 8,

    /// <summary>連續完整 Worker timeout response 已到達部署設定的有限門檻。</summary>
    MaximumConsecutiveCompleteWorkerTimeouts = 9,

    /// <summary>Supervisor 在完整 response 前耗盡 operation deadline，必須立即退休 generation。</summary>
    SupervisorTimeout = 10,

    /// <summary>Supervisor-side cancellation 中斷完整 frame，必須立即退休 generation。</summary>
    SupervisorCancellation = 11,

    /// <summary>Frame 讀寫遭致命中斷，IPC 狀態不再可信，必須立即退休 generation。</summary>
    FatalFrameInterruption = 12
}
