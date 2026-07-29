using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
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
/// 驗證 Development Local Gateway 的 transport 與 workload identity 邊界。
/// Development 必須固定使用 Microsoft Negotiate，且受保護端點只接受 HTTPS loopback 連線；
/// 測試刻意註冊一個會信任惡意 header 的 fake scheme，證明正式 Development 預設 scheme 不會被設定值或測試 DI 污染。
/// 每個 <see cref="WebApplicationFactory{TEntryPoint}"/> 由單一測試以 <c>await using</c> 擁有並確定性停止 Kestrel／TestServer，
/// 測試 executor 不建立 CRM、SQL、Token、Socket、timer 或背景工作，因此 disposal 後不留下跨案例可變狀態。
/// </summary>
public sealed class GatewayKestrelNegotiateTests
{
    private const string BoundPrincipalName = @"IIS APPPOOL\ChurchReport";
    private const string ProtectedOperationPath =
        "/v1/organizations/crm82/operations/runtime.health.whoami";

    /// <summary>
    /// Development 即使收到設定檔指定的 Testing fake scheme 與 X-Principal／X-Workload spoof headers，
    /// 仍必須先由 Kestrel 的 server-owned address feature 證明唯一 listener 使用核准的 localhost:7244，
    /// 再由 Negotiate handler challenge；caller-controlled headers 不得建立任何 ClaimsPrincipal 或進入 executor。
    /// Factory 是 listener、client 與 service provider 的唯一生命週期 owner，測試完成後會依序 Dispose，且不保留跨案例連線或可變狀態。
    /// </summary>
    [Fact]
    public async Task Development_kestrel_challenges_with_negotiate_and_ignores_spoofed_identity_headers()
    {
        await using var factory = CreateDevelopmentGatewayFactory(useKestrel: true);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var addresses = factory.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses;
        addresses.Should().Contain("https://localhost:7244");

        client.BaseAddress = new Uri("https://localhost:7244", UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Post, ProtectedOperationPath)
        {
            Content = JsonContent.Create(new { parameters = new Dictionary<string, object?>() })
        };
        request.Headers.TryAddWithoutValidation("X-Principal", BoundPrincipalName).Should().BeTrue();
        request.Headers.TryAddWithoutValidation("X-Workload", "church-report-service").Should().BeTrue();

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Select(static value => value.Scheme)
            .Should().Contain("Negotiate");
        factory.Services.GetRequiredService<BoundaryRecordingExecutor>().CallCount.Should().Be(0);
    }

    /// <summary>
    /// 驗證 Development provider 合併基底設定後，仍會把 crm82 的 production URI、ADFS metadata 與所有秘密參考完整替換為不可路由的本機安全邊界。
    /// 這個檢查只讀取 Host 已發布的 configuration snapshot，不解析環境變數中的秘密、不建立 Dynamics transport，也不啟動額外背景工作；
    /// Factory 是 configuration、service provider 與 TestServer 的唯一 owner，案例結束時確定性 Dispose，避免跨測試保留 profile 或 credential reference。
    /// 若未來基底 production profile 新增秘密欄位而 Development 未明確清空，本測試必須 fail closed，阻止開發工作站因既有環境變數而接觸 production CRM。
    /// </summary>
    [Fact]
    public async Task Development_profile_replaces_production_target_and_secret_references()
    {
        await using var factory = CreateDevelopmentGatewayFactory(useKestrel: false);
        using var client = factory.CreateClient();
        var profile = factory.Services
            .GetRequiredService<IConfiguration>()
            .GetSection("DynamicsProfiles:Profiles:crm82");

        profile["OrganizationBaseUri"].Should().Be("https://dynamics-local.invalid/");
        profile["OrganizationWebApiBaseUri"]
            .Should().Be("https://dynamics-local.invalid/api/data/v8.2/");
        profile["AuthMode"].Should().Be("Windows");
        profile["CredentialSource"].Should().Be("HostIdentity");
        profile.GetValue<bool>("WarmUpOnActivation").Should().BeFalse();

        // Development 必須逐一遮蔽所有會驅動 production token／credential resolution 的 key；
        // 空值只存在 configuration snapshot，不建立 cache、subscription 或跨 request mutable state。
        var forbiddenReferenceKeys = new[]
        {
            "SecretReference",
            "UserNameSecretName",
            "PasswordSecretName",
            "DomainSecretName",
            "AuthorityUri",
            "AuthoritySecretName",
            "ResourceUri",
            "ClientId",
            "ClientIdSecretName",
            "ClientSecretName",
            "CredentialReferenceName"
        };
        foreach (var key in forbiddenReferenceKeys)
        {
            profile[key].Should().BeNullOrEmpty();
        }
    }

    /// <summary>
    /// HTTP loopback request 必須在 Negotiate handshake、request materialization 與 executor 前收到 403；
    /// transport confidentiality 與 localhost process boundary 是 authentication 之前的先決條件。
    /// </summary>
    [Fact]
    public async Task Development_http_loopback_request_is_forbidden_before_authentication_and_executor()
    {
        await using var factory = CreateDevelopmentGatewayFactory(useKestrel: false);

        var responseContext = await SendOperationWithConnectionMetadataAsync(
            factory,
            scheme: "http",
            remoteIpAddress: IPAddress.Loopback);

        responseContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        factory.Services.GetRequiredService<BoundaryRecordingExecutor>().CallCount.Should().Be(0);
    }

