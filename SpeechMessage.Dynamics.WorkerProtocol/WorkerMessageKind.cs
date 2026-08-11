namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 定義 framed IPC envelope 的封閉訊息種類；codec 先驗證 magic 與 frame bounds，再依此分派，
/// 未知種類不得解析成其他訊息或觸發 fallback，避免跨生命週期命令混淆。
/// </summary>
public enum WorkerMessageKind
{
    /// <summary>Supervisor 送往 Worker 的具名 operation 要求。</summary>
    Request = 1,
    /// <summary>Worker 完成 identity／版本驗證後的就緒證據。</summary>
    Ready = 2,
    /// <summary>Worker 回傳的 bounded typed 成功或去識別化失敗結果。</summary>
    Response = 3,
    /// <summary>Supervisor 要求 Worker 停止接單並於 deadline 前排空。</summary>
    Drain = 4
}
