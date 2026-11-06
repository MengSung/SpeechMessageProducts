/*
 * 檔案整體說明
 *
 * 目的與定位：本檔組合底圖、基準尺寸與多個點擊區域成為imagemap訊息。
 *
 * 主要功能與流程：
 * 本檔宣告 ImagemapMessage。
 * 資料／選項欄位包含 Type、QuickReply、BaseUrl、AltText、BaseSize、Video、Actions，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/ISendMessage.cs 追查目前工作樹中的合作／使用位置。
 */
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
