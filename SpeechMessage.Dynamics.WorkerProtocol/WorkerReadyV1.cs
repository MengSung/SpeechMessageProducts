using System;

namespace SpeechMessage.Dynamics.WorkerProtocol;

/// <summary>
/// 表示 Worker 完成啟動 identity、CE 版本、package lock 與 Profile generation 驗證後的就緒 envelope。
/// 物件只含 bounded scalar；它不攜帶 SDK client、credential、endpoint 或 Session，且 nonce 防止舊 Worker 的 Ready 被新 process 重用。
/// </summary>
public sealed class WorkerReadyV1
{
    /// <summary>建立與單一 Worker process／generation 綁定的 Ready 證據。</summary>
    public WorkerReadyV1(
        int protocolVersion,
        string processNonce,
        OfficialWorkerKind workerKind,
        string packageLockId,
        string profileGenerationId,
        string ceVersion)
    {
        ProtocolVersion = protocolVersion;
        ProcessNonce = processNonce ?? throw new ArgumentNullException(nameof(processNonce));
        WorkerKind = workerKind;
        PackageLockId = packageLockId ?? throw new ArgumentNullException(nameof(packageLockId));
        ProfileGenerationId = profileGenerationId ??
            throw new ArgumentNullException(nameof(profileGenerationId));
        CeVersion = ceVersion ?? throw new ArgumentNullException(nameof(ceVersion));
    }

    /// <summary>取得 wire protocol 版本。</summary>
    public int ProtocolVersion { get; }

    /// <summary>取得綁定目前 Worker process 的 nonce。</summary>
    public string ProcessNonce { get; }

    /// <summary>取得已啟動且版本固定的 Worker 種類。</summary>
    public OfficialWorkerKind WorkerKind { get; }

    /// <summary>取得已驗證 SDK package graph 的 lock 識別碼。</summary>
    public string PackageLockId { get; }

    /// <summary>取得 immutable Profile generation 識別碼。</summary>
    public string ProfileGenerationId { get; }

    /// <summary>取得與 WorkerKind 必須精確相符的 CE major.minor 版本。</summary>
    public string CeVersion { get; }
}
