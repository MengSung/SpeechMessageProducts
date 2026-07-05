// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Webhooks/WebhookEvent.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class WebhookEvent
// 主要成員：CreateFrom、Type、Source、Timestamp
// 引用命名空間：System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;

namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// The webhook event generated on the LINE Platform.
    /// https://developers.line.me/en/docs/messaging-api/reference/#webhook-event-objects
    /// </summary>
    public abstract class WebhookEvent
    {
        /// <summary>
        /// Identifier for the type of event
        /// </summary>
        public WebhookEventType Type { get; }

        /// <summary>
        /// JSON object which contains the source of the event
        /// </summary>
        public WebhookEventSource Source { get; }

        /// <summary>
        /// Time of the event in milliseconds
        /// </summary>
        public long Timestamp { get; }

        public WebhookEvent(WebhookEventType type, WebhookEventSource source, long timestamp)
        {
            Type = type;
            Source = source;
            Timestamp = timestamp;
        }

        internal static WebhookEvent CreateFrom(dynamic dynamicObject)
        {
            if (dynamicObject == null) { throw new ArgumentNullException(nameof(dynamicObject)); }

            var eventSource = WebhookEventSource.CreateFrom(dynamicObject?.source);

            if (eventSource == null)
            {
                return null;
            }
            if (!Enum.TryParse((string)dynamicObject.type, true, out WebhookEventType eventType))
            {
                return null;
            }

            switch (eventType)
            {
                case WebhookEventType.Message:
                    EventMessage eventMessage = EventMessage.CreateFrom(dynamicObject);
                    if (eventMessage == null) { return null; }
                    return new MessageEvent(eventSource, (long)dynamicObject.timestamp, eventMessage, (string)dynamicObject.replyToken);
                case WebhookEventType.Follow:
                    return new FollowEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken);
                case WebhookEventType.Unfollow:
                    return new UnfollowEvent(eventSource, (long)dynamicObject.timestamp);
                case WebhookEventType.Join:
                    return new JoinEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken);
                case WebhookEventType.Leave:
                    return new LeaveEvent(eventSource, (long)dynamicObject.timestamp);
                case WebhookEventType.Postback:
                    var postback = new Postback(
                        (string)dynamicObject.postback?.data,
                        (string)dynamicObject.postback?.@params?.date,
                        (string)dynamicObject.postback?.@params?.time,
                        (string)dynamicObject.postback?.@params?.datetime);
                    return new PostbackEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken, postback);
                case WebhookEventType.Beacon:
                    if (!Enum.TryParse((string)dynamicObject.beacon.type, true, out BeaconType beaconType))
                    {
                        return null;
                    }
                    return new BeaconEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken,
                        (string)dynamicObject.beacon.hwid, beaconType, (string)dynamicObject.beacon.dm);
                case WebhookEventType.AccountLink:
                    var link = new Link((string)dynamicObject.link?.result, (string)dynamicObject.link?.nonce);
                    return new AccountLinkEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken, link);
                case WebhookEventType.Things:
                    var thingsType = (ThingsType)Enum.Parse(typeof(ThingsType), (string)dynamicObject.things?.type, true);
                    var things = new Things((string)dynamicObject.things?.deviceId, thingsType);
                    return DeviceEvent.Create(eventSource, (long)dynamicObject.timestamp, things);
                case WebhookEventType.MemberJoined:

                    var joinedMembers = new List<WebhookEventSource>();
                    foreach (var member in dynamicObject.joined.members)
                    {
                        joinedMembers.Add(WebhookEventSource.CreateFrom(member));
                    }
                    return new MemberJoinEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken, joinedMembers);

                case WebhookEventType.MemberLeft:
                    var leftMembers = new List<WebhookEventSource>();
                    foreach (var member in dynamicObject.left.members)
                    {
                        leftMembers.Add(WebhookEventSource.CreateFrom(member));
                    }
                    return new MemberLeaveEvent(eventSource, (long)dynamicObject.timestamp, leftMembers);
                default:
                    return null;
            }
        }
    }
}
