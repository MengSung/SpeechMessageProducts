// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Imagemap/Video.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class Video
// 主要成員：OriginalContentUrl、PreviewImageUrl、Area、ExternalLink
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
