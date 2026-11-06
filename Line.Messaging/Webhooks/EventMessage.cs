/*
 * 檔案整體說明
 *
 * 目的與定位：本檔作為webhook訊息內容的共同基底，依訊息型別保留正式訊息ID。
 *
 * 主要功能與流程：
 * 本檔宣告 EventMessage。
 * 主要操作入口包含 CreateFrom。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/FileEventMessage.cs、Line.Messaging/Webhooks/LocationEventMessage.cs、
 * Line.Messaging/Webhooks/MessageEvent.cs、Line.Messaging/Webhooks/MidiaEventMessage.cs、
 * Line.Messaging/Webhooks/StickerEventMessage.cs 追查目前工作樹中的合作／使用位置。
 */
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
