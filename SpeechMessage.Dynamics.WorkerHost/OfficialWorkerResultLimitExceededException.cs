using System;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 表示官方 CRM Worker 已取得完整結果，但該結果超過 operation contract 的固定 page、row、
/// aggregate item 或 canonical byte 上限。例外訊息固定且不包含 CRM payload、端點、認證、
/// Session 或 SDK exception；<see cref="OfficialWorkerSession"/> 只會將它映射成
/// <c>crm.operation.result-too-large</c>，不會截斷或保留部分結果。
/// </summary>
public sealed class OfficialWorkerResultLimitExceededException : Exception
{
    /// <summary>不含任何 caller 或 upstream 資料的固定 sanitized 訊息。</summary>
    public const string FixedMessage =
        "The official Dynamics worker result exceeded its bounded contract.";

    /// <summary>建立一個不攜帶原始結果或 inner exception 的固定 overflow signal。</summary>
    public OfficialWorkerResultLimitExceededException()
        : base(FixedMessage)
    {
    }
}
