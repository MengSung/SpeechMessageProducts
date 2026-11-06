/*
 * 檔案整體說明
 *
 * 目的與定位：本檔將多張卡片組成可橫向切換的模板。
 *
 * 主要功能與流程：
 * 本檔宣告 CarouselTemplate。
 * 資料／選項欄位包含 Type、Columns、ImageAspectRatio、ImageSize，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 */
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
