/*
 * 檔案整體說明
 *
 * 目的與定位：本檔作為LINE Things裝置事件共同基底，將裝置資料投影為具體事件。
 *
 * 主要功能與流程：
 * 本檔宣告 DeviceEvent。
 * 主要操作入口包含 Create。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/DeviceLinkEvent.cs、Line.Messaging/Webhooks/DeviceUnLinkEvent.cs、
 * Line.Messaging/Webhooks/WebhookEvent.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    public abstract class DeviceEvent : WebhookEvent
    {
        public Things Things { get; }
        public DeviceEvent(WebhookEventSource source, long timestamp, Things things)
            : base(WebhookEventType.Things, source, timestamp)
        {
            Things = things;
        }

        public static DeviceEvent Create(WebhookEventSource source, long timestamp, Things things)
        {
            return (things.Type == ThingsType.Link)
                ? new DeviceLinkEvent(source, timestamp, things) as DeviceEvent
                : new DeviceUnlinkEvent(source, timestamp, things) as DeviceEvent;
        }
    }

}
