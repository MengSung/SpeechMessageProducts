// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/GatewayProductClientTests.cs
// 目的：驗證產品端 Gateway executor 與共用 HttpClient factory。
//
// 保母教學：
// - 不連真實 Gateway。
// - 用 fake HttpMessageHandler 模擬 HTTP 回應。
// ============================================================================

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.Gateway;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;

namespace SpeechMessage.Dynamics.Tests;

public sealed class GatewayProductClientTests
{
    /// <summary>
    /// 驗證 Gateway executor 僅傳送產品契約允許的請求欄位，並將成功回應反序列化為封閉的
    /// <see cref="OperationResponseData"/>。測試明確確認工作負載主體不會經 HTTP 外送，
    /// 也確認產品端不再接收 OData value 包裝或其他 CRM 內部資料。
    /// </summary>
    [Fact]
    public async Task Gateway_executor_posts_to_versioned_operation_route()
    {
        HttpRequestMessage? seen = null;
        string? seenJson = null;
        var handler = new StubHandler(async request =>
        {
            seen = request;
            seenJson = await request.Content!.ReadAsStringAsync();
            var payload = JsonSerializer.Serialize(new
            {
                succeeded = true,
                data = OperationResponseData.ForWhoAmI(
                    OperationIds.RuntimeHealthWhoAmI,
                    "9.1",
                    new WhoAmIResponseData
                    {
                        UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        BusinessUnitId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333")
                    })
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://dynamics-gateway.internal/")
        };

        var options = Options.Create(new ProductDynamicsOptions
        {
            ConnectionMode = ConnectionMode.DedicatedGateway,
            ProfileAlias = "jesus-prod",
            Gateway = new GatewayEndpointOptions
            {
                Endpoint = "https://dynamics-gateway.internal/",
                ApiPrefix = "/v1"
            }
        });

        var executor = new GatewayDynamicsOperationExecutor(
            httpClient,
            options,
            NullLogger<GatewayDynamicsOperationExecutor>.Instance);

        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "jesus-prod",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "church-report-service",
            Parameters = new Dictionary<string, object?>()
        });

        result.Succeeded.Should().BeTrue();
        var responseData = result.Data!;
        responseData.Should().NotBeNull();
        responseData.OperationId.Should().Be(OperationIds.RuntimeHealthWhoAmI);
        responseData.ResponseKind.Should().Be(OperationResponseKind.WhoAmI);
        responseData.WhoAmI!.UserId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        seen.Should().NotBeNull();
        seen!.Method.Should().Be(HttpMethod.Post);
        seen.RequestUri!.AbsolutePath.Should().Be(
            "/v1/organizations/jesus-prod/operations/runtime.health.whoami");

        using var document = JsonDocument.Parse(seenJson!);
        document.RootElement.TryGetProperty("workloadSubjectId", out _).Should().BeFalse(
            "the Gateway must derive workload identity from its authenticated server principal");
    }

    /// <summary>
    /// 驗證未知的外層 Gateway JSON 成員會在封閉契約反序列化時被拒絕，且錯誤結果只使用
    /// 已淨化的產品端訊息。這避免未登錄的 CRM 路由、權杖或延伸資料被保留在例外、快取或 DTO。
    /// </summary>
    [Fact]
    public async Task Gateway_executor_rejects_unknown_outer_response_member()
    {
        const string payload = """
        {
          "succeeded": true,
          "untrustedEnvelope": "must-not-cross-boundary",
          "data": {
            "operationId": "runtime.health.whoami",
            "ceVersion": "9.1",
            "responseKind": "WhoAmI",
            "whoAmI": { "userId": "11111111-1111-1111-1111-111111111111" }
          }
        }
        """;
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            })));
        var executor = CreateExecutor(httpClient, "https://localhost:7244/", "crm91");

        var result = await executor.ExecuteAsync(CreateRequest("crm91"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        result.Data.Should().BeNull();
        result.ErrorMessage.Should().NotContain("untrustedEnvelope");
    }

    /// <summary>
    /// 驗證巢狀 data 中的 OData continuation 這類未知成員同樣會 fail-closed，且不會把
    /// CRM 主機或 API 路徑回傳給產品。這個測試固定 transport 已完成 bounded read；拒絕發生在
    /// 同一個 response scope 中，沒有新增可跨請求保留的 stream、buffer 或 URI owner。
    /// </summary>
    [Fact]
    public async Task Gateway_executor_rejects_unknown_nested_response_member()
    {
        const string payload = """
        {
          "succeeded": true,
          "data": {
            "operationId": "runtime.health.whoami",
            "ceVersion": "9.1",
            "responseKind": "WhoAmI",
            "whoAmI": { "userId": "11111111-1111-1111-1111-111111111111" },
            "@odata.nextLink": "https://untrusted.example/api/data/v9.1/WhoAmI"
          }
        }
        """;
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            })));
        var executor = CreateExecutor(httpClient, "https://localhost:7244/", "crm91");

        var result = await executor.ExecuteAsync(CreateRequest("crm91"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        result.Data.Should().BeNull();
        result.ErrorMessage.Should().NotContain("untrusted.example");
    }

    /// <summary>
    /// 驗證成功旗標不能繞過 discriminator 與 branch 的對應關係。當 WhoAmI 宣告卻缺少
    /// whoAmI branch 時，建構契約失敗必須被轉換成可預期的上游失敗，而非讓未淨化例外離開
    /// Gateway 或讓不相符的資料進入產品 DTO。
    /// </summary>
    [Fact]
    public async Task Gateway_executor_rejects_successful_response_with_mismatched_data_branch()
    {
        const string payload = """
        {
          "succeeded": true,
          "data": {
            "operationId": "runtime.health.whoami",
            "ceVersion": "9.1",
            "responseKind": "WhoAmI",
            "feeRecords": []
          }
        }
        """;
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            })));
        var executor = CreateExecutor(httpClient, "https://localhost:7244/", "crm91");

        var result = await executor.ExecuteAsync(CreateRequest("crm91"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        result.Data.Should().BeNull();
    }

    /// <summary>
    /// 驗證 union branch 內的未登錄欄位也會被嚴格拒絕，而非只檢查 envelope 的第一層。
    /// 此案例模擬上游嘗試在費用記錄夾帶原始 OData 資料；executor 必須回傳已淨化失敗，且
    /// 不讓該欄位進入 <see cref="OperationExecutionResult.Data"/>、快取或日誌訊息。
    /// </summary>
    [Fact]
    public async Task Gateway_executor_rejects_unknown_member_inside_response_branch()
    {
        const string payload = """
        {
          "succeeded": true,
          "data": {
            "operationId": "fee.dedication.retrieve.by.contact",
            "ceVersion": "9.1",
            "responseKind": "Package01FeeRecords",
            "feeRecords": [
              {
                "feeId": "11111111-1111-1111-1111-111111111111",
                "rawOData": "must-not-cross-boundary"
              }
            ]
          }
        }
        """;
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            })));
        var executor = CreateExecutor(httpClient, "https://localhost:7244/", "crm91");

        var result = await executor.ExecuteAsync(CreateRequest("crm91"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        result.Data.Should().BeNull();
        result.ErrorMessage.Should().NotContain("rawOData");
    }

    /// <summary>
    /// 驗證語法不完整的 JSON 會被轉成受控上游失敗，而不會讓 JSON parser 的內部資訊或原始
    /// response body 離開 ProductClient。此路徑仍沿用既有 bounded read 與 using response scope，
    /// 因此沒有遺留可跨請求使用的 HTTP 內容或緩衝區。
    /// </summary>
    [Fact]
    public async Task Gateway_executor_rejects_malformed_json_response_contract()
    {
        const string payload = """{ "succeeded": true, "data": """;
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            })));
        var executor = CreateExecutor(httpClient, "https://localhost:7244/", "crm91");

        var result = await executor.ExecuteAsync(CreateRequest("crm91"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        result.Data.Should().BeNull();
        result.ErrorMessage.Should().NotContain("succeeded");
    }

    /// <summary>
    /// 驗證回應位元組不是合法 UTF-8 時，在 JSON 解析前便 fail-closed 並釋放內容串流。錯誤結果
    /// 不保留或回顯無效位元組，確保解碼例外不會成為跨產品的資料保留或診斷洩漏管道。
    /// </summary>
    [Fact]
    public async Task Gateway_executor_rejects_invalid_utf8_response_and_disposes_stream()
    {
        var content = new TrackingStreamContent(new byte[] { 0xC3, 0x28 });
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            })));
        var executor = CreateExecutor(httpClient, "https://localhost:7244/", "crm91");

        var result = await executor.ExecuteAsync(CreateRequest("crm91"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        result.Data.Should().BeNull();
        result.ErrorMessage.Should().NotContain("C3");
        content.StreamDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Gateway_executor_rejects_request_profile_override_before_http_send()
    {
        var sends = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            Interlocked.Increment(ref sends);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var executor = CreateExecutor(httpClient, "https://localhost:7244/", "crm91");

        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "crm82",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "church-report-service",
            Parameters = new Dictionary<string, object?>()
        });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.InvalidParameter);
        sends.Should().Be(0);
    }

    [Fact]
    public async Task Gateway_executor_rejects_declared_oversized_response_before_body_read()
    {
        var content = new ThrowOnReadContent(contentLength: 2048);
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            })));
        var executor = CreateExecutor(
            httpClient,
            "https://localhost:7244/",
            "crm91",
            maxResponseBytes: 1024);

        var result = await executor.ExecuteAsync(CreateRequest("crm91"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        result.ErrorMessage.Should().Contain("exceeded");
        content.ReadAttempted.Should().BeFalse();
    }

    [Fact]
    public async Task Gateway_executor_rejects_oversized_chunked_response_and_disposes_stream()
    {
        var content = new TrackingStreamContent(new byte[2048]);
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            })));
        var executor = CreateExecutor(
            httpClient,
            "https://localhost:7244/",
            "crm91",
            maxResponseBytes: 1024);

        var result = await executor.ExecuteAsync(CreateRequest("crm91"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UpstreamFailure);
        result.ErrorMessage.Should().Contain("exceeded");
        content.StreamDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Gateway_executor_preserves_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromCanceled<HttpResponseMessage>(cancellation.Token)));
        var executor = CreateExecutor(httpClient, "https://localhost:7244/", "crm91");

        var act = () => executor.ExecuteAsync(CreateRequest("crm91"), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Unbounded_static_gateway_http_client_factory_is_not_part_of_the_product_client()
    {
        typeof(GatewayDynamicsOperationExecutor).Assembly.GetType(
                "SpeechMessage.Dynamics.ProductClient.Gateway.GatewayHttpClientFactory")
            .Should().BeNull("endpoint keyed static clients have no bounded lifecycle owner");
    }

    [Fact]
    public void Gateway_handler_is_isolated_bounded_and_owned_by_http_client_factory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSpeechMessageDynamicsGatewayProductClient(options =>
        {
            options.ConnectionMode = ConnectionMode.DedicatedGateway;
            options.ProfileAlias = "jesus-prod";
            options.Gateway = new GatewayEndpointOptions
            {
                Endpoint = "https://dynamics-gateway.internal/",
                ApiPrefix = "/v1"
            };
        });

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var executor = provider.GetRequiredService<IDynamicsOperationExecutor>();
        var sockets = FindSocketsHttpHandler(executor);

        sockets.Should().NotBeNull();
        sockets!.UseCookies.Should().BeFalse();
        sockets.AllowAutoRedirect.Should().BeFalse();
        sockets.UseProxy.Should().BeFalse();
        sockets.AutomaticDecompression.Should().Be(DecompressionMethods.None);
        sockets.MaxConnectionsPerServer.Should().BeInRange(1, 16);
        sockets.PooledConnectionLifetime.Should().BeGreaterThan(TimeSpan.Zero);
        sockets.PooledConnectionIdleTimeout.Should().BeGreaterThan(TimeSpan.Zero);
    }

    private static GatewayDynamicsOperationExecutor CreateExecutor(
        HttpClient httpClient,
        string endpoint,
        string profileAlias,
        int maxResponseBytes = 2_097_152)
    {
        var options = Options.Create(new ProductDynamicsOptions
        {
            ConnectionMode = ConnectionMode.DedicatedGateway,
            ProfileAlias = profileAlias,
            Gateway = new GatewayEndpointOptions
            {
                Endpoint = endpoint,
                ApiPrefix = "/v1",
                MaxResponseBytes = maxResponseBytes
            }
        });

        return new GatewayDynamicsOperationExecutor(
            httpClient,
            options,
            NullLogger<GatewayDynamicsOperationExecutor>.Instance);
    }

    private static OperationExecutionRequest CreateRequest(string profileAlias)
        => new()
        {
            ProfileAlias = profileAlias,
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "church-report-service",
            Parameters = new Dictionary<string, object?>()
        };

    private static SocketsHttpHandler? FindSocketsHttpHandler(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            if (current is SocketsHttpHandler sockets)
            {
                return sockets;
            }

            for (var type = current.GetType(); type is not null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(
                             System.Reflection.BindingFlags.Instance |
                             System.Reflection.BindingFlags.NonPublic |
                             System.Reflection.BindingFlags.Public))
                {
                    if (field.GetValue(current) is { } nested &&
                        (nested is HttpMessageHandler || nested is HttpClient))
                    {
                        pending.Push(nested);
                    }
                }
            }
        }

        return null;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _handler(request);
    }

    private sealed class ThrowOnReadContent : HttpContent
    {
        private int _readAttempted;

        public ThrowOnReadContent(long contentLength)
        {
            Headers.ContentLength = contentLength;
        }

        public bool ReadAttempted => Volatile.Read(ref _readAttempted) == 1;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Volatile.Write(ref _readAttempted, 1);
            return Task.FromException(new InvalidOperationException("upstream-sensitive-body"));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = Headers.ContentLength!.Value;
            return true;
        }
    }

    private sealed class TrackingStreamContent : HttpContent
    {
        private readonly byte[] _bytes;
        private TrackingMemoryStream? _stream;

        public TrackingStreamContent(byte[] bytes)
        {
            _bytes = bytes;
        }

        public bool StreamDisposed => _stream?.Disposed == true;

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            _stream = new TrackingMemoryStream(_bytes);
            return Task.FromResult<Stream>(_stream);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public TrackingMemoryStream(byte[] bytes)
            : base(bytes, writable: false)
        {
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
