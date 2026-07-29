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
                data = new { value = Array.Empty<object>() }
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
            ExecutionMode = DynamicsExecutionMode.Gateway,
            ProfileAlias = "jesus-prod",
            Gateway = new GatewayModeOptions
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
        seen.Should().NotBeNull();
        seen!.Method.Should().Be(HttpMethod.Post);
        seen.RequestUri!.AbsolutePath.Should().Be(
            "/v1/organizations/jesus-prod/operations/runtime.health.whoami");

        using var document = JsonDocument.Parse(seenJson!);
        document.RootElement.TryGetProperty("workloadSubjectId", out _).Should().BeFalse(
            "the Gateway must derive workload identity from its authenticated server principal");
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
            options.ExecutionMode = DynamicsExecutionMode.Gateway;
            options.ProfileAlias = "jesus-prod";
            options.Gateway = new GatewayModeOptions
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
            ExecutionMode = DynamicsExecutionMode.Gateway,
            ProfileAlias = profileAlias,
            Gateway = new GatewayModeOptions
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
