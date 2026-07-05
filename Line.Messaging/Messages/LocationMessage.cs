// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/LocationMessage.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LocationMessage
// 主要成員：Type、QuickReply、Title、Address、Latitude、Longitude
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
