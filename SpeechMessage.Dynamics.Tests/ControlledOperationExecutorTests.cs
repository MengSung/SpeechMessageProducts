// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ControlledOperationExecutorTests.cs
// 目的：確認受控 executor 會拒絕未知操作/非法參數，並在 admission 後呼叫 live client。
// ============================================================================

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class ControlledOperationExecutorTests
{
    [Fact]
    public async Task Unknown_operation_is_rejected()
    {
        var executor = CreateExecutor(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = "entity.generic.retrieve.blocked",
            WorkloadSubjectId = "test-workload",
            Parameters = new Dictionary<string, object?>()
        });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.UnknownOperation);
    }

    [Fact]
    public async Task Unknown_parameter_is_rejected()
    {
        var executor = CreateExecutor(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.FeeDedicationRetrieveByContact,
            WorkloadSubjectId = "test-workload",
            Parameters = new Dictionary<string, object?>
            {
                ["contactId"] = Guid.NewGuid().ToString(),
                ["rawFetchXml"] = "<fetch/>"
            }
        });

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(DynamicsErrorCodes.InvalidParameter);
        result.ErrorMessage.Should().Contain("rawFetchXml");
    }

    [Fact]
    public async Task WhoAmI_live_path_succeeds_with_fake_http_and_admission()
    {
        var executor = CreateExecutor(new StubHandler(request =>
        {
            request.RequestUri!.AbsoluteUri.Should().Be("https://crm.example.local/api/data/v9.1/WhoAmI");
            return JsonResponse("""{"UserId":"11111111-1111-1111-1111-111111111111"}""");
        }));

        var result = await executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "test-workload"
        });

        result.Succeeded.Should().BeTrue();
        result.Data.Should().NotBeNull();
    }

    private static IDynamicsOperationExecutor CreateExecutor(HttpMessageHandler handler)
    {
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationWebApiBaseUri = "https://crm.example.local/api/data/v9.1/",
            CeVersion = "9.1",
            AuthMode = DynamicsAuthMode.Windows,
            CredentialSource = DynamicsCredentialSource.HostIdentity,
            TimeoutSeconds = 15,
            MaxConnectionsPerServer = 2,
            Admission = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                AggregateMaxInFlight = 4,
                MaximumRuntimeHosts = 2,
                LocalQueueCapacity = 8,
                MaxInFlightAndQueuedPerWorkload = 4,
                QueueAdmissionTimeoutSeconds = 5,
                AdmissionNamespaceId = "test-admission",
                LeaseNamespaceId = "test-lease"
            }
        });

        OrganizationAdmissionPlan.TryCreate(options.Value, options.Value.Admission, out var plan, out _)
            .Should().BeTrue();

        var admission = new OrganizationAdmissionManager(
            plan!,
            new InMemoryRuntimeHostSlotCoordinator(),
            NullLogger<OrganizationAdmissionManager>.Instance);

        var transport = new DynamicsHttpTransport(handler, NullLogger<DynamicsHttpTransport>.Instance, disposeHandler: true);
        var client = new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            new StaticAdfsOAuthTokenProvider("unused-for-windows"),
            NullLogger<DynamicsWebApiClient>.Instance);
        return new ControlledOperationExecutor(client, admission);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class StaticAdfsOAuthTokenProvider : IAdfsOAuthTokenProvider
    {
        private readonly string _token;
        public StaticAdfsOAuthTokenProvider(string token) => _token = token;
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_token);
    }
}
