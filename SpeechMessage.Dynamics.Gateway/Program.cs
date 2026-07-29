// ============================================================================
// 檔案：SpeechMessage.Dynamics.Gateway/Program.cs
// 目的：Dynamics Access Gateway 的 ASP.NET Core 進入點。
//
// 保母教學：
// - 這是正式環境預設邊界：產品打 Gateway，不要直接拿 Organization 連線器。
// - Gateway 不接受任意 CRUD / 任意 FetchXML；只接受已註冊 operations。
// - 目前是 Phase 1：已可執行 WhoAmI 與 Package 1 fee-read 的 no-SDK HTTP 路徑。
// - 生產部署時請把 AuthMode / 秘密參考改成真實 profile 設定。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.DependencyInjection;
using SpeechMessage.Dynamics.WebApi.Runtime;
using Microsoft.AspNetCore.Server.IISIntegration;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);

var workloadAuthenticationScheme =
    builder.Configuration["DynamicsGateway:AuthenticationScheme"]
    ?? IISDefaults.AuthenticationScheme;
var authentication = builder.Services.AddAuthentication(workloadAuthenticationScheme);
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IWorkloadSubjectResolver, ConfigurationWorkloadSubjectResolver>();

// 保母提醒：
// WebApi 設定應來自設定檔或秘密庫，不可把密碼寫死在程式碼。
// 預設 HostIdentity 方便本機開發；正式環境請改成對應 profile。
builder.Services.AddSpeechMessageDynamicsWebApi(options =>
{
    options.OrganizationWebApiBaseUri =
        builder.Configuration["DynamicsWebApi:OrganizationWebApiBaseUri"];
    options.OrganizationBaseUri =
        builder.Configuration["DynamicsWebApi:OrganizationBaseUri"];
    options.CeVersion =
        builder.Configuration["DynamicsWebApi:CeVersion"]
        ?? "9.1";
    options.AuthMode = Enum.TryParse<DynamicsAuthMode>(
            builder.Configuration["DynamicsWebApi:AuthMode"],
            ignoreCase: true,
            out var authMode)
        ? authMode
        : DynamicsAuthMode.Windows;
    options.CredentialSource = Enum.TryParse<DynamicsCredentialSource>(
            builder.Configuration["DynamicsWebApi:CredentialSource"],
            ignoreCase: true,
            out var credentialSource)
        ? credentialSource
        : DynamicsCredentialSource.HostIdentity;
    options.SecretReference =
        builder.Configuration["DynamicsWebApi:SecretReference"];
    options.UserNameSecretName =
        builder.Configuration["DynamicsWebApi:UserNameSecretName"];
    options.PasswordSecretName =
        builder.Configuration["DynamicsWebApi:PasswordSecretName"];
    options.DomainSecretName =
        builder.Configuration["DynamicsWebApi:DomainSecretName"];
    options.AuthoritySecretName =
        builder.Configuration["DynamicsWebApi:AuthoritySecretName"];
    options.ClientIdSecretName =
        builder.Configuration["DynamicsWebApi:ClientIdSecretName"];
    options.CredentialReferenceName =
        builder.Configuration["DynamicsWebApi:CredentialReferenceName"];
    options.FeasibilityEvidenceId =
        builder.Configuration["DynamicsWebApi:FeasibilityEvidenceId"];
    options.AuthorityUri =
        builder.Configuration["DynamicsWebApi:AuthorityUri"];
    options.ResourceUri =
        builder.Configuration["DynamicsWebApi:ResourceUri"];
    options.ClientId =
        builder.Configuration["DynamicsWebApi:ClientId"];
    options.ClientSecretName =
        builder.Configuration["DynamicsWebApi:ClientSecretName"];
    options.AllowLocalDevPasswordGrant =
        string.Equals(
            builder.Configuration["DynamicsWebApi:AllowLocalDevPasswordGrant"],
            "true",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            builder.Configuration["DynamicsWebApi:AllowLocalDevPasswordGrant"],
            "1",
            StringComparison.OrdinalIgnoreCase);
    options.TimeoutSeconds = 30;
    options.MaxConnectionsPerServer = 4;
    options.MaxResponseBytes = builder.Configuration.GetValue(
        "DynamicsWebApi:MaxResponseBytes", 2_097_152);
    options.Admission.ExpectedOrganizationId = Guid.TryParse(
            builder.Configuration["DynamicsWebApi:Admission:ExpectedOrganizationId"],
            out var orgId)
        ? orgId
        : Guid.Parse("11111111-1111-1111-1111-111111111111");
    options.Admission.AggregateMaxInFlight = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:AggregateMaxInFlight", 24);
    options.Admission.MaximumRuntimeHosts = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:MaximumRuntimeHosts", 6);
    options.Admission.LocalQueueCapacity = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:LocalQueueCapacity", 48);
    options.Admission.MaxDispatchEnvelopeBytes = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:MaxDispatchEnvelopeBytes", 65536);
    options.Admission.QueueAdmissionTimeoutSeconds = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:QueueAdmissionTimeoutSeconds", 15);
    options.Admission.MaxInFlightAndQueuedPerWorkload = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:MaxInFlightAndQueuedPerWorkload", 8);
    options.Admission.AdmissionNamespaceId =
        builder.Configuration["DynamicsWebApi:Admission:AdmissionNamespaceId"]
        ?? "gateway-local-admission";
    options.Admission.LeaseNamespaceId =
        builder.Configuration["DynamicsWebApi:Admission:LeaseNamespaceId"]
        ?? "gateway-local-host-lease";
    options.Admission.AdmissionEpoch = builder.Configuration.GetValue<long>(
        "DynamicsWebApi:Admission:AdmissionEpoch", 1);
    options.Admission.RuntimeHostSlotLeaseTtlSeconds = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:RuntimeHostSlotLeaseTtlSeconds", 120);
    options.Admission.RuntimeHostSlotRenewalIntervalSeconds = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:RuntimeHostSlotRenewalIntervalSeconds", 30);
    options.Admission.RuntimeHostSlotExpiryFenceSeconds = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:RuntimeHostSlotExpiryFenceSeconds", 10);
    options.Admission.MaximumOutboundWorkLifetimeSeconds = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:MaximumOutboundWorkLifetimeSeconds", 35);
    options.Admission.ShutdownDrainTimeoutSeconds = builder.Configuration.GetValue(
        "DynamicsWebApi:Admission:ShutdownDrainTimeoutSeconds", 45);
    options.Admission.RequireDurableHostCoordinator =
        !builder.Environment.IsEnvironment("Testing");

    // 本機 scaffolding 後備值：若完全沒設定，至少給可驗證的 root。
    if (string.IsNullOrWhiteSpace(options.OrganizationWebApiBaseUri) &&
        string.IsNullOrWhiteSpace(options.OrganizationBaseUri))
    {
        options.OrganizationWebApiBaseUri = "https://localhost/api/data/v9.1/";
    }
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    var coordinatorConnection =
        builder.Configuration.GetConnectionString("DynamicsControlPlane")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:DynamicsControlPlane is required outside Testing.");
    builder.Services.AddSqlRuntimeHostSlotCoordinator(options =>
    {
        options.ConnectionString = coordinatorConnection;
        options.CommandTimeoutSeconds = builder.Configuration.GetValue(
            "DynamicsHostCoordinator:CommandTimeoutSeconds", 5);
        options.QuarantineSeconds = builder.Configuration.GetValue(
            "DynamicsHostCoordinator:QuarantineSeconds", 180);
    });
    builder.Services.AddHostedService<DynamicsGatewayReadinessService>();
}

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { status = "ok", service = "SpeechMessage.Dynamics.Gateway" });
});
app.MapGet("/ready", (HttpContext context, SpeechMessage.Dynamics.WebApi.Capacity.IOrganizationAdmissionManager admissionManager) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var snapshot = admissionManager.GetSnapshot();
    var body = new
    {
        status = snapshot.HostSlotReady ? "ready" : "not-ready",
        service = "SpeechMessage.Dynamics.Gateway",
        snapshot.InFlight,
        snapshot.Queued,
        snapshot.ActivePermits,
        snapshot.HostLeaseExpiresAtUtc,
        snapshot.RenewalLoopActive
    };
    return snapshot.HostSlotReady
        ? Results.Ok(body)
        : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});

