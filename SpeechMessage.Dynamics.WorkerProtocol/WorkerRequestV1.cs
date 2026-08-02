using System;
using System.Collections.Generic;
using System.Linq;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public sealed class WorkerRequestV1
{
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
        Parameters = parameters is null
            ? throw new ArgumentNullException(nameof(parameters))
            : parameters.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
    }

    public int ProtocolVersion { get; }

    public string ProcessNonce { get; }

    public Guid RequestId { get; }

    public string ProfileGenerationId { get; }

    public string OperationDefinitionRevision { get; }

    public string CapabilityOperationId { get; }

    public long DeadlineUtcTicks { get; }

    public IReadOnlyDictionary<string, WorkerValue> Parameters { get; }
}
