// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/ImageMessage.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class ImageMessage
// 主要成員：Type、QuickReply、OriginalContentUrl、PreviewImageUrl
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace Line.Messaging
{
    /// <summary>
    /// 表示 LINE 圖片訊息
    /// Represents a LINE image message
    /// </summary>
    /// <remarks>
    /// 圖片訊息用於傳送圖片內容給使用者。
    /// 需要提供原始圖片 URL 和預覽圖片 URL（通常是縮圖）。
    /// <para>
    /// Image message is used to send image content to users.
    /// Requires both original image URL and preview image URL (usually a thumbnail).
    /// </para>
    /// <para>
    /// 圖片規格限制：
    /// - 原始圖片：JPEG/PNG 格式，最大 1024×1024 像素，最大 10MB
    /// - 預覽圖片：JPEG 格式，最大 240×240 像素，最大 1MB
    /// - 兩個 URL 都必須使用 HTTPS 協定
    /// </para>
    /// <para>
    /// Image specifications:
    /// - Original image: JPEG/PNG format, max 1024×1024 pixels, max 10MB
    /// - Preview image: JPEG format, max 240×240 pixels, max 1MB
    /// - Both URLs must use HTTPS protocol
    /// </para>
    /// <para>
    /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#image-message
    /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#image-message
    /// </para>
    /// </remarks>
    /// <example>
    /// 使用範例：
    /// <code>
    /// // 建立圖片訊息
    /// var message = new ImageMessage(
    ///     originalContentUrl: "https://example.com/images/original.jpg",
    ///     previerImageUrl: "https://example.com/images/preview.jpg"
    /// );
    ///
    /// // 建立帶有快速回覆的圖片訊息
    /// var messageWithQuickReply = new ImageMessage(
    ///     originalContentUrl: "https://example.com/images/original.jpg",
    ///     previerImageUrl: "https://example.com/images/preview.jpg",
    ///     quickReply: new QuickReply { ... }
    /// );
    ///
    /// // 傳送訊息
    /// await client.ReplyMessageAsync(replyToken, message);
    /// </code>
    /// </example>
    /// <seealso cref="ISendMessage"/>
    /// <seealso cref="MessageType.Image"/>
    /// <seealso cref="VideoMessage"/>
    public class ImageMessage : ISendMessage
    {
        /// <summary>
        /// 取得訊息類型，固定為 Image
        /// Gets the message type, always Image
        /// </summary>
        public MessageType Type { get; } = MessageType.Image;

        /// <summary>
        /// 取得或設定快速回覆選單
        /// Gets or sets the quick reply menu
        /// </summary>
        /// <value>
        /// 快速回覆選單物件，可包含最多 13 個快速回覆按鈕
        /// Quick reply object that can contain up to 13 quick reply buttons
        /// </value>
        /// <remarks>
        /// 快速回覆功能支援 LINE iOS 8.11.0 及 Android 8.11.0 以上版本。
        /// Quick reply feature is supported on LINE iOS 8.11.0 and Android 8.11.0 or later.
        /// </remarks>
        /// <seealso cref="QuickReply"/>
        public QuickReply QuickReply { get; set; }

        /// <summary>
        /// 取得原始圖片的 URL
        /// Gets the URL of the original image
        /// </summary>
        /// <value>
        /// 原始圖片 URL，最多 1000 字元
        /// Original image URL, maximum 1000 characters
        /// </value>
        /// <remarks>
        /// 原始圖片規格要求：
        /// - 通訊協定：HTTPS
        /// - 圖片格式：JPEG 或 PNG
        /// - 最大尺寸：1024×1024 像素
        /// - 最大檔案大小：10MB
        /// - URL 最大長度：1000 字元
        /// <para>
        /// Original image specifications:
        /// - Protocol: HTTPS
        /// - Image format: JPEG or PNG
        /// - Maximum size: 1024×1024 pixels
        /// - Maximum file size: 10MB
        /// - Maximum URL length: 1000 characters
        /// </para>
        /// <para>
        /// 注意：圖片必須可公開存取，LINE 伺服器需要能夠下載該圖片。
        /// Note: Image must be publicly accessible, LINE servers need to be able to download it.
        /// </para>
        /// </remarks>
        public string OriginalContentUrl { get; }

        /// <summary>
        /// 取得預覽圖片的 URL（通常是縮圖）
        /// Gets the URL of the preview image (usually a thumbnail)
        /// </summary>
        /// <value>
        /// 預覽圖片 URL，最多 1000 字元
        /// Preview image URL, maximum 1000 characters
        /// </value>
        /// <remarks>
        /// 預覽圖片規格要求：
        /// - 通訊協定：HTTPS
        /// - 圖片格式：JPEG
        /// - 最大尺寸：240×240 像素
        /// - 最大檔案大小：1MB
        /// - URL 最大長度：1000 字元
        /// <para>
        /// Preview image specifications:
        /// - Protocol: HTTPS
        /// - Image format: JPEG
        /// - Maximum size: 240×240 pixels
        /// - Maximum file size: 1MB
        /// - Maximum URL length: 1000 characters
        /// </para>
        /// <para>
        /// 注意：預覽圖會顯示在聊天室中，使用者點擊後才會載入原始圖片。
        /// Note: Preview image is displayed in the chat, original image is loaded when user taps it.
        /// </para>
        /// </remarks>
        public string PreviewImageUrl { get; }

        /// <summary>
        /// 初始化 ImageMessage 的新執行個體
        /// Initializes a new instance of the ImageMessage class
        /// </summary>
        /// <param name="originalContentUrl">
        /// 原始圖片的 HTTPS URL（最多 1000 字元）
        /// 格式：JPEG 或 PNG
        /// 最大尺寸：1024×1024 像素
        /// 最大檔案：10MB
        /// <para>
        /// HTTPS URL of the original image (maximum 1000 characters)
        /// Format: JPEG or PNG
        /// Maximum size: 1024×1024 pixels
        /// Maximum file size: 10MB
        /// </para>
        /// </param>
        /// <param name="previerImageUrl">
        /// 預覽圖片的 HTTPS URL（最多 1000 字元）
        /// 格式：JPEG
        /// 最大尺寸：240×240 像素
        /// 最大檔案：1MB
        /// <para>
        /// HTTPS URL of the preview image (maximum 1000 characters)
        /// Format: JPEG
        /// Maximum size: 240×240 pixels
        /// Maximum file size: 1MB
        /// </para>
        /// </param>
        /// <param name="quickReply">
        /// 快速回覆選單（選用），預設為 null
        /// Quick reply menu (optional), default is null
        /// </param>
        /// <exception cref="System.ArgumentNullException">
        /// 當 originalContentUrl 或 previerImageUrl 為 null 時可能拋出
        /// May be thrown when originalContentUrl or previerImageUrl is null
        /// </exception>
        /// <example>
        /// 使用範例：
        /// <code>
        /// // 基本用法
        /// var message = new ImageMessage(
        ///     "https://example.com/images/photo.jpg",
        ///     "https://example.com/images/photo_thumb.jpg"
        /// );
        ///
        /// // 帶有快速回覆
        /// var quickReply = new QuickReply
        /// {
        ///     Items = new List&lt;QuickReplyItem&gt;
        ///     {
        ///         new QuickReplyItem(new MessageTemplateAction("喜歡", "👍")),
        ///         new QuickReplyItem(new MessageTemplateAction("不喜歡", "👎"))
        ///     }
        /// };
        /// var messageWithQuickReply = new ImageMessage(
        ///     "https://example.com/images/photo.jpg",
        ///     "https://example.com/images/photo_thumb.jpg",
        ///     quickReply
        /// );
        ///
        /// // 傳送訊息
        /// await client.PushMessageAsync(userId, message);
        /// </code>
        /// </example>
        public ImageMessage(string originalContentUrl, string previerImageUrl, QuickReply quickReply = null)
        {
            OriginalContentUrl = originalContentUrl;
            PreviewImageUrl = previerImageUrl;
            QuickReply = quickReply;
        }
    }
}
