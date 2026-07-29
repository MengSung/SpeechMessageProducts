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
using SpeechMessage.Dynamics.Gateway.Security;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.DependencyInjection;
using SpeechMessage.Dynamics.WebApi.Runtime;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Server.IISIntegration;
using System.Net;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);

if (builder.Environment.IsDevelopment())
{
    // Development 的 Local Gateway 直接由 Kestrel 擁有 HTTP connection，因此必須註冊真正的 Negotiate handler；
    // 不讀取可覆寫的 AuthenticationScheme 設定，避免 Testing fake scheme 或 header-based handler 外洩到開發執行個體。
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
}
else
{
    // Production 保留既有 Central/IIS Windows Authentication 邊界；Testing 才允許每個 TestServer
    // 明確選擇私有 fake scheme。Production 不接受 configuration 任意改寫 scheme，避免繞過 IIS principal authority。
    var authenticationScheme = builder.Environment.IsEnvironment("Testing")
        ? builder.Configuration["DynamicsGateway:AuthenticationScheme"]
            ?? IISDefaults.AuthenticationScheme
        : IISDefaults.AuthenticationScheme;
    builder.Services.AddAuthentication(authenticationScheme);
}

builder.Services.AddAuthorization();

// Gateway 從部署擁有的 Alias Catalog 建立多 Profile Runtime；產品 Request 只能提供已授權 Alias，
// 不能傳入 CRM Endpoint、Transport、Credential 或 Secret Reference。Central／Local Gateway 使用同一段程式，
// 差異只在產品端 Gateway.Endpoint 指向中央服務或 localhost。
var dynamicsProfiles = LoadDynamicsProfileDefinitions(
    builder.Configuration,
    builder.Environment);
builder.Services.AddSpeechMessageDynamicsProfiles(dynamicsProfiles);
builder.Services.AddSingleton<IGatewayOperationAuthorizer>(serviceProvider =>
    new ConfigurationGatewayOperationAuthorizer(
        serviceProvider.GetRequiredService<IConfiguration>(),
        dynamicsProfiles.Select(static profile => profile.ProfileAlias)));
builder.Services.AddHostedService<GatewayOperationAuthorizationStartupValidator>();

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

if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/v1") &&
            !IsDevelopmentHttpsLoopbackRequest(context))
        {
            // Transport 檢查必須位於 authentication、authorization 與 Minimal API body binding 前：
            // 非 HTTPS loopback caller 在 Negotiate handshake、配置 OperationHttpRequest、取得 admission permit
            // 或接觸 executor 前直接 403；只有 HTTPS loopback request 才能進入標準 Negotiate 401 challenge／authentication。
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context).ConfigureAwait(false);
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { status = "ok", service = "SpeechMessage.Dynamics.Gateway" });
});
app.MapGet("/ready", (HttpContext context, IDynamicsProfileRuntimeManager runtimeManager) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var snapshot = runtimeManager.GetSnapshot();
    var profiles = snapshot.Profiles
        .Select(profile => new
        {
            alias = profile.Key.ProfileAlias,
            generation = profile.Key.Generation,
            ceVersion = profile.Key.CeVersion,
            state = profile.State.ToString(),
            activeExecutions = profile.ActiveExecutionCount,
            inFlight = profile.Admission.InFlight,
            queued = profile.Admission.Queued,
            activePermits = profile.Admission.ActivePermits,
            hostSlotReady = profile.Admission.HostSlotReady,
            hostLeaseExpiresAtUtc = profile.Admission.HostLeaseExpiresAtUtc,
            renewalLoopActive = profile.Admission.RenewalLoopActive
        })
        .ToArray();
    var activeProfiles = snapshot.Profiles
        .Where(static profile => profile.State == DynamicsProfileRuntimeState.Active)
        .ToArray();
    var isReady = snapshot.IsReady &&
        activeProfiles.Length > 0 &&
        activeProfiles.All(static profile => profile.Admission.HostSlotReady);
    var body = new
    {
        status = isReady ? "ready" : "not-ready",
        service = "SpeechMessage.Dynamics.Gateway",
        profiles
    };
    return isReady
        ? Results.Ok(body)
        : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});

// 受控操作入口：
// 受控操作端點：POST /v1/organizations/{alias}/operations/{capabilityOperationId}。
// alias 與 capabilityOperationId 必須通過伺服器端 mapping/registry；要求本文不能改寫目標 URI、認證或 workload 身分。
app.MapPost(
    "/v1/organizations/{alias}/operations/{capabilityOperationId}",
    async Task<IResult> (
        string alias,
        string capabilityOperationId,
        OperationHttpRequest body,
        HttpContext httpContext,
        IGatewayOperationAuthorizer operationAuthorizer,
        IDynamicsOperationExecutor executor,
        CancellationToken cancellationToken) =>
    {
        var authorization = operationAuthorizer.Authorize(
            httpContext.User,
            alias,
            capabilityOperationId);
        if (!authorization.Succeeded)
        {
            return Results.Forbid();
        }

        // 必須完整通過 principal→workload→alias→operation 後才建立 request；三個 routing/security 欄位
        // 全部採 server canonical 值，caller body 只能提供 registry 後續仍會驗證的 bounded parameters。
        var request = new OperationExecutionRequest
        {
            ProfileAlias = authorization.ProfileAlias,
            CapabilityOperationId = authorization.CapabilityOperationId,
            WorkloadSubjectId = authorization.WorkloadSubjectId,
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
    })))
    .RequireAuthorization();

