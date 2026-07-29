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

        var httpClient = new HttpClient(handler)
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
}
