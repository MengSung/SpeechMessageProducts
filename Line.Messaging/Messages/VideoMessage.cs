namespace Line.Messaging
{
    /// <summary>
    /// 表示 LINE 影片訊息
    /// Represents a LINE video message
    /// </summary>
    /// <remarks>
    /// 影片訊息用於傳送影片內容給使用者。
    /// 需要提供影片檔案 URL 和預覽圖片 URL（影片縮圖）。
    /// <para>
    /// Video message is used to send video content to users.
    /// Requires both video file URL and preview image URL (video thumbnail).
    /// </para>
    /// <para>
    /// 影片規格限制：
    /// - 影片檔案：MP4 格式，最長 1 分鐘，最大 200MB
    /// - 預覽圖片：JPEG 格式，最大 240×240 像素，最大 1MB
    /// - 兩個 URL 都必須使用 HTTPS 協定
    /// </para>
    /// <para>
    /// Video specifications:
    /// - Video file: MP4 format, maximum 1 minute, max 200MB
    /// - Preview image: JPEG format, max 240×240 pixels, max 1MB
    /// - Both URLs must use HTTPS protocol
    /// </para>
    /// <para>
    /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#video-message
    /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#video-message
    /// </para>
    /// </remarks>
    /// <example>
    /// 使用範例：
    /// <code>
    /// // 建立影片訊息
    /// var message = new VideoMessage(
    ///     originalContentUrl: "https://example.com/videos/sample.mp4",
    ///     previerImageUrl: "https://example.com/images/video_preview.jpg"
    /// );
    /// 
    /// // 建立帶有快速回覆的影片訊息
    /// var messageWithQuickReply = new VideoMessage(
    ///     originalContentUrl: "https://example.com/videos/sample.mp4",
    ///     previerImageUrl: "https://example.com/images/video_preview.jpg",
    ///     quickReply: new QuickReply { ... }
    /// );
    /// 
    /// // 傳送訊息
    /// await client.ReplyMessageAsync(replyToken, message);
    /// </code>
    /// </example>
    /// <seealso cref="ISendMessage"/>
    /// <seealso cref="MessageType.Video"/>
    /// <seealso cref="ImageMessage"/>
    /// <seealso cref="AudioMessage"/>
    public class VideoMessage : ISendMessage
    {
        /// <summary>
        /// 取得訊息類型，固定為 Video
        /// Gets the message type, always Video
        /// </summary>
        public MessageType Type { get; } = MessageType.Video;

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
        /// 取得影片檔案的 URL
        /// Gets the URL of the video file
        /// </summary>
        /// <value>
        /// 影片檔案 URL，最多 1000 字元
        /// Video file URL, maximum 1000 characters
        /// </value>
        /// <remarks>
        /// 影片檔案規格要求：
        /// - 通訊協定：HTTPS
        /// - 影片格式：MP4
        /// - 最大長度：1 分鐘
        /// - 最大檔案大小：200MB
        /// - URL 最大長度：1000 字元
        /// <para>
        /// Video file specifications:
        /// - Protocol: HTTPS
        /// - Video format: MP4
        /// - Maximum duration: 1 minute
        /// - Maximum file size: 200MB
        /// - Maximum URL length: 1000 characters
        /// </para>
        /// <para>
        /// 注意：
        /// - 影片必須可公開存取，LINE 伺服器需要能夠下載該影片
        /// - 建議使用 H.264 編碼以確保最佳相容性
        /// - 超過 1 分鐘的影片將無法播放
        /// </para>
        /// <para>
        /// Note:
        /// - Video must be publicly accessible, LINE servers need to be able to download it
        /// - H.264 codec is recommended for best compatibility
        /// - Videos longer than 1 minute cannot be played
        /// </para>
        /// </remarks>
        public string OriginalContentUrl { get; }

        /// <summary>
        /// 取得或設定預覽圖片的 URL（影片縮圖）
        /// Gets or sets the URL of the preview image (video thumbnail)
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
        /// 注意：
        /// - 預覽圖會顯示在聊天室中，使用者點擊後才會播放影片
        /// - 建議使用影片的首幀或中間幀作為預覽圖
        /// </para>
        /// <para>
        /// Note:
        /// - Preview image is displayed in the chat, video plays when user taps it
        /// - Recommended to use the first frame or a middle frame of the video as preview
        /// </para>
        /// </remarks>
        public string PreviewImageUrl { get; set; }

        /// <summary>
        /// 初始化 VideoMessage 的新執行個體
        /// Initializes a new instance of the VideoMessage class
        /// </summary>
        /// <param name="originalContentUrl">
        /// 影片檔案的 HTTPS URL（最多 1000 字元）
        /// 格式：MP4
        /// 最大長度：1 分鐘
        /// 最大檔案：200MB
        /// <para>
        /// HTTPS URL of the video file (maximum 1000 characters)
        /// Format: MP4
        /// Maximum duration: 1 minute
        /// Maximum file size: 200MB
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
        /// var message = new VideoMessage(
        ///     "https://example.com/videos/tutorial.mp4",
        ///     "https://example.com/images/tutorial_thumb.jpg"
        /// );
        /// 
        /// // 帶有快速回覆
        /// var quickReply = new QuickReply
        /// {
        ///     Items = new List&lt;QuickReplyItem&gt;
        ///     {
        ///         new QuickReplyItem(new MessageTemplateAction("再看一次", "replay")),
        ///         new QuickReplyItem(new MessageTemplateAction("下一個", "next"))
        ///     }
        /// };
        /// var messageWithQuickReply = new VideoMessage(
        ///     "https://example.com/videos/tutorial.mp4",
        ///     "https://example.com/images/tutorial_thumb.jpg",
        ///     quickReply
        /// );
        /// 
        /// // 傳送訊息
        /// await client.PushMessageAsync(userId, message);
        /// </code>
        /// </example>
        public VideoMessage(string originalContentUrl, string previerImageUrl, QuickReply quickReply = null)
        {
            OriginalContentUrl = originalContentUrl;
            PreviewImageUrl = previerImageUrl;
            QuickReply = quickReply;
        }
    }
}
