using System;
using System.Collections.Generic;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 在 Worker 執行任何 SDK operation 前，驗證 envelope、process nonce、deadline、operation allowlist 與 active request 唯一性。
/// 驗證採 fail-closed 且不配置外部資源；只有全部條件成立才將 request ID 登錄至呼叫端擁有的 bounded active set。
/// </summary>
public static class WorkerRequestValidator
{
    /// <summary>
    /// 驗證要求並以原子集合操作登錄 active request ID。
    /// 呼叫端仍是 active set 的生命週期 owner，必須在 operation 完成、取消或失敗的 <c>finally</c> 移除 ID，避免容量與重複偵測狀態洩漏。
    /// </summary>
    public static void ValidateAndRegister(
        WorkerRequestV1 request,
        string expectedProcessNonce,
        DateTimeOffset now,
        ISet<string> allowedCapabilityOperationIds,
        ISet<Guid> activeRequestIds,
        WorkerProtocolLimits limits)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (expectedProcessNonce is null)
        {
            throw new ArgumentNullException(nameof(expectedProcessNonce));
        }

        if (allowedCapabilityOperationIds is null)
        {
            throw new ArgumentNullException(nameof(allowedCapabilityOperationIds));
        }

        if (activeRequestIds is null)
        {
            throw new ArgumentNullException(nameof(activeRequestIds));
        }

        WorkerEnvelopeValidator.ValidateRequest(
            request,
            limits ?? throw new ArgumentNullException(nameof(limits)));

        if (request.ProtocolVersion != WorkerProtocolVersion.Current)
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.UnsupportedProtocolVersion,
                "The worker protocol version is unsupported.");
        }

        if (!string.Equals(
                request.ProcessNonce,
                expectedProcessNonce,
                StringComparison.Ordinal))
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.InvalidProcessNonce,
                "The worker process nonce is invalid.");
        }

        if (request.DeadlineUtcTicks <= now.UtcDateTime.Ticks)
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.ExpiredDeadline,
                "The worker request deadline has expired.");
        }

        if (!allowedCapabilityOperationIds.Contains(request.CapabilityOperationId))
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.UnknownOperation,
                "The worker operation is not registered.");
        }

        // Add 是唯一的登錄點；重複 ID 在 SDK dispatch 前拒絕，避免同一要求於同一 Worker
        // 並行執行兩次。移除責任刻意留給上層 operation owner 的 finally，維持對稱生命週期。
        if (!activeRequestIds.Add(request.RequestId))
        {
            throw ProtocolFailure(
                WorkerProtocolFailureCategory.DuplicateRequestId,
                "The worker request identifier is already active.");
        }
    }

    private static WorkerProtocolException ProtocolFailure(
        WorkerProtocolFailureCategory category,
        string message)
    {
        return new WorkerProtocolException(category, message);
    }
}
