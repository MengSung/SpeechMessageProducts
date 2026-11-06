/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述位置訊息的標題、地址與經緯度。
 *
 * 主要功能與流程：
 * 本檔宣告 LocationMessage。
 * 資料／選項欄位包含 Type、QuickReply、Title、Address、Latitude、Longitude，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/ISendMessage.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace Line.Messaging
{
    /// <summary>
    /// Location
    /// https://developers.line.me/en/docs/messaging-api/reference/#location
    /// </summary>
    public class LocationMessage : ISendMessage
    {
        public MessageType Type { get; } = MessageType.Location;

        /// <summary>
        /// These properties are used for the quick reply feature
        /// </summary>
        public QuickReply QuickReply { get; set; }

        /// <summary>
        /// Title
        /// Max: 100 characters
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Address
        /// Max: 100 characters
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

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="title">
        /// Title
        /// Max: 100 characters
        /// </param>
        /// <param name="address">
        /// Address
        /// Max: 100 characters
        /// </param>
        /// <param name="latitude">
        /// Latitude
        /// </param>
        /// <param name="longitude">
        /// Longitude
        /// </param>
        /// <param name="quickReply">
        /// QuickReply
        /// </param>
        public LocationMessage(string title, string address, decimal latitude, decimal longitude, QuickReply quickReply = null)
        {
            Title = title.Substring(0, Math.Min(title.Length, 100));
            Address = address.Substring(0, Math.Min(address.Length, 100));
            Latitude = latitude;
            Longitude = longitude;
            QuickReply = quickReply;
        }
    }
}