// 受控操作入口：
// POST /v1/organizations/{alias}/operations/{capabilityOperationId}
app.MapPost(
    "/v1/organizations/{alias}/operations/{capabilityOperationId}",
    async Task<IResult> (
        string alias,
        string capabilityOperationId,
        OperationHttpRequest body,
        HttpContext httpContext,
        IWorkloadSubjectResolver workloadResolver,
        IDynamicsOperationExecutor executor,
        CancellationToken cancellationToken) =>
    {
        if (!workloadResolver.TryResolve(httpContext.User, out var workloadSubjectId))
        {
            return Results.Forbid();
        }

        // 保母提醒：
        // 正式環境的 WorkloadSubjectId 必須來自已驗證的 workload identity。
        // scaffolding 先允許 body 傳入，方便契約測試；不可當成安全模型。
        var request = new OperationExecutionRequest
        {
            ProfileAlias = alias,
            CapabilityOperationId = capabilityOperationId,
            WorkloadSubjectId = workloadSubjectId,
            Parameters = body.Parameters ?? new Dictionary<string, object?>(),
            IdempotencyKey = body.IdempotencyKey
        };

        var result = await executor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.Ok(result)
            : Results.BadRequest(result);
    })
    .RequireAuthorization();

app.MapGet(
    "/v1/operations",
    () => Results.Ok(Package01OperationRegistry.All.Select(x => new
    {
        x.CapabilityOperationId,
        x.Package,
        x.OperationKind,
        x.TemplateKind,
        x.DataClassification
    })));

app.Run();

/// <summary>
/// Gateway HTTP body 模型（scaffolding）。
/// </summary>
public sealed class OperationHttpRequest
{
    public string? IdempotencyKey { get; set; }
    public Dictionary<string, object?>? Parameters { get; set; }
}

/// <summary>
/// 給 WebApplicationFactory / 測試參考 Program。
/// </summary>
public partial class Program;

public interface IWorkloadSubjectResolver
{
    bool TryResolve(System.Security.Claims.ClaimsPrincipal principal, out string workloadSubjectId);
}

public sealed class ConfigurationWorkloadSubjectResolver : IWorkloadSubjectResolver
{
    private readonly IReadOnlyDictionary<string, string> _mappings;

    public ConfigurationWorkloadSubjectResolver(IConfiguration configuration)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in configuration.GetSection("DynamicsGateway:WorkloadMappings").GetChildren())
        {
            var principal = entry.Key.Trim();
            var subject = entry.Value?.Trim();
            if (principal.Length == 0 || string.IsNullOrWhiteSpace(subject) || subject.Length > 128)
            {
                continue;
            }

            mappings[principal] = subject;
        }

        _mappings = mappings;
    }

    public bool TryResolve(
        System.Security.Claims.ClaimsPrincipal principal,
        out string workloadSubjectId)
    {
        workloadSubjectId = string.Empty;
        if (principal.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(principal.Identity.Name))
        {
            return false;
        }

        return _mappings.TryGetValue(principal.Identity.Name.Trim(), out workloadSubjectId!);
    }
}
