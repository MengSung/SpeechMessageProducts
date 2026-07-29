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

    [Fact]
    public async Task Lease_loss_cancels_in_flight_web_api_work_and_releases_permit()
    {
        var admission = new CancellingAdmissionManager();
        var webApi = new CancellationObservingWebApiClient();
        var executor = new ControlledOperationExecutor(webApi, admission);
        using var callerCts = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(new OperationExecutionRequest
        {
            ProfileAlias = "jesus-dev",
            CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
            WorkloadSubjectId = "test-workload"
        }, callerCts.Token);

        await webApi.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            admission.CancelLease();
            await Task.Delay(200);

            execution.IsCompleted.Should().BeTrue(
                "lease loss must cancel CRM traffic without waiting for caller cancellation");
            (await execution).Succeeded.Should().BeFalse();
            admission.PermitDisposed.Should().BeTrue();
        }
        finally
        {
            callerCts.Cancel();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
        }
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

    private sealed class CancellingAdmissionManager : IOrganizationAdmissionManager
    {
        private readonly CancellationTokenSource _leaseLost = new();

        public CancellingAdmissionManager()
        {
            var options = new DynamicsWebApiOptions
            {
                OrganizationWebApiBaseUri = "https://crm.example.local/api/data/v9.1/",
                MaxConnectionsPerServer = 1,
                Admission = new OrganizationAdmissionOptions
                {
                    ExpectedOrganizationId = Guid.Parse("23232323-2323-2323-2323-232323232323"),
                    AggregateMaxInFlight = 1,
                    MaximumRuntimeHosts = 1,
                    AdmissionNamespaceId = "executor-cancel",
                    LeaseNamespaceId = "executor-cancel"
                }
            };
            OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out _)
                .Should().BeTrue();
            Plan = plan!;
        }

        public OrganizationAdmissionPlan Plan { get; }
        public bool PermitDisposed { get; private set; }

        public void CancelLease() => _leaseLost.Cancel();

        public Task EnsureHostSlotAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AdmissionAcquireResult> AcquireAsync(
            DispatchEnvelope envelope,
            CancellationToken cancellationToken)
            => Task.FromResult(AdmissionAcquireResult.Success(new Permit(this, _leaseLost.Token)));

        public AdmissionMetricsSnapshot GetSnapshot() => throw new NotSupportedException();

        public void Dispose() => _leaseLost.Dispose();

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private sealed class Permit : IAdmissionPermit
        {
            private readonly CancellingAdmissionManager _owner;
            private int _disposed;

            public Permit(CancellingAdmissionManager owner, CancellationToken leaseLostToken)
            {
                _owner = owner;
                LeaseLostToken = leaseLostToken;
            }

            public Guid CorrelationId { get; } = Guid.NewGuid();
            public long HostFencingToken => 1;
            public CancellationToken LeaseLostToken { get; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.PermitDisposed = true;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class CancellationObservingWebApiClient : IDynamicsWebApiClient
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OperationExecutionResult> WhoAmIAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task<OperationExecutionResult> ExecuteRegisteredOperationAsync(
            OperationDefinition definition,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The cancellation test unexpectedly completed without cancellation.");
            }
            catch (OperationCanceledException)
            {
                return OperationExecutionResult.Failure(
                    DynamicsErrorCodes.HostSlotUnavailable,
                    "Lease lost.");
            }
        }
    }
}
