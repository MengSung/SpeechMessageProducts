// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs
// 目的：向 ADFS 取得 Web API 用的 Bearer access token（IFD/claims）。
//
// 保姆級教學：
// 1. jesus 這類 IFD 環境，Web API 不能靠 Windows NTLM；會拿到 HTTP 302 去 ADFS。
// 2. 這裡用 OAuth2 token endpoint 取 access_token，再放到 Authorization: Bearer。
// 3. 正式環境應走非密碼服務流程（client credentials / certificate）。
// 4. local-dev-manifest 才允許 username/password grant（把既有 CrmConnection 帳密當服務帳號），
//    這是為了本機 Tier A 打通 IFD，不是瀏覽器登入、也不是 per-user token pool。
// 5. token 以 profile 維度快取；過期前刷新；不做 cookie/session 持久化。
// ============================================================================

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// ADFS OAuth access token 提供者。
/// </summary>
public interface IAdfsOAuthTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 以 ADFS oauth2/token 取得並快取 access token。
/// </summary>
public sealed class AdfsOAuthTokenProvider : IAdfsOAuthTokenProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DynamicsWebApiOptions _options;
    private readonly ISecretResolver _secretResolver;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<AdfsOAuthTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public AdfsOAuthTokenProvider(
        IOptions<DynamicsWebApiOptions> options,
        ISecretResolver secretResolver,
        ILogger<AdfsOAuthTokenProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // 1) 若外部已直接提供 bearer token 秘密，優先使用（正式可接 secret store）。
        var directTokenRef = _options.CredentialReferenceName;
        if (!string.IsNullOrWhiteSpace(directTokenRef) &&
            _secretResolver.TryResolve(directTokenRef, out var directToken) &&
            !string.IsNullOrWhiteSpace(directToken))
        {
            return directToken!;
        }

        // 2) 快取未過期直接重用。
        if (!string.IsNullOrWhiteSpace(_cachedToken) &&
            DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
        {
            return _cachedToken!;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedToken) &&
                DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
            {
                return _cachedToken!;
            }

            var token = await RequestNewTokenAsync(cancellationToken).ConfigureAwait(false);
            _cachedToken = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresInSeconds));
            _logger.LogInformation(
                "ADFS access token acquired. ExpiresIn={ExpiresIn}s Authority={Authority}",
                token.ExpiresInSeconds,
                ResolveAuthority());
            return _cachedToken!;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TokenResponse> RequestNewTokenAsync(CancellationToken cancellationToken)
    {
        var authority = ResolveAuthority();
        var tokenEndpoint = authority.TrimEnd('/') + "/oauth2/token";
        var resource = ResolveResource();
        var clientId = ResolveClientId();

        using var content = new FormUrlEncodedContent(BuildTokenForm(clientId, resource));
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // IHttpClientFactory 建立的 HttpClient 不可 Dispose；只有自建 client 才 Dispose，避免 socket 洩漏。
        HttpClient? ownedClient = null;
        var http = CreateHttpClient(out ownedClient);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // 不把 body 全量丟到 UI（可能含敏感細節），只留狀態與短摘要。
                var preview = body.Length <= 300 ? body : body.Substring(0, 300);
                throw new InvalidOperationException(
                    $"ADFS token request failed HTTP {(int)response.StatusCode} from '{tokenEndpoint}'. BodyPreview={preview}");
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var accessNode) ||
                accessNode.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(accessNode.GetString()))
            {
                throw new InvalidOperationException("ADFS token response missing access_token.");
            }

            var expiresIn = 3600;
            if (root.TryGetProperty("expires_in", out var expNode))
            {
                if (expNode.ValueKind == JsonValueKind.Number && expNode.TryGetInt32(out var n))
                {
                    expiresIn = n;
                }
                else if (expNode.ValueKind == JsonValueKind.String &&
                         int.TryParse(expNode.GetString(), out var s))
                {
                    expiresIn = s;
                }
            }

            return new TokenResponse(accessNode.GetString()!, expiresIn);
        }
        finally
        {
            ownedClient?.Dispose();
        }
    }

    private List<KeyValuePair<string, string>> BuildTokenForm(string clientId, string resource)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("resource", resource),
            new("grant_type", "password")
        };

        // local-dev password grant：使用服務帳號（CrmConnection bridge / env secrets）
        if (!_options.AllowLocalDevPasswordGrant)
        {
            throw new InvalidOperationException(
                "AdfsOAuth password grant is disabled. Provide CredentialReferenceName bearer token " +
                "or enable AllowLocalDevPasswordGrant only for local-dev-manifest.");
        }

        if (string.IsNullOrWhiteSpace(_options.UserNameSecretName) ||
            string.IsNullOrWhiteSpace(_options.PasswordSecretName))
        {
            throw new InvalidOperationException(
                "AdfsOAuth local-dev password grant requires UserNameSecretName and PasswordSecretName.");
        }

        if (!_secretResolver.TryResolve(_options.UserNameSecretName, out var userName) ||
            string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("Failed to resolve ADFS username secret.");
        }

        if (!_secretResolver.TryResolve(_options.PasswordSecretName, out var password) ||
            string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Failed to resolve ADFS password secret.");
        }

        // ADFS 有時要 UPN、有時要 DOMAIN\user；先用秘密原值。
        form.Add(new("username", userName!));
        form.Add(new("password", password!));

        // 可選 client_secret（confidential client）
        if (!string.IsNullOrWhiteSpace(_options.ClientSecretName) &&
            _secretResolver.TryResolve(_options.ClientSecretName, out var clientSecret) &&
            !string.IsNullOrWhiteSpace(clientSecret))
        {
            form.Add(new("client_secret", clientSecret!));
        }

        return form;
    }

    private string ResolveAuthority()
    {
        if (!string.IsNullOrWhiteSpace(_options.AuthorityUri))
        {
            return _options.AuthorityUri.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_options.AuthoritySecretName) &&
            _secretResolver.TryResolve(_options.AuthoritySecretName, out var authority) &&
            !string.IsNullOrWhiteSpace(authority))
        {
            return authority!.Trim();
        }

        throw new InvalidOperationException(
            "AdfsOAuth requires AuthorityUri or AuthoritySecretName.");
    }

    private string ResolveResource()
    {
        if (!string.IsNullOrWhiteSpace(_options.ResourceUri))
        {
            return _options.ResourceUri.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_options.OrganizationBaseUri))
        {
            return _options.OrganizationBaseUri.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_options.OrganizationWebApiBaseUri) &&
            Uri.TryCreate(_options.OrganizationWebApiBaseUri, UriKind.Absolute, out var webApi))
        {
            return webApi.GetLeftPart(UriPartial.Authority) + "/";
        }

        throw new InvalidOperationException(
            "AdfsOAuth requires ResourceUri or OrganizationBaseUri/OrganizationWebApiBaseUri.");
    }

    private string ResolveClientId()
    {
        if (!string.IsNullOrWhiteSpace(_options.ClientId))
        {
            return _options.ClientId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_options.ClientIdSecretName) &&
            _secretResolver.TryResolve(_options.ClientIdSecretName, out var clientId) &&
            !string.IsNullOrWhiteSpace(clientId))
        {
            return clientId!.Trim();
        }

        throw new InvalidOperationException(
            "AdfsOAuth requires ClientId or ClientIdSecretName. " +
            "Register a public/native client application in ADFS for the CRM resource.");
    }

    private HttpClient CreateHttpClient(out HttpClient? ownedClient)
    {
        ownedClient = null;
        if (_httpClientFactory is not null)
        {
            // factory client 由 factory 管理生命週期，呼叫端不可 Dispose。
            return _httpClientFactory.CreateClient("dynamics-adfs-token");
        }

        // DI 尚未提供 factory 時（例如部分測試/Embedded 自建 container）用短生命週期 client。
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        ownedClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120))
        };
        return ownedClient;
    }

    private sealed record TokenResponse(string AccessToken, int ExpiresInSeconds);
}