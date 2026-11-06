/*
 * 檔案整體說明
 *
 * 目的與定位：本檔列出模板影像的填滿或完整顯示模式。
 *
 * 主要功能與流程：
 * 本檔宣告 ImageSizeType。
 * 資料／選項欄位包含 ImageSizeType，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/Template/ButtonsTemplate.cs、Line.Messaging/Messages/Template/CarouselTemplate.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    public enum ImageSizeType
    {
        /// <summary>
        /// The image fills the entire image area. Parts of the image that do not fit in the area are not displayed.
        /// </summary>
        Cover,

        /// <summary>
        /// The entire image is displayed in the image area.
        /// A background is displayed in the unused areas to the left and right of vertical images and in the areas above and below horizontal images.
        /// </summary>
        Contain
    }
}
