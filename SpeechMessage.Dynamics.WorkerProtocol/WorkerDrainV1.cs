using System;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public sealed class WorkerDrainV1
{
    public WorkerDrainV1(
        int protocolVersion,
        string processNonce,
        long deadlineUtcTicks)
    {
        ProtocolVersion = protocolVersion;
        ProcessNonce = processNonce ?? throw new ArgumentNullException(nameof(processNonce));
        DeadlineUtcTicks = deadlineUtcTicks;
    }

    public int ProtocolVersion { get; }

    public string ProcessNonce { get; }

    public long DeadlineUtcTicks { get; }
}
