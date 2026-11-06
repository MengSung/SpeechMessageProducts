/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述imagemap內嵌影片的網址、預覽圖、播放區域及外部連結。
 *
 * 主要功能與流程：
 * 本檔宣告 Video。
 * 資料／選項欄位包含 Video、OriginalContentUrl、PreviewImageUrl、Area、ExternalLink，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/ISendMessage.cs、Line.Messaging/Messages/ImagemapMessage.cs、
 * Line.Messaging/Messages/MessageType.cs、Line.Messaging/Messages/VideoMessage.cs、
 * Line.Messaging/Webhooks/EventMessage.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    public class Video
    {
        /// <summary>
        /// URL of the video file (Max: 1000 characters)
        /// HTTPS, mp4
        /// / Max: 1 minute
        /// / Max: 10 MB
        /// / Note: A very wide or tall video may be cropped when played in some environments.
        /// </summary>
        public string OriginalContentUrl { get; }

        /// <summary>
        /// URL of the preview image (Max: 1000 characters)
        /// HTTP, JPEG
        /// / Max: 240 x 240 pixels
        /// / Max: 1 MB
        /// </summary>
        public string PreviewImageUrl { get; }

        /// <summary>
        /// Imagemap Area
        /// </summary>
        public ImagemapArea Area { get; }

        /// <summary>
        /// Label. Displayed after the video is finished.
        /// And Webpage URL. Called when the label displayed after the video is tapped.
        /// </summary>
        public ExternalLink ExternalLink { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="originalContentUrl">
        /// URL of the video file (Max: 1000 characters)
        /// HTTPS, mp4
        /// / Max: 1 minute
        /// / Max: 10 MB
        /// / Note: A very wide or tall video may be cropped when played in some environments.
        /// </param>
        /// <param name="previewImageUrl">
        /// URL of the preview image (Max: 1000 characters)
        /// HTTP, JPEG
        /// / Max: 240 x 240 pixels
        /// / Max: 1 MB
        /// </param>
        /// <param name="area">
        /// Imagemap Area
        /// </param>
        /// <param name="externalLink">
        /// Label. Displayed after the video is finished.
        /// And Webpage URL. Called when the label displayed after the video is tapped.
        /// </param>
        public Video(string originalContentUrl, string previewImageUrl, ImagemapArea area, ExternalLink externalLink)
        {
            OriginalContentUrl = originalContentUrl;
            PreviewImageUrl = previewImageUrl;
            Area = area;
            ExternalLink = externalLink;
        }
    }
}
