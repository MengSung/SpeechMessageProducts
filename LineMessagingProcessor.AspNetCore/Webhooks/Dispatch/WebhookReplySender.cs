/*
檔案整體說明

目的與定位：提供 P3-08 Webhook Reply API 的 scoped 傳輸邊界，將 Core 的穩定回覆
資源鍵與有界參數轉成單次 LINE TextMessage。

主要功能與流程：先以已授權 scope 建立文字 context，再由 GoodShepherd 合併 catalog
解析使用者可見文字與教會 sender 名稱；icon 只有部署提供 HTTPS URL 時才加入。每次
傳送都透過命名 IHttpClientFactory 建立短命 client，再以該 scope 的 access token 建立
專屬 LineMessagingClient 與 LineMessagingProcessorClass；SDK 傳輸排空後依序釋放 processor、
SDK credential 與 client。共用 handler 由 factory 管理且禁用 cookie／redirect，Authorization
只附在本次 SDK request，不改動共用 DefaultRequestHeaders；只使用當筆 envelope 的 reply token，
不改走 push 或固定開發者帳號。

使用邊界與維護：本檔不保存 token、文字、Entity、HttpContext 或跨事件狀態，也不建立
背景工作、timer、cache 或訂閱。sender 擁有單次 SDK client 與 factory 回傳的 HttpClient；
handler／connection pool 由 IHttpClientFactory 擁有。成功或失敗都等待傳輸完成並 Dispose，
真實例外保留原型別與堆疊交給 queue／Hosting diagnostics；caller 取消只在傳輸前 fail fast，
實際 HTTP timeout 由命名 client 的有限預設逾時界定。catalog、scope、factory 與 handler 設定由 Host 擁有。
*/
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Core.Templates;
using Microsoft.Extensions.Http;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;

/// <summary>單筆 Webhook 回覆的傳輸契約；實際 owner 必須等待傳輸完成後才返回。</summary>
public interface IWebhookReplySender
{
    /// <summary>依結果資源鍵傳送一次性 reply；沒有 reply key 時不建立傳輸。</summary>
    /// <param name="scope">已核對組織、教會、頻道及 actor 的單事件 scope。</param>
    /// <param name="replyToken">該 envelope 的一次性 token。</param>
    /// <param name="result">已完成業務操作的純值結果。</param>
    /// <param name="cancellationToken">queue 的單事件取消訊號。</param>
    ValueTask SendAsync(
        ResolvedWebhookScope scope,
        string? replyToken,
        WebhookBusinessResult result,
        CancellationToken cancellationToken);
}

/// <summary>
/// 使用真 LINE processor 的 scoped Reply 實作；每次操作建立並釋放專屬 SDK credential 與
/// factory client，底層 handler 只共用無 cookie 的連線池，沒有共享可變認證狀態。
/// </summary>
public sealed class LineWebhookReplySender : IWebhookReplySender
{
    private const string HttpClientName = "LineWebhookReplySender";
    private readonly GoodShepherdTextCatalog catalog;
    private readonly SenderNameFactory senderNames;
    private readonly IHttpClientFactory httpClientFactory;

    /// <summary>
    /// 建立單事件 Reply sender；每次傳輸由命名 HttpClient factory 提供短命 client，
    /// token 專屬的 SDK client 與 processor 只在單筆傳輸期間存在。
    /// </summary>
    /// <param name="catalog">Host 擁有、無 request 狀態的合併文字 catalog。</param>
    /// <param name="httpClientFactory">
    /// 由 Host／測試擁有的 HttpClient factory；本 sender 只擁有每次 CreateClient 回傳的 client，
    /// 於 SDK 傳輸排空後釋放，不保存 cookie、request、token 或跨事件狀態。
    /// </param>
    public LineWebhookReplySender(
        GoodShepherdTextCatalog catalog,
        IHttpClientFactory httpClientFactory)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        senderNames = new SenderNameFactory(this.catalog);
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// 將結果轉成一則 catalog text message 並以本事件 token 回覆；傳輸失敗原樣向上拋出。
    /// Processor 僅由本次呼叫擁有，等待 SDK 操作完成後立即 Dispose。
    /// </summary>
    /// <param name="scope">已驗證組織、教會及會友的本事件 scope。</param>
    /// <param name="replyToken">同一 envelope 的一次性 reply token，不得替換為 push recipient。</param>
    /// <param name="result">不含 request／SDK 狀態的 bounded 回覆結果。</param>
    /// <param name="cancellationToken">queue 本事件取消訊號。</param>
    /// <returns>完成單次 SDK Reply 後結束的非同步作業。</returns>
    public async ValueTask SendAsync(
        ResolvedWebhookScope scope,
        string? replyToken,
        WebhookBusinessResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(result);
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
        using var sdkClient = new LineMessagingClient(httpClient, scope.AccessToken);
        using var processor = new LineMessagingProcessorClass(sdkClient);
        await processor.ReplyMessagesAsync(replyToken, new List<ISendMessage> { message }).ConfigureAwait(false);
    }
}
