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
/// 以 ADFS oauth2/token 取得並快取 access token，並由單一 Profile Generation 確定性擁有其快取、
/// single-flight Semaphore、取消來源與選用的 HTTP Handler／Client。
/// Provider 不保存終端使用者 Session，也不以 User、LINE ID、JWT 或 Request 身分建立 Token Pool Key；
/// 一個實例只服務一份不可變的 Profile Generation 設定。Generation 退休時必須先取消並等待進行中的
/// Token 工作，再清除 Token 引用並 Dispose HTTP 與同步資源，避免舊 Credential／Socket／Semaphore 被保留。
/// </summary>
public sealed class AdfsOAuthTokenProvider : IAdfsOAuthTokenProvider, IDisposable, IAsyncDisposable
{
    private const int MaxTokenResponseBytes = 32 * 1024;

    private readonly DynamicsWebApiOptions _options;
    private readonly ISecretResolver _secretResolver;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<AdfsOAuthTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _disposeGate = new();
    private readonly SocketsHttpHandler? _ownedHttpHandler;
    private readonly HttpClient? _ownedHttpClient;

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private Task? _disposeTask;
    private int _disposeStarted;

    /// <summary>
    /// 建立一個 Generation-local ADFS Token Provider。
    /// 有 <paramref name="httpClientFactory"/> 時，每次要求只擁有短生命週期 HttpClient wrapper，底層 handler pool 由 DI Host 擁有；
    /// 未提供 Factory 時，Provider 會建立並長期重用一組禁用 Cookie、Redirect、Proxy、Decompression 與 PreAuthenticate 的
    /// SocketsHttpHandler／HttpClient，並在 Provider Dispose 時一併回收，確保不同 Profile Generation 不共用 Token Socket 狀態。
    /// Constructor 不解析 Secret、不送 HTTP，也不啟動背景工作。
    /// </summary>
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

