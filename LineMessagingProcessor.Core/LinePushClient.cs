using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LineMessagingProcessor.Core;

/// <summary>
/// 固定 LINE push 端點的單次傳輸實作，沒有重試佇列、背景工作或管理者 notifier。
/// .NET Framework 的程序級共用 HttpClient 不含使用者狀態，net10 的 HttpClient 由主機擁有；
/// 每個操作獨佔 HTTP 訊息、十秒逾時與取消連結，結束前按 using 次序釋放。
/// </summary>
public sealed class LinePushClient : ILinePushClient
{
    /// <summary>外部或沙箱程序擁有的無狀態連線池，不保存任何當次憑證或結果。</summary>
    private readonly HttpClient _httpClient;
    /// <summary>建立操作專屬逾時來源；委派本身不捕獲使用者、教會或 request 狀態。</summary>
    private readonly Func<TimeSpan, CancellationTokenSource> _createTimeout;
    /// <summary>固定十秒的 Core 內部期限，不讓未驗證輸入延長資源生命週期。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

#if NETFRAMEWORK
    /// <summary>
    /// 沙箱 AppDomain／程序是此唯一客戶端與連線池的 owner，卸載時一併回收。
    /// 它不使用 Cookie、預設認證或自動轉址，沒有 per-user 狀態；每個傳輸取消與內容另有 using owner。
    /// </summary>
    private static readonly HttpClient SharedClient = CreateSharedClient();

    /// <summary>使用沙箱程序生命週期內的無狀態客戶端，不為每次 Plugin 執行建立連線池。</summary>
    public LinePushClient() : this(SharedClient) { }
#endif

    /// <summary>連線由呼叫端擁有；必須使用無 Cookie、無預設憑證且不自動轉址的管線。</summary>
    /// <param name="httpClient">可重用且無教會／使用者狀態的客戶端；Core 不 Dispose 它。</param>
#if NETFRAMEWORK
    internal LinePushClient(HttpClient httpClient) : this(httpClient, StartTimeout) { }
#else
    public LinePushClient(HttpClient httpClient) : this(httpClient, StartTimeout) { }
#endif

    /// <summary>統一雙目標的計時來源所有權；來源必須逐次建立，回傳後由單一操作 Dispose。</summary>
    /// <param name="httpClient">外部擁有的無狀態客戶端。</param>
    /// <param name="createTimeout">依給定期限建立獨立來源的排程器，不得回傳共用或快取來源。</param>
    internal LinePushClient(HttpClient httpClient, Func<TimeSpan, CancellationTokenSource> createTimeout)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _createTimeout = createTimeout ?? throw new ArgumentNullException(nameof(createTimeout));
    }

    /// <summary>以逐請求標頭與快照內容推播；只轉換已核定的傳輸失敗，其他例外往外拋。</summary>
    /// <param name="credential">當次頻道憑證。</param>
    /// <param name="request">當次不可變請求。</param>
    /// <param name="ct">明確 caller 取消；只在尚未收到 HTTP 回應時生效，回應抵達後以狀態碼分類結果。</param>
    /// <returns>不持有 request／response／credential 的結果。</returns>
    public async Task<LinePushResult> PushAsync(LineChannelCredential credential, LinePushRequest request, CancellationToken ct)
    {
#if NETFRAMEWORK
        if (credential is null) throw new ArgumentNullException(nameof(credential));
        if (request is null) throw new ArgumentNullException(nameof(request));
#else
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(request);
#endif
        ct.ThrowIfCancellationRequested();

        using var timeout = _createTimeout(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        message.Headers.Add("X-Line-Retry-Key", request.RetryKey.ToString("D", CultureInfo.InvariantCulture));
        var body = JsonConvert.SerializeObject(new
        {
            to = request.UserId,
            messages = request.Messages.Select(text => new { type = "text", text }).ToArray()
        });
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            // 只需要標頭；不緩衝、不讀取也不保留錯誤本文，回傳前釋放回應及內容串流。
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var outcome = status switch
            {
                >= 200 and <= 299 => LinePushOutcome.Success,
                409 => LinePushOutcome.AlreadyAccepted,
                429 or (>= 500 and <= 599) => LinePushOutcome.Retryable,
                _ => LinePushOutcome.Permanent
            };
            var requestId = response.Headers.TryGetValues("x-line-request-id", out var values) ? values.FirstOrDefault() : null;
            return new LinePushResult(outcome, response.StatusCode, requestId, null);
        }
        catch (OperationCanceledException exception)
        {
            // caller 取消只在沒有收到 HTTP 回應時中止操作；其他取消來源都是無回應的傳輸失敗。
            // 一旦 SendAsync 已回傳 response，狀態碼結果優先，避免丟失服務端已知的接受／拒絕資訊。
            ct.ThrowIfCancellationRequested();
            return new LinePushResult(LinePushOutcome.Retryable, null, null, exception);
        }
        catch (HttpRequestException exception)
        {
            // 本層沒有收到 HTTP 回應，原始實例交給最終邊界；不能在可恢復重試階段重複告警。
            // .NET Framework 不會像 net10 自動把同時發生的 caller 取消正規化，因此在此明確優先辨識。
            ct.ThrowIfCancellationRequested();
            return new LinePushResult(LinePushOutcome.Retryable, null, null, exception);
        }
    }

    /// <summary>跨框架共用的正式排程器，建立具固定期限的獨立來源，由呼叫的 using 確定性釋放。</summary>
    /// <param name="duration">Core 固定的十秒期限。</param>
    /// <returns>尚未與其他操作共用的新來源。</returns>
    private static CancellationTokenSource StartTimeout(TimeSpan duration) => new(duration);

#if NETFRAMEWORK
    /// <summary>
    /// 只在 AppDomain 建立一次無狀態連線池，不覆寫程序級 TLS 設定。
    /// TLS 仍受沙箱 .NET Framework 目標相容開關與作業系統設定影響；net462 的 LINE 推播須在 P2-04 前置另行驗收。
    /// </summary>
    /// <returns>由程序擁有、沒有 Cookie 或使用者憑證的客戶端。</returns>
    private static HttpClient CreateSharedClient()
    {
        var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, UseDefaultCredentials = false };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }
#endif
}
