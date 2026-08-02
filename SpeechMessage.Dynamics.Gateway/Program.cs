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

using System.Buffers;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.DependencyInjection;
using SpeechMessage.Dynamics.ControlPlane.Runtime;
using SpeechMessage.Dynamics.Gateway;
using SpeechMessage.Dynamics.Gateway.RequestLimits;
using SpeechMessage.Dynamics.Gateway.Security;
using SpeechMessage.Dynamics.WorkerSupervisor;

var builder = WebApplication.CreateBuilder(args);

// 發布工具將實際 Worker executable hash、generation 與 Organization identity 寫在 Gateway
// 執行檔旁的 deployment-owned overlay。此載入器只投影 allowlisted Worker scalar，且以一次性
// in-memory snapshot 覆寫 appsettings 內的 fail-closed 占位；不建立 reload watcher 或 mutable cache。
_ = OfficialWorkerDeploymentConfiguration.TryAddAdjacentOverlay(
    builder.Configuration,
    AppContext.BaseDirectory);

builder.Services.AddOptions<GatewayRequestBodyLimitOptions>()
    .BindConfiguration(GatewayRequestBodyLimitOptions.SectionName)
    .Validate(options =>
    {
        try
        {
            options.Validate();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }, "Gateway request body maximum or JSON depth is outside the hard deployment boundary.")
    .ValidateOnStart();

// Kestrel 必須在 listener 啟動前使用相同 deployment section 與 hard validator；transport 最多允許 configured wire bytes，
// 不建立獨立 fallback/default。Callback 只建立短生命週期整數 snapshot，不保存 IConfiguration、request 或 buffer reference。
builder.WebHost.ConfigureKestrel((context, options) =>
{
    var limits = GatewayRequestBodyLimitOptions.BindAndValidate(context.Configuration);
    options.Limits.MaxRequestBodySize = limits.MaxRequestBodyBytes;
});

// IIS in-process/out-of-process hosting 由 DI Options 取得同一個已驗證 limit；IISServerOptions 與 Gateway options 都由 Host 唯一擁有，
// 沒有 reload subscription、timer 或 cleanup。若設定無效，ValidateOnStart 在接流量前中止，而不是讓 IIS/Kestrel 產生不同邊界。
builder.Services.AddOptions<IISServerOptions>()
    .Configure<IOptions<GatewayRequestBodyLimitOptions>>((options, limits) =>
        options.MaxRequestBodySize = limits.Value.MaxRequestBodyBytes);

builder.Services.AddSingleton(ArrayPool<byte>.Shared);
builder.Services.AddSingleton<GatewayOperationRequestBodyReader>();
builder.Services.AddControllers();
builder.Services.AddOptions<Microsoft.AspNetCore.Mvc.JsonOptions>()
    .Configure<IOptions<GatewayRequestBodyLimitOptions>>((options, limits) =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.JsonSerializerOptions.MaxDepth = limits.Value.JsonMaxDepth;
    });
builder.Services.AddOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>()
    .Configure<IOptions<GatewayRequestBodyLimitOptions>>((options, limits) =>
    {
        options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.SerializerOptions.MaxDepth = limits.Value.JsonMaxDepth;
    });

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
    if (builder.Environment.IsEnvironment("Testing"))
    {
        // WebApplicationFactory 的 ConfigureAppConfiguration provider 可能在 Program 建立 service descriptors 後才加入；
        // Testing default scheme 必須延後到 AuthenticationOptions materialization 時讀取最終 IConfiguration snapshot，
        // 否則 Program 會錯用基底 Windows scheme，而測試 DI 若再指定 default 就會遮蔽這個 production-path 漂移。
        // Delegate 只讀一個 bounded scheme name，不保留 IConfiguration、request 或 handler reference；Options singleton
        // 由 DI 唯一擁有，沒有 timer、subscription、背景 Task 或 cleanup，且只允許 Testing 使用可覆寫 scheme。
        builder.Services.AddAuthentication();
        builder.Services.AddOptions<AuthenticationOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                var configuredScheme = configuration["DynamicsGateway:AuthenticationScheme"];
                options.DefaultScheme = string.IsNullOrWhiteSpace(configuredScheme)
                    ? IISDefaults.AuthenticationScheme
                    : configuredScheme;
            });
    }
    else
    {
        // Production 固定由 Central/IIS Windows Authentication 建立 principal；configuration 即使宣告已註冊的 fake scheme
        // 也不能改寫 default，避免 caller-controlled header handler 或測試 handler 進入正式 trust boundary。
        builder.Services.AddAuthentication(IISDefaults.AuthenticationScheme);
    }
}

