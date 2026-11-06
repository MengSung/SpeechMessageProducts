/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述使用者傳入的位置標題、地址與經緯度。
 *
 * 主要功能與流程：
 * 本檔宣告 LocationEventMessage。
 * 資料／選項欄位包含 Title、Address、Latitude、Longitude，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/EventMessage.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Message object which contains the location data sent from the source.
    /// </summary>
    public class LocationEventMessage : EventMessage
    {
        /// <summary>
        /// Title
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Address
        /// </summary>
        public string Address { get; }

        /// <summary>
        /// Latitude
        /// </summary>
        public decimal Latitude { get; }

        /// <summary>
        /// Longitude
        /// </summary>
        public decimal Longitude { get; }

        public LocationEventMessage(string id, string title, string address, decimal latitude, decimal longitude) : base(EventMessageType.Location, id)
        {
            Title = title;
            Address = address;
            Latitude = latitude;
            Longitude = longitude;
        }
    }
}
