// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Webhooks/WebhookEventSource.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class WebhookEventSource
// 主要成員：CreateFrom、Type、Id、UserId
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Webhook Event Source. Source could be User, Group or Room.
    /// </summary>
    public class WebhookEventSource
    {
        public EventSourceType Type { get; }

        /// <summary>
        /// User, Group or Room Id
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// UserId of the Group or Room
        /// </summary>
        public string UserId { get; }

        public WebhookEventSource(EventSourceType type, string sourceId, string userId)
        {
            Type = type;
            Id = sourceId;
            UserId = userId;
        }

        internal static WebhookEventSource CreateFrom(dynamic source)
        {
            if (source == null) { return null; }
            if (!Enum.TryParse((string)source.type, true, out EventSourceType sourceType))
            {
                return null;
            }
            var sourceId = "";
            switch (sourceType)
            {
                case EventSourceType.User:
                    sourceId = (string)source.userId;
                    break;
                case EventSourceType.Group:
                    sourceId = (string)source.groupId;
                    break;
                case EventSourceType.Room:
                    sourceId = (string)source.roomId;
                    break;
                default:
                    return null;
            }
            return new WebhookEventSource(sourceType, sourceId, (string)source.userId);
        }
    }
}
