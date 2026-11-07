/*
檔案整體說明

目的與定位：提供 Hosting 唯一的管理者例外 LINE push 傳輸，取代 ChurchReport 內的
實際 HTTP 實作。只接受 diagnostics 已去識別化的文字，不提供業務使用者任意推播入口。

主要功能與流程：
建構時委派 Tenancy 取得環境→CRM 組織→預設頻道的單次 token，並讀取管理者收件人；
後備收件人由產品組合根提供，本共用層不硬編碼任何產品或使用者 ID。
每次 SendAsync 建立自有 Authorization／JSON request，headers-only 接收 response，
不讀供應商本文；非成功狀態只拋固定失敗訊息，不記錄或通知自身故障。

資源與隔離：
sender 僅屬單一受信任部署 owner，保存啟動快照而非 IConfiguration、request 或 Session；
不允許由未驗證使用者切換頻道。自有 HttpClient 不使用 cookie、不跟 redirect，
每站一條連線、pool lifetime 五分鐘、每筆五秒逾時；沒有額外 queue、timer 或 retry。
可注入的 transport 在成功建構後移交 sender 擁有；設定解析失敗時仍由 caller 擁有。
request／response 在操作結束即 Dispose，Host 必須先停止並 drain diagnostics，再關閉
sender／client／transport。以 Hosting 離線 handler 測試驗證，不宣稱供應商實機結果。
*/
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Tenancy;

namespace SpeechMessage.Hosting;

/// <summary>
/// 單一部署 owner 的例外專用 LINE 傳輸；token 僅存在啟動快照與單次 request，
/// 不保存任何使用者 Session，也不使用共用可變 HTTP 標頭。
/// </summary>
public sealed class LineExceptionSender : IDisposable
{
    /// <summary>本 sender 唯一擁有的 client，Dispose 時同時釋放其 transport。</summary>
    private readonly HttpClient client;
    /// <summary>由 Tenancy 驗證的部署快照；不重新讀取 provider，關閉時清除參照。</summary>
    private string token;
    /// <summary>受信任部署設定或組合根提供的管理者收件人，不來自 request。</summary>
    private string recipient;
    /// <summary>關閉的唯一交接旗標；避免重複釋放或把已關閉 sender 再提供給下一個 caller。</summary>
    private int disposed;

    /// <summary>
    /// 在安全 guard 完成後建立部署專用 sender。所有設定先解析，再接管或建立 transport；
    /// 不保存 IConfiguration，注入的 handler 亦不可包含其他 owner 的 cookie／憑證狀態。
    /// </summary>
    /// <param name="configuration">Host 已通過安全防護的啟動設定，只在建構時借用。</param>
    /// <param name="fallbackRecipient">產品組合根明確提供的舊管理者後備值；null 代表沒有後備。</param>
    /// <param name="transport">可選的受信任傳輸；成功建構後由本 sender 負責 Dispose。</param>
    public LineExceptionSender(
        IConfiguration configuration,
        string? fallbackRecipient = null,
        HttpMessageHandler? transport = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var organization = configuration["CrmConnection:Organization"];
        if (!string.IsNullOrWhiteSpace(organization))
            organization = char.ToUpperInvariant(organization[0]) + organization[1..].ToLowerInvariant();
        token = LegacyLineChannelAccess.GetOptionalAccessToken(
            configuration,
            string.IsNullOrWhiteSpace(organization) ? null : organization,
            LineChannelSelection.EnvironmentThenOrganizationThenDefault);
        recipient = configuration["ExceptionNotifications:AdminLineUserId"] ?? fallbackRecipient ?? string.Empty;
        client = new HttpClient(transport ?? CreateTransport(), disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>
    /// 發送單筆已去識別化的例外摘要；request／response 均為操作區域 owner，不讀供應商本文。
    /// caller 取消與內部五秒逾時原樣向上傳遞，由 diagnostics 依原 caller token 判別，
    /// 不建立 retry、不遞迴通知、不以完整回應或憑證組合錯誤訊息。
    /// </summary>
    /// <param name="message">diagnostics 產生的安全 metadata，不得傳原始例外或個資。</param>
    /// <param name="cancellationToken">本筆通知的 caller 取消訊號，不保存至下一筆。</param>
    /// <returns>HTTP 操作完成工作；僅成功狀態代表 provider 接受，不宣稱使用者已收訊。</returns>
    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var accessToken = Volatile.Read(ref token);
        var target = Volatile.Read(ref recipient);
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("Exception LINE notification configuration is missing.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        request.Content = JsonContent.Create(new { to = target, messages = new[] { new { type = "text", text = message } } });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("LINE exception delivery failed.", null, response.StatusCode);
    }

    /// <summary>
    /// 先拒絕新操作，再關閉唯一 client／transport；即使外部 transport 的 Dispose 失敗，
    /// finally 仍清除 token／收件人參照，且不重入已失敗的 owner。
    /// Host 應先完成 diagnostics drain；與關閉競態的既有 HTTP 操作會由 client 取消，
    /// 不能將本物件再次借給其他部署或 request。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            client.Dispose();
        }
        finally
        {
            Interlocked.Exchange(ref token, string.Empty);
            Interlocked.Exchange(ref recipient, string.Empty);
        }
    }

    /// <summary>建立不保存 cookie、不可跨站轉送 Authorization 的部署專用有界連線池。</summary>
    /// <returns>由 HttpClient 接管的單一 handler，不放入 static 或其他 owner 的快取。</returns>
    private static SocketsHttpHandler CreateTransport() => new()
    {
        UseCookies = false,
        AllowAutoRedirect = false,
        MaxConnectionsPerServer = 1,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };
}
