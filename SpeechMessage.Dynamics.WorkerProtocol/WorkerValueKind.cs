namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 定義 Worker IPC 可序列化的封閉 scalar/container 類型。
/// 不包含 byte array、stream、SDK Entity、任意 CLR object 或 credential 類型，避免無界資料與跨 process Session 狀態進入協定。
/// </summary>
public enum WorkerValueKind
{
    /// <summary>沒有 payload 的 null。</summary>
    Null = 0,
    /// <summary>canonical true/false scalar。</summary>
    Boolean = 1,
    /// <summary>invariant Int64 scalar。</summary>
    Int64 = 2,
    /// <summary>invariant G29 decimal scalar。</summary>
    Decimal = 3,
    /// <summary>受 UTF-8 byte 上限約束的文字。</summary>
    String = 4,
    /// <summary>固定 N 格式 Guid。</summary>
    Guid = 5,
    /// <summary>以 UTC ticks 表示的日期時間。</summary>
    UtcDateTime = 6,
    /// <summary>受深度與總 item 上限約束的陣列。</summary>
    Array = 7,
    /// <summary>受深度、總 member 與欄位 denylist 約束的物件。</summary>
    Object = 8
}
