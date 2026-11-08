/*
 * 檔案整體說明
 *
 * 目的與定位：固定LINE端點的單次傳輸owner，供文字相容入口、會友推播及宮格綁定共用；
 * 沒有重試佇列、背景工作或管理者notifier。.NET Framework的程序級共用HttpClient不含使用者狀態，net10的
 * HttpClient 由主機擁有； 每個操作獨佔 HTTP 訊息、十秒逾時與取消連結，結束前按 using 次序釋放。
 *
 * 主要功能與流程：
 * 本檔宣告 LinePushClient。
 * - 外部或沙箱程序擁有的無狀態連線池，不保存任何當次憑證或結果。
 * - 建立操作專屬逾時來源；委派本身不捕獲使用者、教會或 request 狀態。
 * - 固定十秒的 Core 內部期限，不讓未驗證輸入延長資源生命週期。
 * - 沙箱 AppDomain／程序是此唯一客戶端與連線池的 owner，卸載時一併回收。 它不使用 Cookie、預設認證或自動轉址，沒有 per-user 狀態；每個傳輸取消與內容另有 using owner。
 * 主要操作入口包含 PushAsync、StartTimeout、CreateSharedClient。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
/// 固定LINE端點的共用單次傳輸實作；會友核對與宮格綁定見partial，沒有重試佇列、背景工作或管理者notifier。
/// .NET Framework 的程序級共用 HttpClient 不含使用者狀態，net10 的 HttpClient 由主機擁有；
/// 每個操作獨佔 HTTP 訊息、十秒逾時與取消連結，結束前按 using 次序釋放。
/// </summary>
public sealed partial class LinePushClient : ILinePushClient, ILineMemberPushClient
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
        var body = JsonConvert.SerializeObject(new
        {
            to = request.UserId,
            messages = request.Messages.Select(text => new { type = "text", text }).ToArray()
        });
        return (await SendCoreAsync(HttpMethod.Post, "message/push", credential, body, request.RetryKey, null, ct)
            .ConfigureAwait(false)).Result;
    }

    /// <summary>唯一HTTP傳輸owner；每次擁有十秒CTS、linked token、request與response，沒有預設標頭狀態。</summary>
    private async Task<TransportResult> SendCoreAsync(HttpMethod method, string path, LineChannelCredential credential,
        string? body, Guid? retryKey, string? expectedAlias, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var timeout = _createTimeout(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var message = new HttpRequestMessage(method, "https://api.line.me/v2/bot/" + path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        if (retryKey.HasValue)
            message.Headers.Add("X-Line-Retry-Key", retryKey.Value.ToString("D", CultureInfo.InvariantCulture));
        if (body is not null) message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
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
            string? menuId = null;
            string? reason = expectedAlias is not null && status == 404 ? "AliasNotFound" : null;
            if (expectedAlias is not null && outcome == LinePushOutcome.Success)
            {
                menuId = await ReadMenuIdAsync(response, expectedAlias, linked.Token).ConfigureAwait(false);
                if (menuId is null) { outcome = LinePushOutcome.Permanent; reason = "InvalidResponse"; }
            }
            return new TransportResult(new LinePushResult(outcome, response.StatusCode, requestId, null, reason), menuId);
        }
        catch (OperationCanceledException exception)
        {
            // caller取消才是正常中止；內部十秒或HttpClient逾時保留原例外供最終邊界處理。
            ct.ThrowIfCancellationRequested();
            return new TransportResult(new LinePushResult(LinePushOutcome.Retryable, null, null, exception), null);
        }
        catch (HttpRequestException exception)
        {
            ct.ThrowIfCancellationRequested();
            return new TransportResult(new LinePushResult(LinePushOutcome.Retryable, null, null, exception), null);
        }
    }

    /// <summary>單次HTTP純值結果，最多保留有效宮格ID，不保存回應、本文、憑證或SDK。</summary>
    private sealed class TransportResult(LinePushResult result, string? menuId)
    {
        /// <summary>已釋放傳輸資源的固定結果。</summary>
        internal LinePushResult Result { get; } = result;
        /// <summary>只供本次alias查詢後的綁定使用，不跨請求快取。</summary>
        internal string? MenuId { get; } = menuId;
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
