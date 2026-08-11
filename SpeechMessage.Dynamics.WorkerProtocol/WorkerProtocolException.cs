using System;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 表示可安全跨 Worker protocol 邊界分類的固定失敗。
/// 例外只攜帶 allowlisted category 與非敏感訊息，不得包裝 raw SDK response、endpoint、credential、token 或 Session 資料。
/// </summary>
public sealed class WorkerProtocolException : Exception
{
    /// <summary>建立一個具有固定分類的 protocol 失敗。</summary>
    public WorkerProtocolException(
        WorkerProtocolFailureCategory category,
        string message)
        : base(message)
    {
        Category = category;
    }

    /// <summary>取得呼叫端可用來 fail closed 的穩定分類；不得據此切換 Connector 或自動重試寫入。</summary>
    public WorkerProtocolFailureCategory Category { get; }
}
