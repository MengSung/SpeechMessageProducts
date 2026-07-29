// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DynamicsHttpTransport.cs
// 目的：擁有 SocketsHttpHandler + HttpClient 的 profile 級傳輸實作。
//
// 保母教學：
// - UseCookies=false：避免 cookie 型 session 殘留。
// - AllowAutoRedirect=false：避免授權請求被導到未核准主機。
// - UseProxy=false：不吃系統代理，避免憑證外洩路徑。
// - 不把 Authorization 放進 DefaultRequestHeaders；每個 request 自己帶。
// - Dispose 必須剛好一次，避免 handler / socket 洩漏。
// ============================================================================

using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 長壽命 Dynamics HTTP transport。
/// </summary>
public sealed class DynamicsHttpTransport : IDynamicsHttpTransport
{
    private readonly HttpClient _httpClient;
    private readonly SocketsHttpHandler? _ownedHandler;
    private readonly ILogger<DynamicsHttpTransport> _logger;
    private int _disposed;

    /// <summary>
    /// 正式建構：依設定建立 handler / client。
    /// </summary>
    public DynamicsHttpTransport(
        IOptions<DynamicsWebApiOptions> options,
        ISecretResolver secretResolver,
        ILogger<DynamicsHttpTransport> logger)
        : this(CreateOwnedClient(options.Value, secretResolver), ownsHandler: true, logger)
    {
    }

    /// <summary>
    /// 測試建構：注入外部 handler（例如 mock）。
    /// </summary>
    public DynamicsHttpTransport(HttpMessageHandler handler, ILogger<DynamicsHttpTransport> logger, bool disposeHandler = true)
        : this(new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler)), disposeHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        }, ownsHandler: disposeHandler, logger)
    {
    }

    private DynamicsHttpTransport(HttpClient httpClient, bool ownsHandler, ILogger<DynamicsHttpTransport> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownedHandler = null;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // ownsHandler 目前由 HttpClient(disposeHandler) 管理；保留欄位方便未來擴充。
        _ = ownsHandler;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(request);
        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _httpClient.Dispose();
        _ownedHandler?.Dispose();
        _logger.LogDebug("DynamicsHttpTransport disposed.");
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static HttpClient CreateOwnedClient(DynamicsWebApiOptions options, ISecretResolver secretResolver)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secretResolver);

        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            UseProxy = false,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = Math.Clamp(options.MaxConnectionsPerServer, 1, 128),
            PooledConnectionLifetime = TimeSpan.FromMinutes(Math.Clamp(options.PooledConnectionLifetimeMinutes, 1, 240)),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 300))
        };

        ApplyWindowsCredentialsIfNeeded(options, secretResolver, handler);

        // disposeHandler: true => HttpClient 釋放時一併釋放 handler。
        var client = new HttpClient(handler, disposeHandler: true)
        {
            // 實際逾時由 request + CancellationTokenSource 控制，避免 DefaultRequestHeaders 污染。
            Timeout = Timeout.InfiniteTimeSpan
        };

        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("OData-Version", "4.0");
        client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        return client;
    }

    private static void ApplyWindowsCredentialsIfNeeded(
        DynamicsWebApiOptions options,
        ISecretResolver secretResolver,
        SocketsHttpHandler handler)
    {
        if (options.AuthMode != DynamicsAuthMode.Windows)
        {
            handler.Credentials = null;
            return;
        }

        if (options.CredentialSource == DynamicsCredentialSource.HostIdentity)
        {
            handler.Credentials = CredentialCache.DefaultCredentials;
            return;
        }

        // SecretReference：只允許非人類服務帳號，從秘密庫解析。
        var userNameRef = options.UserNameSecretName;
        var passwordRef = options.PasswordSecretName;
        if (string.IsNullOrWhiteSpace(userNameRef) || string.IsNullOrWhiteSpace(passwordRef))
        {
            throw new InvalidOperationException(
                "Windows SecretReference requires UserNameSecretName and PasswordSecretName.");
        }

        if (!secretResolver.TryResolve(userNameRef, out var userName) || string.IsNullOrEmpty(userName))
        {
            throw new InvalidOperationException("Failed to resolve Windows username secret reference.");
        }

        if (!secretResolver.TryResolve(passwordRef, out var password) || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Failed to resolve Windows password secret reference.");
        }

        string? domain = null;
        if (!string.IsNullOrWhiteSpace(options.DomainSecretName))
        {
            if (!secretResolver.TryResolve(options.DomainSecretName, out domain))
            {
                throw new InvalidOperationException("Failed to resolve Windows domain secret reference.");
            }
        }

        // 將 DOMAIN\user 拆成明確網域與帳號，並透過 CredentialCache 只對核准的 Web API root/origin 提供 NTLM/Negotiate。
        // IFD/claims 組織仍可能回傳 302 到 ADFS；Windows 認證無法完成該登入流程，呼叫端必須改用經核准的 AdfsOAuth。
        var user = userName.Trim();
        var dom = domain?.Trim();
        if (user.Contains('\\', StringComparison.Ordinal))
        {
            var parts = user.Split('\\', 2);
            if (!string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
            {
                dom = parts[0];
                user = parts[1];
            }
        }

        var credential = string.IsNullOrWhiteSpace(dom)
            ? new NetworkCredential(user, password)
            : new NetworkCredential(user, password, dom);

        if (Uri.TryCreate(options.OrganizationWebApiBaseUri, UriKind.Absolute, out var webApiRoot))
        {
            var cache = new CredentialCache();
            cache.Add(webApiRoot, "NTLM", credential);
            cache.Add(webApiRoot, "Negotiate", credential);
            var origin = new Uri(webApiRoot.GetLeftPart(UriPartial.Authority) + "/");
            cache.Add(origin, "NTLM", credential);
            cache.Add(origin, "Negotiate", credential);
            handler.Credentials = cache;
        }
        else
        {
            handler.Credentials = credential;
        }
    }
}
