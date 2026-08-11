using System;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 表示 Supervisor 對指定 Worker process 發出的有界 drain 命令。
/// nonce 將命令綁定單一 process，absolute deadline 防止背景排空無限存活；此 DTO 不持有 Timer、Task、Stream 或取消註冊。
/// </summary>
public sealed class WorkerDrainV1
{
    /// <summary>建立只含 protocol、nonce 與有限期限的 drain envelope。</summary>
    public WorkerDrainV1(
        int protocolVersion,
        string processNonce,
        long deadlineUtcTicks)
    {
        ProtocolVersion = protocolVersion;
        ProcessNonce = processNonce ?? throw new ArgumentNullException(nameof(processNonce));
        DeadlineUtcTicks = deadlineUtcTicks;
    }

    /// <summary>取得必須符合目前 codec 的 protocol version。</summary>
    public int ProtocolVersion { get; }

    /// <summary>取得用來阻止 drain 命令跨 Worker process 套用的 nonce。</summary>
    public string ProcessNonce { get; }

    /// <summary>取得 Worker 必須完成排空或進入強制終止的 UTC absolute deadline ticks。</summary>
    public long DeadlineUtcTicks { get; }
}
