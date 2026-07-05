// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/ImagemapMessage.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class ImagemapMessage
// 主要成員：Type、QuickReply、BaseUrl、AltText、BaseSize、Video、Actions
// 引用命名空間：System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Imagemaps are images with one or more links. You can assign one link for the entire image or multiple links which correspond to different regions of the image.
    /// https://developers.line.me/en/docs/messaging-api/reference/#imagemap-message
    /// </summary>
    public class ImagemapMessage : ISendMessage
    {
        public MessageType Type { get; } = MessageType.Imagemap;

        /// <summary>
        /// These properties are used for the quick reply feature
        /// </summary>
        public QuickReply QuickReply { get; set; }

        /// <summary>
        /// Base URL of image (Max: 1000 characters)
        /// HTTPS
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Alternative text
        /// Max: 400 characters
        /// </summary>
        public string AltText { get; }

        /// <summary>
        /// Width of base image (set to 1040px）
        /// Height of base image（set to the height that corresponds to a width of 1040px）
        /// </summary>
        public ImagemapSize BaseSize { get; }

        /// <summary>
        /// Video to play on imagemap
        /// </summary>
        public Video Video { get; }

        /// <summary>
        /// Action when tapped.
        /// Max: 50
        /// </summary>
        public IList<IImagemapAction> Actions { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="baseUrl">
        /// Base URL of image (Max: 1000 characters)
        /// HTTPS
        /// </param>
        /// <param name="altText">
        /// Alternative text
        /// Max: 400 characters
        /// </param>
        /// <param name="baseSize">
        /// Width of base image (set to 1040px）
        /// Height of base image（set to the height that corresponds to a width of 1040px）
        /// </param>
        /// <param name="actions">
        /// Action when tapped.
        /// Max: 50
        /// </param>
        /// <param name="quickReply">
        /// QuickReply
        /// </param>
        /// <param name="video">
        /// Video to play on imagemap
        /// </param>
        public ImagemapMessage(string baseUrl, string altText, ImagemapSize baseSize, IList<IImagemapAction> actions, QuickReply quickReply = null, Video video = null)
        {
            BaseUrl = baseUrl;
            AltText = altText.Substring(0, Math.Min(altText.Length, 400)); ;
            BaseSize = baseSize;
            Actions = actions;
            QuickReply = quickReply;
            Video = video;
        }
    }
}
