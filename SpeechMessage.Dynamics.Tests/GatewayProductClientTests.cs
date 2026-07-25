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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.Gateway;

namespace SpeechMessage.Dynamics.Tests;

public sealed class GatewayProductClientTests
{
    [Fact]
    public async Task Gateway_executor_posts_to_versioned_operation_route()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(async request =>
        {
            seen = request;
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
    }

    [Fact]
    public void Shared_gateway_http_client_is_reused_per_endpoint()
    {
        var a = GatewayHttpClientFactory.GetSharedClient("https://dynamics-gateway.internal/");
        var b = GatewayHttpClientFactory.GetSharedClient("https://dynamics-gateway.internal/");
        var c = GatewayHttpClientFactory.GetSharedClient("https://other-gateway.internal/");

        ReferenceEquals(a, b).Should().BeTrue();
        ReferenceEquals(a, c).Should().BeFalse();
        a.BaseAddress!.AbsoluteUri.Should().Be("https://dynamics-gateway.internal/");
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