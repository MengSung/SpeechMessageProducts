/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述圖片輪播中的單一圖片與動作。
 *
 * 主要功能與流程：
 * 本檔宣告 ImageCarouselColumn。
 * 資料／選項欄位包含 ImageCarouselColumn、ImageUrl、Action，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/Template/ImageCarouselTemplate.cs、
 * LineMessagingProcessor.Workflows/LineCarouselColumnFactory.cs、
 * LineMessagingProcessor.Workflows/LineNotificationContent.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    /// <summary>
    /// Column object for image carousel
    /// </summary>
    public class ImageCarouselColumn
    {
        /// <summary>
        /// Image URL (Max: 1000 characters)
        /// HTTPS
        /// JPEG or PNG
        /// Aspect ratio: 1:1
        /// Max width: 1024px
        /// Max: 1 MB
        /// </summary>
        public string ImageUrl { get; }

        /// <summary>
        /// Action when image is tapped
        /// </summary>
        public ITemplateAction Action { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="imageUrl">
        /// Image URL (Max: 1000 characters)
        /// HTTPS
        /// JPEG or PNG
        /// Aspect ratio: 1:1
        /// Max width: 1024px
        /// Max: 1 MB
        /// </param>
        /// <param name="action">
        /// Action when image is tapped
        /// </param>
        public ImageCarouselColumn(string imageUrl, ITemplateAction action)
        {
            ImageUrl = imageUrl;
            Action = action;
        }
    }
}
