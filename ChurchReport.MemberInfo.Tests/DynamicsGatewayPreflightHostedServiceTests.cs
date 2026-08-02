using System.Net;
using System.Text;
using System.Text.Json;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.Gateway;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 ChurchReport 啟動階段的 Local／Central Gateway WhoAmI preflight。
/// 測試將 HTTP 限制在記憶體內的 <see cref="HttpMessageHandler"/>，不接觸真實 Gateway、CRM 或秘密；
/// 主要保護 feature flag 的嚴格 no-op、設定 fail-closed、正式 executor pipeline、caller cancellation、
/// 內部逾時與 spoof identity header 禁止等啟動信任邊界。
/// </summary>
public sealed class DynamicsGatewayPreflightHostedServiceTests
{
    /// <summary>
    /// 驗證 flag=false 時 StartAsync 在解析 process host、executor、provider 或 HttpClient 前即返回。
    /// fake host 的 invocation count 是主要 assertion；若被呼叫，代表安全預設已不再是零配置、零網路、零資源。
    /// </summary>
    [Fact]
    public async Task Flag_false_is_a_strict_no_op_before_executor_or_http_creation()
    {
        var host = new RecordingProcessHost(_ =>
            throw new InvalidOperationException("flag=false 不得建立 executor。"));
        var service = CreateService(CreateConfiguration(enabled: false), host);

        await service.StartAsync(CancellationToken.None);

        host.GatewayExecutorRequests.Should().Be(0);
    }

    /// <summary>
    /// 驗證 Package 1 啟用時拒絕 Embedded，且在解析 Gateway executor、HttpClient 或任何其他 process-owned
    /// 資源前 fail closed。ChurchReport 僅能透過已部署的 Gateway 呼叫官方 worker，不能把 Embedded 當成
    /// 本機 transport 或 fallback。
    /// </summary>
    [Fact]
    public async Task Embedded_mode_fails_closed_before_gateway_executor_resolution()
    {
        var host = new RecordingProcessHost(_ =>
            throw new InvalidOperationException("Embedded mode 不得建立 Gateway executor。"));
        var service = CreateService(
            CreateConfiguration(enabled: true, executionMode: "Embedded"),
            host);

        var action = () => service.StartAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Gateway*");
        host.GatewayExecutorRequests.Should().Be(0);
    }

    /// <summary>
    /// 注入架構標籤 LocalGateway 作為非法 enum 值，驗證 bind/preflight 在解析 executor 前 fail-closed。
    /// Central／Local 只能由 Gateway endpoint 區分，不得被靜默降級成 enum 預設 Gateway；主要 assertion 是
    /// 固定設定錯誤訊息與零 executor invocation，確保錯誤字串不會觸發任何 provider／HTTP ownership。
    /// </summary>
    [Fact]
    public async Task Unknown_execution_mode_fails_closed_before_executor_resolution()
    {
        var host = new RecordingProcessHost(_ =>
            throw new InvalidOperationException("非法 execution mode 不得建立 executor。"));
        var service = CreateService(
            CreateConfiguration(enabled: true, executionMode: "LocalGateway"),
            host);

        var action = () => service.StartAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*LocalGateway*DynamicsExecutionMode*");
        host.GatewayExecutorRequests.Should().Be(0);
    }

    /// <summary>
    /// 以不安全的 HTTP endpoint 注入設定錯誤，驗證 host StartAsync 立即觸發 ProductClient 的正式
    /// ValidateOnStart／IOptions 驗證並 fail-closed。測試不配置可用 handler，因此若設定驗證被延後，
    /// 將暴露為錯誤的啟動成功或網路企圖，而不是可辨識的 options failure。
    /// </summary>
    [Fact]
    public async Task Enabled_gateway_with_invalid_options_fails_during_host_start()
    {
        var host = new DonationDynamicsAccessProcessHost();
        var service = CreateService(
            CreateConfiguration(enabled: true, endpoint: "http://localhost:7244/"),
            host);

        var action = () => service.StartAsync(CancellationToken.None);

        await action.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage("*absolute HTTPS URI*");
        await host.DisposeAsync();
    }

