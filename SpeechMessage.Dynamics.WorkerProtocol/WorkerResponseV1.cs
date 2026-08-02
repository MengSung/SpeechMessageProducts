using System;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public sealed class WorkerResponseV1
{
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

    public int ProtocolVersion { get; }

    public string ProcessNonce { get; }

    public Guid RequestId { get; }

    public WorkerResponseOutcome Outcome { get; }

    public WorkerValue? Result { get; }

    public string? ErrorCode { get; }

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
