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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

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
    options.TimeoutSeconds = 30;
    options.MaxConnectionsPerServer = 4;
    options.Admission.ExpectedOrganizationId = Guid.TryParse(
            builder.Configuration["DynamicsWebApi:Admission:ExpectedOrganizationId"],
            out var orgId)
        ? orgId
        : Guid.Parse("11111111-1111-1111-1111-111111111111");
    options.Admission.AggregateMaxInFlight = 24;
    options.Admission.MaximumRuntimeHosts = 6;
    options.Admission.LocalQueueCapacity = 48;
    options.Admission.MaxDispatchEnvelopeBytes = 65536;
    options.Admission.QueueAdmissionTimeoutSeconds = 15;
    options.Admission.MaxInFlightAndQueuedPerWorkload = 8;
    options.Admission.AdmissionNamespaceId =
        builder.Configuration["DynamicsWebApi:Admission:AdmissionNamespaceId"]
        ?? "gateway-local-admission";
    options.Admission.LeaseNamespaceId =
        builder.Configuration["DynamicsWebApi:Admission:LeaseNamespaceId"]
        ?? "gateway-local-host-lease";
    options.Admission.RequireDurableHostCoordinator = false;

    // 本機 scaffolding 後備值：若完全沒設定，至少給可驗證的 root。
    if (string.IsNullOrWhiteSpace(options.OrganizationWebApiBaseUri) &&
        string.IsNullOrWhiteSpace(options.OrganizationBaseUri))
    {
        options.OrganizationWebApiBaseUri = "https://localhost/api/data/v9.1/";
    }
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "SpeechMessage.Dynamics.Gateway" }));

// 受控操作入口：
// POST /v1/organizations/{alias}/operations/{capabilityOperationId}
app.MapPost(
    "/v1/organizations/{alias}/operations/{capabilityOperationId}",
    async Task<IResult> (
        string alias,
        string capabilityOperationId,
        OperationHttpRequest body,
        IDynamicsOperationExecutor executor,
        CancellationToken cancellationToken) =>
    {
        // 保母提醒：
        // 正式環境的 WorkloadSubjectId 必須來自已驗證的 workload identity。
        // scaffolding 先允許 body 傳入，方便契約測試；不可當成安全模型。
        var request = new OperationExecutionRequest
        {
            ProfileAlias = alias,
            CapabilityOperationId = capabilityOperationId,
            WorkloadSubjectId = string.IsNullOrWhiteSpace(body.WorkloadSubjectId)
                ? "anonymous-scaffold"
                : body.WorkloadSubjectId!,
            Parameters = body.Parameters ?? new Dictionary<string, object?>(),
            IdempotencyKey = body.IdempotencyKey
        };

        var result = await executor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.Ok(result)
            : Results.BadRequest(result);
    });

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
    public string? WorkloadSubjectId { get; set; }
    public string? IdempotencyKey { get; set; }
    public Dictionary<string, object?>? Parameters { get; set; }
}

/// <summary>
/// 給 WebApplicationFactory / 測試參考 Program。
/// </summary>
public partial class Program;
