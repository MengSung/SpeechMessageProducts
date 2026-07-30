// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs
// 目的：以 Profile Generation 為唯一生命週期邊界，安全取得並短暫快取 ADFS access token。
//
// 安全與效能原則：
// 1. 只接受 CredentialReferenceName 或 RefreshTokenSecretName 指向的受控秘密來源。
// 2. 永久禁止 ROPC/password grant、檔案型 token store 與 client-secret token exchange。
// 3. access token 只存在目前 provider generation 的記憶體；Dispose 時清除參考並取消共用工作。
// 4. 同一 generation 同時間最多一個 token HTTP request；caller 取消只退出自己的等待，不取消共用工作。
// 5. HTTP response 使用 ResponseHeadersRead、有界緩衝區與歸還前清零，避免大型或敏感回應長期滯留。
// ============================================================================

using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 定義 ADFS OAuth access token 的最小取得契約。實作不得把 caller Session、使用者身分或要求資料加入
/// token pool key；所有可變 token 狀態必須限制在單一 Dynamics Profile Generation 內。
/// </summary>
public interface IAdfsOAuthTokenProvider
{
    /// <summary>
    /// 取得可供目前 Profile Generation 使用的 bearer token。呼叫者的取消只取消自己的等待；真正的共用
    /// token acquisition 只由 provider Dispose 所擁有的 cancellation token 終止。
    /// </summary>
    /// <param name="cancellationToken">個別呼叫者的有界等待取消權杖。</param>
    /// <returns>只供目前呼叫鏈使用的 bearer token 字串。</returns>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 單一 Profile Generation 擁有的 ADFS OAuth token provider。
///
/// <para>
/// Trust boundary：秘密值只能從 <see cref="ISecretResolver"/> 進入；不得由產品 JSON、HTTP request、Session、
/// static cache 或檔案取得。provider 本身是 access-token cache、single-flight attempt、HTTP wrapper 與
/// generation cancellation token 的唯一 owner。
/// </para>
/// <para>
/// Race 與 cleanup：短生命週期 monitor 只保護 cache、active attempt identity 與 terminal dispose 狀態；
/// 網路 I/O 絕不在 monitor 內等待。Dispose 先把 generation 標成 terminal，再取消並等待 active attempt，
/// 最後清除 token 參考及釋放 HttpClient／handler／CancellationTokenSource，避免 socket、Task 或 token retention。
/// </para>
/// </summary>
public sealed class AdfsOAuthTokenProvider : IAdfsOAuthTokenProvider, IDisposable, IAsyncDisposable
{
    private const int MaxTokenResponseBytes = 32 * 1024;
    private const int MinimumCacheLifetimeSeconds = 60;
    private const int MaximumCacheLifetimeSeconds = 86_400;
    private const int RefreshSkewSeconds = 60;

    private readonly DynamicsWebApiOptions _options;
    private readonly ISecretResolver _secretResolver;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<AdfsOAuthTokenProvider> _logger;
    private readonly object _stateGate = new();
    private readonly CancellationTokenSource _generationCts = new();
    private readonly HttpClient? _ownedHttpClient;

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private TokenAttempt? _activeAttempt;
    private Task? _disposeTask;
    private int _disposeStarted;

