using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Gateway `/ready` 只反映能安全容納 outbound 工作的 host-slot lease 狀態。
/// readiness 回應同時必須 NoStore，避免代理或瀏覽器快取過期的可用狀態而把流量送往已失去 lease 的主機。
/// </summary>
public sealed class GatewayReadinessTests
{
    /// <summary>
    /// 使用 stub snapshot 同時覆蓋 Ready/NotReady，並證明 endpoint 不自行猜測或延長 lease 狀態。
    /// </summary>
    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    public async Task Ready_endpoint_reflects_safe_host_slot_window(
        bool ready,
        HttpStatusCode expectedStatus)
    {
        await using var factory = CreateFactory(new StubAdmissionManager(ready));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(expectedStatus);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private static WebApplicationFactory<Program> CreateFactory(IOrganizationAdmissionManager manager)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DynamicsWebApi:OrganizationWebApiBaseUri"] = "https://crm.example.test/api/data/v9.1/",
                    ["DynamicsWebApi:CeVersion"] = "9.1",
                    ["DynamicsWebApi:Admission:ExpectedOrganizationId"] = "11111111-1111-1111-1111-111111111111"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrganizationAdmissionManager>();
                services.AddSingleton(manager);
            });
        });

    private sealed class StubAdmissionManager : IOrganizationAdmissionManager
    {
        private readonly bool _ready;

        public StubAdmissionManager(bool ready)
        {
            _ready = ready;
            var options = new DynamicsWebApiOptions
            {
                OrganizationWebApiBaseUri = "https://crm.example.test/api/data/v9.1/",
                MaxConnectionsPerServer = 1,
                Admission = new OrganizationAdmissionOptions
                {
                    ExpectedOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    AggregateMaxInFlight = 1,
                    MaximumRuntimeHosts = 1,
                    AdmissionNamespaceId = "ready-test",
                    LeaseNamespaceId = "ready-test"
                }
            };
            OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out _)
                .Should().BeTrue();
            Plan = plan!;
        }

        public OrganizationAdmissionPlan Plan { get; }

        public Task EnsureHostSlotAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AdmissionAcquireResult> AcquireAsync(
            DispatchEnvelope envelope,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public AdmissionMetricsSnapshot GetSnapshot()
            => new()
            {
                LocalMaxInFlight = 1,
                InFlight = 0,
                Queued = 0,
                LocalQueueCapacity = 0,
                AcceptedCount = 0,
                RejectedCount = 0,
                TimeoutCount = 0,
                HostSlotReady = _ready,
                HostFencingToken = _ready ? 1 : 0,
                HostLeaseExpiresAtUtc = _ready ? DateTimeOffset.UtcNow.AddMinutes(1) : null,
                ActivePermits = 0,
                RenewalLoopActive = _ready
            };

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
