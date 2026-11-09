/*
檔案整體說明
目的與定位：P3-06 正式 queue handler，以真 LINE SDK 事件模型分派至 GoodShepherd 具體核心或 HTTP／CRM adapter。
主要功能與流程：每筆 envelope 建構單事件 wrapper，使用 WebhookEventParser 解析真實 SDK 型別，依 12 種 Golden 事件逐一窄分派。頻道與使用者只作不可信提示；任何 CRM／profile 行為先由 scope resolver 以組織、account 指派及正式 contact ID 授權。媒體 image/video/file/location/sticker 明確沿舊 comment-only 無副作用忽略；audio 真下載並寫 annotation。
使用邊界與維護：Scoped handler 只活於 queue scope；不保存 token、replyToken、Entity 或事件，無 static state。所有 HTTP／CRM await 於回傳前完成，遵守 queue 事件取消；例外原樣拋交 queue diagnostics，正常 host cancellation 沿取消語義。Reply／letter 尚待 P3-08，正式 intent catalog 待 P3-07。已知 no-op、待後續包或未綁定事件只寫不含事件識別資料的固定 EventId 資訊紀錄；未知 parser 分支也具名紀錄，不能回假成功。
*/
using Line.Messaging.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.Extensions.Logging;
using SpeechMessageProducts.GoodShepherd.Core.Appointments;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Members;

namespace LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;

