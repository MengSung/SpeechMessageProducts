// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Webhooks/EventMessage.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class EventMessage
// 主要成員：CreateFrom、Id、Type
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
    /// Contents of the message
    /// </summary>
    public class EventMessage
    {
        /// <summary>
        /// Message ID
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// EventMessageType
        /// </summary>
        public EventMessageType Type { get; }

        public EventMessage(EventMessageType type, string id)
        {
            Type = type;
            Id = id;
        }

        internal static EventMessage CreateFrom(dynamic dynamicObject)
        {
            var message = dynamicObject?.message;
            if (message == null) { return null; }
            if (!Enum.TryParse((string)message.type, true, out EventMessageType messageType))
            {
                return null;
            }
            switch (messageType)
            {
                case EventMessageType.Text:
                    return new TextEventMessage((string)message.id, (string)message.text);
                case EventMessageType.Image:
                case EventMessageType.Audio:
                case EventMessageType.Video:
                    ContentProvider contentProvider = null;
                    if (Enum.TryParse((string)message.contentProvider?.type,true, out ContentProviderType providerType))
                    {
                        contentProvider = new ContentProvider(providerType,
                                (string)message.contentProvider?.originalContentUrl,
                                (string)message.contentProvider?.previewContentUrl);
                    }
                    return new MediaEventMessage(messageType, (string)message.id, contentProvider, (int?)message.duration);
                case EventMessageType.Location:
                    return new LocationEventMessage((string)message.id, (string)message.title, (string)message.address,
                        (decimal)message.latitude, (decimal)message.longitude);
                case EventMessageType.Sticker:
                    return new StickerEventMessage((string)message.id, (string)message.packageId, (string)message.stickerId);
                case EventMessageType.File:
                    return new FileEventMessage((string)message.id, (string)message.fileName, (long)message.fileSize);
                default:
                    return null;
            }
        }
    }
}
