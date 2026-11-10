/*
檔案整體說明

目的與定位：提供 P3-08 Webhook Reply API 的 scoped 傳輸邊界，將 Core 的穩定回覆
資源鍵與有界參數轉成單次 LINE TextMessage，並提供預設關閉的測試站回覆觀察紀錄。

主要功能與流程：先以已授權 scope 建立文字 context，再由 GoodShepherd 合併 catalog
解析使用者可見文字與教會 sender 名稱；icon 只有部署提供 HTTPS URL 時才加入。每次
傳送都透過命名 IHttpClientFactory 建立短命 client，Authorization 僅附於該次 request，
不改動共用 DefaultRequestHeaders；只使用當筆 envelope 的 reply token，不改走 push 或固定開發者帳號。
呼叫端等待 HTTP response headers／body 完成後讀取實際狀態碼，response、request 與短命 client
均在本次操作結束時 Dispose；factory 單獨管理共用無 cookie／redirect handler。回覆紀錄只在
部署啟動快照明確開啟時輸出固定 EventId，且在寫入前遮罩 actor／display LINE userId。

使用邊界與維護：本檔不保存 token、文字、Entity、HttpContext 或跨事件狀態，也不建立
背景工作、timer、cache 或訂閱。sender 擁有單次 request、response 與 factory 回傳的 HttpClient；
handler／connection pool 由 IHttpClientFactory 擁有。回覆 log 設定預設關閉、只從受信任部署組態
讀取一次；啟用時只記 event ID、固定 message type、遮罩後的回覆文字、scope 語系及 HTTP status，
不記 channel token、reply token、secret 或未遮罩 LINE userId。成功、非成功與取消都等待傳輸結束並
Dispose；HTTP 非成功轉成只含狀態碼的安全 LineResponseException，原 transport exception 保持原型別
向 queue／Hosting diagnostics 傳遞。scope、catalog、logger、factory 與 handler 設定由 Host 擁有。
*/
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using LineMessagingProcessor;
using LineMessagingProcessor.Core.Templates;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;

/// <summary>單筆 Webhook 回覆的傳輸契約；實際 owner 必須等待傳輸完成後才返回。</summary>
public interface IWebhookReplySender
{
    /// <summary>依結果資源鍵傳送一次性 reply；沒有 reply key 時不建立傳輸。</summary>
    /// <param name="webhookEventId">此已驗簽 envelope 的穩定 event ID，只供本次傳送觀察。</param>
    /// <param name="scope">已核對組織、教會、頻道及 actor 的單事件 scope。</param>
    /// <param name="replyToken">該 envelope 的一次性 token。</param>
    /// <param name="result">已完成業務操作的純值結果。</param>
    /// <param name="cancellationToken">queue 的單事件取消訊號。</param>
    ValueTask SendAsync(
        string webhookEventId,
        ResolvedWebhookScope scope,
        string? replyToken,
        WebhookBusinessResult result,
        CancellationToken cancellationToken);
}

