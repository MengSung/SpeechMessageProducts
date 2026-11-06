/*
 * 檔案整體說明
 *
 * 目的與定位：本檔組合多個圖片卡片成為圖片輪播模板。
 *
 * 主要功能與流程：
 * 本檔宣告 ImageCarouselTemplate。
 * 資料／選項欄位包含 Type、Columns，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 */
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Template with multiple images which can be cycled like a carousel.
    /// https://developers.line.me/en/docs/messaging-api/reference/#image-carousel
    /// </summary>
    public class ImageCarouselTemplate : ITemplate
    {
        public TemplateType Type { get; } = TemplateType.Image_carousel;

        /// <summary>
        /// Array of columns
        /// Max: 10
        /// </summary>
        public IList<ImageCarouselColumn> Columns { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="columns">
        /// Array of columns
        /// Max: 10
        /// </param>
        public ImageCarouselTemplate(IList<ImageCarouselColumn> columns = null)
        {
            Columns = columns ?? new List<ImageCarouselColumn>();
        }
    }
}