/// <summary>正式 scoped SDK 分派器；沒有 sink、NotImplemented 或成功丟棄分支。</summary>
public sealed class WebhookDispatcher(IWebhookScopeResolver scopes, WebhookTextHandler text, WebhookHttpAdapter http,
    WebhookAudioHandler audio, ILogger<WebhookDispatcher> logger) : IWebhookEnvelopeHandler
{
    /// <summary>一 envelope 僅有一 event；使用上游已保存的 clone，再以真 SDK parser 解析並等待具體操作。</summary>
    public async ValueTask HandleAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = envelope.Event.GetRawText();
        var parsed = System.Linq.Enumerable.SingleOrDefault(WebhookEventParser.Parse("{\"events\":[" + json + "]}"));
        if (parsed is null)
        {
            RecordDisposition("Webhook.UnknownEventTypeDeferred", "Webhook event type is not supported by the current SDK parser.");
            return;
        }
        var source = parsed.Source;
        if (source is null || !ResolvedWebhookScope.ValidSource(source.Id))
        {
            RecordDisposition("Webhook.InvalidSourceIgnored", "Webhook dispatch ignored an event with an invalid source boundary.");
            return;
        }
        switch (parsed)
        {
            case MessageEvent { Message: TextEventMessage message }:
            {
                if (!TryGetActorSourceId(source.UserId, out var actorSourceId))
                {
                    RecordDisposition("Webhook.MissingUserSourceIgnored", "Webhook event requires a user source identity.");
                    return;
                }
                var scope = scopes.Resolve(envelope.ChannelId, actorSourceId, source.Id, cancellationToken);
                if (scope.Contact is not null)
                    RecordBusinessResult("Text", text.Handle(
                        scope.Service,
                        scope.Actor(),
                        scope.DisplayContact,
                        scope.DisplaySourceId,
                        message.Text,
                        cancellationToken));
                else RecordDisposition("Webhook.UnboundTextIgnored", "Webhook text dispatch requires a bound contact.");
                return;
            }
            case MessageEvent { Message: MediaEventMessage { Type: EventMessageType.Audio } message }:
            {
                if (!TryGetActorSourceId(source.UserId, out var actorSourceId))
                {
                    RecordDisposition("Webhook.MissingUserSourceIgnored", "Webhook event requires a user source identity.");
                    return;
                }
                var scope = scopes.Resolve(envelope.ChannelId, actorSourceId, source.Id, cancellationToken);
                if (scope.Contact is not null)
                    RecordBusinessResult("Audio", await audio.HandleAsync(scope, envelope.WebhookEventId, message.Id, cancellationToken).ConfigureAwait(false));
                else RecordDisposition("Webhook.UnboundAudioIgnored", "Webhook audio dispatch requires a bound contact.");
                return;
            }
            case MessageEvent { Message: MediaEventMessage { Type: EventMessageType.Image } }:
            case MessageEvent { Message: MediaEventMessage { Type: EventMessageType.Video } }:
            case MessageEvent { Message: FileEventMessage }:
            case MessageEvent { Message: LocationEventMessage }:
            case MessageEvent { Message: StickerEventMessage }:
                RecordDisposition("Webhook.LegacyMediaNoOp", "Webhook media event has no proven legacy side effect.");
                return;
            case FollowEvent:
            {
                if (!TryGetActorSourceId(source.UserId, out var actorSourceId))
                {
                    RecordDisposition("Webhook.MissingUserSourceIgnored", "Webhook event requires a user source identity.");
                    return;
                }
                var scope = scopes.Resolve(envelope.ChannelId, actorSourceId, source.Id, cancellationToken);
                var profile = await http.ReadProfileAsync(scope, cancellationToken).ConfigureAwait(false);
                RecordBusinessResult("Follow", WebhookMembershipHandler.Follow(
                    scope.Service, scope.OrganizationId, scope.ChurchId, scope.SourceId, scope.Contact,
                    profile.DisplayName, profile.PictureUrl, profile.StatusMessage, cancellationToken));
                return;
            }
            case UnfollowEvent:
            {
                if (!TryGetActorSourceId(source.UserId, out var actorSourceId))
                {
                    RecordDisposition("Webhook.MissingUserSourceIgnored", "Webhook event requires a user source identity.");
                    return;
                }
                var scope = scopes.Resolve(envelope.ChannelId, actorSourceId, source.Id, cancellationToken);
                RecordBusinessResult("Unfollow", WebhookMembershipHandler.Unfollow(
                    scope.Service, scope.ChurchId, scope.SourceId, scope.Contact, cancellationToken));
                return;
            }
            case JoinEvent:
            {
                var scope = scopes.Resolve(envelope.ChannelId, source.Id, source.Id, cancellationToken);
                var profile = source.Id[0] == 'C'
                    ? await http.ReadProfileAsync(scope, cancellationToken).ConfigureAwait(false)
                    : new WebhookProfile(
                        WebhookText.Neutral("Webhook.Word.Room") + "-" + source.Id,
                        string.Empty,
                        string.Empty);
                RecordBusinessResult("Join", WebhookMembershipHandler.Join(
                    scope.Service,
                    scope.OrganizationId,
                    scope.ChurchId,
                    scope.SourceId,
                    scope.Contact,
                    profile.DisplayName,
                    profile.PictureUrl,
                    cancellationToken));
                return;
            }
            case MemberJoinEvent:
                RecordDisposition("Webhook.MemberJoinReplyPendingP308", "Webhook member-join reply is pending P3-08.");
                return;
            case LeaveEvent:
                RecordDisposition("Webhook.LeaveNoCrmMutation", "Webhook leave preserves the legacy no-CRM-mutation behavior.");
                return;
            case PostbackEvent postback:
            {
                if (!TryGetActorSourceId(source.UserId, out var actorSourceId))
                {
                    RecordDisposition("Webhook.MissingUserSourceIgnored", "Webhook event requires a user source identity.");
                    return;
                }
                var scope = scopes.Resolve(envelope.ChannelId, actorSourceId, source.Id, cancellationToken);
                if (scope.Contact is not null)
                    RecordBusinessResult("Postback", WebhookPostbackHandler.Handle(scope.Service, scope.Actor(), postback.Postback.Data, cancellationToken));
                else RecordDisposition("Webhook.UnboundPostbackIgnored", "Webhook postback dispatch requires a bound contact.");
                return;
            }
            case AccountLinkEvent:
                RecordDisposition("Webhook.AccountLinkReplyPendingP308", "Webhook account-link reply is pending P3-08.");
                return;
            default:
                RecordDisposition("Webhook.UnsupportedParsedEvent", "Webhook event type has no dispatch implementation.");
                return;
        }
    }

    /// <summary>
    /// 只接受 LINE user source 作為會友 actor。群組／聊天室的 C/R ID 是訊息容器，
    /// 不是人員身分；缺少供應商提供的 userId 時必須在授權查詢前停止，避免跨使用者
    /// 或跨教會資料被錯誤套用。這個純值檢查不保存事件或共享狀態。
    /// </summary>
    private static bool TryGetActorSourceId(string? userId, out string actorSourceId)
    {
        actorSourceId = userId ?? string.Empty;
        return actorSourceId.Length == 33 &&
            actorSourceId[0] == 'U' &&
            ResolvedWebhookScope.ValidSource(actorSourceId);
    }

    /// <summary>
    /// 將 Core 明確結果轉成固定安全診斷；只允許短 ASCII 業務代碼，狀態以 enum 呈現，
    /// 不記錄使用者本文、事件識別、顯示名稱、Entity、replyToken、憑證或例外。
    /// </summary>
    private void RecordBusinessResult(string operation, WebhookBusinessResult result)
    {
        var code = result.Code;
        var safeCode = code.Length is > 0 and <= 96 &&
            code.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-')
                ? code
                : "InvalidBusinessResultCode";
        RecordDisposition(
            $"Webhook.{operation}.{result.Status}.{safeCode}",
            "Webhook business handler returned an explicit disposition.");
    }

    /// <summary>寫入固定、無事件識別欄位的資訊紀錄；不保存或格式化事件、token、Contact 或 exception。</summary>
    private void RecordDisposition(string eventName, string message) =>
        logger.LogInformation(new EventId(30600, eventName), "{WebhookDisposition}", message);
}