/// <summary>
/// 驗證 Development 受保護端點是否由 HTTPS loopback peer 呼叫。
/// 只信任 Kestrel/TestServer 寫入的 connection metadata，不讀取 X-Forwarded-For、Forwarded、Host 或其他 caller header；
/// <see cref="HttpConnectionInfo.RemoteIpAddress"/> 缺失時 fail closed。方法無配置、無 I/O、無 allocation-heavy parsing，
/// 可在所有 Development <c>/v1</c> request 的 authentication 前置 middleware 執行，且不持有 request scope 之外的 reference。
/// </summary>
static bool IsDevelopmentHttpsLoopbackRequest(HttpContext context)
{
    var remoteIpAddress = context.Connection.RemoteIpAddress;
    return context.Request.IsHttps &&
        remoteIpAddress is not null &&
        IPAddress.IsLoopback(remoteIpAddress);
}

/// <summary>
/// 從部署設定建立不可變 Profile Definitions。
/// 優先讀取 <c>DynamicsProfiles:Profiles:{alias}</c>；若尚未遷移則讀取舊 <c>DynamicsWebApi</c> 區段，
/// 並依明確 CE 版本衍生 crm82／crm91 Alias。方法只綁定 Secret Reference 名稱，不解析秘密值或建立網路資源。
/// </summary>
static IReadOnlyCollection<DynamicsProfileDefinition> LoadDynamicsProfileDefinitions(
    IConfiguration configuration,
    IHostEnvironment environment)
{
    var definitions = new List<DynamicsProfileDefinition>();
    var profileSections = configuration
        .GetSection("DynamicsProfiles:Profiles")
        .GetChildren()
        .ToArray();

    if (profileSections.Length > 0)
    {
        foreach (var profileSection in profileSections)
        {
            var options = new DynamicsWebApiOptions();
            profileSection.Bind(options);
            ApplyTestingEndpointFallback(options, environment);
            definitions.Add(new DynamicsProfileDefinition(
                profileSection.Key,
                options,
                profileSection.GetValue("WarmUpOnActivation", false)));
        }

        return definitions;
    }

    // 舊區段只作為遷移相容入口；它仍必須是單一明確版本，不能在 Request 失敗後自動切換 8.2／9.1。
    var legacyOptions = new DynamicsWebApiOptions();
    configuration.GetSection(DynamicsWebApiOptions.SectionName).Bind(legacyOptions);
    ApplyTestingEndpointFallback(legacyOptions, environment);
    var legacyAlias = legacyOptions.CeVersion.Trim() switch
    {
        "8.2" => "crm82",
        "9.1" => "crm91",
        _ => throw new InvalidOperationException(
            "Legacy DynamicsWebApi:CeVersion must be exactly 8.2 or 9.1.")
    };
    definitions.Add(new DynamicsProfileDefinition(
        legacyAlias,
        legacyOptions,
        configuration.GetValue("DynamicsProfiles:LegacyWarmUpOnActivation", false)));
    return definitions;
}

/// <summary>
/// 只在 Testing 環境為完全缺少 Endpoint 的測試 Host 補上 localhost Web API root。
/// 非 Testing 環境必須 fail closed，避免 Central／Local Gateway 因設定遺漏而猜測或誤連 CRM 目標。
/// </summary>
static void ApplyTestingEndpointFallback(
    DynamicsWebApiOptions options,
    IHostEnvironment environment)
{
    if (!string.IsNullOrWhiteSpace(options.OrganizationWebApiBaseUri) ||
        !string.IsNullOrWhiteSpace(options.OrganizationBaseUri))
    {
        return;
    }

    if (!environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException(
            "Every Dynamics profile requires OrganizationBaseUri or OrganizationWebApiBaseUri.");
    }

    var version = options.CeVersion.Trim();
    if (version is not ("8.2" or "9.1"))
    {
        throw new InvalidOperationException(
            "Testing Dynamics profile CeVersion must be exactly 8.2 or 9.1.");
    }

    options.OrganizationWebApiBaseUri = $"https://localhost/api/data/v{version}/";
}

app.Run();

/// <summary>
/// Gateway HTTP body 模型。它只接受冪等鍵與受 Operation Registry 約束的命名參數，
/// 不接受 CRM Endpoint、Profile Transport、Credential、Token、Authorization Header 或任意 FetchXML。
/// </summary>
public sealed class OperationHttpRequest
{
    /// <summary>取得或設定寫入型 Operation 使用的 bounded 冪等鍵；唯讀操作可省略。</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>取得或設定 Operation Definition 已宣告的命名參數；未知參數會在外呼 CRM 前被拒絕。</summary>
    public Dictionary<string, object?>? Parameters { get; set; }
}

/// <summary>
/// 給 WebApplicationFactory / 測試參考 Program。
/// </summary>
public partial class Program;
