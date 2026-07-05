// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Template/ButtonsTemplate.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class ButtonsTemplate
// 主要成員：Type、ThumbnailImageUrl、ImageAspectRatio、ImageSize、ImageBackgroundColor、Title、Text、Actions、DefaultAction
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
    /// Template message with an image, title, text, and multiple action buttons.
    /// https://developers.line.me/en/docs/messaging-api/reference/#buttons
    /// </summary>
    public class ButtonsTemplate : ITemplate
    {
        public TemplateType Type { get; } = TemplateType.Buttons;

        /// <summary>
        /// Image URL (Max: 1000 characters)
        /// HTTPS
        /// JPEG or PNG
        /// Aspect ratio: 1:1.51
        /// Max width: 1024px
        /// Max: 1 MB
        /// </summary>
        public string ThumbnailImageUrl { get; }

        /// <summary>
        /// Aspect ratio of the image. Specify one of the following values:
        /// rectangle: 1.51:1
        /// square: 1:1
        /// The default value is rectangle.
        /// </summary>
        public ImageAspectRatioType ImageAspectRatio { get; }

        /// <summary>
        /// Size of the image. Specify one of the following values:
        /// cover: The image fills the entire image area.Parts of the image that do not fit in the area are not displayed.
        /// contain: The entire image is displayed in the image area.A background is displayed in the unused areas to the left and right of vertical images and in the areas above and below horizontal images.
        /// The default value is cover.
        /// </summary>
        public ImageSizeType ImageSize { get; }

        /// <summary>
        /// Background color of image. Specify a RGB color value. The default value is #FFFFFF (white).
        /// </summary>
        public string ImageBackgroundColor { get; }

        /// <summary>
        /// Title
        /// Max: 40 characters
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Message text
        /// Max: 160 characters(no image or title)
        /// Max: 60 characters(message with an image or title)
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Action when tapped
        /// Max: 4
        /// </summary>
        public IList<ITemplateAction> Actions { get; }

        /// <summary>
        /// Action when image is tapped; set for the entire image, title, and text area
        /// </summary>
        public ITemplateAction DefaultAction { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="text">
        /// Message text
        /// Max: 160 characters(no image or title)
        /// Max: 60 characters(message with an image or title)
        /// </param>
        /// <param name="thumbnailImageUrl">
        /// Image URL (Max: 1000 characters)
        /// HTTPS
        /// JPEG or PNG
        /// Aspect ratio: 1:1.51
        /// Max width: 1024px
        /// Max: 1 MB
        /// </param>
        /// <param name="title">
        /// Title
        /// Max: 40 characters
        /// </param>
        /// <param name="actions">
        /// Action when tapped
        /// Max: 4
        /// </param>
        /// <param name="imageAspectRatio">
        /// Aspect ratio of the image. Specify one of the following values:
        /// rectangle: 1.51:1
        /// square: 1:1
        /// The default value is rectangle.
        /// </param>
        /// <param name="imageSize">
        /// Size of the image. Specify one of the following values:
        /// cover: The image fills the entire image area.Parts of the image that do not fit in the area are not displayed.
        /// contain: The entire image is displayed in the image area.A background is displayed in the unused areas to the left and right of vertical images and in the areas above and below horizontal images.
        /// The default value is cover.
        /// </param>
        /// <param name="imageBackgroundColor">
        /// Background color of image. Specify a RGB color value. The default value is #FFFFFF (white).
        /// </param>
        /// <param name="defaultAction">
        /// Action when image is tapped; set for the entire image, title, and text area
        /// </param>
        public ButtonsTemplate(string text, string thumbnailImageUrl = null, string title = null, IList<ITemplateAction> actions = null,
             ImageAspectRatioType imageAspectRatio = ImageAspectRatioType.Rectangle, ImageSizeType imageSize = ImageSizeType.Cover, string imageBackgroundColor = "#FFFFFF",
             ITemplateAction defaultAction = null)
        {
            ThumbnailImageUrl = thumbnailImageUrl;
            Title = title?.Substring(0, Math.Min(title.Length, 40));
            Text = (string.IsNullOrEmpty(thumbnailImageUrl) && string.IsNullOrEmpty(title))
                ? text.Substring(0, Math.Min(text.Length, 160)) : text.Substring(0, Math.Min(text.Length, 60));
            Actions = actions ?? new List<ITemplateAction>();
            ImageAspectRatio = imageAspectRatio;
            ImageSize = imageSize;
            ImageBackgroundColor = imageBackgroundColor;
            DefaultAction = defaultAction;
        }
    }
}