    /// <summary>
    /// 建立尚未執行秘密解析或網路 I/O 的 generation-local provider。
    /// 當未注入 <see cref="IHttpClientFactory"/> 時，provider 會建立並唯一擁有一組禁用 Cookie、Proxy 與 Redirect
    /// 的 handler/client；當使用 factory 時，每次 acquisition 只取得短生命週期 client wrapper 並確定性 Dispose，
    /// 長生命週期 handler pool 則由外層 Generic Host 擁有。
    /// </summary>
    /// <param name="options">啟動時固定、不可由 request 覆寫的 Dynamics Web API 設定。</param>
    /// <param name="secretResolver">受控秘密解析邊界；不得回傳跨 tenant 共用的 mutable credential container。</param>
    /// <param name="logger">只允許記錄固定分類與非敏感生命週期資訊的 logger。</param>
    /// <param name="httpClientFactory">選用的 host-owned HttpClient factory。</param>
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
            var handler = CreateOwnedHandler();
            _ownedHttpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = ResolveBoundedTimeout()
            };
        }
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ValidateSafeTokenSourceConfiguration();

        // 預先核發 bearer 不需要 ADFS HTTP；回傳前仍在 state gate 內重查 terminal 狀態，避免 Dispose
        // 與秘密解析競爭時，由已退休 generation 發佈新的 token 結果。
        if (!string.IsNullOrWhiteSpace(_options.CredentialReferenceName) &&
            _secretResolver.TryResolve(_options.CredentialReferenceName, out var directToken) &&
            !string.IsNullOrWhiteSpace(directToken))
        {
            lock (_stateGate)
            {
                ThrowIfDisposed();
                return directToken!;
            }
        }

        TokenAttempt attempt;
        lock (_stateGate)
        {
            ThrowIfDisposed();

            if (!string.IsNullOrWhiteSpace(_cachedToken) &&
                DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-RefreshSkewSeconds))
            {
                return _cachedToken;
            }

            attempt = _activeAttempt ?? StartTokenAttemptUnderLock();
        }

        // WaitAsync 只套用 caller token；共用 HTTP 工作使用 generation token，因此一個 Session／request 的
        // 中止不會取消其他 caller 正在共用的 refresh。caller 離開後不建立 continuation 或 registration retention。
        var result = await attempt.Execution.WaitAsync(cancellationToken).ConfigureAwait(false);
        return result.GetTokenOrThrow(this);
    }

    /// <summary>
    /// 在 state gate 已持有時建立唯一 TokenAttempt，先發佈 identity 再啟動非同步工作，封閉同步完成與 Dispose
    /// 交錯造成的 stale completed task race。方法只建立一個 Task，不建立背景 thread、timer 或無界 queue。
    /// </summary>
    private TokenAttempt StartTokenAttemptUnderLock()
    {
        var attempt = new TokenAttempt();
        _activeAttempt = attempt;
        attempt.Start(ExecuteTokenAttemptAsync(attempt));
        return attempt;
    }

    /// <summary>
    /// 執行 generation-owned token acquisition，並把成功結果寫入 generation-local cache。
    /// 所有預期失敗都轉成非 faulted 的結果物件，避免全部 caller 已取消時留下無人觀察的 faulted Task；
    /// 呼叫者真正讀取結果時再保留原始 stack 重新拋出。finally 以 attempt identity 清除 active slot，防止
    /// 舊工作的遲到完成清掉較新的 attempt。
    /// </summary>
    private async Task<TokenAttemptResult> ExecuteTokenAttemptAsync(TokenAttempt attempt)
    {
        try
        {
            var token = await RequestNewTokenAsync(_generationCts.Token).ConfigureAwait(false);
            lock (_stateGate)
            {
                ThrowIfDisposed();
                _cachedToken = token.AccessToken;
                var boundedLifetime = Math.Clamp(
                    token.ExpiresInSeconds,
                    MinimumCacheLifetimeSeconds,
                    MaximumCacheLifetimeSeconds);
                _expiresAt = DateTimeOffset.UtcNow.AddSeconds(boundedLifetime);
            }

            _logger.LogInformation(
                "ADFS access token acquired for the active profile generation. ExpiresIn={ExpiresInSeconds}s.",
                Math.Clamp(
                    token.ExpiresInSeconds,
                    MinimumCacheLifetimeSeconds,
                    MaximumCacheLifetimeSeconds));
            return TokenAttemptResult.Success(token.AccessToken);
        }
        catch (OperationCanceledException) when (_generationCts.IsCancellationRequested)
        {
            return TokenAttemptResult.GenerationDisposed();
        }
        catch (Exception exception)
        {
            return TokenAttemptResult.Failure(exception);
        }
        finally
        {
            lock (_stateGate)
            {
                if (ReferenceEquals(_activeAttempt, attempt))
                {
                    _activeAttempt = null;
                }
            }
        }
    }

    /// <summary>
    /// 以 refresh token grant 呼叫固定的 ADFS token endpoint。request、form content、response、stream 與
    /// factory client wrapper 都有明確 using/finally owner；使用 ResponseHeadersRead，讓內容在有界 parser
    /// 中串流讀取，而不是由 HttpClient 先做無界緩衝。失敗訊息只含 HTTP status，不回顯 endpoint 或 body。
    /// </summary>
    private async Task<TokenResponse> RequestNewTokenAsync(CancellationToken cancellationToken)
    {
        var refreshToken = ResolveRequiredRefreshToken();
        var authority = ResolveAuthority();
        var tokenEndpoint = authority.TrimEnd('/') + "/oauth2/token";
        var resource = ResolveResource();
        var clientId = ResolveClientId();

        using var content = new FormUrlEncodedContent(
            new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("resource", resource),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            });
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var httpLease = CreateHttpClientLease();
        try
        {
            using var response = await httpLease.Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"ADFS token request failed with HTTP {(int)response.StatusCode}.");
            }

            var parsed = await ReadBoundedTokenResponseAsync(
                response.Content,
                cancellationToken).ConfigureAwait(false);
            return new TokenResponse(parsed.AccessToken, parsed.ExpiresInSeconds);
        }
        finally
        {
            if (httpLease.DisposeAfterUse)
            {
                httpLease.Client.Dispose();
            }
        }
    }

    /// <summary>
    /// 驗證已禁止的 token source 未被設定。這項檢查在任何 secret resolution、HttpClient 建立或 cache mutation
    /// 之前執行，確保舊 local-dev password grant 與未經驗證的 client-secret exchange 無法靜默回復。
    /// </summary>
    private void ValidateSafeTokenSourceConfiguration()
    {
        if (_options.AllowLocalDevPasswordGrant)
        {
            throw new InvalidOperationException(
                "ADFS password grant is disabled. Use CredentialReferenceName or RefreshTokenSecretName.");
        }

        if (!string.IsNullOrWhiteSpace(_options.ClientSecretName))
        {
            throw new InvalidOperationException(
                "ADFS client-secret token exchange is disabled until a supported service-identity flow is proven.");
        }
    }

    /// <summary>
    /// 從唯一允許的 refresh-token secret reference 解析短生命週期字串。不存在或解析失敗時在任何 HTTP 工作前
    /// fail closed；不會回退到檔案、Session、環境預設帳密或 local-dev manifest。
    /// </summary>
    private string ResolveRequiredRefreshToken()
    {
        if (!string.IsNullOrWhiteSpace(_options.RefreshTokenSecretName) &&
            _secretResolver.TryResolve(_options.RefreshTokenSecretName, out var refreshToken) &&
            !string.IsNullOrWhiteSpace(refreshToken))
        {
            return refreshToken!;
        }

        throw new InvalidOperationException(
            "AdfsOAuth has no usable token source. Configure CredentialReferenceName or RefreshTokenSecretName.");
    }

    /// <summary>
    /// 解析部署固定的 ADFS authority。值不得來自 caller；設定缺失時立即 fail closed。
    /// </summary>
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

        throw new InvalidOperationException("AdfsOAuth requires AuthorityUri or AuthoritySecretName.");
    }

    /// <summary>
    /// 解析固定 CRM resource/audience；不接受 request query 或使用者輸入，避免 token 被核發給錯誤 audience。
    /// </summary>
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

    /// <summary>
    /// 解析部署固定的 public-client ID。client ID 可直接設定或以 reference 解析，但不得由產品 request 覆寫。
    /// </summary>
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

        throw new InvalidOperationException("AdfsOAuth requires ClientId or ClientIdSecretName.");
    }

    /// <summary>
    /// 建立目前 acquisition 使用的 HttpClient lease。factory wrapper 每次用完即 Dispose；owned client 則在
    /// generation Dispose 才釋放，以保留連線重用效能。兩條路徑都使用相同有界 timeout。
    /// </summary>
    private HttpClientLease CreateHttpClientLease()
    {
        if (_httpClientFactory is not null)
        {
            var factoryClient = _httpClientFactory.CreateClient("dynamics-adfs-token");
            factoryClient.Timeout = ResolveBoundedTimeout();
            return new HttpClientLease(factoryClient, DisposeAfterUse: true);
        }

        return new HttpClientLease(
            _ownedHttpClient ?? throw new InvalidOperationException(
                "Owned ADFS token HttpClient is unavailable."),
            DisposeAfterUse: false);
    }

    /// <summary>
    /// 將設定逾時限制在 5～120 秒，避免錯誤設定造成過短重試風暴或長時間保留 request／socket／buffer。
    /// </summary>
    private TimeSpan ResolveBoundedTimeout()
        => TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120));

    /// <summary>
    /// 建立 generation-owned handler。禁用 Cookie、Redirect、Proxy、預先認證與自動解壓縮，避免跨 request
    /// 隱含狀態與壓縮炸彈；single-flight 已把 token endpoint 併發限制為一，連線生命週期則限制 DNS stale retention。
    /// </summary>
    private static SocketsHttpHandler CreateOwnedHandler()
        => new()
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            PreAuthenticate = false,
            MaxConnectionsPerServer = 1,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

    /// <summary>
    /// 在任何 secret 或 HTTP 工作前檢查 terminal generation flag。Volatile read 與 state gate 的第二次檢查
    /// 共同封閉 Dispose／Get 競爭；一旦 Dispose 開始，新的要求必須 fail closed。
    /// </summary>
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeStarted) != 0,
            this);

    /// <summary>
    /// 同步 Dispose 直接等待使用 ConfigureAwait(false) 的非同步 cleanup，不建立 Task.Run 或 fire-and-forget 工作。
    /// 多次同步／非同步 Dispose 共用同一 cleanup task，因此資源只會被釋放一次。
    /// </summary>
    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// 將 generation 原子地標成 terminal、取消 active token HTTP，並建立唯一 cleanup task。
    /// cancellation 只屬於 generation owner，不接受 caller shutdown token，避免清理途中被中止而遺留資源。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            if (_disposeTask is null)
            {
                Volatile.Write(ref _disposeStarted, 1);
                _generationCts.Cancel();
                var activeExecution = _activeAttempt?.Execution;
                _disposeTask = DisposeCoreAsync(activeExecution);
            }

            return new ValueTask(_disposeTask);
        }
    }

    /// <summary>
    /// 等待共用 acquisition drain 後清除敏感參考並釋放 HTTP 與 cancellation resources。
    /// TokenAttempt task 不會 fault，因此 cleanup 不會因上游 ADFS 錯誤而跳過；即使沒有 active attempt，
    /// 仍走相同 deterministic cleanup 路徑。
    /// </summary>
    private async Task DisposeCoreAsync(Task<TokenAttemptResult>? activeExecution)
    {
        if (activeExecution is not null)
        {
            _ = await activeExecution.ConfigureAwait(false);
        }

        lock (_stateGate)
        {
            _cachedToken = null;
            _expiresAt = DateTimeOffset.MinValue;
            _activeAttempt = null;
        }

        _ownedHttpClient?.Dispose();
        _generationCts.Dispose();
    }

    /// <summary>
    /// 以固定最大容量串流讀取 token JSON。Content-Length 與實際讀取量都受限制；租用的陣列在 finally
    /// 以密碼學清零後歸還，stream ownership 也在方法結束時確定釋放。
    /// </summary>
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

    /// <summary>
    /// 解析最小必要欄位。refresh_token 回應欄位被直接 Skip，不建立額外 managed string，也不嘗試持久化；
    /// 未知欄位同樣略過。缺少 access_token 時以固定錯誤分類 fail closed。
    /// </summary>
    private static ParsedTokenResponse ParseTokenResponse(ReadOnlySpan<byte> responseBody)
    {
        var reader = new Utf8JsonReader(responseBody);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new InvalidOperationException("ADFS token response is not a JSON object.");
        }

        string? accessToken = null;
        var expiresIn = 3600;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new InvalidOperationException("ADFS token response is malformed.");
            }

            var isAccessToken = reader.ValueTextEquals("access_token"u8);
            var isExpiresIn = reader.ValueTextEquals("expires_in"u8);
            if (!reader.Read())
            {
                throw new InvalidOperationException("ADFS token response is malformed.");
            }

            if (isAccessToken && reader.TokenType == JsonTokenType.String)
            {
                accessToken = reader.GetString();
            }
            else if (isExpiresIn &&
                     reader.TokenType == JsonTokenType.Number &&
                     reader.TryGetInt32(out var numericExpiresIn))
            {
                expiresIn = numericExpiresIn;
            }
            else if (isExpiresIn &&
                     reader.TokenType == JsonTokenType.String &&
                     int.TryParse(reader.GetString(), out var stringExpiresIn))
            {
                expiresIn = stringExpiresIn;
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

        return new ParsedTokenResponse(accessToken, expiresIn);
    }

    /// <summary>
    /// 表示目前 request 使用的 HttpClient 及 wrapper ownership；只有 factory wrapper 需要每次 Dispose。
    /// </summary>
    private readonly record struct HttpClientLease(HttpClient Client, bool DisposeAfterUse);

    /// <summary>
    /// 表示成功 token acquisition 的最小資料；不包含 refresh token、endpoint 或 credential metadata。
    /// </summary>
    private readonly record struct TokenResponse(string AccessToken, int ExpiresInSeconds);

    /// <summary>
    /// 表示有界 JSON parser 的最小輸出；只保留 caller 必須使用的 access token 與生命週期秒數。
    /// </summary>
    private readonly record struct ParsedTokenResponse(string AccessToken, int ExpiresInSeconds);

    /// <summary>
    /// single-flight attempt 的 immutable identity 與唯一 execution task owner。Start 只能在 state gate 內呼叫一次；
    /// 欄位在發佈給其他 caller 前完成設定，因此不需要額外 volatile 或 cancellation registration。
    /// </summary>
    private sealed class TokenAttempt
    {
        /// <summary>
        /// 取得不會 fault 的共用執行 Task；未 Start 前存取代表生命週期程式錯誤並立即失敗。
        /// </summary>
        public Task<TokenAttemptResult> Execution { get; private set; } = null!;

        /// <summary>
        /// 設定唯一 execution task。呼叫者必須持有 provider state gate，避免重複啟動與 identity race。
        /// </summary>
        public void Start(Task<TokenAttemptResult> execution)
        {
            if (Execution is not null)
            {
                throw new InvalidOperationException("Token attempt has already started.");
            }

            Execution = execution ?? throw new ArgumentNullException(nameof(execution));
        }
    }

    /// <summary>
    /// 將成功、取得失敗與 generation disposal 編碼為非 faulted task result，避免 caller 全部取消後產生
    /// unobserved task exception。失敗只在實際 caller 取用時以原始 stack 重新拋出；結果離開 active identity 後
    /// 不會被 static collection 或 cache 保留。
    /// </summary>
    private sealed class TokenAttemptResult
    {
        private readonly string? _token;
        private readonly Exception? _error;
        private readonly bool _generationDisposed;

        private TokenAttemptResult(string? token, Exception? error, bool generationDisposed)
        {
            _token = token;
            _error = error;
            _generationDisposed = generationDisposed;
        }

        /// <summary>建立成功結果，只保留 access token。</summary>
        public static TokenAttemptResult Success(string token)
            => new(token, error: null, generationDisposed: false);

        /// <summary>建立固定的 generation disposal 結果，不保留 CancellationTokenSource。</summary>
        public static TokenAttemptResult GenerationDisposed()
            => new(token: null, error: null, generationDisposed: true);

        /// <summary>建立取得失敗結果；錯誤只存活到 active attempt 與 caller task 被釋放。</summary>
        public static TokenAttemptResult Failure(Exception error)
            => new(token: null, error ?? throw new ArgumentNullException(nameof(error)), generationDisposed: false);

        /// <summary>
        /// 取出 token 或以正確分類拋出。generation cleanup 一律回報 ObjectDisposedException；一般失敗則保留
        /// 原始 stack，且不包裝或附加可能包含 credential／response body 的訊息。
        /// </summary>
        public string GetTokenOrThrow(AdfsOAuthTokenProvider owner)
        {
            if (_generationDisposed)
            {
                throw new ObjectDisposedException(owner.GetType().FullName);
            }

            if (_error is not null)
            {
                ExceptionDispatchInfo.Capture(_error).Throw();
            }

            return _token ?? throw new InvalidOperationException("Token attempt completed without a token.");
        }
    }
}
