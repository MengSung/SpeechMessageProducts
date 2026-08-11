using System;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 表示 Worker 回傳給 Supervisor 的 immutable response envelope。
/// 成功只能攜帶一個 bounded typed result；失敗只能攜帶固定 error code，兩種 shape 互斥，避免 raw SDK/credential/Session 資料跨 process 邊界。
/// </summary>
public sealed class WorkerResponseV1
{
    /// <summary>建立 response；完整 shape 仍由 codec 驗證後才能寫入 IPC。</summary>
    public WorkerResponseV1(
        int protocolVersion,
        string processNonce,
        Guid requestId,
        WorkerResponseOutcome outcome,
        WorkerValue? result,
        string? errorCode)
    {
        ProtocolVersion = protocolVersion;
        ProcessNonce = processNonce ?? throw new ArgumentNullException(nameof(processNonce));
        RequestId = requestId;
        Outcome = outcome;
        Result = result;
        ErrorCode = errorCode;
    }

    /// <summary>取得 wire protocol 版本。</summary>
    public int ProtocolVersion { get; }

    /// <summary>取得綁定目前 Worker process 的 nonce。</summary>
    public string ProcessNonce { get; }

    /// <summary>取得對應原始要求的 ID，Supervisor 必須精確比對。</summary>
    public Guid RequestId { get; }

    /// <summary>取得封閉且可去識別化處理的結果分類。</summary>
    public WorkerResponseOutcome Outcome { get; }

    /// <summary>取得僅在成功時存在的 bounded typed 結果。</summary>
    public WorkerValue? Result { get; }

    /// <summary>取得僅在失敗時存在的 allowlisted error code。</summary>
    public string? ErrorCode { get; }

    /// <summary>建立不含 error code 的成功 response。</summary>
    public static WorkerResponseV1 Success(
        int protocolVersion,
        string processNonce,
        Guid requestId,
        WorkerValue result)
    {
        return new WorkerResponseV1(
            protocolVersion,
            processNonce,
            requestId,
            WorkerResponseOutcome.Success,
            result ?? throw new ArgumentNullException(nameof(result)),
            null);
    }

    /// <summary>建立不含 Result 的失敗 response，拒絕將 Success outcome 偽裝為錯誤。</summary>
    public static WorkerResponseV1 Failure(
        int protocolVersion,
        string processNonce,
        Guid requestId,
        WorkerResponseOutcome outcome,
        string errorCode)
    {
        if (outcome == WorkerResponseOutcome.Success)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        return new WorkerResponseV1(
            protocolVersion,
            processNonce,
            requestId,
            outcome,
            null,
            errorCode ?? throw new ArgumentNullException(nameof(errorCode)));
    }
}
