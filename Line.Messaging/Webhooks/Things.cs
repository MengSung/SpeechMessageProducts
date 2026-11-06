/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述LINE Things裝置識別與事件種類。
 *
 * 主要功能與流程：
 * 本檔宣告 Things。
 * 資料／選項欄位包含 Things、DeviceId、Type，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/DeviceEvent.cs、Line.Messaging/Webhooks/DeviceLinkEvent.cs、
 * Line.Messaging/Webhooks/DeviceUnLinkEvent.cs、Line.Messaging/Webhooks/ThingsType.cs、
 * Line.Messaging/Webhooks/WebhookEvent.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// LINE Things
    /// </summary>
    public class Things
    {
        /// <summary>
        /// Device ID of the LINE Things-compatible device that was linked with LINE
        /// </summary>
        public string DeviceId { get; }

        /// <summary>
        /// Link or Unlink
        /// </summary>
        public ThingsType Type { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="deviceId">Device ID of the LINE Things-compatible device that was linked with LINE</param>
        /// <param name="type">Link or Unlink</param>
        public Things(string deviceId, ThingsType type)
        {
            DeviceId = deviceId;
            Type = type;
        }

    }
}