        if (_httpClientFactory is null)
        {
            _ownedHttpHandler = CreateOwnedHandler();
            _ownedHttpClient = new HttpClient(_ownedHttpHandler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120))
            };
        }
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCts.Token);
        await _gate.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        try
        {
            // Dispose 會先設定狀態再取消 waiter；成功取得 Gate 後重查，確保退休 Generation 不會開始新的 Secret／HTTP 工作。
            ThrowIfDisposed();

            // 1) 若外部已直接提供 bearer token 秘密，優先使用（正式可接 secret store）。
            var directTokenRef = _options.CredentialReferenceName;
            if (!string.IsNullOrWhiteSpace(directTokenRef) &&
                _secretResolver.TryResolve(directTokenRef, out var directToken) &&
                !string.IsNullOrWhiteSpace(directToken))
            {
                return directToken!;
            }

            // 1.5) local-dev token store 仍有效的 access_token 可直接用；正式 multi-profile 設定會另外禁止此路徑。
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

            // 2) 快取未過期直接重用。所有讀寫都在 Gate 內，Dispose 因此能等待唯一 owner 完成後安全清除引用。
            if (!string.IsNullOrWhiteSpace(_cachedToken) &&
                DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
            {
                return _cachedToken!;
            }

            var token = await RequestNewTokenAsync(linkedCts.Token).ConfigureAwait(false);
            _cachedToken = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresInSeconds));
            _logger.LogInformation(
                "ADFS access token acquired. ExpiresIn={ExpiresIn}s",
                token.ExpiresInSeconds);
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

        var httpLease = CreateHttpClientLease();
        try
        {
            using var response = await httpLease.Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // 不把 body 全量丟到 UI（可能含敏感細節），只留狀態與短摘要。
                throw new InvalidOperationException(
                    $"ADFS token request failed HTTP {(int)response.StatusCode} from '{tokenEndpoint}'.");
            }

            var token = await ReadBoundedTokenResponseAsync(response.Content, cancellationToken).ConfigureAwait(false);
            TryPersistTokens(token.AccessToken, token.ExpiresInSeconds, token.RefreshToken);
            return new TokenResponse(token.AccessToken, token.ExpiresInSeconds);
        }
        finally
        {
            // Factory 路徑的 wrapper 屬於單次要求；Generation-owned client 則跨 refresh 重用，直到 Provider Dispose。
            if (httpLease.DisposeAfterUse)
            {
                httpLease.Client.Dispose();
            }
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

    /// <summary>
    /// 取得本次 Token Request 應使用的 HttpClient 與 ownership 標記。
    /// Factory 路徑的 wrapper 由單次要求 Dispose；沒有 Factory 時回傳 Generation-owned Client，
    /// 此 Client 不可由單次要求釋放，必須等 Provider／Generation 完成 drain 後統一 Dispose。
    /// </summary>
    private HttpClientLease CreateHttpClientLease()
    {
        if (_httpClientFactory is not null)
        {
            // IHttpClientFactory 擁有可重用 handler pool；本次要求只擁有短生命週期 client wrapper，完成交換後立即 Dispose。
            var factoryClient = _httpClientFactory.CreateClient("dynamics-adfs-token");
            factoryClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120));
            return new HttpClientLease(factoryClient, DisposeAfterUse: true);
        }

        return new HttpClientLease(
            _ownedHttpClient ?? throw new InvalidOperationException("Owned ADFS token HttpClient is unavailable."),
            DisposeAfterUse: false);
    }

    /// <summary>
    /// 建立只屬於此 Profile Generation 的 Token Handler。
    /// Cookie、Redirect、Proxy、Decompression 與 PreAuthenticate 全部停用，避免跨要求 Session 狀態、
    /// Proxy Credential 或隱性導向被保存；PooledConnectionLifetime 有界，兼顧安全重用與 DNS／端點更新。
    /// </summary>
    private static SocketsHttpHandler CreateOwnedHandler()
        => new()
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            PreAuthenticate = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

    /// <summary>
    /// 若 Provider 已開始 Dispose，立即在 Secret Resolver、Token Store 或 HTTP I/O 前失敗。
    /// 使用 Volatile 讀取可讓所有執行緒看見退休狀態，不需要取得已可能被 Dispose 的 Semaphore。
    /// </summary>
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeStarted) != 0,
            this);

    /// <summary>
    /// 同步釋放 Provider 並確定性等待非同步取消、Gate 排空與 HTTP 資源回收完成。
    /// Task.Run 用來隔離呼叫端 SynchronizationContext，但仍同步觀察所有清理例外，不留下 fire-and-forget Task。
    /// </summary>
    public void Dispose()
        => Task.Run(async () => await DisposeAsync().ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// 啟動或加入唯一的 Provider Dispose 工作。第一個呼叫會先發布 disposed 狀態並取消所有進行中／等待中的 Token 工作，
    /// 接著等待 single-flight Gate，確定沒有程式仍讀寫 Token Cache 後才清除 Token 引用、Dispose Generation-owned Client／Handler、
    /// CancellationTokenSource 與 Semaphore。後續呼叫共享同一 Task，不會重複釋放或產生 ObjectDisposed race。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            Volatile.Write(ref _disposeStarted, 1);
            _disposeCts.Cancel();
            _disposeTask = DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    /// <summary>
    /// 執行唯一的清理核心。必須先取得 Gate 才能清除 Cache 與 Dispose Client，
    /// 否則進行中的 Request 可能仍使用已釋放的 Handler、Token 字串或 CancellationTokenSource。
    /// </summary>
    private async Task DisposeCoreAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _cachedToken = null;
            _expiresAt = DateTimeOffset.MinValue;
            _ownedHttpClient?.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
            _disposeCts.Dispose();
        }
    }

    private static async Task<ParsedTokenResponse> ReadBoundedTokenResponseAsync(
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
                    return ParseTokenResponse(buffer.AsSpan(0, totalRead));
                }

                totalRead += read;
            }

            throw new InvalidOperationException("ADFS token response exceeds the maximum supported size.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
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
                if (reader.TokenType == JsonTokenType.String)
                {
                    accessToken = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }
            else if (isRefreshToken)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    refreshToken = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
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
                else
                {
                    reader.Skip();
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

    /// <summary>
    /// 描述一次 Token Request 借用的 HttpClient 及其釋放責任；不保存 Token、Credential 或 Request Body。
    /// </summary>
    private readonly record struct HttpClientLease(HttpClient Client, bool DisposeAfterUse);

    /// <summary>
    /// Provider 內部最小 Token 結果，只在 single-flight Gate 內短暫存在並立即寫入 Generation-local Cache。
    /// </summary>
    private sealed record TokenResponse(string AccessToken, int ExpiresInSeconds);

    /// <summary>
    /// 有界 JSON Parser 的結果；Refresh Token 只用於選用的 local-dev store，正式設定不得啟用該路徑。
    /// </summary>
    private sealed record ParsedTokenResponse(string AccessToken, int ExpiresInSeconds, string? RefreshToken);
}
