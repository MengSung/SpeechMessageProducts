/*
 * 檔案整體說明
 * 目的與定位：F12 .NET 10 部署工具借用 LINE client 的 Core 傳輸 owner，公開入口只收單一
 * Channel token，不提供任意 URI 或共用認證狀態；舊 Framework 目標不提供此工具專用 API。
 * 主要流程：先以既有 LineChannelCredential 驗證安全標頭輸入，再接管專屬 handler，配置
 * 十五秒 HttpClient 與 256KiB 一般回應緩衝，最後呼叫未過時的 SDK 建構式。端點沿用 SDK
 * 固定 LINE JSON／媒體端點，認證僅由 SDK 附於逐筆 request，不進入預設標頭或本類別欄位。
 * 資源與隔離：一個命令、一個 Channel、一個 lease 獨占 handler／HttpClient／SDK；不建立
 * 靜態狀態、快取、通知、重試或背景工作。caller 先 await 所有借用操作、處置回傳串流並排空，
 * 才能 Dispose；不得讓借用、操作與 Dispose 競爭。SDK 清除 credential 後，finally 關閉 HTTP。
 * 保留邊界：token 字串無法保證抹除，只清掉 SDK 持有的參照；caller 自管秘密來源與最短存活期。
 * HttpClient 的期限與緩衝不限制 ResponseHeadersRead 後的媒體串流，該串流須由 caller 限期關閉。
 * 失敗與驗證：輸入或空 handler 被拒絕前不接管依賴；接受後的配置／建構失敗則回收已擁有資源。
 * 原始例外交給 caller 的共用告警入口；若建構與清理同時失敗，以 Aggregate 保留原錯在前，
 * 不讓清理錯誤遮蔽原因。本檔不記錄秘密或自行通知。合成 handler 聚焦測試驗證
 * A/B/A、逾時排空、故障清理與一次釋放；交付另檢查繁中說明、UTF-8 無 BOM 與 CRLF。
 */
#if NET10_0_OR_GREATER
using System;
using System.Net.Http;
using System.Threading;
using Line.Messaging;

namespace LineMessagingProcessor.Core;

/// <summary>供 .NET 10 命令借用既有 LINE SDK，獨占一個 Channel 的短命傳輸及憑證生命週期。</summary>
/// <remarks>
/// caller 是 lease 唯一 owner，必須先 await／排空全部操作及關閉回傳串流，再 Dispose。
/// 不得共用至另一 Channel、快取 Client，或讓借用及操作與 Dispose 競爭；本類別不代為追蹤操作。
/// 一般回應最多緩衝 256KiB，HTTP 請求最多十五秒；媒體串流讀取期限另由 caller 負責。
/// </remarks>
public sealed class LineMessagingClientLease : IDisposable
{
    /// <summary>此 lease 唯一擁有的 HTTP；SDK 僅借用，最後由 Dispose 的 finally 關閉。</summary>
    private readonly HttpClient _httpClient;

    /// <summary>唯一持有 Channel credential 的 SDK；原子取出並清空參照同時關閉 getter。</summary>
    private LineMessagingClient? _messagingClient;

    /// <summary>先驗證 token，再配置獨占無 cookie／轉址的 Sockets 傳輸，不接受自訂端點。</summary>
    /// <param name="channelAccessToken">caller 從已授權秘密來源取得的單一 Channel token，不記錄或回顯。</param>
    /// <exception cref="ArgumentException">token 空白或不符合既有可見 ASCII 標頭契約。</exception>
    public LineMessagingClientLease(string channelAccessToken)
        : this(new LineChannelCredential(channelAccessToken), CreateHandler(), TimeSpan.FromSeconds(15)) { }

    /// <summary>離線測試注入專屬 handler；輸入驗證成功才接受所有權，後續失敗及 Dispose 均由 lease 清理。</summary>
    /// <param name="channelAccessToken">僅供此 Channel 的合成 token，驗證失敗不接管 handler。</param>
    /// <param name="handler">不得共用的合成傳輸；接受前屬 caller，接受後不可再由 caller 處置。</param>
    internal LineMessagingClientLease(string channelAccessToken, HttpMessageHandler handler)
        : this(channelAccessToken, handler, TimeSpan.FromSeconds(15)) { }

