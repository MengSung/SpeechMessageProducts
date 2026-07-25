// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/Gateway/GatewayHttpClientFactory.cs
// 目的：產品打 Gateway 時共用長生命週期 HttpClient / handler。
//
// 保母教學：
// 1. 為什麼不能每次 new HttpClient？
//    - 會耗盡 socket，造成 TIME_WAIT 堆積與連線變慢。
// 2. 這是「產品 -> Gateway」的 pool，不是 per-user CRM session pool。
// 3. 以 Gateway Endpoint 當 key，一個 endpoint 共用一個 client。
// 4. 不做 cookie container；Gateway 認證應走服務身分 / mTLS / 內部 token。
// ============================================================================

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace SpeechMessage.Dynamics.ProductClient.Gateway;

/// <summary>
/// Gateway HTTP client 的 process-level 工廠。
/// </summary>
public static class GatewayHttpClientFactory
{
    private static readonly ConcurrentDictionary<string, HttpClient> Clients =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 取得（或建立）指定 Gateway endpoint 的共用 HttpClient。
    /// </summary>
    public static HttpClient GetSharedClient(string gatewayEndpoint, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(gatewayEndpoint))
        {
            throw new ArgumentException("gatewayEndpoint is required.", nameof(gatewayEndpoint));
        }

        if (!Uri.TryCreate(gatewayEndpoint.Trim(), UriKind.Absolute, out var baseUri) || baseUri is null)
        {
            throw new ArgumentException("gatewayEndpoint must be an absolute URI.", nameof(gatewayEndpoint));
        }

        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("gatewayEndpoint must use http or https.", nameof(gatewayEndpoint));
        }

        var key = baseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/";
        return Clients.GetOrAdd(key, static (cacheKey, state) =>
        {
            var handler = new SocketsHttpHandler
            {
                // 不接受 cookie，避免 session leakage。
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 8
            };

            var client = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri(cacheKey, UriKind.Absolute),
                Timeout = state
            };
            client.DefaultRequestHeaders.ExpectContinue = false;
            return client;
        }, timeout ?? TimeSpan.FromSeconds(60));
    }
}