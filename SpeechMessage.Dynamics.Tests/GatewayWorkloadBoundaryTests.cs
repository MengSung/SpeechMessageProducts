using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

public sealed class GatewayWorkloadBoundaryTests
{
    [Fact]
    public async Task Unauthenticated_caller_is_rejected_before_executor()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(executor, principalName: null, mapped: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/v1/organizations/jesus-prod/operations/runtime.health.whoami",
            Json("{\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        executor.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Authenticated_but_unmapped_caller_is_rejected_before_executor()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            principalName: @"SPEECHMESSAGE\UnmappedService$",
            mapped: false);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/v1/organizations/jesus-prod/operations/runtime.health.whoami",
            Json("{\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        executor.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Hostile_body_identity_cannot_override_server_mapped_workload()
    {
        const string principal = @"SPEECHMESSAGE\ChurchReport$";
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(executor, principal, mapped: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/v1/organizations/jesus-prod/operations/runtime.health.whoami",
            Json("{\"workloadSubjectId\":\"attacker-tenant\",\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "unknown caller-controlled identity fields must be rejected");
        executor.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Mapped_server_principal_is_the_only_workload_authority()
    {
        const string principal = @"SPEECHMESSAGE\ChurchReport$";
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(executor, principal, mapped: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/v1/organizations/jesus-prod/operations/runtime.health.whoami",
            Json("{\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executor.CallCount.Should().Be(1);
        executor.LastRequest!.WorkloadSubjectId.Should().Be("church-report-service");
    }

    private static WebApplicationFactory<Program> CreateFactory(
        RecordingExecutor executor,
        string? principalName,
        bool mapped)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["DynamicsWebApi:OrganizationWebApiBaseUri"] = "https://crm.example.test/api/data/v9.1/",
                    ["DynamicsWebApi:CeVersion"] = "9.1",
                    ["DynamicsWebApi:Admission:ExpectedOrganizationId"] = "11111111-1111-1111-1111-111111111111",
                    ["DynamicsGateway:AuthenticationScheme"] = TestAuthenticationHandler.SchemeName,
                    ["DynamicsGateway:TestPrincipalName"] = principalName
                };
                if (mapped)
                {
                    values[$"DynamicsGateway:WorkloadMappings:{principalName}"] = "church-report-service";
                }

                config.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
                services.RemoveAll<IDynamicsOperationExecutor>();
                services.AddSingleton<IDynamicsOperationExecutor>(executor);
            });
        });
    }

    private static StringContent Json(string value)
        => new(value, Encoding.UTF8, "application/json");

    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        public int CallCount { get; private set; }
        public OperationExecutionRequest? LastRequest { get; private set; }

        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(OperationExecutionResult.Success(new { value = Array.Empty<object>() }));
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestWorkload";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principalName = Context.RequestServices
                .GetRequiredService<IConfiguration>()["DynamicsGateway:TestPrincipalName"];
            if (string.IsNullOrWhiteSpace(principalName))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, principalName) },
                SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