    /// <summary>
    /// 即使使用 HTTPS，只要 peer address 不是 loopback 就必須在 authentication 前 fail closed；
    /// 判斷只讀取 server connection metadata，不能信任 X-Forwarded-For 等 caller-controlled headers。
    /// </summary>
    [Fact]
    public async Task Development_https_remote_request_is_forbidden_before_authentication_and_executor()
    {
        await using var factory = CreateDevelopmentGatewayFactory(useKestrel: false);

        var responseContext = await SendOperationWithConnectionMetadataAsync(
            factory,
            scheme: "https",
            remoteIpAddress: IPAddress.Parse("203.0.113.10"));

        responseContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        factory.Services.GetRequiredService<BoundaryRecordingExecutor>().CallCount.Should().Be(0);
    }

    /// <summary>
    /// 缺少 peer address 時不能推測為 localhost；Development operation 必須拒絕未知 connection boundary，
    /// 避免 TestServer、proxy 或未來 hosting adapter 因 metadata 缺失而默認放行。
    /// </summary>
    [Fact]
    public async Task Development_https_request_without_remote_address_fails_closed_before_authentication()
    {
        await using var factory = CreateDevelopmentGatewayFactory(useKestrel: false);

        var responseContext = await SendOperationWithConnectionMetadataAsync(
            factory,
            scheme: "https",
            remoteIpAddress: null);

        responseContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        factory.Services.GetRequiredService<BoundaryRecordingExecutor>().CallCount.Should().Be(0);
    }

    /// <summary>
    /// 建立 Development 測試 Host（真實 Kestrel challenge 或可注入 metadata 的 TestServer），並移除唯一會連 durable SQL control-plane 的 readiness hosted service，
    /// 並以純記憶體 executor 取代 outbound runtime。Fake scheme 雖被註冊，卻不得成為 Development default；
    /// Factory disposal 是 server、service provider 與所有 request scope 的唯一 cleanup owner。
    /// </summary>
    private static WebApplicationFactory<Program> CreateDevelopmentGatewayFactory(bool useKestrel)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // RED 基線刻意要求一個 fake scheme；GREEN 必須由 Program 忽略此值並固定使用 Negotiate。
                    ["DynamicsGateway:AuthenticationScheme"] = HeaderTrustingFakeAuthenticationHandler.SchemeName
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

                // 不指定 default scheme；這個惡意 fake 只用來證明 Development 的 Program default 不受測試設定污染。
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, HeaderTrustingFakeAuthenticationHandler>(
                        HeaderTrustingFakeAuthenticationHandler.SchemeName,
                        _ => { });
                services.RemoveAll<IDynamicsOperationExecutor>();
                services.AddSingleton<BoundaryRecordingExecutor>();
                services.AddSingleton<IDynamicsOperationExecutor>(serviceProvider =>
                    serviceProvider.GetRequiredService<BoundaryRecordingExecutor>());
            });
        });

        if (useKestrel)
        {
            // .NET 10 WebApplicationFactory 的 Kestrel mode 才提供 Negotiate 所需的 connection items；
            // listener 使用 appsettings.Development.json 的 HTTPS localhost:7244，Factory disposal 是 listener 的唯一 cleanup owner。
            factory.UseKestrel();
        }

        return factory;
    }

    /// <summary>
    /// 透過 TestServer 直接設定不可由一般 HTTP header 偽造的 connection metadata，
    /// 只測 transport filter 位於 authentication 前的排序。Request body stream 由本方法唯一擁有，等待 pipeline 完成後才 Dispose，
    /// 因此不會在非同步 JSON binding 尚未完成時提早回收 buffer。
    /// </summary>
    private static async Task<HttpContext> SendOperationWithConnectionMetadataAsync(
        WebApplicationFactory<Program> factory,
        string scheme,
        IPAddress? remoteIpAddress)
    {
        using var requestBody = new MemoryStream(Encoding.UTF8.GetBytes("{\"parameters\":{}}"));
        return await factory.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethod.Post.Method;
            context.Request.Scheme = scheme;
            context.Request.Host = new HostString("localhost");
            context.Request.Path = ProtectedOperationPath;
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = requestBody.Length;
            context.Request.Body = requestBody;
            context.Connection.RemoteIpAddress = remoteIpAddress;
        });
    }

    /// <summary>
    /// 記錄通過 transport 與 authorization 邊界的 request；實例只屬於單一 Factory，
    /// 不做 outbound I/O、不啟動背景 Task，讓 CallCount=0 成為「executor 前拒絕」的直接生命週期證據。
    /// </summary>
    private sealed class BoundaryRecordingExecutor : IDynamicsOperationExecutor
    {
        /// <summary>取得 executor 呼叫次數；單一 Factory 的測試流程循序存取，不跨執行緒共享。</summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// 記錄 request 後立即回傳固定成功結果；不保留 ClaimsPrincipal、HttpContext、stream、credential 或 cancellation registration。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(OperationExecutionResult.Success(new { value = Array.Empty<object>() }));
        }
    }

    /// <summary>
    /// 刻意示範禁止進入正式環境的反例 handler：它信任 X-Principal header 並產生 authenticated principal。
    /// 型別為私有巢狀且只在測試 Host 註冊；若 Development 誤把 configuration scheme 當 default，本測試會被錯誤放行而失敗。
    /// </summary>
    private sealed class HeaderTrustingFakeAuthenticationHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        /// <summary>取得測試用惡意 scheme 名稱；正式 Gateway 不得選用。</summary>
        public const string SchemeName = "HeaderTrustingDevelopmentFake";

        /// <summary>
        /// 建立 request-scoped fake handler；基底類別擁有正常 handler lifecycle，本型別不建立 socket、timer、subscription 或背景 Task。
        /// </summary>
        public HeaderTrustingFakeAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <summary>
        /// 從惡意 X-Principal header 建立 principal，僅供負向測試偵測 scheme 外洩；
        /// 缺少 header 時回傳 NoResult，且不保留 header、claims 或 request reference。
        /// </summary>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principalName = Request.Headers["X-Principal"].ToString();
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