/// <summary>
/// 使用 LINE Reply API 的 scoped 傳輸實作；Authorization 只放在單次 request，
/// 共用 handler 不保存 cookie 或 credential，回應、request 與短命 client 都由本次操作釋放。
/// </summary>
public sealed class LineWebhookReplySender : IWebhookReplySender
{
    private const string HttpClientName = "LineWebhookReplySender";
    private const string ReplyEndpoint = "https://api.line.me/v2/bot/message/reply";
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new StringEnumConverter { NamingStrategy = new CamelCaseNamingStrategy() } },
        NullValueHandling = NullValueHandling.Ignore
    };
    private readonly GoodShepherdTextCatalog catalog;
    private readonly SenderNameFactory senderNames;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<LineWebhookReplySender> logger;
    private readonly bool enableReplyContentLogging;

    /// <summary>
    /// 建立單事件 Reply sender；每次傳輸由命名 HttpClient factory 提供短命 client，
    /// 不在 sender 欄位保存任何事件、reply token 或 Channel credential。
    /// </summary>
    /// <param name="catalog">Host 擁有、無 request 狀態的合併文字 catalog。</param>
    /// <param name="httpClientFactory">
    /// 由 Host／測試擁有的 HttpClient factory；本 sender 只擁有每次 CreateClient 回傳的 client，
    /// 於 SDK 傳輸排空後釋放，不保存 cookie、request、token 或跨事件狀態。
    /// </param>
    public LineWebhookReplySender(
        GoodShepherdTextCatalog catalog,
        IHttpClientFactory httpClientFactory)
        : this(catalog, httpClientFactory, NullLogger<LineWebhookReplySender>.Instance, false)
    {
    }

    /// <summary>
    /// 建立單事件 Reply sender；logger 與開關是 Host 提供的啟動快照，預設不輸出回覆本文。
    /// </summary>
    /// <param name="catalog">Host 擁有、無 request 狀態的合併文字 catalog。</param>
    /// <param name="httpClientFactory">Host 擁有的命名 client factory；sender 只處置每次建立的短命 client。</param>
    /// <param name="logger">Host 擁有、category 固定為此 sender 型別的 logger。</param>
    /// <param name="enableReplyContentLogging">測試站明確開啟才記錄已遮罩的單次回覆資料；預設關閉。</param>
    public LineWebhookReplySender(
        GoodShepherdTextCatalog catalog,
        IHttpClientFactory httpClientFactory,
        ILogger<LineWebhookReplySender> logger,
        bool enableReplyContentLogging = false)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        senderNames = new SenderNameFactory(this.catalog);
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.enableReplyContentLogging = enableReplyContentLogging;
    }

    /// <summary>
    /// 將結果轉成一則 catalog text message 並以本事件 token 回覆；傳輸失敗原樣向上拋出。
    /// 單次 request、response 與 factory client 均在本次非同步呼叫結束時 Dispose。
    /// </summary>
    /// <param name="webhookEventId">已驗簽 envelope 的穩定事件識別，只在本次回覆紀錄中使用。</param>
    /// <param name="scope">已驗證組織、教會及會友的本事件 scope。</param>
    /// <param name="replyToken">同一 envelope 的一次性 reply token，不得替換為 push recipient。</param>
    /// <param name="result">不含 request／SDK 狀態的 bounded 回覆結果。</param>
    /// <param name="cancellationToken">queue 本事件取消訊號。</param>
    /// <returns>完成單次 LINE Reply HTTP 回應並釋放本次資源後結束的非同步作業。</returns>
    public async ValueTask SendAsync(
        string webhookEventId,
        ResolvedWebhookScope scope,
        string? replyToken,
        WebhookBusinessResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(webhookEventId) || webhookEventId.Length > 64)
            throw new ArgumentException("A bounded webhook event ID is required.", nameof(webhookEventId));
        cancellationToken.ThrowIfCancellationRequested();
        if (result.ReplyKey is null) return;
        if (string.IsNullOrWhiteSpace(replyToken))
            throw new InvalidOperationException("A reply token is required for a reply result.");

        var arguments = new TextArg[result.ReplyArguments.Count];
        for (var index = 0; index < result.ReplyArguments.Count; index++)
        {
            var argument = result.ReplyArguments[index];
            arguments[index] = new TextArg(argument.Name, argument.Value);
        }

        var text = catalog.GetText(scope.TextContext, new TextKey(result.ReplyKey), arguments);
        if (text.Length == 0 || text.Length > 2000)
            throw new InvalidOperationException("The reply text exceeds the LINE text budget.");
        var message = new TextMessage(text);
        if (!string.IsNullOrWhiteSpace(scope.SenderIconUrl))
        {
            var sender = senderNames.Create(scope.TextContext, scope.ChurchName);
            message.Sender = new Sender(sender, scope.SenderIconUrl);
        }

        using var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, ReplyEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", scope.AccessToken);
        request.Content = new System.Net.Http.StringContent(
            JsonConvert.SerializeObject(new { replyToken, messages = new List<ISendMessage> { message } }, JsonSettings),
            System.Text.Encoding.UTF8,
            "application/json");
        using var response = await httpClient.SendAsync(
            request,
            System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (enableReplyContentLogging)
            LogReplyResponse(webhookEventId, scope, text, response.StatusCode);

        if (!response.IsSuccessStatusCode)
            throw new LineResponseException($"LINE API request failed. HTTP {(int)response.StatusCode}.")
            {
                StatusCode = response.StatusCode,
                ResponseMessage = new ErrorResponseMessage
                {
                    Message = "LINE API request failed.",
                    Details = Array.Empty<ErrorResponseMessage.ErrorDetails>()
                }
            };
    }

    /// <summary>
    /// 只在啟用的測試站輸出固定結構事件；精確移除本次 scope 的 actor/display LINE ID，
    /// 不記錄任何 credential，且 logger 不持有額外狀態或資源。
    /// </summary>
    private void LogReplyResponse(
        string webhookEventId,
        ResolvedWebhookScope scope,
        string text,
        HttpStatusCode statusCode)
    {
        var safeText = MaskUserId(text, scope.SourceId);
        if (!string.Equals(scope.DisplaySourceId, scope.SourceId, StringComparison.Ordinal))
            safeText = MaskUserId(safeText, scope.DisplaySourceId);
        logger.LogInformation(
            new EventId(30610, "Webhook.ReplyResponse"),
            "Webhook reply API response {WebhookEventId} {MessageType} {Text} {Culture} HTTP {StatusCode}",
            webhookEventId,
            "text",
            safeText,
            scope.MemberCulture,
            (int)statusCode);
    }

    /// <summary>將單一已驗證 LINE userId 改為 U*** 加末四碼；非目標字串原樣保留。</summary>
    private static string MaskUserId(string text, string userId)
    {
        if (userId.Length == 0) return text;
        if (userId.Length < 6) return text.Replace(userId, "***", StringComparison.Ordinal);
        var masked = userId[..1] + "***" + userId[^4..];
        return text.Replace(userId, masked, StringComparison.Ordinal);
    }
}
