using System.Diagnostics;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class Phase4IsolationSoakTests
{
    [Fact]
    public async Task Five_workloads_two_generations_and_two_endpoints_drain_without_cross_talk()
    {
        const int operationCount = 6000;
        var coordinator = new InMemoryRuntimeHostSlotCoordinator();
        var organizationConcurrency = new SharedConcurrencyCounter();
        var otherOrganizationConcurrency = new SharedConcurrencyCounter();
        var generationA = CreateRuntime(
            "https://crm-a.example.local/org/api/data/v9.1/",
            "org-a-shared-slot",
            coordinator,
            organizationConcurrency);
        var generationB = CreateRuntime(
            "https://crm-a.example.local/org/api/data/v9.1/",
            "org-a-shared-slot",
            coordinator,
            organizationConcurrency);
        var endpointB = CreateRuntime(
            "https://crm-b.example.local/other/api/data/v8.2/",
            "org-b-slot",
            coordinator,
            otherOrganizationConcurrency);
        var runtimes = new[] { generationA, generationB, endpointB };
        var workloads = new[] { "church-report", "line", "payment", "batch", "diagnostics" };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var failures = new System.Collections.Concurrent.ConcurrentBag<OperationExecutionResult>();
            await Parallel.ForEachAsync(
                Enumerable.Range(0, operationCount),
                new ParallelOptions { MaxDegreeOfParallelism = 64 },
                async (index, cancellationToken) =>
                {
                    var result = await runtimes[index % runtimes.Length].Executor.ExecuteAsync(
                    new OperationExecutionRequest
                    {
                        ProfileAlias = index % 3 == 2 ? "org-b" : "org-a",
                        CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                        WorkloadSubjectId = workloads[index % workloads.Length]
                    },
                    cancellationToken);
                    if (!result.Succeeded)
                    {
                        failures.Add(result);
                    }
                }).WaitAsync(TimeSpan.FromSeconds(30));
            failures.Should().BeEmpty();
        }
        finally
        {
            stopwatch.Stop();
            foreach (var runtime in runtimes)
            {
                var snapshot = runtime.Manager.GetSnapshot();
                snapshot.InFlight.Should().Be(0);
                snapshot.Queued.Should().Be(0);
                snapshot.ActivePermits.Should().Be(0);
                snapshot.TrackedWorkloadCount.Should().Be(0,
                    "per-workload counters must be removed after the last permit drains");
                await runtime.DisposeAsync();
            }
        }

        organizationConcurrency.MaximumObserved.Should().BeLessThanOrEqualTo(8,
            "two generations share AggregateMaxInFlight=8 for the same physical organization");
        otherOrganizationConcurrency.MaximumObserved.Should().BeLessThanOrEqualTo(4);
        runtimes.Should().OnlyContain(runtime => runtime.Handler.DisposeCount == 1);
        runtimes.Should().OnlyContain(runtime => runtime.Handler.CrossTalkCount == 0);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    private static RuntimeFixture CreateRuntime(
        string webApiRoot,
        string leaseNamespace,
        IRuntimeHostSlotCoordinator coordinator,
        SharedConcurrencyCounter concurrency)
    {
        var options = Options.Create(new DynamicsWebApiOptions
        {
            OrganizationWebApiBaseUri = webApiRoot,
            CeVersion = webApiRoot.Contains("v8.2", StringComparison.Ordinal) ? "8.2" : "9.1",
            AuthMode = DynamicsAuthMode.Windows,
            CredentialSource = DynamicsCredentialSource.HostIdentity,
            TimeoutSeconds = 5,
            MaxConnectionsPerServer = 4,
            MaxResponseBytes = 4096,
            Admission = new OrganizationAdmissionOptions
            {
                ExpectedOrganizationId = leaseNamespace == "org-a-shared-slot"
                    ? Guid.Parse("45454545-4545-4545-4545-454545454545")
                    : Guid.Parse("56565656-5656-5656-5656-565656565656"),
                AggregateMaxInFlight = leaseNamespace == "org-a-shared-slot" ? 8 : 4,
                MaximumRuntimeHosts = leaseNamespace == "org-a-shared-slot" ? 2 : 1,
                LocalQueueCapacity = 64,
                MaxInFlightAndQueuedPerWorkload = 64,
                QueueAdmissionTimeoutSeconds = 10,
                AdmissionNamespaceId = leaseNamespace + "-admission",
                LeaseNamespaceId = leaseNamespace
            }
        });
        OrganizationAdmissionPlan.TryCreate(options.Value, options.Value.Admission, out var plan, out var error)
            .Should().BeTrue(error?.ErrorMessage);

        var manager = new OrganizationAdmissionManager(
            plan!,
            coordinator,
            NullLogger<OrganizationAdmissionManager>.Instance);
        var handler = new TrackingHandler(new Uri(webApiRoot), concurrency);
        var transport = new DynamicsHttpTransport(
            handler,
            NullLogger<DynamicsHttpTransport>.Instance,
            disposeHandler: true);
        var client = new DynamicsWebApiClient(
            options,
            transport,
            new DictionarySecretResolver(new Dictionary<string, string>()),
            new StaticTokenProvider(),
            NullLogger<DynamicsWebApiClient>.Instance);
        var executor = new ControlledOperationExecutor(client, manager);
        return new RuntimeFixture(manager, transport, executor, handler);
    }

    private sealed record RuntimeFixture(
        OrganizationAdmissionManager Manager,
        DynamicsHttpTransport Transport,
        ControlledOperationExecutor Executor,
        TrackingHandler Handler)
    {
        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            await Transport.DisposeAsync();
        }
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        private readonly Uri _approvedRoot;
        private readonly SharedConcurrencyCounter _concurrency;
        private int _disposeCount;
        private int _crossTalkCount;

        public TrackingHandler(Uri approvedRoot, SharedConcurrencyCounter concurrency)
        {
            _approvedRoot = approvedRoot;
            _concurrency = concurrency;
        }

        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public int CrossTalkCount => Volatile.Read(ref _crossTalkCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var active = _concurrency.Enter();
            try
            {
                if (request.RequestUri is null ||
                    !request.RequestUri.AbsoluteUri.StartsWith(_approvedRoot.AbsoluteUri, StringComparison.OrdinalIgnoreCase) ||
                    request.Headers.Authorization is not null ||
                    request.Headers.Contains("Cookie"))
                {
                    Interlocked.Increment(ref _crossTalkCount);
                }

                await Task.Delay(1, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"UserId\":\"67676767-6767-6767-6767-676767676767\"}",
                        Encoding.UTF8,
                        "application/json")
                };
            }
            finally
            {
                _concurrency.Exit(active);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
            }

            base.Dispose(disposing);
        }
    }

    private sealed class SharedConcurrencyCounter
    {
        private int _active;
        private int _maximumObserved;

        public int MaximumObserved => Volatile.Read(ref _maximumObserved);

        public int Enter()
        {
            var active = Interlocked.Increment(ref _active);
            var observed = Volatile.Read(ref _maximumObserved);
            while (active > observed)
            {
                var previous = Interlocked.CompareExchange(ref _maximumObserved, active, observed);
                if (previous == observed)
                {
                    break;
                }

                observed = previous;
            }

            return active;
        }

        public void Exit(int _) => Interlocked.Decrement(ref _active);
    }

    private sealed class StaticTokenProvider : IAdfsOAuthTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("unused");
    }
}