    /// <summary>注入有限短期限，沿用正式生命週期；期限設定屬接受後步驟，其故障必須回收已接管的傳輸。</summary>
    /// <param name="channelAccessToken">先驗證但不另存欄位的合成 token。</param>
    /// <param name="handler">通過 token 與非空檢查後，所有權唯一轉交 lease。</param>
    /// <param name="timeout">大於零且不超過十五秒，禁止無限等待或放寬正式期限。</param>
    internal LineMessagingClientLease(string channelAccessToken, HttpMessageHandler handler, TimeSpan timeout)
        : this(new LineChannelCredential(channelAccessToken), handler ?? throw new ArgumentNullException(nameof(handler)), timeout) { }

    /// <summary>兩個前置輸入已驗證，進入本建構式即接受 handler；HttpClient 建立前後皆有唯一回收路徑。</summary>
    /// <param name="credential">既有值物件已驗證的 token，只借給 SDK 建構，不保留此值物件。</param>
    /// <param name="handler">已接受且不可再由 caller 清理的專屬 handler。</param>
    /// <param name="timeout">接受後驗證的有限測試／正式期限。</param>
    private LineMessagingClientLease(LineChannelCredential credential, HttpMessageHandler handler, TimeSpan timeout)
    {
        HttpClient? ownedClient = null;
        try
        {
            ownedClient = new HttpClient(handler, disposeHandler: true);
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(15))
                throw new ArgumentOutOfRangeException(nameof(timeout), "A finite timeout up to fifteen seconds is required.");
            ownedClient.Timeout = timeout;
            ownedClient.MaxResponseContentBufferSize = 256 * 1024;
            _messagingClient = new LineMessagingClient(ownedClient, credential.AccessToken);
            _httpClient = ownedClient;
        }
        catch (Exception original)
        {
            // client 尚未建立才直接清 handler；建立後由 client 級聯處置，避免同一資源被清理兩次。
            try
            {
                if (ownedClient == null) handler.Dispose();
                else ownedClient.Dispose();
            }
            catch (Exception cleanup)
            {
                // 兩個實例都交給 caller 的同一告警入口；不轉成字串，也不重試已處置的 handler。
                throw new AggregateException("LINE transport construction and cleanup failed.", original, cleanup);
            }
            throw;
        }
    }

    /// <summary>借用此 Channel 的 SDK；只有 lease 擁有 Dispose，caller 須在 owner 存活期間完成全部操作。</summary>
    /// <exception cref="ObjectDisposedException">lease 已關閉，禁止再次借用或發送。</exception>
    public ILineMessagingClient Client => Volatile.Read(ref _messagingClient)
        ?? throw new ObjectDisposedException(nameof(LineMessagingClientLease));

    /// <summary>caller 排空後冪等關閉：先禁止借用並清 SDK credential，finally 再關閉自有 HTTP／handler。</summary>
    /// <remarks>
    /// 不啟動非同步清理；caller 若尚未排空，HttpClient 關閉會中止進行中的請求。
    /// handler 清理即使拋錯，lease 仍維持關閉，重複呼叫不再處置。
    /// </remarks>
    public void Dispose()
    {
        var messagingClient = Interlocked.Exchange(ref _messagingClient, null);
        if (messagingClient == null) return;
        try { messagingClient.Dispose(); }
        finally { _httpClient.Dispose(); }
    }

    /// <summary>配置正式命令專屬傳輸；配置本身失敗時也由此工廠回收，成功才交給建構式接管。</summary>
    /// <returns>無 cookie、禁止轉址且最多兩條伺服器連線的全新 Sockets handler。</returns>
    private static SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler();
        try
        {
            handler.UseCookies = false;
            handler.AllowAutoRedirect = false;
            handler.MaxConnectionsPerServer = 2;
            return handler;
        }
        catch
        {
            handler.Dispose();
            throw;
        }
    }
}
#endif
