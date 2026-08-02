using System;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public sealed class WorkerReadyV1
{
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

    public int ProtocolVersion { get; }

    public string ProcessNonce { get; }

    public OfficialWorkerKind WorkerKind { get; }

    public string PackageLockId { get; }

    public string ProfileGenerationId { get; }

    public string CeVersion { get; }
}
