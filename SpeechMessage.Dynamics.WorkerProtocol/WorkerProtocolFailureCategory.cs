namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 定義 Worker IPC 可公開的封閉失敗分類。
/// 分類刻意不含 raw payload、身分、路由或 credential，可供 parent fail closed，同時避免錯誤細節造成跨使用者資訊洩漏。
/// </summary>
public enum WorkerProtocolFailureCategory
{
    /// <summary>Frame 宣告零、負值或其他不合法長度。</summary>
    InvalidFrameLength = 1,
    /// <summary>Frame 超過 deployment-owned byte 上限。</summary>
    FrameTooLarge = 2,
    /// <summary>Stream 在宣告長度讀滿前結束。</summary>
    IncompleteFrame = 3,
    /// <summary>單一 frame 後仍存在額外 byte，拒絕黏包或混淆輸入。</summary>
    TrailingFrameData = 4,
    /// <summary>訊息使用非目前支援的 protocol version。</summary>
    UnsupportedProtocolVersion = 5,
    /// <summary>訊息 nonce 不屬於目前 Worker process。</summary>
    InvalidProcessNonce = 6,
    /// <summary>同一 process 內已有相同 active request ID。</summary>
    DuplicateRequestId = 7,
    /// <summary>要求在 dispatch 前已超過 absolute deadline。</summary>
    ExpiredDeadline = 8,
    /// <summary>要求的 capability operation 未在 Worker allowlist。</summary>
    UnknownOperation = 9,
    /// <summary>Envelope shape、型別、識別碼或固定欄位不合法。</summary>
    InvalidEnvelope = 10,
    /// <summary>巢狀深度、項目、成員或字串 byte 數超過有界限制。</summary>
    EnvelopeLimitExceeded = 11
}
