// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Template/CarouselTemplate.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class CarouselTemplate
// 主要成員：Type、Columns、ImageAspectRatio、ImageSize
// 引用命名空間：System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Template message with multiple columns which can be cycled like a carousel.
    /// https://developers.line.me/en/docs/messaging-api/reference/#carousel
    /// </summary>
    public class CarouselTemplate : ITemplate
    {
        public TemplateType Type { get; } = TemplateType.Carousel;

        /// <summary>
        /// Array of columns
        /// Max: 10
        /// </summary>
        public IList<CarouselColumn> Columns { get; }

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
        /// Constructor
        /// </summary>
        /// <param name="columns">
        /// Array of columns
        /// Max: 10
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
        public CarouselTemplate(IList<CarouselColumn> columns = null,
            ImageAspectRatioType imageAspectRatio = ImageAspectRatioType.Rectangle, ImageSizeType imageSize = ImageSizeType.Cover)
        {
            Columns = columns ?? new List<CarouselColumn>();
            ImageAspectRatio = imageAspectRatio;
            ImageSize = imageSize;
        }
    }
}
