using System.IO.Pipelines;
using FluentAssertions;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

public sealed class OfficialWorkerSessionTests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ready_request_response_and_drain_release_the_client_once()
    {
        var client = new FakeOfficialCrmClient(isReady: true);
        var factory = new FakeOfficialCrmClientFactory(client);
        var bootstrap = WorkerBootstrapArguments.Parse(
        [
            "--pipe", "speechmessage-dynamics-0123456789abcdef",
            "--nonce", Nonce,
            "--protocol", "1",
            "--worker-kind", "OfficialCrm91Worker",
            "--package-lock", "crm91-xrmtooling-9.1.1.65-core-9.0.2.60",
            "--profile-generation", "profile-generation-0001"
        ]);
        var operationRevisions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime.health.whoami"] = "operation-revision-0001"
        };
        var session = new OfficialWorkerSession(
            factory,
            bootstrap,
            "9.1",
            operationRevisions,
            WorkerProtocolLimits.Default,
            () => Now);
        var toWorker = new Pipe();
        var fromWorker = new Pipe();
        await using var workerInput = toWorker.Reader.AsStream();
        await using var supervisorOutput = toWorker.Writer.AsStream();
        await using var workerOutput = fromWorker.Writer.AsStream();
        await using var supervisorInput = fromWorker.Reader.AsStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);

        var runTask = session.RunAsync(workerInput, workerOutput, timeout.Token);
        var readyPayload = await WorkerFrameCodec.ReadAsync(
            supervisorInput,
            cancellationToken: timeout.Token);
        var ready = codec.DeserializeReady(readyPayload);
        ready.ProcessNonce.Should().Be(Nonce);
        ready.WorkerKind.Should().Be(OfficialWorkerKind.OfficialCrm91Worker);

        var request = new WorkerRequestV1(
            WorkerProtocolVersion.Current,
            Nonce,
            Guid.NewGuid(),
            bootstrap.ProfileGenerationId,
            operationRevisions["runtime.health.whoami"],
            "runtime.health.whoami",
            Now.AddMinutes(1).UtcTicks,
            new Dictionary<string, WorkerValue>());
        await WorkerFrameCodec.WriteAsync(
            supervisorOutput,
            codec.SerializeRequest(request),
            cancellationToken: timeout.Token);
        var responsePayload = await WorkerFrameCodec.ReadAsync(
            supervisorInput,
            cancellationToken: timeout.Token);
        var response = codec.DeserializeResponse(responsePayload);

        response.Outcome.Should().Be(WorkerResponseOutcome.Success);
        response.RequestId.Should().Be(request.RequestId);
        client.ExecuteCount.Should().Be(1);

        await WorkerFrameCodec.WriteAsync(
            supervisorOutput,
            codec.SerializeDrain(new WorkerDrainV1(
                WorkerProtocolVersion.Current,
                Nonce,
                Now.AddMinutes(1).UtcTicks)),
            cancellationToken: timeout.Token);

        (await runTask).Should().Be(OfficialWorkerSessionExitCode.CleanDrain);
        factory.CreateCount.Should().Be(1);
        client.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_not_ready_disposes_the_client_without_publishing_ready()
    {
        var client = new FakeOfficialCrmClient(isReady: false);
        var factory = new FakeOfficialCrmClientFactory(client);
        var bootstrap = WorkerBootstrapArguments.Parse(
        [
            "--pipe", "speechmessage-dynamics-0123456789abcdef",
            "--nonce", Nonce,
            "--protocol", "1",
            "--worker-kind", "OfficialCrm82Worker",
            "--package-lock", "crm82-xrmtooling-8.2.0.5-core-8.2.0.2",
            "--profile-generation", "profile-generation-0001"
        ]);
        var session = new OfficialWorkerSession(
            factory,
            bootstrap,
            "8.2",
            new Dictionary<string, string>(StringComparer.Ordinal),
            WorkerProtocolLimits.Default,
            () => Now);
        var toWorker = new Pipe();
        var fromWorker = new Pipe();
        await using var workerInput = toWorker.Reader.AsStream();
        await using var workerOutput = fromWorker.Writer.AsStream();

        var outcome = await session.RunAsync(
            workerInput,
            workerOutput,
            CancellationToken.None);

        outcome.Should().Be(OfficialWorkerSessionExitCode.ClientNotReady);
        client.DisposeCount.Should().Be(1);
        fromWorker.Reader.TryRead(out var readResult).Should().BeFalse();
    }

    private sealed class FakeOfficialCrmClientFactory : IOfficialCrmClientFactory
    {
        private readonly IOfficialCrmClient _client;

        public FakeOfficialCrmClientFactory(IOfficialCrmClient client)
        {
            _client = client;
        }

        public int CreateCount { get; private set; }

        public IOfficialCrmClient Create(string profileGenerationId)
        {
            profileGenerationId.Should().NotBeNullOrWhiteSpace();
            CreateCount++;
            return _client;
        }
    }

    private sealed class FakeOfficialCrmClient : IOfficialCrmClient
    {
        public FakeOfficialCrmClient(bool isReady)
        {
            IsReady = isReady;
        }

        public bool IsReady { get; }

        public int ExecuteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public WorkerValue Execute(WorkerRequestV1 request)
        {
            ExecuteCount++;
            return WorkerValue.FromObject(new Dictionary<string, WorkerValue>
            {
                ["userId"] = WorkerValue.FromGuid(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                ["organizationId"] = WorkerValue.FromGuid(Guid.Parse("22222222-2222-2222-2222-222222222222"))
            });
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
