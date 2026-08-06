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
    /// 驗證 shared startup classifier 只用 exception family 將 secure-channel failure 投影成固定 enum。
    /// 故障注入不含 URI、帳密或原始 SDK error；決定性斷言保護診斷不會透過 exception text 洩漏
    /// deployment metadata，同時讓兩個 net48 Worker 使用相同分類規則。
    /// </summary>
    [Fact]
    public void Startup_failure_classifier_maps_secure_channel_web_exception_without_retaining_detail()
    {
        var failure = new System.Net.WebException(
            "test-only secure channel failure",
            System.Net.WebExceptionStatus.SecureChannelFailure);

        var category = OfficialCrmClientStartupFailureClassifier.Classify(failure);

        category.Should().Be(OfficialCrmClientStartupFailureCategory.SecureChannel);
    }

    /// <summary>
    /// 驗證 SDK 在 client 尚未 ready 時完全沒有提供 startup exception，會得到與「存在但未知」不同的
    /// 固定去識別化分類。此 regression 不建立真實 CRM client 或網路連線；決定性斷言保護 operator 能安全
    /// 區分「SDK 未留診斷」與「未知 exception family」，而不把 exception 文字、端點、帳密或堆疊保留到
    /// Worker、IPC 或跨 profile 狀態。
    /// </summary>
    [Fact]
    public void Startup_failure_classifier_maps_absent_sdk_diagnostic_without_retaining_detail()
    {
        var category = OfficialCrmClientStartupFailureClassifier.Classify(exception: null);

        category.Should().Be(OfficialCrmClientStartupFailureCategory.DiagnosticUnavailable);
    }

    /// <summary>
    /// 驗證 SDK 以 framework timeout exception 表示連線建立逾時時，分類器會將其投影為既有 transport
    /// 類別。故障只使用 test-owned exception；決定性斷言保護 timeout 不會落入需要猜測的未知類別，也不會
    /// 將 timeout message 或其他部署資料保留到診斷輸出。
    /// </summary>
    [Fact]
    public void Startup_failure_classifier_maps_framework_timeout_to_transport()
    {
        var category = OfficialCrmClientStartupFailureClassifier.Classify(
            new TimeoutException("test-only timeout"));

        category.Should().Be(OfficialCrmClientStartupFailureCategory.Transport);
    }

    /// <summary>
    /// 驗證 SDK 建構或初始化層以 framework <see cref="InvalidOperationException"/> 表示設定／初始化失敗時，
    /// 會被投影為固定的 SDK initialization 分類。測試不使用真實 profile 或 credential；決定性斷言只保留
    /// 可供下一步診斷的無敏感原因，避免把原始訊息、端點或登入資料輸出。
    /// </summary>
    [Fact]
    public void Startup_failure_classifier_maps_invalid_operation_to_sdk_initialization()
    {
        var category = OfficialCrmClientStartupFailureClassifier.Classify(
            new InvalidOperationException("test-only SDK initialization failure"));

        category.Should().Be(OfficialCrmClientStartupFailureCategory.SdkInitialization);
    }

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
    /// 驗證 SDK client 已建立但固定 WhoAmI identity probe 未通過時，Worker 不得將它混同於一般
    /// SDK client 未就緒。故障注入 client 只公開去識別化 startup state；決定性斷言是固定 exit code、
    /// 不發布 READY，且 client 仍恰好 Dispose 一次，避免診斷分支造成資源或 profile 狀態殘留。
    /// </summary>
    [Fact]
    public async Task RunAsync_identity_probe_not_ready_returns_distinct_sanitized_exit_code()
    {
        var client = new IdentityProbeNotReadyClient();
        var factory = new FakeOfficialCrmClientFactory(client);
        var session = new OfficialWorkerSession(
            factory,
            CreateBootstrap(),
            "9.1",
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

        outcome.Should().Be(OfficialWorkerSessionExitCode.IdentityProbeNotReady);
        client.DisposeCount.Should().Be(1);
        fromWorker.Reader.TryRead(out var readResult).Should().BeFalse();
    }

    /// <summary>
    /// 驗證 SDK 已建立但其固定且去識別化的啟動診斷判定為 authentication failure 時，Session 必須
    /// 回傳獨立 exit code 而非模糊地歸類為一般 client-not-ready。此 fault injection 不含帳號、密碼、
    /// endpoint、token 或 SDK exception；決定性斷言是未發布 READY 且 client 恰好 Dispose 一次，
    /// 讓 operator 可安全區分需要檢查的邊界而不改變 fail-closed 行為。
    /// </summary>
    [Fact]
    public async Task RunAsync_sdk_authentication_not_ready_returns_distinct_sanitized_exit_code()
    {
        var client = new SdkAuthenticationNotReadyClient();
        var factory = new FakeOfficialCrmClientFactory(client);
        var session = new OfficialWorkerSession(
            factory,
            CreateBootstrap(),
            "9.1",
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

        outcome.Should().Be(OfficialWorkerSessionExitCode.AuthenticationNotReady);
        client.DisposeCount.Should().Be(1);
        fromWorker.Reader.TryRead(out var readResult).Should().BeFalse();
    }

    /// <summary>
    /// 驗證 SDK client 尚未 ready 且沒有留下 startup exception 時，Session 回傳固定的「診斷不可用」exit code。
    /// 此 fault injection 不包含 CRM SDK 物件、帳密、端點或原始 exception；決定性斷言是沒有 READY frame、
    /// client 仍恰好釋放一次，且 Supervisor 可以安全區分資料缺失與未知 exception family。
    /// </summary>
    [Fact]
    public async Task RunAsync_sdk_diagnostic_unavailable_returns_distinct_sanitized_exit_code()
    {
        var client = new SdkDiagnosticUnavailableClient();
        var factory = new FakeOfficialCrmClientFactory(client);
        var session = new OfficialWorkerSession(
            factory,
            CreateBootstrap(),
            "9.1",
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

        outcome.Should().Be(OfficialWorkerSessionExitCode.SdkDiagnosticUnavailable);
        client.DisposeCount.Should().Be(1);
        fromWorker.Reader.TryRead(out var readResult).Should().BeFalse();
    }

    /// <summary>
    /// 驗證 SDK startup 診斷映射為初始化 failure 時，Session 回傳獨立 exit code 而不誤導為帳密、TLS 或
    /// transport。替身不包含 CRM 物件、登入資料或 exception detail；決定性斷言是未發布 READY、唯一
    /// cleanup 與固定 exit code，讓後續診斷可停在初始化邊界而不改變 fail-closed 行為。
    /// </summary>
    [Fact]
    public async Task RunAsync_sdk_initialization_not_ready_returns_distinct_sanitized_exit_code()
    {
        var client = new SdkInitializationNotReadyClient();
        var factory = new FakeOfficialCrmClientFactory(client);
        var session = new OfficialWorkerSession(
            factory,
            CreateBootstrap(),
            "9.1",
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

        outcome.Should().Be(OfficialWorkerSessionExitCode.SdkInitializationNotReady);
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

    /// <summary>
    /// 模擬已建立 SDK client、但 Worker-local 固定 identity probe 未通過的啟動狀態。
    /// 此替身不保存或模擬 CRM SDK／credential；它只驗證 Session 對去識別化狀態採用正確 exit code，
    /// 並在不發布 READY 的路徑仍執行唯一 client cleanup。
    /// </summary>
    private sealed class IdentityProbeNotReadyClient :
        IOfficialCrmClient,
        IOfficialCrmClientStartupDiagnostics
    {
        public bool IsReady => false;

        public OfficialCrmClientStartupReadiness StartupReadiness =>
            OfficialCrmClientStartupReadiness.IdentityProbeNotReady;

        public OfficialCrmClientStartupFailureCategory StartupFailureCategory =>
            OfficialCrmClientStartupFailureCategory.None;

        public int DisposeCount { get; private set; }

        public WorkerValue Execute(WorkerRequestV1 request) =>
            throw new InvalidOperationException("The identity-probe-not-ready test client must not execute requests.");

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    /// <summary>
    /// 模擬官方 SDK 在 READY 前只回傳固定 authentication failure 類別的 client。替身刻意不保留
    /// 原始例外、Credential 或 IFD metadata；其唯一用途是保護 Session 對安全診斷 enum 的 exit-code
    /// 映射，並證明診斷分支仍維持一次性 disposal。
    /// </summary>
    private sealed class SdkAuthenticationNotReadyClient :
        IOfficialCrmClient,
        IOfficialCrmClientStartupDiagnostics
    {
        public bool IsReady => false;

        public OfficialCrmClientStartupReadiness StartupReadiness =>
            OfficialCrmClientStartupReadiness.SdkClientNotReady;

        public OfficialCrmClientStartupFailureCategory StartupFailureCategory =>
            OfficialCrmClientStartupFailureCategory.Authentication;

        public int DisposeCount { get; private set; }

        public WorkerValue Execute(WorkerRequestV1 request) =>
            throw new InvalidOperationException(
                "The sdk-authentication-not-ready test client must not execute requests.");

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    /// <summary>
    /// 模擬官方 SDK client 未 ready、但 <c>LastCrmException</c> 不存在或無法安全讀取的情形。
    /// 替身只公開共用 enum，沒有可保留的 exception graph；用來保護 Session 對診斷資料缺失的
    /// fail-closed exit mapping 與一次性 cleanup，不觸及真實 CE、credential 或網路。
    /// </summary>
    private sealed class SdkDiagnosticUnavailableClient :
        IOfficialCrmClient,
        IOfficialCrmClientStartupDiagnostics
    {
        public bool IsReady => false;

        public OfficialCrmClientStartupReadiness StartupReadiness =>
            OfficialCrmClientStartupReadiness.SdkClientNotReady;

        public OfficialCrmClientStartupFailureCategory StartupFailureCategory =>
            OfficialCrmClientStartupFailureCategory.DiagnosticUnavailable;

        public int DisposeCount { get; private set; }

        public WorkerValue Execute(WorkerRequestV1 request) =>
            throw new InvalidOperationException(
                "The sdk-diagnostic-unavailable test client must not execute requests.");

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    /// <summary>
    /// 模擬 SDK 在建立可用 client 前回報固定 initialization 分類的 worker-local client。此替身的唯一目的
    /// 是保護 Session 的 sanitized exit-code mapping；它不配置 network、credential、SDK payload 或任何
    /// 跨 request mutable state，並以 DisposeCount 驗證失敗分支仍有唯一的資源釋放 owner。
    /// </summary>
    private sealed class SdkInitializationNotReadyClient :
        IOfficialCrmClient,
        IOfficialCrmClientStartupDiagnostics
    {
        public bool IsReady => false;

        public OfficialCrmClientStartupReadiness StartupReadiness =>
            OfficialCrmClientStartupReadiness.SdkClientNotReady;

        public OfficialCrmClientStartupFailureCategory StartupFailureCategory =>
            OfficialCrmClientStartupFailureCategory.SdkInitialization;

        public int DisposeCount { get; private set; }

        public WorkerValue Execute(WorkerRequestV1 request) =>
            throw new InvalidOperationException(
                "The sdk-initialization-not-ready test client must not execute requests.");

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
