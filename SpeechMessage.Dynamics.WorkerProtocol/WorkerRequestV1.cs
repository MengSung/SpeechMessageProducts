using System;
using System.Collections.Generic;
using System.Linq;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 表示 Supervisor 經 framed IPC 傳入 Worker 的 immutable、bounded、具名 operation 要求。
/// 建構時複製 parameter dictionary，避免呼叫端在驗證後競態修改內容；要求不允許 credential、endpoint、Session 或任意 SDK 型別。
/// </summary>
public sealed class WorkerRequestV1
{
    /// <summary>
    /// 建立與單一 process nonce、Profile generation、operation revision 與 absolute deadline 綁定的要求快照。
    /// </summary>
    public WorkerRequestV1(
        int protocolVersion,
        string processNonce,
        Guid requestId,
        string profileGenerationId,
        string operationDefinitionRevision,
        string capabilityOperationId,
        long deadlineUtcTicks,
        IReadOnlyDictionary<string, WorkerValue> parameters)
    {
        ProtocolVersion = protocolVersion;
        ProcessNonce = processNonce ?? throw new ArgumentNullException(nameof(processNonce));
        RequestId = requestId;
        ProfileGenerationId = profileGenerationId ??
            throw new ArgumentNullException(nameof(profileGenerationId));
        OperationDefinitionRevision = operationDefinitionRevision ??
            throw new ArgumentNullException(nameof(operationDefinitionRevision));
        CapabilityOperationId = capabilityOperationId ??
            throw new ArgumentNullException(nameof(capabilityOperationId));
        DeadlineUtcTicks = deadlineUtcTicks;
        // 立即以 ordinal key 複製有限參數，確保 validate/serialize/execute 看到同一快照；
        // 不持有呼叫端 mutable dictionary，避免跨執行緒競態或上一要求狀態滲入下一要求。
        Parameters = parameters is null
            ? throw new ArgumentNullException(nameof(parameters))
            : parameters.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
    }

    /// <summary>取得 wire protocol 版本。</summary>
    public int ProtocolVersion { get; }

    /// <summary>取得綁定目前 Worker process 的 nonce。</summary>
    public string ProcessNonce { get; }

    /// <summary>取得目前 process 內唯一且只在 active window 存活的要求識別碼。</summary>
    public Guid RequestId { get; }

    /// <summary>取得 immutable Profile generation；不得由產品 request 自選。</summary>
    public string ProfileGenerationId { get; }

    /// <summary>取得 server-owned operation definition revision，用來拒絕 stale contract。</summary>
    public string OperationDefinitionRevision { get; }

    /// <summary>取得 allowlisted capability operation ID，不是任意 CRM request 名稱。</summary>
    public string CapabilityOperationId { get; }

    /// <summary>取得 dispatch 前必須檢查的 UTC absolute deadline ticks。</summary>
    public long DeadlineUtcTicks { get; }

    /// <summary>取得建構時複製的 bounded typed parameter 快照。</summary>
    public IReadOnlyDictionary<string, WorkerValue> Parameters { get; }
}
