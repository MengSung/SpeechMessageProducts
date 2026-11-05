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
    /// LINE webhook 的單筆事件；保留原有型別、來源與時間建構契約，新投遞欄位僅由解析器設定。
    /// 模型不驗證簽章也不授予租戶權限；呼叫端必須先驗原始 body 的簽章再消費事件。
    /// </summary>
    public abstract class WebhookEvent
    {
        /// <summary>平台提供的穩定事件 ID；重送保持同值，缺少欄位的舊事件為 null；不取代後續業務資料庫 ID。</summary>
        public string WebhookEventId { get; private set; }

        /// <summary>本次投遞資訊；每個事件獨立持有，不保留 JSON、HTTP request、token 或跨事件的可變狀態。</summary>
        public DeliveryContext DeliveryContext { get; private set; } = new DeliveryContext(false);

        /// <summary>
        /// 事件類別，由已知 enum 判定；未知類別沿用舊行為略過，不猜測業務意圖。
        /// </summary>
        public WebhookEventType Type { get; }

        /// <summary>
        /// 此事件專屬的來源資料；只供路由提示，不能直接視為伺服器端已授權使用者。
        /// </summary>
        public WebhookEventSource Source { get; }

        /// <summary>
        /// 平台事件時間的 Unix 毫秒值；重送時不以接收時間覆寫，供後續去重與排序。
        /// </summary>
        public long Timestamp { get; }

        /// <summary>建立事件既有欄位；建構時沒有 HTTP、token 或共用可變狀態，投遞資訊預設為非重送。</summary>
        /// <param name="type">已知事件類別。</param><param name="source">事件專屬來源。</param><param name="timestamp">平台事件 Unix 毫秒。</param>
        public WebhookEvent(WebhookEventType type, WebhookEventSource source, long timestamp)
        {
            Type = type;
            Source = source;
            Timestamp = timestamp;
        }

        /// <summary>在既有各類事件建構後補入共同投遞欄位；所有類別共走此入口，避免遺漏某個 switch 分支。</summary>
        /// <param name="dynamicObject">呼叫端已完成驗簽及 JSON 解析的單筆物件；不保存原 JSON。</param><returns>可辨識的 detached 事件，或沿用舊行為回 null。</returns>
        internal static WebhookEvent CreateFrom(dynamic dynamicObject)
        {
            WebhookEvent result = CreateEvent(dynamicObject);
            if (result == null) return null;
            result.WebhookEventId = (string)dynamicObject.webhookEventId;
            result.DeliveryContext = new DeliveryContext((bool?)dynamicObject.deliveryContext?.isRedelivery ?? false);
            return result;
        }

        /// <summary>保留既有類別映射，不把投遞 ID 當成訊息或資料庫記錄的 ID；整個候選完成後才交還呼叫端。</summary>
        /// <param name="dynamicObject">單筆已解析事件。</param><returns>已知事件或 null。</returns>
        private static WebhookEvent CreateEvent(dynamic dynamicObject)
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