builder.Services.AddAuthorization();

// Gateway 從部署擁有的 Alias Catalog 建立多 Profile Runtime；產品 Request 只能提供已授權 Alias，
// 不能傳入 CRM Endpoint、Transport、Credential 或 Secret Reference。Central／Local Gateway 使用同一段程式，
// 差異只在產品端 Gateway.Endpoint 指向中央服務或 localhost。
var dynamicsProfiles = LoadDynamicsProfileDefinitions(
    builder.Configuration,
    builder.Environment);
builder.Services.AddSpeechMessageDynamicsOfficialWorkers(dynamicsProfiles);
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

app.Use(async (context, next) =>
{
    if (IsProductOperationOrCatalogRequest(context.Request))
    {
        SetPrivateNoStoreResponseHeaders(context.Response);
    }

    await next(context).ConfigureAwait(false);
});

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
        HttpContext httpContext,
        IGatewayOperationAuthorizer operationAuthorizer,
        GatewayOperationRequestBodyReader bodyReader,
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

        // Body reader 只能在 authentication 與 principal→workload→alias→operation 全部成功後執行；
        // unsupported media type、declared oversize、chunked limit+1、malformed/deep/unknown/duplicate JSON
        // 都在建立 executor request 前轉成受控 415/413/400。
        // Reader 不 Dispose ASP.NET request stream，且在 return/throw/cancel 前清零並歸還唯一 pooled buffer。
        var bodyRead = await bodyReader.ReadAsync(
            httpContext.Request,
            cancellationToken).ConfigureAwait(false);
        if (bodyRead.Status == GatewayOperationRequestBodyReadStatus.UnsupportedMediaType)
        {
            // 只回傳固定 415，不回顯 Content-Type 或 body；此時 reader 尚未讀 stream、租 buffer 或建立 executor request。
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (bodyRead.Status == GatewayOperationRequestBodyReadStatus.PayloadTooLarge)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (bodyRead.Status != GatewayOperationRequestBodyReadStatus.Success ||
            bodyRead.Request is null)
        {
            return Results.BadRequest();
        }

        var body = bodyRead.Request;

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
    IResult (
        HttpContext httpContext,
        IGatewayOperationAuthorizer operationAuthorizer) =>
    {
        var authorization = operationAuthorizer.AuthorizeOperationCatalog(httpContext.User);
        if (!authorization.Succeeded)
        {
            return Results.Forbid();
        }

        // Catalog 與 execution 共用同一份 immutable principal→workload binding；只投影 binding 明列的 operation，
        // 不因 authenticated 就發布整個 registry。Registry 與白名單都是啟動期 bounded 唯讀集合，這裡刻意不建立
        // 跨 request cache、principal-keyed dictionary 或背景 refresh；request 結束後匿名投影即可回收，且不觸發 executor／transport。
        var operations = Package01OperationRegistry.All
            .Where(definition => authorization.CapabilityOperationIds.Contains(
                definition.CapabilityOperationId,
                StringComparer.OrdinalIgnoreCase))
            .Select(static definition => new
            {
                definition.CapabilityOperationId,
                definition.Package,
                definition.OperationKind,
                definition.TemplateKind,
                definition.DataClassification
            });
        return Results.Ok(operations);
    })
    .RequireAuthorization();

/// <summary>
/// 判定 request 是否為產品可見的受控操作或操作目錄。比對只依 HTTP method 與固定 REST path 前綴，
/// 不讀取 body、principal、token 或路由參數，也不建立任何快取、計時器或可釋放資源；因此可在驗證前安全設定
/// 回應的快取邊界，並讓未驗證或已拒絕的回應同樣受保護。
/// </summary>
static bool IsProductOperationOrCatalogRequest(HttpRequest request)
{
    var path = request.Path.Value;
    return (HttpMethods.IsGet(request.Method) &&
            string.Equals(path, "/v1/operations", StringComparison.Ordinal)) ||
        (HttpMethods.IsPost(request.Method) &&
            path is not null &&
            path.StartsWith("/v1/organizations/", StringComparison.Ordinal));
}

