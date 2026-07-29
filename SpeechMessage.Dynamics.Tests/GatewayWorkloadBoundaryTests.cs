using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Gateway workload 身分與能力的唯一權威是伺服器驗證後的 immutable principal→workload→alias→operation binding。
/// 未驗證、未 mapping、alias／operation 越權或企圖在 JSON 本文夾帶 workloadSubjectId 的呼叫，
/// 必須在 request materialization、executor、admission 與 Dynamics outbound 流量之前被拒絕。
/// </summary>
public sealed class GatewayWorkloadBoundaryTests
{
    private const string DefaultPrincipalName = @"SPEECHMESSAGE\ChurchReport$";
    private const string DefaultProfileAlias = "crm82";
    private const string DefaultOperationId = "runtime.health.whoami";

    /// <summary>未驗證呼叫不得進入 executor，避免匿名要求消耗 admission 或接觸任何 profile。</summary>
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

    /// <summary>已驗證但未授權 mapping 的 principal 仍須 fail-closed，不能從 alias 或本文推導權限。</summary>
    [Fact]
    public async Task Authenticated_but_unmapped_caller_is_rejected_before_executor()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            principalName: @"SPEECHMESSAGE\UnmappedService$",
            mapped: false,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["DynamicsGateway:TestWindowsSid"] = "S-1-5-21-1000-2000-3000-4999"
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/v1/organizations/jesus-prod/operations/runtime.health.whoami",
            Json("{\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        executor.CallCount.Should().Be(0);
    }

    /// <summary>惡意本文欄位不得覆寫 server-mapped workload，防止跨產品/租戶容量與稽核歸屬污染。</summary>
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

    /// <summary>只有伺服器 mapping 的固定 workloadSubjectId 可以送入 executor。</summary>
    [Fact]
    public async Task Mapped_server_principal_is_the_only_workload_authority()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(executor, DefaultPrincipalName, mapped: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            $"/v1/organizations/{DefaultProfileAlias}/operations/{DefaultOperationId}",
            Json("{\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executor.CallCount.Should().Be(1);
        executor.LastRequest!.WorkloadSubjectId.Should().Be("church-report-service");
    }

    /// <summary>
    /// 當 authentication handler 同時提供 Windows SID 與 principal name，SID binding 必須優先；
    /// 這可讓部署在帳號顯示名稱變更時仍綁定穩定 Windows security authority，且不得把 name binding 的 workload 混入稽核／容量 key。
    /// </summary>
    [Fact]
    public async Task Windows_sid_binding_takes_precedence_over_principal_name_binding()
    {
        const string windowsSid = "S-1-5-21-1000-2000-3000-4000";
        var overrides = new Dictionary<string, string?>(CreateBindingValues(
            index: 1,
            principalName: DefaultPrincipalName,
            workloadSubjectId: "name-bound-service",
            allowedAlias: DefaultProfileAlias,
            allowedOperation: DefaultOperationId))
        {
            ["DynamicsGateway:TestWindowsSid"] = windowsSid,
            ["DynamicsGateway:WorkloadBindings:2:WindowsSid"] = windowsSid,
            ["DynamicsGateway:WorkloadBindings:2:WorkloadSubjectId"] = "sid-bound-service",
            ["DynamicsGateway:WorkloadBindings:2:ProfileAliases:0"] = DefaultProfileAlias,
            ["DynamicsGateway:WorkloadBindings:2:CapabilityOperationIds:0"] = DefaultOperationId
        };
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            DefaultPrincipalName,
            mapped: false,
            configurationOverrides: overrides);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            $"/v1/organizations/{DefaultProfileAlias}/operations/{DefaultOperationId}",
            Json("{\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executor.CallCount.Should().Be(1);
        executor.LastRequest!.WorkloadSubjectId.Should().Be("sid-bound-service");
    }

    /// <summary>
    /// 驗證 authenticated principal 提供語法有效但未 mapping 的 SID 時，authorizer 仍可回退到完整 exact principal name binding；
    /// fallback 不接受 wildcard、prefix 或 caller header，且只在 SID lookup 沒有命中時執行，避免一個已 mapping SID 被名稱 binding 覆寫。
    /// 測試 executor 只記錄單一 request snapshot，不建立 admission、transport、token 或背景工作，Factory disposal 是所有 request scope 的唯一 cleanup owner。
    /// </summary>
    [Fact]
    public async Task Valid_unmapped_sid_falls_back_to_exact_principal_name_binding()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            DefaultPrincipalName,
            mapped: true,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["DynamicsGateway:TestWindowsSid"] = "S-1-5-21-1000-2000-3000-4888"
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            $"/v1/organizations/{DefaultProfileAlias}/operations/{DefaultOperationId}",
            Json("{\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executor.CallCount.Should().Be(1);
        executor.LastRequest!.WorkloadSubjectId.Should().Be("church-report-service");
    }

    /// <summary>
    /// 驗證 principal mapping 不是 alias 的萬用通行證；即使 caller 已驗證且 workload 已綁定，
    /// 未列入同一個伺服器 binding 的 Profile Alias 仍必須在建立執行要求、取得 admission permit 或接觸 executor 前拒絕。
    /// </summary>
    [Fact]
    public async Task Mapped_workload_cannot_call_alias_outside_binding()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            DefaultPrincipalName,
            mapped: true,
            allowedAlias: DefaultProfileAlias,
            allowedOperation: "fee.dedication.retrieve.by.contact.date.range");
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<IConfiguration>()
            .GetSection("DynamicsProfiles:Profiles:crm91")
            .Exists()
            .Should().BeTrue("crm91 必須是已載入的 known profile，才能證明拒絕來自 binding 而不是 catalog miss");

