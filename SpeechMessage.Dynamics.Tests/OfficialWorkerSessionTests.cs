using System.IO.Pipelines;
using FluentAssertions;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證官方 Worker session 的 READY、request、drain、錯誤分類與 client cleanup ordering。
/// 測試使用 request-local Pipe 與 fake client；每個案例都 await session 結束並確認唯一 client owner
/// 已釋放，不保留 Credential、Token、Endpoint、Session 或背景工作跨越測試邊界。
/// </summary>
public sealed class OfficialWorkerSessionTests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 證明正常 READY／request／response／drain 流程只建立並釋放一次 client，
    /// request ID 與 pipe stream 均由單一測試生命週期擁有。
    /// </summary>
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

    /// <summary>
    /// 證明 client 尚未 ready 時不發布 READY frame，且即使沒有進入 message loop 仍決定性釋放 client。
    /// </summary>
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

    /// <summary>
    /// 證明 CRM adapter 或 bounded result validator 回報固定 overflow 例外時，Session 只回傳
    /// <c>UpstreamFailure + crm.operation.result-too-large</c>，不洩漏例外內容且仍可正常 drain／dispose。
    /// </summary>
    [Fact]
    public async Task RunAsync_result_limit_exception_maps_to_typed_upstream_failure()
    {
        var response = await ExecuteSingleRequestAsync(
            new FakeOfficialCrmClient(
                isReady: true,
                execute: request =>
                {
                    request.Parameters.Should().NotContainKey("contactName");
                    var emptyPage = WorkerValue.FromArray(Array.Empty<WorkerValue>());
                    return WorkerValue.FromArray(
                        Enumerable.Repeat(emptyPage, 5).ToArray());
                }),
            CreatePackage01Request());

        response.Outcome.Should().Be(WorkerResponseOutcome.UpstreamFailure);
        response.ErrorCode.Should().Be("crm.operation.result-too-large");
    }

    /// <summary>
    /// 證明一般 CRM client 例外維持既有 generic sanitized mapping，避免把所有 upstream failure
    /// 錯誤分類成 result-too-large 或將原始 SDK／端點／認證例外文字送上 IPC。
    /// </summary>
    [Fact]
    public async Task RunAsync_general_exception_remains_generic_upstream_failure()
    {
        var response = await ExecuteSingleRequestAsync(
            new FakeOfficialCrmClient(
                isReady: true,
                execute: _ => throw new InvalidOperationException("sensitive upstream detail")),
            CreatePackage01Request());

        response.Outcome.Should().Be(WorkerResponseOutcome.UpstreamFailure);
        response.ErrorCode.Should().Be("crm.operation.failed");
    }

    /// <summary>
    /// 證明 CRM adapter 回傳錯誤 row/page shape 時不會降級成 generic upstream failure；
    /// Session 必須以 ProtocolFailure 結束並釋放 client，避免繼續信任已違反 wire contract 的 Worker。
    /// </summary>
    [Fact]
    public async Task RunAsync_malformed_package01_result_terminates_as_protocol_failure()
    {
        var client = new FakeOfficialCrmClient(
            isReady: true,
            execute: _ => WorkerValue.FromObject(
                new Dictionary<string, WorkerValue>(StringComparer.Ordinal)));
        var factory = new FakeOfficialCrmClientFactory(client);
        var bootstrap = CreateBootstrap();
        var session = new OfficialWorkerSession(
            factory,
            bootstrap,
            "9.1",
            OfficialWorkerOperations.CreateRevisionMap(),
            Package01FeeWorkerContract.ProtocolLimits,
            () => Now);
        var toWorker = new Pipe();
        var fromWorker = new Pipe();
        await using var workerInput = toWorker.Reader.AsStream();
        await using var supervisorOutput = toWorker.Writer.AsStream();
        await using var workerOutput = fromWorker.Writer.AsStream();
        await using var supervisorInput = fromWorker.Reader.AsStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var codec = new WorkerEnvelopeCodec(Package01FeeWorkerContract.ProtocolLimits);

        var runTask = session.RunAsync(workerInput, workerOutput, timeout.Token);
        _ = codec.DeserializeReady(await WorkerFrameCodec.ReadAsync(
            supervisorInput,
            cancellationToken: timeout.Token));
        await WorkerFrameCodec.WriteAsync(
            supervisorOutput,
            codec.SerializeRequest(CreatePackage01Request()),
            cancellationToken: timeout.Token);

        (await runTask).Should().Be(OfficialWorkerSessionExitCode.ProtocolFailure);
        client.DisposeCount.Should().Be(1);
    }

    private static WorkerRequestV1 CreatePackage01Request() =>
        new(
            WorkerProtocolVersion.Current,
            Nonce,
            Guid.NewGuid(),
            "profile-generation-0001",
            Package01FeeWorkerContract.OperationDefinitionRevision,
            Package01FeeWorkerContract.CapabilityOperationId,
            Now.AddMinutes(1).UtcTicks,
            new Dictionary<string, WorkerValue>(StringComparer.Ordinal)
            {
                ["contactId"] = WorkerValue.FromGuid(
                    Guid.Parse("11111111-1111-1111-1111-111111111111")),
                ["startDate"] = WorkerValue.FromUtcDateTime(Now),
                ["endDate"] = WorkerValue.FromUtcDateTime(Now.AddDays(1)),
                ["contactName"] = WorkerValue.FromString("discard-me")
            });

    private static async Task<WorkerResponseV1> ExecuteSingleRequestAsync(
        FakeOfficialCrmClient client,
        WorkerRequestV1 request)
    {
        var factory = new FakeOfficialCrmClientFactory(client);
        var bootstrap = CreateBootstrap();
        var session = new OfficialWorkerSession(
            factory,
            bootstrap,
            "9.1",
            OfficialWorkerOperations.CreateRevisionMap(),
            Package01FeeWorkerContract.ProtocolLimits,
            () => Now);
        var toWorker = new Pipe();
        var fromWorker = new Pipe();
        await using var workerInput = toWorker.Reader.AsStream();
        await using var supervisorOutput = toWorker.Writer.AsStream();
        await using var workerOutput = fromWorker.Writer.AsStream();
        await using var supervisorInput = fromWorker.Reader.AsStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var codec = new WorkerEnvelopeCodec(Package01FeeWorkerContract.ProtocolLimits);

        var runTask = session.RunAsync(workerInput, workerOutput, timeout.Token);
        _ = codec.DeserializeReady(await WorkerFrameCodec.ReadAsync(
            supervisorInput,
            cancellationToken: timeout.Token));
        await WorkerFrameCodec.WriteAsync(
            supervisorOutput,
            codec.SerializeRequest(request),
            cancellationToken: timeout.Token);
        var response = codec.DeserializeResponse(await WorkerFrameCodec.ReadAsync(
            supervisorInput,
            cancellationToken: timeout.Token));
        await WorkerFrameCodec.WriteAsync(
            supervisorOutput,
            codec.SerializeDrain(new WorkerDrainV1(
                WorkerProtocolVersion.Current,
                Nonce,
                Now.AddMinutes(1).UtcTicks)),
            cancellationToken: timeout.Token);

        (await runTask).Should().Be(OfficialWorkerSessionExitCode.CleanDrain);
        client.DisposeCount.Should().Be(1);
        return response;
    }

    private static WorkerBootstrapArguments CreateBootstrap() =>
        WorkerBootstrapArguments.Parse(
        [
            "--pipe", "speechmessage-dynamics-0123456789abcdef",
            "--nonce", Nonce,
            "--protocol", "1",
            "--worker-kind", "OfficialCrm91Worker",
            "--package-lock", "crm91-xrmtooling-9.1.1.65-core-9.0.2.60",
            "--profile-generation", "profile-generation-0001"
        ]);

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
        private readonly Func<WorkerRequestV1, WorkerValue>? _execute;

        public FakeOfficialCrmClient(
            bool isReady,
            Func<WorkerRequestV1, WorkerValue>? execute = null)
        {
            IsReady = isReady;
            _execute = execute;
        }

        public bool IsReady { get; }

        public int ExecuteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public WorkerValue Execute(WorkerRequestV1 request)
        {
            ExecuteCount++;
            if (_execute is not null)
            {
                return _execute(request);
            }

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