    /// <summary>
    /// 注入 Gateway 回傳的正式失敗 envelope，驗證 WhoAmI 失敗阻止 host ready，且例外不得回顯
    /// caller-controlled／upstream-controlled 的錯誤內容。主要 assertion 是 StartAsync 丟出固定、已消毒的
    /// fail-closed 例外，而 executor 只被呼叫一次。
    /// </summary>
    [Fact]
    public async Task WhoAmI_failure_prevents_startup_without_echoing_upstream_details()
    {
        var executor = new DelegateExecutor((_, _) => Task.FromResult(
            OperationExecutionResult.Failure(
                DynamicsErrorCodes.UpstreamFailure,
                "不得回顯的上游內容 https://crm.example/ token=secret")));
        var host = new RecordingProcessHost(_ => executor);
        var service = CreateService(CreateConfiguration(enabled: true), host);

        var action = () => service.StartAsync(CancellationToken.None);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("WhoAmI preflight");
        exception.Which.Message.Should().NotContain("crm.example");
        exception.Which.Message.Should().NotContain("secret");
        executor.Invocations.Should().Be(1);
    }

    /// <summary>
    /// 使用永不自行完成、但會遵守 cancellation token 的 executor 注入逾時，驗證 preflight 的 bounded
    /// owner 會取消下游並轉成明確 <see cref="TimeoutException"/>。這保護啟動不會永久卡住，也確認 timeout
    /// cleanup 不依賴背景 timer 或 fire-and-forget 工作繼續持有 provider／HttpClient。
    /// </summary>
    [Fact]
    public async Task WhoAmI_timeout_prevents_startup_with_an_explicit_timeout_failure()
    {
        var executor = DelegateExecutor.CreateBlocking();
        var host = new RecordingProcessHost(_ => executor);
        var service = CreateService(
            CreateConfiguration(enabled: true),
            host,
            timeout: TimeSpan.FromMilliseconds(30));

        var action = () => service.StartAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*WhoAmI preflight*");
        executor.Invocations.Should().Be(1);
    }

