/*
 * 檔案整體說明
 *
 * 目的與定位：本檔表示LINE Things裝置解除連結的事件。
 *
 * 主要功能與流程：
 * 本檔宣告 DeviceUnlinkEvent。
 * 功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/DeviceEvent.cs、Line.Messaging/Webhooks/WebhookApplication.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Indicates that a LINE Things-compatible device has been unlinked from LINE by a user operation. For more information, see Receiving device unlink events via webhook.
    /// </summary>
    public class DeviceUnlinkEvent : DeviceEvent
    {
        public DeviceUnlinkEvent(WebhookEventSource source, long timestamp, Things things) : base(source, timestamp, things)
        {
        }
    }

}
