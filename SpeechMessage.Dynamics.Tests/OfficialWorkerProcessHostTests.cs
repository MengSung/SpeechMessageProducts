using FluentAssertions;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 net48 Worker composition host 對 bootstrap、pipe stream 與官方 client 的唯一所有權，
/// 確保啟動失敗與正常 session 結束都不留下 stream、client、timer 或背景工作。
/// </summary>
public sealed class OfficialWorkerProcessHostTests
{
    private const string PackageLockId = "crm91-xrmtooling-9.1.1.65-core-9.0.2.60";

    /// <summary>
    /// 證明 client 未就緒時 session 仍只建立、釋放一次 client，且 process host 確定釋放 pipe stream。
    /// </summary>
    [Fact]
    public async Task RunAsync_disposes_the_pipe_and_client_once_when_not_ready()
    {
        var client = new FakeClient(isReady: false);
        var factory = new FakeClientFactory(client);
        var stream = new TrackingStream();
        var connector = new FakePipeConnector(stream);
        var host = new OfficialWorkerProcessHost(
            OfficialWorkerKind.OfficialCrm91Worker,
            PackageLockId,
            "9.1",
            factory,
            connector,
            OfficialWorkerOperations.CreateRevisionMap());

        var outcome = await host.RunAsync(ValidArguments(), CancellationToken.None);

        outcome.Should().Be(OfficialWorkerSessionExitCode.ClientNotReady);
        connector.ConnectCount.Should().Be(1);
        stream.DisposeCount.Should().Be(1);
        factory.CreateCount.Should().Be(1);
        client.DisposeCount.Should().Be(1);
    }

    /// <summary>
    /// 證明錯誤 executable kind 或 package-lock 在 pipe／profile／credential／client 所有權開始前即 fail closed。
    /// </summary>
    [Theory]
    [InlineData("OfficialCrm82Worker", PackageLockId)]
    [InlineData("OfficialCrm91Worker", "crm91-other-package-lock")]
    public async Task RunAsync_rejects_wrong_executable_identity_before_resource_acquisition(
        string workerKind,
        string packageLockId)
    {
        var client = new FakeClient(isReady: false);
        var factory = new FakeClientFactory(client);
        var connector = new FakePipeConnector(new TrackingStream());
        var host = new OfficialWorkerProcessHost(
            OfficialWorkerKind.OfficialCrm91Worker,
            PackageLockId,
            "9.1",
            factory,
            connector,
            OfficialWorkerOperations.CreateRevisionMap());
        var arguments = ValidArguments();
        arguments[7] = workerKind;
        arguments[9] = packageLockId;

        var action = async () => await host.RunAsync(arguments, CancellationToken.None);

        await action.Should().ThrowAsync<WorkerProtocolException>();
        connector.ConnectCount.Should().Be(0);
        factory.CreateCount.Should().Be(0);
        client.DisposeCount.Should().Be(0);
    }

    private static string[] ValidArguments() =>
    [
        "--pipe", "speechmessage-dynamics-0123456789abcdef",
        "--nonce", "0123456789abcdef0123456789abcdef",
        "--protocol", "1",
        "--worker-kind", "OfficialCrm91Worker",
        "--package-lock", PackageLockId,
        "--profile-generation", "profile-generation-0001"
    ];

    private sealed class FakePipeConnector : IOfficialWorkerPipeConnector
    {
        private readonly Stream _stream;

        public FakePipeConnector(Stream stream)
        {
            _stream = stream;
        }

        public int ConnectCount { get; private set; }

        public Stream Connect(string pipeName)
        {
            pipeName.Should().StartWith("speechmessage-dynamics-");
            ConnectCount++;
            return _stream;
        }
    }

    private sealed class TrackingStream : MemoryStream
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class FakeClientFactory : IOfficialCrmClientFactory
    {
        private readonly IOfficialCrmClient _client;

        public FakeClientFactory(IOfficialCrmClient client)
        {
            _client = client;
        }

        public int CreateCount { get; private set; }

        public IOfficialCrmClient Create(string profileGenerationId)
        {
            CreateCount++;
            return _client;
        }
    }

    private sealed class FakeClient : IOfficialCrmClient
    {
        public FakeClient(bool isReady)
        {
            IsReady = isReady;
        }

        public bool IsReady { get; }

        public int DisposeCount { get; private set; }

        public WorkerValue Execute(WorkerRequestV1 request)
        {
            throw new InvalidOperationException();
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