    /// <summary>
    /// 注入 host shutdown cancellation，驗證 StartAsync 保留 caller 的取消語意，不可誤報為 Gateway timeout
    /// 或吞掉取消後繼續啟動。executor 以同一 token 停止，確保沒有孤兒背景工作留住 request／provider state。
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_is_preserved_instead_of_being_reported_as_timeout()
    {
        var executor = DelegateExecutor.CreateBlocking();
        var host = new RecordingProcessHost(_ => executor);
        var service = CreateService(
            CreateConfiguration(enabled: true),
            host,
            timeout: TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var action = () => service.StartAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        executor.Invocations.Should().Be(1);
    }

    /// <summary>
    /// 以真正的 <see cref="GatewayDynamicsOperationExecutor"/> 與記憶體 handler 驗證 preflight 使用
    /// <see cref="OperationIds.RuntimeHealthWhoAmI"/> 正式路由。handler 檢查產品沒有加入 X-Principal／
    /// X-Workload spoof header，JSON 也沒有 workloadSubjectId；身份必須由 Gateway 的已驗證 server principal 推導。
    /// </summary>
    [Fact]
    public async Task WhoAmI_uses_the_product_executor_pipeline_without_spoof_identity_headers()
    {
        HttpRequestMessage? seenRequest = null;
        string? seenBody = null;
        using var httpClient = new HttpClient(new StubHandler(async (request, _) =>
        {
            seenRequest = request;
            seenBody = await request.Content!.ReadAsStringAsync();
            var payload = JsonSerializer.Serialize(new
            {
                succeeded = true,
                data = OperationResponseData.ForWhoAmI(
                    OperationIds.RuntimeHealthWhoAmI,
                    "9.1",
                    new WhoAmIResponseData { UserId = Guid.Empty })
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }));
        var executor = CreateGatewayExecutor(httpClient);
        var host = new RecordingProcessHost(_ => executor);
        var service = CreateService(CreateConfiguration(enabled: true), host);

        await service.StartAsync(CancellationToken.None);

        host.GatewayExecutorRequests.Should().Be(1);
        seenRequest.Should().NotBeNull();
        seenRequest!.RequestUri!.AbsolutePath.Should().Be(
            "/v1/organizations/jesus-prod/operations/runtime.health.whoami");
        seenRequest.Headers.Contains("X-Principal").Should().BeFalse();
        seenRequest.Headers.Contains("X-Workload").Should().BeFalse();
        using var body = JsonDocument.Parse(seenBody!);
        body.RootElement.TryGetProperty("workloadSubjectId", out _).Should().BeFalse();
    }

    /// <summary>
    /// 建立 production hosted service；三參數路徑模擬 DI，四參數 timeout 僅縮短故障注入等待。
    /// </summary>
    /// <param name="configuration">不含秘密的測試設定。</param>
    /// <param name="host">記錄 executor 解析次數的 process host。</param>
    /// <param name="timeout">選用的 bounded preflight 逾時。</param>
    /// <returns>尚未啟動且未擁有第二個 HttpClient 的 hosted service。</returns>
    private static DynamicsGatewayPreflightHostedService CreateService(
        IConfiguration configuration,
        IDonationDynamicsAccessProcessHost host,
        TimeSpan? timeout = null)
    {
        return timeout.HasValue
            ? new DynamicsGatewayPreflightHostedService(
                configuration,
                host,
                NullLogger<DynamicsGatewayPreflightHostedService>.Instance,
                timeout.Value)
            : new DynamicsGatewayPreflightHostedService(
                configuration,
                host,
                NullLogger<DynamicsGatewayPreflightHostedService>.Instance);
    }

    /// <summary>
    /// 建立只含 Gateway 公開產品欄位的記憶體設定。測試 endpoint 為 localhost 或保留 internal 名稱，
    /// 不含 CRM URL、credential、token、authorization header 或 transport 選擇。
    /// </summary>
    private static IConfiguration CreateConfiguration(
        bool enabled,
        string executionMode = "Gateway",
        string endpoint = "https://localhost:7244/")
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package01FeeReadsEnabled"] = enabled ? "true" : "false",
                ["DynamicsAccess:ExecutionMode"] = executionMode,
                ["DynamicsAccess:ProfileAlias"] = "jesus-prod",
                ["DynamicsAccess:Gateway:Endpoint"] = endpoint,
                ["DynamicsAccess:Gateway:ApiPrefix"] = "/v1"
            })
            .Build();
    }

    /// <summary>
    /// 建立正式 Gateway executor，但由測試提供唯一 HttpClient owner；hosted service 只能取得 executor，
    /// 不得建立第二個 client 或 handler。回應 byte limit 使用 production 預設，避免測試繞過正式解析路徑。
    /// </summary>
    private static GatewayDynamicsOperationExecutor CreateGatewayExecutor(HttpClient httpClient)
    {
        return new GatewayDynamicsOperationExecutor(
            httpClient,
            Options.Create(new ProductDynamicsOptions
            {
                ExecutionMode = DynamicsExecutionMode.Gateway,
                ProfileAlias = "jesus-prod",
                Gateway = new GatewayModeOptions
                {
                    Endpoint = "https://localhost:7244/",
                    ApiPrefix = "/v1"
                }
            }),
            NullLogger<GatewayDynamicsOperationExecutor>.Instance);
    }

