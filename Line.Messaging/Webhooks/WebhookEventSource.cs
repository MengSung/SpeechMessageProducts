/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述事件來自使用者、群組或聊天室以及對應識別，不把來源字串當成產品授權。
 *
 * 主要功能與流程：
 * 本檔宣告 WebhookEventSource。
 * 主要操作入口包含 CreateFrom。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/AccountLinkEvent.cs、Line.Messaging/Webhooks/BeaconEvent.cs、
 * Line.Messaging/Webhooks/DeviceEvent.cs、Line.Messaging/Webhooks/DeviceLinkEvent.cs、
 * Line.Messaging/Webhooks/DeviceUnLinkEvent.cs 追查目前工作樹中的合作／使用位置。
 */
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
