using System;
using System.Collections.Generic;

namespace SpeechMessage.Dynamics.WorkerProtocol;

public static class WorkerRequestValidator
{
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