/// <summary>
/// 將產品可見的 Gateway 操作回應標示為僅限單次私有傳遞，避免 CRM 業務資料、錯誤狀態或 workload 可見的操作目錄
/// 被瀏覽器、反向 Proxy 或共享快取重播。此 helper 不保存 HttpContext、principal、body、token 或任何可釋放資源；
/// response header 的唯一 owner 仍是 ASP.NET Core request scope，request 結束時由框架完成清理。
/// </summary>
static void SetPrivateNoStoreResponseHeaders(HttpResponse response)
{
    response.Headers.CacheControl = "no-store, private";
}

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
/// 只讀取 <c>DynamicsProfiles:Profiles:{alias}</c> 的官方 Worker 設定；不存在時立即 fail closed，
/// 不建立舊 transport fallback，也不解析 Worker-local Credential、Token 或 CRM SDK 狀態。
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

    if (profileSections.Length == 0)
    {
        throw new InvalidOperationException(
            "DynamicsProfiles:Profiles must contain at least one official worker profile.");
    }

    // 每個 Alias 在啟動時固定選取一個版本隔離的官方 Worker；Request 失敗時不得切換版本或 transport。
    foreach (var profileSection in profileSections)
    {
        var workerVersion = ParseOfficialWorkerVersion(
            GetRequiredProfileValue(profileSection, "WorkerKind"));
        var admissionOptions = new OrganizationAdmissionOptions();
        profileSection.GetSection("Admission").Bind(admissionOptions);

        definitions.Add(new DynamicsProfileDefinition(
            profileSection.Key,
            GetRequiredProfileValue(profileSection, "WorkerProfileGenerationId"),
            workerVersion,
            GetRequiredProfileValue(profileSection, "OrganizationBaseUri"),
            ResolveWorkerExecutablePath(
                GetRequiredProfileValue(profileSection, "WorkerExecutablePath"),
                environment.ContentRootPath),
            GetRequiredProfileValue(profileSection, "WorkerExecutableSha256"),
            GetRequiredProfileValue(profileSection, "PackageLockId"),
            admissionOptions,
            profileSection.GetValue("WorkerCount", 1),
            profileSection.GetValue("MaxInFlightPerWorker", 1),
            profileSection.GetValue("WarmUpOnActivation", false),
            ReadOptionalPositiveSeconds(profileSection, "StartupTimeoutSeconds"),
            ReadOptionalPositiveSeconds(profileSection, "OperationTimeoutSeconds"),
            ReadOptionalPositiveSeconds(profileSection, "DrainTimeoutSeconds"),
            ReadOptionalPositiveSeconds(profileSection, "CancellationGracePeriodSeconds")));
    }

    return definitions;
}

/// <summary>
/// 將部署擁有的 WorkerKind 精確映射到版本隔離的官方 Worker graph；未知值在任何 process／pipe 建立前失敗。
/// </summary>
static OfficialWorkerVersion ParseOfficialWorkerVersion(string rawWorkerKind)
{
    return rawWorkerKind switch
    {
        "OfficialCrm82Worker" => OfficialWorkerVersion.Ce82,
        "OfficialCrm91Worker" => OfficialWorkerVersion.Ce91,
        _ => throw new InvalidOperationException(
            "WorkerKind must be exactly OfficialCrm82Worker or OfficialCrm91Worker.")
    };
}

/// <summary>
/// 讀取不可為空且沒有前後空白的 profile 欄位；錯誤不回顯設定內容，避免路徑或部署資訊外洩。
/// </summary>
static string GetRequiredProfileValue(
    IConfigurationSection profileSection,
    string key)
{
    var value = profileSection[key];
    if (string.IsNullOrWhiteSpace(value) ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Dynamics profile field '{key}' is required and must not contain outer whitespace.");
    }

    return value;
}

/// <summary>
/// 將相對 Worker 路徑固定解析到 Gateway content root。此處不展開環境變數，也不從 request、
/// 使用者 Session 或目前目錄取得替代值；Supervisor 仍會在建立 process/pipe 前驗證完整 SHA-256。
/// </summary>
static string ResolveWorkerExecutablePath(
    string configuredPath,
    string contentRootPath)
{
    var candidate = Path.IsPathFullyQualified(configuredPath)
        ? configuredPath
        : Path.Combine(contentRootPath, configuredPath);
    return Path.GetFullPath(candidate);
}

/// <summary>
/// 將可選秒數轉成有限正值；缺少時不建立 timer 或 cancellation owner，而由 immutable definition
/// 套用其受控預設值。
/// </summary>
static TimeSpan? ReadOptionalPositiveSeconds(
    IConfigurationSection profileSection,
    string key)
{
    var rawValue = profileSection[key];
    if (rawValue is null)
    {
        return null;
    }

    if (!int.TryParse(
            rawValue,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var seconds) ||
        seconds is < 1 or > 600)
    {
        throw new InvalidOperationException(
            $"Dynamics profile field '{key}' must be an integer from 1 through 600.");
    }

    return TimeSpan.FromSeconds(seconds);
}

app.Run();

/// <summary>
/// 給 WebApplicationFactory / 測試參考 Program。
/// </summary>
public partial class Program;