    /// <summary>
    /// 記錄 hosted service 是否要求 process host 解析 Gateway executor。
    /// 此替身不擁有 executor／HttpClient，也不建立 provider；生命週期由測試方法持有，Dispose 僅記錄呼叫，
    /// 用來證明 flag=false 與 Embedded 分支在資源 ownership 開始前即停止。
    /// </summary>
    private sealed class RecordingProcessHost : IDonationDynamicsAccessProcessHost
    {
        private readonly Func<ProductDynamicsOptions, IDynamicsOperationExecutor> _gatewayFactory;

        /// <summary>
        /// 建立無資源擁有權的記錄替身。
        /// </summary>
        /// <param name="gatewayFactory">只有 Gateway 分支可呼叫的 executor factory。</param>
        public RecordingProcessHost(
            Func<ProductDynamicsOptions, IDynamicsOperationExecutor> gatewayFactory)
        {
            _gatewayFactory = gatewayFactory;
        }

        /// <summary>
        /// process host 被要求解析 Gateway executor 的總次數；以 Interlocked 更新以涵蓋併行啟動競爭。
        /// </summary>
        public int GatewayExecutorRequests => Volatile.Read(ref _gatewayExecutorRequests);

        private int _gatewayExecutorRequests;

        /// <summary>
        /// preflight 測試不經 legacy lifecycle，因此 facade 發佈為 no-op，且不建立任何 executor／provider。
        /// </summary>
        public void PublishCompatibilityFacade()
        {
        }

        /// <summary>
        /// 替身沒有 static facade ownership，撤銷為 no-op。
        /// </summary>
        public void UnpublishCompatibilityFacade()
        {
        }

        /// <summary>
        /// 記錄一次 Gateway executor 解析並交由測試 factory 回傳；不快取、不 Dispose executor。
        /// </summary>
        public IDynamicsOperationExecutor GetOrCreateGatewayExecutor(ProductDynamicsOptions options)
        {
            Interlocked.Increment(ref _gatewayExecutorRequests);
            return _gatewayFactory(options);
        }

        /// <summary>
        /// 替身沒有 provider、handler、timer 或 socket owner，因此 Dispose 為同步完成的 no-op。
        /// </summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// 可注入成功、失敗或等待取消行為的 executor。它只保存 bounded delegate 與 invocation count，
    /// 不保存要求身份、credential、token 或 response buffer；blocking 模式完全由收到的 token 結束。
    /// </summary>
    private sealed class DelegateExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, CancellationToken, Task<OperationExecutionResult>> _execute;
        private int _invocations;

        /// <summary>
        /// 建立由測試方法擁有的 executor delegate。
        /// </summary>
        public DelegateExecutor(
            Func<OperationExecutionRequest, CancellationToken, Task<OperationExecutionResult>> execute)
        {
            _execute = execute;
        }

        /// <summary>
        /// 已開始的操作數；以 Interlocked 計數避免 timeout／取消 continuation 的競爭造成讀值撕裂。
        /// </summary>
        public int Invocations => Volatile.Read(ref _invocations);

        /// <summary>
        /// 建立只會在 cancellation 到達時結束的 executor，用於證明 preflight 的 timeout／shutdown owner。
        /// </summary>
        public static DelegateExecutor CreateBlocking()
        {
            return new DelegateExecutor(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return OperationExecutionResult.Success(data: null);
            });
        }

        /// <summary>
        /// 轉送正式 request 與 cancellation token，並在 delegate 前原子增加 invocation count。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _invocations);
            return _execute(request, cancellationToken);
        }
    }

    /// <summary>
    /// 將 Gateway HTTP 完全限制在測試記憶體內的 handler；不啟動 socket、DNS、proxy、cookie 或背景 timer。
    /// callback 收到 production executor 建立的 request，讓測試檢查正式 route、header 與 body 信任邊界。
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        /// <summary>
        /// 建立由測試方法擁有的 bounded callback handler。
        /// </summary>
        public StubHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        /// <summary>
        /// 將 request 轉送給測試 callback，保留 caller cancellation；不進行任何真實 HTTP I/O。
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
