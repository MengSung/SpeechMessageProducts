using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerHost;

public sealed class OfficialWorkerSession
{
    private readonly IOfficialCrmClientFactory _clientFactory;
    private readonly WorkerBootstrapArguments _bootstrap;
    private readonly string _ceVersion;
    private readonly IReadOnlyDictionary<string, string> _operationRevisions;
    private readonly ISet<string> _allowedOperationIds;
    private readonly WorkerProtocolLimits _limits;
    private readonly WorkerEnvelopeCodec _codec;
    private readonly Func<DateTimeOffset> _utcNow;

    public OfficialWorkerSession(
        IOfficialCrmClientFactory clientFactory,
        WorkerBootstrapArguments bootstrap,
        string ceVersion,
        IReadOnlyDictionary<string, string> operationRevisions,
        WorkerProtocolLimits limits,
        Func<DateTimeOffset>? utcNow = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _ceVersion = ceVersion ?? throw new ArgumentNullException(nameof(ceVersion));
        _operationRevisions = operationRevisions is null
            ? throw new ArgumentNullException(nameof(operationRevisions))
            : operationRevisions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
        _allowedOperationIds = new HashSet<string>(
            _operationRevisions.Keys,
            StringComparer.Ordinal);
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _codec = new WorkerEnvelopeCodec(limits);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

        var expectedCeVersion = bootstrap.WorkerKind switch
        {
            OfficialWorkerKind.OfficialCrm82Worker => "8.2",
            OfficialWorkerKind.OfficialCrm91Worker => "9.1",
            _ => throw new ArgumentOutOfRangeException(nameof(bootstrap))
        };
        if (!string.Equals(_ceVersion, expectedCeVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The worker CE version does not match the official worker kind.",
                nameof(ceVersion));
        }
    }

    public async Task<OfficialWorkerSessionExitCode> RunAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        IOfficialCrmClient? client = null;
        var exitCode = OfficialWorkerSessionExitCode.UnexpectedFailure;
        try
        {
            client = _clientFactory.Create(_bootstrap.ProfileGenerationId);
            if (client is null || !client.IsReady)
            {
                exitCode = OfficialWorkerSessionExitCode.ClientNotReady;
            }
            else
            {
                await PublishReadyAsync(output, cancellationToken).ConfigureAwait(false);
                exitCode = await RunMessageLoopAsync(
                    client,
                    input,
                    output,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            exitCode = OfficialWorkerSessionExitCode.Cancelled;
        }
        catch (WorkerProtocolException)
        {
            exitCode = OfficialWorkerSessionExitCode.ProtocolFailure;
        }
        catch
        {
            exitCode = OfficialWorkerSessionExitCode.UnexpectedFailure;
        }
        finally
        {
            if (client is not null)
            {
                try
                {
                    client.Dispose();
                }
                catch
                {
                    exitCode = OfficialWorkerSessionExitCode.ClientDisposeFailure;
                }
            }
        }

        return exitCode;
    }

    private async Task PublishReadyAsync(
        Stream output,
        CancellationToken cancellationToken)
    {
        var ready = new WorkerReadyV1(
            WorkerProtocolVersion.Current,
            _bootstrap.ProcessNonce,
            _bootstrap.WorkerKind,
            _bootstrap.PackageLockId,
            _bootstrap.ProfileGenerationId,
            _ceVersion);
        await WorkerFrameCodec.WriteAsync(
            output,
            _codec.SerializeReady(ready),
            _limits.MaximumFrameBytes,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OfficialWorkerSessionExitCode> RunMessageLoopAsync(
        IOfficialCrmClient client,
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        var activeRequestIds = new HashSet<Guid>();
        while (true)
        {
            var payload = await WorkerFrameCodec.ReadAsync(
                input,
                _limits.MaximumFrameBytes,
                cancellationToken).ConfigureAwait(false);
            switch (_codec.DetectMessageKind(payload))
            {
                case WorkerMessageKind.Request:
                    await ExecuteRequestAsync(
                        client,
                        payload,
                        output,
                        activeRequestIds,
                        cancellationToken).ConfigureAwait(false);
                    break;
                case WorkerMessageKind.Drain:
                    var drain = _codec.DeserializeDrain(payload);
                    if (!string.Equals(
                            drain.ProcessNonce,
                            _bootstrap.ProcessNonce,
                            StringComparison.Ordinal) ||
                        drain.DeadlineUtcTicks <= _utcNow().UtcDateTime.Ticks)
                    {
                        return OfficialWorkerSessionExitCode.ProtocolFailure;
                    }

                    return OfficialWorkerSessionExitCode.CleanDrain;
                default:
                    return OfficialWorkerSessionExitCode.ProtocolFailure;
            }
        }
    }

    private async Task ExecuteRequestAsync(
        IOfficialCrmClient client,
        byte[] payload,
        Stream output,
        ISet<Guid> activeRequestIds,
        CancellationToken cancellationToken)
    {
        var request = _codec.DeserializeRequest(payload);
        if (!string.Equals(
                request.ProfileGenerationId,
                _bootstrap.ProfileGenerationId,
                StringComparison.Ordinal) ||
            !_operationRevisions.TryGetValue(
                request.CapabilityOperationId,
                out var expectedRevision) ||
            !string.Equals(
                request.OperationDefinitionRevision,
                expectedRevision,
                StringComparison.Ordinal))
        {
            throw new WorkerProtocolException(
                WorkerProtocolFailureCategory.InvalidEnvelope,
                "The worker request generation or revision is invalid.");
        }

        WorkerRequestValidator.ValidateAndRegister(
            request,
            _bootstrap.ProcessNonce,
            _utcNow(),
            _allowedOperationIds,
            activeRequestIds,
            _limits);

        WorkerResponseV1 response;
        try
        {
            var result = client.Execute(request);
            response = result is null
                ? WorkerResponseV1.Failure(
                    WorkerProtocolVersion.Current,
                    _bootstrap.ProcessNonce,
                    request.RequestId,
                    WorkerResponseOutcome.UpstreamFailure,
                    "crm.operation.empty-result")
                : WorkerResponseV1.Success(
                    WorkerProtocolVersion.Current,
                    _bootstrap.ProcessNonce,
                    request.RequestId,
                    result);
        }
        catch
        {
            response = WorkerResponseV1.Failure(
                WorkerProtocolVersion.Current,
                _bootstrap.ProcessNonce,
                request.RequestId,
                WorkerResponseOutcome.UpstreamFailure,
                "crm.operation.failed");
        }
        finally
        {
            activeRequestIds.Remove(request.RequestId);
        }

        await WorkerFrameCodec.WriteAsync(
            output,
            _codec.SerializeResponse(response),
            _limits.MaximumFrameBytes,
            cancellationToken).ConfigureAwait(false);
    }
}
