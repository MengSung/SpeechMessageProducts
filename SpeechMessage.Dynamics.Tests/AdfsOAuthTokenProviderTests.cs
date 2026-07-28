// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs
// 目的：驗證 ADFS OAuth token 提供者的本機 password grant 與 bearer 快取行為。
//
// 保姆級教學：
// 1. 這些測試不連真實 ADFS；用 fake HttpMessageHandler 模擬 token endpoint。
// 2. 重點是：
//    - password grant 會送 client_id / resource / username / password
//    - CredentialReferenceName 有值時直接回傳 bearer，不打 token endpoint
//    - 成功後會快取 token，第二次呼叫不再 HTTP
// 3. jesus IFD 正式連線仍需真實 ADFS ClientId；此處只保證程式契約正確。
// ============================================================================

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class AdfsOAuthTokenProviderTests
{
    [Fact]
    public async Task Direct_bearer_secret_skips_token_endpoint()
    {
        var called = false;
        var provider = CreateProvider(
            options: new DynamicsWebApiOptions
            {
                AuthMode = DynamicsAuthMode.AdfsOAuth,
                CredentialReferenceName = "PREISSUED_TOKEN",
                AuthorityUri = "https://sts.example.local/adfs",
                ClientId = "client-1",
                ResourceUri = "https://crm.example.local/",
                TimeoutSeconds = 10
            },
            secrets: new Dictionary<string, string>
            {
                ["PREISSUED_TOKEN"] = "preissued-access-token"
            },
            responder: _ =>
            {
                called = true;
                return JsonResponse("""{"access_token":"should-not-use","expires_in":3600}""");
            });

        var token = await provider.GetAccessTokenAsync();

        token.Should().Be("preissued-access-token");
        called.Should().BeFalse();
    }

    [Fact]
    public async Task Password_grant_posts_expected_form_and_caches_token()
    {
        var callCount = 0;
        HttpRequestMessage? seen = null;
        string? body = null;

        var provider = CreateProvider(
            options: new DynamicsWebApiOptions
            {
                AuthMode = DynamicsAuthMode.AdfsOAuth,
                AuthorityUri = "https://sts.example.local/adfs",
                ClientId = "client-xyz",
                ResourceUri = "https://jesus.example.local/",
                UserNameSecretName = "USER_SECRET",
                PasswordSecretName = "PASS_SECRET",
                AllowLocalDevPasswordGrant = true,
                TimeoutSeconds = 10
            },
            secrets: new Dictionary<string, string>
            {
                ["USER_SECRET"] = @"SPEECHMESSAGE\Administrator",
                ["PASS_SECRET"] = "not-a-real-password"
            },
            responder: request =>
            {
                callCount++;
                seen = request;
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonResponse("""{"access_token":"adfs-token-001","expires_in":1200,"token_type":"bearer"}""");
            });

        var first = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        first.Should().Be("adfs-token-001");
        second.Should().Be("adfs-token-001");
        callCount.Should().Be(1, "token must be cached until near expiry");

        seen.Should().NotBeNull();
        seen!.Method.Should().Be(HttpMethod.Post);
        seen.RequestUri!.AbsoluteUri.Should().Be("https://sts.example.local/adfs/oauth2/token");
        body.Should().NotBeNull();
        body.Should().Contain("grant_type=password");
        body.Should().Contain("client_id=client-xyz");
        body.Should().Contain("resource=" + Uri.EscapeDataString("https://jesus.example.local/"));
        body.Should().Contain("username=" + Uri.EscapeDataString(@"SPEECHMESSAGE\Administrator"));
        body.Should().Contain("password=not-a-real-password");
    }

    [Fact]
    public async Task Password_grant_disabled_fails_closed()
    {
        var provider = CreateProvider(
            options: new DynamicsWebApiOptions
            {
                AuthMode = DynamicsAuthMode.AdfsOAuth,
                AuthorityUri = "https://sts.example.local/adfs",
                ClientId = "client-xyz",
                ResourceUri = "https://jesus.example.local/",
                UserNameSecretName = "USER_SECRET",
                PasswordSecretName = "PASS_SECRET",
                AllowLocalDevPasswordGrant = false,
                TimeoutSeconds = 10
            },
            secrets: new Dictionary<string, string>
            {
                ["USER_SECRET"] = "u",
                ["PASS_SECRET"] = "p"
            },
            responder: _ => JsonResponse("""{"access_token":"x","expires_in":60}"""));

        var act = async () => await provider.GetAccessTokenAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no usable token source*");
    }

    [Fact]
    public async Task Token_endpoint_error_does_not_retain_or_echo_response_body()
    {
        const string sensitiveResponseMarker = "server-response-must-not-be-retained";
        var provider = CreateProvider(
            options: CreatePasswordGrantOptions(),
            secrets: CreatePasswordGrantSecrets(),
            responder: _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(sensitiveResponseMarker, Encoding.UTF8, "text/plain")
            });

        var act = async () => await provider.GetAccessTokenAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().NotContain(sensitiveResponseMarker);
        exception.Which.Message.Should().NotContain("BodyPreview");
    }

    [Fact]
    public async Task Oversized_token_response_is_rejected_before_unbounded_buffering()
    {
        var oversizedJson = "{\"access_token\":\"" + new string('x', 65_536) + "\"}";
        var provider = CreateProvider(
            options: CreatePasswordGrantOptions(),
            secrets: CreatePasswordGrantSecrets(),
            responder: _ => JsonResponse(oversizedJson));

        var act = async () => await provider.GetAccessTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*maximum supported size*");
    }

    [Fact]
    public async Task Factory_token_client_uses_the_configured_bounded_timeout()
    {
        var options = CreatePasswordGrantOptions();
        options.TimeoutSeconds = 7;
        var factory = new StubHttpClientFactory(_ => JsonResponse("""{"access_token":"token","expires_in":60}"""));
        var provider = new AdfsOAuthTokenProvider(
            Options.Create(options),
            new DictionarySecretResolver(CreatePasswordGrantSecrets()),
            NullLogger<AdfsOAuthTokenProvider>.Instance,
            factory);

        _ = await provider.GetAccessTokenAsync();

        factory.LastClient.Should().NotBeNull();
        factory.LastClient!.Timeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task Factory_token_client_wrapper_is_disposed_after_token_request()
    {
        var factory = new TrackingHttpClientFactory(
            _ => JsonResponse("""{"access_token":"token","expires_in":60}"""));
        var provider = new AdfsOAuthTokenProvider(
            Options.Create(CreatePasswordGrantOptions()),
            new DictionarySecretResolver(CreatePasswordGrantSecrets()),
            NullLogger<AdfsOAuthTokenProvider>.Instance,
            factory);

        _ = await provider.GetAccessTokenAsync();

        factory.LastClient.Should().NotBeNull();
        factory.LastClient!.IsDisposed.Should().BeTrue();
    }


    [Fact]
    public async Task Refresh_token_grant_posts_expected_form()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        var storePath = Path.Combine(Path.GetTempPath(), "adfs-local-token-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            LocalDevAdfsTokenStore.Save(storePath, new LocalDevAdfsTokenRecord
            {
                RefreshToken = "refresh-abc",
                AccessToken = "old-access",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            });

            var provider = CreateProvider(
                options: new DynamicsWebApiOptions
                {
                    AuthMode = DynamicsAuthMode.AdfsOAuth,
                    AuthorityUri = "https://sts.example.local/adfs",
                    ClientId = "client-xyz",
                    ResourceUri = "https://jesus.example.local/",
                    LocalDevTokenStorePath = storePath,
                    AllowLocalDevPasswordGrant = false,
                    TimeoutSeconds = 10
                },
                secrets: new Dictionary<string, string>(),
                responder: request =>
                {
                    seen = request;
                    body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return JsonResponse("""{"access_token":"refreshed-001","expires_in":900,"refresh_token":"refresh-abc"}""");
                });

            var token = await provider.GetAccessTokenAsync();
            token.Should().Be("refreshed-001");
            seen!.RequestUri!.AbsoluteUri.Should().Be("https://sts.example.local/adfs/oauth2/token");
            body.Should().Contain("grant_type=refresh_token");
            body.Should().Contain("refresh_token=refresh-abc");
            body.Should().Contain("client_id=client-xyz");
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }
    private static AdfsOAuthTokenProvider CreateProvider(
        DynamicsWebApiOptions options,
        IReadOnlyDictionary<string, string> secrets,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var factory = new StubHttpClientFactory(responder);
        return new AdfsOAuthTokenProvider(
            Options.Create(options),
            new DictionarySecretResolver(secrets),
            NullLogger<AdfsOAuthTokenProvider>.Instance,
            factory);
    }

    private static DynamicsWebApiOptions CreatePasswordGrantOptions()
        => new()
        {
            AuthMode = DynamicsAuthMode.AdfsOAuth,
            AuthorityUri = "https://sts.example.local/adfs",
            ClientId = "client-xyz",
            ResourceUri = "https://jesus.example.local/",
            UserNameSecretName = "USER_SECRET",
            PasswordSecretName = "PASS_SECRET",
            AllowLocalDevPasswordGrant = true,
            TimeoutSeconds = 10
        };

    private static IReadOnlyDictionary<string, string> CreatePasswordGrantSecrets()
        => new Dictionary<string, string>
        {
            ["USER_SECRET"] = "unit-test-user",
            ["PASS_SECRET"] = "unit-test-password"
        };

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>
    /// 測試用 IHttpClientFactory：固定回傳帶 stub handler 的 HttpClient。
    /// </summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _handler = new StubHandler(responder);
        }

        public HttpClient? LastClient { get; private set; }

        public HttpClient CreateClient(string name)
            => LastClient = new(_handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class TrackingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TrackingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _handler = new StubHandler(responder);
        }

        public TrackingHttpClient? LastClient { get; private set; }

        public HttpClient CreateClient(string name)
            => LastClient = new TrackingHttpClient(_handler);
    }

    private sealed class TrackingHttpClient : HttpClient
    {
        public TrackingHttpClient(HttpMessageHandler handler)
            : base(handler, disposeHandler: false)
        {
        }

        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