        using var response = await client.PostAsync(
            "/v1/organizations/crm91/operations/fee.dedication.retrieve.by.contact.date.range",
            Json("{\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 alias 授權不能隱含整個 operation registry；不在 binding 白名單的 capability operation
    /// 必須在 request materialization、queue admission 與 executor 前 fail closed，避免跨產品能力提升。
    /// </summary>
    [Fact]
    public async Task Mapped_workload_cannot_call_operation_outside_binding()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            DefaultPrincipalName,
            mapped: true,
            allowedAlias: DefaultProfileAlias,
            allowedOperation: "fee.dedication.retrieve.by.contact.date.range");
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            $"/v1/organizations/{DefaultProfileAlias}/operations/{DefaultOperationId}",
            Json("{\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// Operation catalog 雖不執行 CRM I/O，仍會揭露伺服器允許的能力面；匿名 caller 不得把它當成公開 discovery endpoint。
    /// </summary>
    [Fact]
    public async Task Operation_catalog_is_not_anonymous()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(executor, principalName: null, mapped: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/operations");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// Operation catalog 會揭露已部署能力，即使 caller 已通過 authentication，只要沒有 server-owned workload binding 仍必須回傳 403；
    /// endpoint 不得建立 executor request、取得 admission permit 或觸發 transport，避免未 mapping Windows 帳號列舉產品能力面。
    /// </summary>
    [Fact]
    public async Task Operation_catalog_rejects_authenticated_unmapped_principal()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            principalName: @"SPEECHMESSAGE\CatalogBrowser$",
            mapped: false,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["DynamicsGateway:TestWindowsSid"] = "S-1-5-21-1000-2000-3000-4777"
            });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/operations");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 已 mapping workload 只能看到同一 immutable binding 明列的 operation subset；registry 其他現在或未來能力不得因 authentication 成功而自動曝光。
    /// 回應只投影非秘密 metadata，不保留 principal、credential、token 或 request reference，且 catalog 查詢不得呼叫 executor 或建立 outbound resource。
    /// </summary>
    [Fact]
    public async Task Operation_catalog_returns_only_mapped_workload_authorized_subset()
    {
        const string allowedOperation = "fee.dedication.retrieve.by.contact.date.range";
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            DefaultPrincipalName,
            mapped: true,
            allowedOperation: allowedOperation);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/operations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operationIds = payload.RootElement
            .EnumerateArray()
            .Select(static element => element.GetProperty("capabilityOperationId").GetString())
            .ToArray();
        operationIds.Should().Equal(allowedOperation);
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// Testing environment 必須由 Program 讀取每個隔離 Factory 提供的 configured scheme；測試 DI 只註冊 handler 而不能自行指定 default，
    /// 因此此斷言直接證明 Host 選擇路徑。Options snapshot 由 DI singleton 管理，測試只讀取且不建立跨案例 subscription 或 mutable cache。
    /// </summary>
    [Fact]
    public async Task Testing_environment_selects_configured_authentication_scheme()
    {
        await using var factory = CreateFactory(
            new RecordingExecutor(),
            DefaultPrincipalName,
            mapped: true);
        using var client = factory.CreateClient();

        var options = factory.Services
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value;

        options.DefaultScheme.Should().Be(TestAuthenticationHandler.SchemeName);
    }

    /// <summary>
    /// Production 即使 configuration 宣告已註冊的惡意 fake scheme，Program 仍必須固定使用 IIS Windows authentication authority；
    /// Factory 移除會接觸 SQL／CRM 的 readiness hosted service，只檢查 authentication options，並由 Factory disposal 確定性回收 Host 與 service provider。
    /// </summary>
    [Fact]
    public async Task Production_environment_ignores_configured_authentication_scheme_override()
    {
        await using var factory = CreateProductionAuthenticationFactory();
        using var client = factory.CreateClient();

        var options = factory.Services
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value;

        options.DefaultScheme.Should().Be(IISDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Wildcard 會把部署設定從有限白名單變成未來 alias／operation 的隱性授權，因此 Host 必須在 listener 接流量前拒絕啟動。
    /// </summary>
    [Fact]
    public void Wildcard_binding_value_fails_host_startup()
    {
        using var factory = CreateFactory(
            new RecordingExecutor(),
            DefaultPrincipalName,
            mapped: true,
            allowedAlias: "*",
            allowedOperation: DefaultOperationId);

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*wildcard*");
    }

    /// <summary>
    /// 同一 principal 若出現兩份 binding，解析順序會變成安全語意的一部分；啟動期必須拒絕，而不是 last-write-wins。
    /// </summary>
    [Fact]
    public void Duplicate_principal_binding_fails_host_startup()
    {
        var overrides = CreateBindingValues(
            index: 2,
            principalName: DefaultPrincipalName,
            workloadSubjectId: "duplicate-service",
            allowedAlias: DefaultProfileAlias,
            allowedOperation: DefaultOperationId);
        using var factory = CreateFactory(
            new RecordingExecutor(),
            DefaultPrincipalName,
            mapped: true,
            configurationOverrides: overrides);

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate*principal*");
    }

    /// <summary>
    /// 同一 binding 內重複列出 operation 不能被 HashSet 靜默去重；重複值通常代表 deployment merge 漂移，
    /// 必須在 Host startup 暴露，避免維護者誤以為兩個不同能力已被審查。
    /// </summary>
    [Fact]
    public void Duplicate_operation_value_fails_host_startup()
    {
        using var factory = CreateFactory(
            new RecordingExecutor(),
            DefaultPrincipalName,
            mapped: true,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["DynamicsGateway:WorkloadBindings:1:CapabilityOperationIds:1"] =
                    DefaultOperationId
            });

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate*operation*");
    }

    /// <summary>
    /// Binding 只能引用目前部署擁有的 immutable profile catalog；拼錯或已移除的 alias 必須讓 Host 啟動失敗，
    /// 不能留到 request time 才猜測、fallback 或建立錯誤 runtime。
    /// </summary>
    [Fact]
    public void Unknown_profile_alias_in_binding_fails_host_startup()
    {
        using var factory = CreateFactory(
            new RecordingExecutor(),
            DefaultPrincipalName,
            mapped: true,
            allowedAlias: "missing-profile",
            allowedOperation: DefaultOperationId);

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown*profile alias*");
    }

    /// <summary>
    /// Binding 只能引用已註冊且可審查的 canonical operation ID；未知值不得在未來 registry 擴張後意外變成有效權限。
    /// </summary>
    [Fact]
    public void Unknown_operation_in_binding_fails_host_startup()
    {
        using var factory = CreateFactory(
            new RecordingExecutor(),
            DefaultPrincipalName,
            mapped: true,
            allowedAlias: DefaultProfileAlias,
            allowedOperation: "runtime.unknown.operation");

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown*operation*");
    }

    /// <summary>
    /// 建立完全隔離的 Testing Host。Fake authentication scheme 只由測試 DI 明確註冊，正式程式碼不持有測試 handler；
    /// executor 由測試唯一擁有且不開啟 CRM、SQL、Token、Socket 或背景工作，Factory disposal 是唯一 cleanup owner。
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactory(
        RecordingExecutor executor,
        string? principalName,
        bool mapped,
        string allowedAlias = DefaultProfileAlias,
        string allowedOperation = DefaultOperationId,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
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
                    // 第二個 profile 使用保留測試網域與獨立 organization identity，只用來證明 crm91 是 known catalog entry；
                    // readiness 已移除且 executor 被記憶體 double 取代，因此不建立 transport、credential、host-slot renewal 或背景 cleanup owner。
                    ["DynamicsProfiles:Profiles:crm91:WarmUpOnActivation"] = "false",
                    ["DynamicsProfiles:Profiles:crm91:OrganizationWebApiBaseUri"] =
                        "https://crm91.example.test/api/data/v9.1/",
                    ["DynamicsProfiles:Profiles:crm91:CeVersion"] = "9.1",
                    ["DynamicsProfiles:Profiles:crm91:AuthMode"] = "Windows",
                    ["DynamicsProfiles:Profiles:crm91:CredentialSource"] = "HostIdentity",
                    ["DynamicsProfiles:Profiles:crm91:Admission:ExpectedOrganizationId"] =
                        "22222222-2222-2222-2222-222222222222",
                    ["DynamicsGateway:AuthenticationScheme"] = TestAuthenticationHandler.SchemeName,
                    ["DynamicsGateway:TestPrincipalName"] = principalName
                };
                if (mapped)
                {
                    // 舊 mapping 暫留於 RED 基線，確保現有程式確實會驗證 principal 後錯誤放行 alias／operation；
                    // GREEN 實作不得再把這個單值 mapping 當作完整授權，唯一 authority 是下方 immutable binding。
                    values[$"DynamicsGateway:WorkloadMappings:{principalName}"] = "church-report-service";
                    foreach (var pair in CreateBindingValues(
                        index: 1,
                        principalName: principalName ?? DefaultPrincipalName,
                        workloadSubjectId: "church-report-service",
                        allowedAlias: allowedAlias,
                        allowedOperation: allowedOperation))
                    {
                        values[pair.Key] = pair.Value;
                    }
                }

                if (configurationOverrides is not null)
                {
                    foreach (var pair in configurationOverrides)
                    {
                        values[pair.Key] = pair.Value;
                    }
                }

                config.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
                services.RemoveAll<IDynamicsOperationExecutor>();
                services.AddSingleton<IDynamicsOperationExecutor>(executor);
            });
        });
    }

    /// <summary>
    /// 建立 Production authentication 選擇測試 Host。Configuration 刻意要求 header-trusting fake scheme，但測試 DI 只註冊 handler、絕不指定 default；
    /// 同時移除唯一會連 durable SQL control-plane 的 readiness service，確保案例沒有外部 I/O、timer 或背景 renewal owner。
    /// Factory 是 Host、service provider 與 handler registration 的唯一 owner，Dispose 後不留下跨測試認證狀態。
    /// </summary>
    private static WebApplicationFactory<Program> CreateProductionAuthenticationFactory()
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DynamicsGateway:AuthenticationScheme"] = TestAuthenticationHandler.SchemeName
                }));
            builder.ConfigureTestServices(services =>
            {
                var readinessDescriptors = services
                    .Where(static descriptor =>
                        descriptor.ServiceType == typeof(IHostedService) &&
                        descriptor.ImplementationType == typeof(DynamicsGatewayReadinessService))
                    .ToArray();
                foreach (var descriptor in readinessDescriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        });

    /// <summary>
    /// 產生一筆配置 provider 可讀的 binding snapshot；回傳的新 Dictionary 由單一 Factory setup 擁有，
    /// 不在測試之間共享或修改，避免平行 xUnit 執行時發生跨案例狀態污染。
    /// </summary>
    private static IReadOnlyDictionary<string, string?> CreateBindingValues(
        int index,
        string principalName,
        string workloadSubjectId,
        string allowedAlias,
        string allowedOperation)
        => new Dictionary<string, string?>
        {
            [$"DynamicsGateway:WorkloadBindings:{index}:PrincipalName"] = principalName,
            [$"DynamicsGateway:WorkloadBindings:{index}:WorkloadSubjectId"] = workloadSubjectId,
            [$"DynamicsGateway:WorkloadBindings:{index}:ProfileAliases:0"] = allowedAlias,
            [$"DynamicsGateway:WorkloadBindings:{index}:CapabilityOperationIds:0"] = allowedOperation
        };

    /// <summary>建立 UTF-8 JSON request content；每個呼叫端以 using 擁有並確定性釋放底層 buffer。</summary>
    private static StringContent Json(string value)
        => new(value, Encoding.UTF8, "application/json");

    /// <summary>
    /// 記錄 authorizer 通過後送達 executor 的唯一 request snapshot；實例只屬於單一測試，
    /// 不執行外部 I/O、不建立背景 Task，讓 CallCount=0 能直接證明拒絕發生於 admission／transport 之前。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        /// <summary>取得本測試 executor 被呼叫的次數；測試不並行共用同一實例。</summary>
        public int CallCount { get; private set; }

        /// <summary>取得最後一筆 server-materialized request；測試結束後隨 Factory 一併失去引用。</summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>
        /// 同步記錄已通過安全邊界的 request 並回傳固定成功結果；不保留 ClaimsPrincipal、HttpContext、Token 或 stream，
        /// 也不啟動可跨測試存活的工作，因此無額外 cleanup owner。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(OperationExecutionResult.Success(new { value = Array.Empty<object>() }));
        }
    }

    /// <summary>
    /// Testing-only authentication handler。身分只來自每個 Factory 的 in-memory configuration，完全忽略 HTTP headers；
    /// 型別為私有巢狀且 scheme 只在 ConfigureTestServices 註冊，避免任何 fake principal 路徑進入 Development／Production。
    /// </summary>
    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        /// <summary>取得只在本測試 Factory 使用的 scheme 名稱。</summary>
        public const string SchemeName = "TestWorkload";

        /// <summary>
        /// 建立由 ASP.NET Core request scope 擁有的測試 handler；handler 不建立 timer、socket、subscription 或背景 Task，
        /// 基底類別在 request pipeline 結束時負責其正常生命週期。
        /// </summary>
        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <summary>
        /// 只把 server-side 測試設定轉成 authenticated ClaimsPrincipal，並可加入同樣來自 server fixture 的 PrimarySid；
        /// 空值回傳 NoResult 以觸發正式 authorization challenge，且刻意不讀取 X-Principal、X-Workload 或其他 caller-controlled header。
        /// </summary>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principalName = Context.RequestServices
                .GetRequiredService<IConfiguration>()["DynamicsGateway:TestPrincipalName"];
            if (string.IsNullOrWhiteSpace(principalName))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.Name, principalName) };
            var windowsSid = Context.RequestServices
                .GetRequiredService<IConfiguration>()["DynamicsGateway:TestWindowsSid"];
            if (!string.IsNullOrWhiteSpace(windowsSid))
            {
                claims.Add(new Claim(ClaimTypes.PrimarySid, windowsSid));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
