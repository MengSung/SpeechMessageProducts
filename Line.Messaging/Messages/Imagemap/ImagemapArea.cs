/*
 * 檔案整體說明
 *
 * 目的與定位：本檔定義imagemap可點擊區域相對左上角的座標及寬高。
 *
 * 主要功能與流程：
 * 本檔宣告 ImagemapArea。
 * 資料／選項欄位包含 ImagemapArea、X、Y、Width、Height，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkGoldenTests.cs、Line.Messaging/Messages/Imagemap/IImagemapAction.cs、
 * Line.Messaging/Messages/Imagemap/MessageImagemapAction.cs、Line.Messaging/Messages/Imagemap/UriImagemapAction.cs、
 * Line.Messaging/Messages/Imagemap/Video.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    /// <summary>
    /// Defines the size of a tappable area. The top left is used as the origin of the area.
    /// https://developers.line.me/en/docs/messaging-api/reference/#imagemap-area-object
    /// </summary>
    public class ImagemapArea
    {
        /// <summary>
        /// Horizontal position relative to the top-left corner of the area.
        /// </summary>
        public int X { get; }
        /// <summary>
        /// Vertical position relative to the top-left corner of the area.
        /// </summary>
        public int Y { get; }
        /// <summary>
        /// Width of the tappable area
        /// </summary>
        public int Width { get; }
        /// <summary>
        /// Height of the tappable area
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="x">
        /// Horizontal position relative to the top-left corner of the area.
        /// </param>
        /// <param name="y">
        /// Vertical position relative to the top-left corner of the area.
        /// </param>
        /// <param name="width">
        /// Width of the tappable area
        /// </param>
        /// <param name="height">
        /// Height of the tappable area
        /// </param>
        public ImagemapArea(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
