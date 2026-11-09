/*
檔案整體說明

目的與定位：提供 P3-08 Webhook Reply API 的 scoped 傳輸邊界，將 Core 的穩定回覆
資源鍵與有界參數轉成單次 LINE TextMessage。

主要功能與流程：先以已授權 scope 建立文字 context，再由 GoodShepherd 合併 catalog
解析使用者可見文字與教會 sender 名稱；icon 只有部署提供 HTTPS URL 時才加入。每次
傳送都以該 scope 的 access token 建立並 Dispose 一個 LineMessagingProcessorClass，
只使用當筆 envelope 的 reply token，不改走 push 或固定開發者帳號。

使用邊界與維護：本檔不保存 token、文字、Entity、HttpContext 或跨事件狀態，也不建立
背景工作、timer、cache 或訂閱。Processor 是本方法唯一的傳輸 owner，回覆完成或失敗
後立即 Dispose；真實例外保留原型別與堆疊交給 queue／Hosting diagnostics，caller
取消只在傳輸前 fail fast。catalog、scope 與設定由外部 Host 擁有。
*/
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Core.Templates;
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

/// <summary>使用真 LINE processor 的 scoped Reply 實作；沒有共享可變傳輸狀態。</summary>
public sealed class LineWebhookReplySender(GoodShepherdTextCatalog catalog) : IWebhookReplySender
{
    private readonly SenderNameFactory senderNames = new(catalog ?? throw new ArgumentNullException(nameof(catalog)));

    /// <summary>
    /// 將結果轉成一則 catalog text message 並以本事件 token 回覆；傳輸失敗原樣向上拋出。
    /// </summary>
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

        using var processor = new LineMessagingProcessorClass(scope.AccessToken);
        await processor.ReplyMessagesAsync(replyToken, new List<ISendMessage> { message }).ConfigureAwait(false);
    }
}
