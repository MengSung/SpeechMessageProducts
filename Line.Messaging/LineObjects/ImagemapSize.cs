/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述imagemap與RichMenu使用的影像寬高及既有標準尺寸。
 *
 * 主要功能與流程：
 * 本檔宣告 ImagemapSize。
 * - LINE RichMenu 長版預設尺寸，對應 2500x1686。 此尺寸必須與上傳圖片和 ActionArea 座標系一致。
 * - LINE RichMenu 短版尺寸，對應 2500x843。 適合較精簡的選單；仍需使用同一套 RichMenu 座標與圖片尺寸規則。
 * 資料／選項欄位包含 ImagemapSize、RichMenuLong、RichMenuShort、Width、Height，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkGoldenTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/Support/RichMenuTestFactory.cs、
 * Line.Messaging/Messages/ImagemapMessage.cs、Line.Messaging/Messages/RichMenu/ResponseRichMenu.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    /// <summary>
    /// Image size.
    /// </summary>
    public class ImagemapSize
    {
        /// <summary>
        /// LINE RichMenu 長版預設尺寸，對應 2500x1686。
        /// 此尺寸必須與上傳圖片和 ActionArea 座標系一致。
        /// </summary>
        public static ImagemapSize RichMenuLong { get; } = new ImagemapSize(2500, 1686);

        /// <summary>
        /// LINE RichMenu 短版尺寸，對應 2500x843。
        /// 適合較精簡的選單；仍需使用同一套 RichMenu 座標與圖片尺寸規則。
        /// </summary>
        public static ImagemapSize RichMenuShort { get; } = new ImagemapSize(2500, 843);

        /// <summary>
        /// Width
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Height
        /// </summary>
        public int Height { get; }

        public ImagemapSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }
}
