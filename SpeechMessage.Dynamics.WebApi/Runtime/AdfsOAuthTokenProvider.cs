// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs
// 目的：向 ADFS 取得 Web API 用的 Bearer access token（IFD/claims）。
//
// 保姆級教學：
// 1. jesus 這類 IFD 環境，Web API 不能靠 Windows NTLM；會拿到 HTTP 302 去 ADFS。
// 2. 這裡用 OAuth2 token endpoint 取 access_token，再放到 Authorization: Bearer。
// 3. 正式環境應走非密碼服務流程（client credentials / certificate）。
// 4. 此環境 ADFS 可能只允許 authorization_code / refresh_token（jesus 實測拒絕 password grant）。
// 5. local-dev：先瀏覽器授權碼登入一次，把 refresh_token 存 LocalDevTokenStore；之後自動 refresh。
// 6. token 以 process 快取；過期前刷新；不做 per-user CRM session pool。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Configuration;
using System.Buffers;
using System.Security.Cryptography;
using System.Net;
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
    private const int MaxTokenResponseBytes = 32 * 1024;

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

        // 1.5) local-dev token store 仍有效的 access_token 可直接用。
        var storePath = ResolveTokenStorePath();
        if (LocalDevAdfsTokenStore.TryLoad(storePath, out var stored) &&
            !string.IsNullOrWhiteSpace(stored?.AccessToken) &&
            stored!.AccessTokenExpiresAtUtc is not null &&
            DateTimeOffset.UtcNow < stored.AccessTokenExpiresAtUtc.Value.AddSeconds(-60))
        {
            _cachedToken = stored.AccessToken;
            _expiresAt = stored.AccessTokenExpiresAtUtc.Value;
            return _cachedToken!;
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

        // Dispose each short-lived client wrapper; IHttpClientFactory retains ownership of its handler pool.
        var http = CreateHttpClient();
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // 不把 body 全量丟到 UI（可能含敏感細節），只留狀態與短摘要。
                throw new InvalidOperationException(
                    $"ADFS token request failed HTTP {(int)response.StatusCode} from '{tokenEndpoint}'.");
            }

            var body = await ReadBoundedResponseAsync(response.Content, cancellationToken).ConfigureAwait(false);
            try
            {
                var token = ParseTokenResponse(body);
                TryPersistTokens(token.AccessToken, token.ExpiresInSeconds, token.RefreshToken);
                return new TokenResponse(token.AccessToken, token.ExpiresInSeconds);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(body);
            }
        }
        finally
        {
            http.Dispose();
        }
    }

    private List<KeyValuePair<string, string>> BuildTokenForm(string clientId, string resource)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("resource", resource)
        };

        // 可選 client_secret（confidential client）
        if (!string.IsNullOrWhiteSpace(_options.ClientSecretName) &&
            _secretResolver.TryResolve(_options.ClientSecretName, out var clientSecret) &&
            !string.IsNullOrWhiteSpace(clientSecret))
        {
            form.Add(new("client_secret", clientSecret!));
        }

        // 優先 refresh_token：符合 jesus ADFS（只允許 authorization_code / refresh_token）
        if (TryResolveRefreshToken(out var refreshToken) && !string.IsNullOrWhiteSpace(refreshToken))
        {
            form.Add(new("grant_type", "refresh_token"));
            form.Add(new("refresh_token", refreshToken!));
            return form;
        }

        // 次選：本機 local-dev password grant（很多 ADFS 會直接 unsupported_grant_type）
        if (_options.AllowLocalDevPasswordGrant)
        {
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

            form.Add(new("grant_type", "password"));
            form.Add(new("username", userName!));
            form.Add(new("password", password!));
            return form;
        }

        throw new InvalidOperationException(
            "AdfsOAuth has no usable token source. " +
            "For jesus ADFS, open /diagnostics/adfs-authorize once to obtain refresh_token, " +
            "or provide CredentialReferenceName / RefreshTokenSecretName.");
    }

    private bool TryResolveRefreshToken(out string? refreshToken)
    {
        refreshToken = null;

        if (!string.IsNullOrWhiteSpace(_options.RefreshTokenSecretName) &&
            _secretResolver.TryResolve(_options.RefreshTokenSecretName, out var fromSecret) &&
            !string.IsNullOrWhiteSpace(fromSecret))
        {
            refreshToken = fromSecret;
            return true;
        }

        var storePath = ResolveTokenStorePath();
        if (LocalDevAdfsTokenStore.TryLoad(storePath, out var record) &&
            !string.IsNullOrWhiteSpace(record?.RefreshToken))
        {
            refreshToken = record!.RefreshToken;
            return true;
        }

        return false;
    }

    private string? ResolveTokenStorePath()
    {
        // 只有明確設定路徑才使用 local token store，避免測試/正式誤讀預設檔。
        return string.IsNullOrWhiteSpace(_options.LocalDevTokenStorePath)
            ? null
            : _options.LocalDevTokenStorePath;
    }


    private void TryPersistTokens(string accessToken, int expiresInSeconds, string? refreshToken)
    {
        var storePath = ResolveTokenStorePath();
        if (string.IsNullOrWhiteSpace(storePath))
        {
            return;
        }

        try
        {
            LocalDevAdfsTokenStore.TryLoad(storePath, out var existing);
            var record = existing ?? new LocalDevAdfsTokenRecord();
            record.AccessToken = accessToken;
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                record.RefreshToken = refreshToken;
            }
            record.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresInSeconds));
            record.AuthorityUri = ResolveAuthority();
            record.ResourceUri = ResolveResource();
            record.ClientId = ResolveClientId();
            LocalDevAdfsTokenStore.Save(storePath, record);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist local-dev ADFS token store at {Path}", storePath);
        }
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

    private HttpClient CreateHttpClient()
    {
        if (_httpClientFactory is not null)
        {
            // The factory owns the reusable handler pool; this request owns and disposes its client wrapper.
            var factoryClient = _httpClientFactory.CreateClient("dynamics-adfs-token");
            factoryClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120));
            return factoryClient;
        }

        // DI 尚未提供 factory 時（例如部分測試/Embedded 自建 container）用短生命週期 client。
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            PreAuthenticate = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var ownedClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120))
        };
        return ownedClient;
    }

    private static async Task<byte[]> ReadBoundedResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength && contentLength > MaxTokenResponseBytes)
        {
            throw new InvalidOperationException("ADFS token response exceeds the maximum supported size.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(MaxTokenResponseBytes + 1);
        try
        {
            var totalRead = 0;
            while (totalRead <= MaxTokenResponseBytes)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(totalRead, MaxTokenResponseBytes + 1 - totalRead),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer.AsSpan(0, totalRead).ToArray();
                }

                totalRead += read;
            }

            throw new InvalidOperationException("ADFS token response exceeds the maximum supported size.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static ParsedTokenResponse ParseTokenResponse(ReadOnlySpan<byte> responseBody)
    {
        var reader = new Utf8JsonReader(responseBody);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new InvalidOperationException("ADFS token response is not a JSON object.");
        }

        string? accessToken = null;
        string? refreshToken = null;
        var expiresIn = 3600;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new InvalidOperationException("ADFS token response is malformed.");
            }

            var isAccessToken = reader.ValueTextEquals("access_token"u8);
            var isRefreshToken = reader.ValueTextEquals("refresh_token"u8);
            var isExpiresIn = reader.ValueTextEquals("expires_in"u8);
            if (!reader.Read())
            {
                throw new InvalidOperationException("ADFS token response is malformed.");
            }

            if (isAccessToken)
            {
                accessToken = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            }
            else if (isRefreshToken)
            {
                refreshToken = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            }
            else if (isExpiresIn)
            {
                if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericExpiresIn))
                {
                    expiresIn = numericExpiresIn;
                }
                else if (reader.TokenType == JsonTokenType.String &&
                         int.TryParse(reader.GetString(), out var stringExpiresIn))
                {
                    expiresIn = stringExpiresIn;
                }
            }
            else
            {
                reader.Skip();
            }
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("ADFS token response missing access_token.");
        }

        return new ParsedTokenResponse(accessToken, expiresIn, refreshToken);
    }

    private sealed record TokenResponse(string AccessToken, int ExpiresInSeconds);
    private sealed record ParsedTokenResponse(string AccessToken, int ExpiresInSeconds, string? RefreshToken);
}
