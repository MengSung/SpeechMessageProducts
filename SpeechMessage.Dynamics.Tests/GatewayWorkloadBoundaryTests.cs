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
using SpeechMessage.Dynamics.Gateway.Security;

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
    private const string TestingBindingSetName = "Testing";

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

    /// <summary>
    /// 在已通過 route alias 與 operation 授權後，惡意本文欄位仍不得覆寫 server-mapped workload。
    /// 測試刻意使用已授權 alias，確保 400 來自有界 JSON 契約驗證，而不是在讀取本文前就被 403 授權邊界攔截；
    /// executor 必須維持零呼叫，避免跨產品／租戶容量與稽核歸屬污染。
    /// </summary>
    [Fact]
    public async Task Hostile_body_identity_cannot_override_server_mapped_workload()
    {
        const string principal = @"SPEECHMESSAGE\ChurchReport$";
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(executor, principal, mapped: true);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            $"/v1/organizations/{DefaultProfileAlias}/operations/{DefaultOperationId}",
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
    /// 成功的產品操作與已授權操作目錄都可能包含 workload 可見的 CRM 業務資料或 capability 資訊，
    /// 因此必須宣告 <c>Cache-Control: no-store, private</c>，不可讓瀏覽器、反向 Proxy 或共享快取重播。
    /// Factory、HttpClient、response 與測試 executor 均由此 test scope 唯一擁有並依 using/await using 順序釋放；
    /// 測試不建立 CRM socket、token、timer、background task 或跨 request cache。
    /// </summary>
    [Fact]
    public async Task Authorized_operation_and_catalog_responses_are_private_no_store()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(executor, DefaultPrincipalName, mapped: true);
        using var client = factory.CreateClient();

        using var operationResponse = await client.PostAsync(
            $"/v1/organizations/{DefaultProfileAlias}/operations/{DefaultOperationId}",
            Json("{\"parameters\":{}}"));
        using var catalogResponse = await client.GetAsync("/v1/operations");

        operationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        catalogResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        operationResponse.Headers.CacheControl.Should().NotBeNull();
        operationResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        operationResponse.Headers.CacheControl.Private.Should().BeTrue();
        catalogResponse.Headers.CacheControl.Should().NotBeNull();
        catalogResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        catalogResponse.Headers.CacheControl.Private.Should().BeTrue();
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
            ["DynamicsGateway:WorkloadBindingSets:Testing:2:WindowsSid"] = windowsSid,
            ["DynamicsGateway:WorkloadBindingSets:Testing:2:WorkloadSubjectId"] = "sid-bound-service",
            ["DynamicsGateway:WorkloadBindingSets:Testing:2:ProfileAliases:0"] = DefaultProfileAlias,
            ["DynamicsGateway:WorkloadBindingSets:Testing:2:CapabilityOperationIds:0"] = DefaultOperationId
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
    /// 驗證 authenticated principal 一旦提供語法有效的 Windows SID，該 SID 就是唯一可信的安全主體鍵；
    /// 即使 principal name 與既有 binding 完全相同，未 mapping 的新 SID 也不得回退到名稱授權。這個 fail-closed 順序可防止舊帳號移除後，
    /// 取得相同名稱但不同 SID 的新帳號繼承舊 workload 的 alias、operation、容量與稽核權限。只有 principal 根本沒有可用 SID 時，
    /// 才允許以完整 exact principal name 相容。測試 executor 只記錄單一 request snapshot，不建立 admission、transport、token 或背景工作；
    /// Factory disposal 是 Host、request scope 與測試 handler 的唯一 cleanup owner，拒絕路徑不應產生需要 drain 或 dispose 的外部資源。
    /// </summary>
    [Fact]
    public async Task Valid_unmapped_sid_does_not_fall_back_to_same_principal_name_binding()
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

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        executor.CallCount.Should().Be(0);
        executor.LastRequest.Should().BeNull();
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
            .Should().BeTrue("crm91 必須是 official worker catalog entry，才能證明拒絕來自 binding 而不是 catalog miss");

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
    /// 快取邊界必須在 authentication/authorization 之前建立，因為 401 會在 endpoint delegate 執行前返回，
    /// 而授權後的 403 與 request-body reader 的 415 也都不應留下可被任何快取重播的產品路由結果。
    /// 每個 Factory 各自持有獨立 Testing Host 與 executor，避免 principal/binding 狀態跨案例共用；所有 client、
    /// response 與 host 於本測試結束時確定釋放，沒有保留 session、credential、token、stream 或背景資源。
    /// </summary>
    [Fact]
    public async Task Operation_and_catalog_controlled_error_responses_are_private_no_store()
    {
        var authorizedExecutor = new RecordingExecutor();
        await using var authorizedFactory = CreateFactory(
            authorizedExecutor,
            DefaultPrincipalName,
            mapped: true);
        using var authorizedClient = authorizedFactory.CreateClient();
        using var unsupportedMediaType = await authorizedClient.PostAsync(
            $"/v1/organizations/{DefaultProfileAlias}/operations/{DefaultOperationId}",
            new StringContent("{}", Encoding.UTF8, "text/plain"));

        var unmappedExecutor = new RecordingExecutor();
        await using var unmappedFactory = CreateFactory(
            unmappedExecutor,
            @"SPEECHMESSAGE\UnmappedService$",
            mapped: false);
        using var unmappedClient = unmappedFactory.CreateClient();
        using var forbiddenCatalog = await unmappedClient.GetAsync("/v1/operations");

        await using var anonymousFactory = CreateFactory(
            new RecordingExecutor(),
            principalName: null,
            mapped: true);
        using var anonymousClient = anonymousFactory.CreateClient();
        using var unauthorizedCatalog = await anonymousClient.GetAsync("/v1/operations");

        unsupportedMediaType.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        forbiddenCatalog.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unauthorizedCatalog.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        foreach (var response in new[] { unsupportedMediaType, forbiddenCatalog, unauthorizedCatalog })
        {
            response.Headers.CacheControl.Should().NotBeNull();
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            response.Headers.CacheControl.Private.Should().BeTrue();
        }
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
    /// Development provider 必須完整取代 Production workload binding 集合，不能依賴 JSON array index 的逐葉覆寫語意。
    /// 本案例直接依正式 Host 的順序載入 base 與 Development JSON，再使用 base 檔案中的 IIS APPPOOL principal 嘗試授權；
    /// 正確設計只允許 Development 選到 Local binding set，因此正式 principal 必須在建立 executor request、取得 admission permit、
    /// 解析 secret 或建立 Dynamics socket 前得到 fail-closed 的 unmapped-principal。Configuration、ClaimsPrincipal 與 authorizer
    /// 全部只屬於本測試方法，不建立 reload subscription、timer、背景 Task 或 disposable resource，也不會把實際 identity 寫入測試輸出。
    /// </summary>
    [Fact]
    public void Development_configuration_does_not_inherit_central_workload_binding()
    {
        var gatewayProjectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "SpeechMessage.Dynamics.Gateway"));
        var configuration = new ConfigurationBuilder()
            .SetBasePath(gatewayProjectPath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();
        var centralPrincipalName = configuration[
            "DynamicsGateway:WorkloadBindingSets:Central:0:PrincipalName"];
        centralPrincipalName.Should().NotBeNullOrWhiteSpace(
            "RED 基線必須先證明 base configuration 確實包含 Central Gateway principal");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, centralPrincipalName!) },
            TestAuthenticationHandler.SchemeName));
        var authorizer = new ConfigurationGatewayOperationAuthorizer(
            configuration,
            new[] { DefaultProfileAlias });

        var authorization = authorizer.Authorize(
            principal,
            DefaultProfileAlias,
            DefaultOperationId);

        authorization.Succeeded.Should().BeFalse();
        authorization.FailureCode.Should().Be("unmapped-principal");
    }

    /// <summary>
    /// Binding set selector 是 deployment-owned authorization authority；缺少、空白、前後空白、wildcard、含 section delimiter
    /// 或不存在的名稱都不能回退到 base、第一組或全部集合。特別覆蓋 <c>Local:0</c>，確保 selector 不會被串接成
    /// configuration path 而穿越具名集合邊界。
    /// 每筆 Theory 使用獨立 Factory 與 configuration snapshot，Host 必須在 listener、executor、admission、secret resolution 與 outbound socket
    /// 建立前同步中止；測試不啟動背景工作，也不保留 configuration reload subscription，因此 Factory Dispose 是唯一 cleanup 邊界。
    /// </summary>
    /// <param name="invalidSelector">刻意不合法或不存在的 selector；只屬於測試設定，不接受 HTTP request 輸入。</param>
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData(" Testing")]
    [InlineData("Testing ")]
    [InlineData("*")]
    [InlineData("?")]
    [InlineData("Local:0")]
    [InlineData("Missing")]
    public void Invalid_active_workload_binding_set_fails_host_startup(string? invalidSelector)
    {
        using var factory = CreateFactory(
            new RecordingExecutor(),
            DefaultPrincipalName,
            mapped: true,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["DynamicsGateway:ActiveWorkloadBindingSet"] = invalidSelector
            });

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// selector 對應的 section 若只是 scalar，就不是可 materialize 的 binding collection，必須在 Host startup fail closed。
    /// 這個案例刻意與真正的 childless JSON object 分開，避免測試名稱過度聲明未驗證的 provider 形狀。
    /// Factory、Host 與 service provider 由本測試同步 Dispose；失敗發生在 executor、CRM／SQL 連線、timer、subscription 或背景任務建立之前。
    /// </summary>
    [Fact]
    public void Selected_scalar_workload_binding_set_fails_host_startup()
    {
        using var factory = CreateFactory(
            new RecordingExecutor(),
            DefaultPrincipalName,
            mapped: true,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["DynamicsGateway:ActiveWorkloadBindingSet"] = "Empty",
                ["DynamicsGateway:WorkloadBindingSets:Empty"] = "declared-without-bindings"
            });

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// 真實 JSON 中的空 object 不會產生 binding children；即使 selector 精確指向該集合，authorizer 仍必須在建立 frozen dictionary 前拒絕。
    /// 測試使用 <see cref="ConfigurationBuilder.AddJsonStream(Stream)"/> 保留 JSON provider 的真實 childless 語意，不以 in-memory scalar 假裝空集合。
    /// JSON stream 由本方法唯一擁有並確定性釋放；configuration snapshot 只含非秘密字串，不建立 reload subscription、timer、Task 或其他 cleanup owner。
    /// </summary>
    [Fact]
    public void Selected_childless_json_workload_binding_set_fails_authorizer_construction()
    {
        const string configurationJson = """
            {
              "DynamicsGateway": {
                "ActiveWorkloadBindingSet": "Empty",
                "WorkloadBindingSets": {
                  "Empty": {}
                }
              }
            }
            """;
        using var configurationStream = new MemoryStream(Encoding.UTF8.GetBytes(configurationJson));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(configurationStream)
            .Build();

        var construct = () => new ConfigurationGatewayOperationAuthorizer(
            configuration,
            new[] { DefaultProfileAlias });

        construct.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one binding*");
    }

    /// <summary>
    /// configuration provider 可同時對一個 section 提供 scalar value 與 child leaves；這種歧義形狀不能因 children 看似完整就被接受。
    /// Authorizer 必須在解析任何 principal、alias 或 operation 之前拒絕 scalar-plus-children，避免 provider 優先序成為隱藏的授權開關。
    /// Factory 是 Host 與 service provider 的唯一 owner；startup 失敗前不得建立 executor call、admission permit、secret、socket 或需要 drain 的背景工作。
    /// </summary>
    [Fact]
    public void Selected_scalar_with_children_workload_binding_set_fails_host_startup()
    {
        var scalarWithChildren = new Dictionary<string, string?>
        {
            ["DynamicsGateway:ActiveWorkloadBindingSet"] = "Ambiguous",
            ["DynamicsGateway:WorkloadBindingSets:Ambiguous"] = "scalar-value",
            ["DynamicsGateway:WorkloadBindingSets:Ambiguous:0:PrincipalName"] =
                DefaultPrincipalName,
            ["DynamicsGateway:WorkloadBindingSets:Ambiguous:0:WorkloadSubjectId"] =
                "ambiguous-service",
            ["DynamicsGateway:WorkloadBindingSets:Ambiguous:0:ProfileAliases:0"] =
                DefaultProfileAlias,
            ["DynamicsGateway:WorkloadBindingSets:Ambiguous:0:CapabilityOperationIds:0"] =
                DefaultOperationId
        };

        using var factory = CreateFactory(
            new RecordingExecutor(),
            DefaultPrincipalName,
            mapped: false,
            configurationOverrides: scalarWithChildren);

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*scalar value*");
    }

    /// <summary>
    /// 具名集合的 selector 比對刻意不分大小寫，但仍是完整名稱 equality；這允許部署工具的 casing 差異，不會導入 prefix、wildcard 或 path traversal 語意。
    /// 案例透過完整 HTTP pipeline 驗證只有 <c>Testing</c> 集合被 materialize，且核准 request 只呼叫一次 executor。
    /// Factory disposal 回收 Host、handler 與 request scope；測試 executor 不建立 CRM、token、timer、background Task 或其他長生命資源。
    /// </summary>
    [Fact]
    public async Task Active_workload_binding_set_selection_is_case_insensitive()
    {
        var executor = new RecordingExecutor();
        await using var factory = CreateFactory(
            executor,
            DefaultPrincipalName,
            mapped: true,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["DynamicsGateway:ActiveWorkloadBindingSet"] = "tEsTiNg"
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            $"/v1/organizations/{DefaultProfileAlias}/operations/{DefaultOperationId}",
            Json("{\"parameters\":{}}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        executor.CallCount.Should().Be(1);
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
                ["DynamicsGateway:WorkloadBindingSets:Testing:1:CapabilityOperationIds:1"] =
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
                    // 第二個 profile 使用 official CE 9.1 worker metadata 與不可路由的 Organization identity，只用來證明
                    // crm91 是 known catalog entry；不含 WebApi endpoint、auth 或 credential。readiness 已移除且 executor
                    // 被記憶體 double 取代，因此不建立 process、pipe、host-slot renewal 或背景 cleanup owner。
                    ["DynamicsProfiles:Profiles:crm91:WorkerProfileGenerationId"] =
                        "crm91-testing-0001",
                    ["DynamicsProfiles:Profiles:crm91:WorkerKind"] = "OfficialCrm91Worker",
                    ["DynamicsProfiles:Profiles:crm91:OrganizationBaseUri"] =
                        "https://crm91.invalid/",
                    ["DynamicsProfiles:Profiles:crm91:WorkerExecutablePath"] =
                        "test-workers\\SpeechMessage.Dynamics.Crm91Worker.exe",
                    ["DynamicsProfiles:Profiles:crm91:WorkerExecutableSha256"] =
                        "1111111111111111111111111111111111111111111111111111111111111111",
                    ["DynamicsProfiles:Profiles:crm91:PackageLockId"] =
                        "crm91-xrmtooling-9.1.1.65-core-9.0.2.60",
                    ["DynamicsProfiles:Profiles:crm91:WorkerCount"] = "1",
                    ["DynamicsProfiles:Profiles:crm91:MaxInFlightPerWorker"] = "1",
                    ["DynamicsProfiles:Profiles:crm91:WarmUpOnActivation"] = "false",
                    ["DynamicsProfiles:Profiles:crm91:Admission:ExpectedOrganizationId"] =
                        "22222222-2222-2222-2222-222222222222",
                    ["DynamicsGateway:AuthenticationScheme"] = TestAuthenticationHandler.SchemeName,
                    ["DynamicsGateway:TestPrincipalName"] = principalName,
                    ["DynamicsGateway:ActiveWorkloadBindingSet"] = TestingBindingSetName
                };
                // Active set 即使沒有案例專屬 mapping 也必須是有效非空集合，讓 401／unmapped 403 測試驗證 request 邊界，
                // 而不是被 startup selector validation 提前取代。這筆 reserved principal 永遠不會由測試 handler 發出，
                // 只存在於單一 Factory snapshot，沒有跨案例 mutable state 或額外 cleanup owner。
                foreach (var pair in CreateBindingValues(
                    index: 0,
                    principalName: @"SPEECHMESSAGE\TestingBaseline$",
                    workloadSubjectId: "testing-baseline-service",
                    allowedAlias: DefaultProfileAlias,
                    allowedOperation: DefaultOperationId))
                {
                    values[pair.Key] = pair.Value;
                }

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
            [$"DynamicsGateway:WorkloadBindingSets:{TestingBindingSetName}:{index}:PrincipalName"] = principalName,
            [$"DynamicsGateway:WorkloadBindingSets:{TestingBindingSetName}:{index}:WorkloadSubjectId"] = workloadSubjectId,
            [$"DynamicsGateway:WorkloadBindingSets:{TestingBindingSetName}:{index}:ProfileAliases:0"] = allowedAlias,
            [$"DynamicsGateway:WorkloadBindingSets:{TestingBindingSetName}:{index}:CapabilityOperationIds:0"] = allowedOperation
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
        /// <remarks>
        /// 此 executor 僅以封閉的 WhoAmI envelope 模擬成功，避免工作負載授權測試透過匿名資料重新引入
        /// 原始 OData <c>value</c>、profile 或 request 資訊；記錄的 request 仍由單一測試 Factory 擁有。
        /// </remarks>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(OperationExecutionResult.Success(
                OperationResponseData.ForWhoAmI(
                    OperationIds.RuntimeHealthWhoAmI,
                    "9.1",
                    new WhoAmIResponseData())));
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
