/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述媒體內容來源與原始／預覽網址，供事件處理選擇下載途徑。
 *
 * 主要功能與流程：
 * 本檔宣告 ContentProvider。
 * 資料／選項欄位包含 ContentProvider、Type、OriginalContentUrl、PreviewImageUrl，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/EventMessage.cs、Line.Messaging/Webhooks/MidiaEventMessage.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Content provider of media message
    /// </summary>
    public class ContentProvider
    {
        /// <summary>
        /// Content Provider Type
        /// </summary>
        public ContentProviderType Type { get; }

        /// <summary>
        /// URL of the media file. Only included when contentProvider.type is external.
        /// </summary>
        public string OriginalContentUrl { get; }


        /// <summary>
        /// URL of the preview image. Only included when contentProvider.type is external.
        /// </summary>
        public string PreviewImageUrl { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="type">
        /// Provider of the media file.
        /// / line: LINE.The binary media data can be retrieved from the content endpoint.
        /// / external: Provider other than LINE
        /// </param>
        /// <param name="originalContentUrl">
        /// URL of the media file. Only included when contentProvider.type is external.
        /// </param>
        /// <param name="previewImageUrl">
        /// URL of the preview image. Only included when contentProvider.type is external.
        /// </param>
        public ContentProvider(ContentProviderType type, string originalContentUrl, string previewImageUrl)
        {
            Type = type;
            OriginalContentUrl = originalContentUrl;
            PreviewImageUrl = previewImageUrl;
        }
    }
}
