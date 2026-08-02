using System;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public sealed class WorkerProtocolException : Exception
{
    public WorkerProtocolException(
        WorkerProtocolFailureCategory category,
        string message)
        : base(message)
    {
        Category = category;
    }

    public WorkerProtocolFailureCategory Category { get; }
}
