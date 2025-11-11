using System;

namespace Line.Messaging
{
    /// <summary>
    /// 表示 LINE 文字訊息
    /// Represents a LINE text message
    /// </summary>
    /// <remarks>
    /// 文字訊息是 LINE Messaging API 中最基本且最常用的訊息類型。
    /// 可以傳送純文字內容給使用者，並支援換行、emoji 和 LINE 表情符號。
    /// <para>
    /// Text message is the most basic and commonly used message type in LINE Messaging API.
    /// Can send plain text content to users and supports line breaks, emoji, and LINE emoticons.
    /// </para>
    /// <para>
    /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#text-message
    /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#text-message
    /// </para>
    /// </remarks>
    /// <example>
    /// 基本使用範例：
    /// <code>
    /// // 建立簡單的文字訊息
    /// var message = new TextMessage("您好，歡迎使用 LINE Bot！");
    /// 
    /// // 建立帶有快速回覆的文字訊息
    /// var messageWithQuickReply = new TextMessage("請選擇：", new QuickReply
    /// {
    ///     Items = new List&lt;QuickReplyItem&gt;
    ///     {
    ///         new QuickReplyItem(new MessageTemplateAction("選項 A", "A")),
    ///         new QuickReplyItem(new MessageTemplateAction("選項 B", "B"))
    ///     }
    /// });
    /// 
    /// // 傳送訊息
    /// await client.ReplyMessageAsync(replyToken, message);
    /// </code>
    /// </example>
    /// <seealso cref="ISendMessage"/>
    /// <seealso cref="MessageType.Text"/>
    /// <seealso cref="QuickReply"/>
    public class TextMessage : ISendMessage
    {
        /// <summary>
        /// 取得訊息類型，固定為 Text
        /// Gets the message type, always Text
        /// </summary>
        public MessageType Type { get; } = MessageType.Text;

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
        /// 取得或設定訊息文字內容
        /// Gets or sets the message text content
        /// </summary>
        /// <value>
        /// 訊息文字，最多 2000 字元（超過會自動截斷）
        /// Message text, maximum 2000 characters (automatically truncated if exceeded)
        /// </value>
        /// <remarks>
        /// 文字內容支援：
        /// - 換行符號（\n）
        /// - Unicode emoji（如 😀）
        /// - LINE 表情符號（使用 $ 符號，如 $(product_id)）
        /// <para>
        /// Text content supports:
        /// - Line breaks (\n)
        /// - Unicode emoji (e.g., 😀)
        /// - LINE emoticons (using $ symbol, e.g., $(product_id))
        /// </para>
        /// </remarks>
        public string Text { get; set; }

        /// <summary>
        /// 初始化 TextMessage 的新執行個體
        /// Initializes a new instance of the TextMessage class
        /// </summary>
        /// <param name="text">
        /// 訊息文字內容，最多 2000 字元。如果超過長度限制，會自動截斷至 2000 字元。
        /// Message text content, maximum 2000 characters. Automatically truncated to 2000 characters if exceeded.
        /// </param>
        /// <param name="quickReply">
        /// 快速回覆選單（選用），預設為 null
        /// Quick reply menu (optional), default is null
        /// </param>
        /// <remarks>
        /// 建構函式會自動處理文字長度限制：
        /// - 如果文字少於 2000 字元，保持原樣
        /// - 如果文字超過 2000 字元，自動截斷至 2000 字元
        /// <para>
        /// Constructor automatically handles text length limit:
        /// - If text is less than 2000 characters, keeps as is
        /// - If text exceeds 2000 characters, automatically truncates to 2000 characters
        /// </para>
        /// </remarks>
        /// <example>
        /// 使用範例：
        /// <code>
        /// // 基本用法
        /// var message1 = new TextMessage("Hello, LINE!");
        /// 
        /// // 帶有換行的文字
        /// var message2 = new TextMessage("第一行\n第二行\n第三行");
        /// 
        /// // 帶有 emoji
        /// var message3 = new TextMessage("感謝您的訂購！😊");
        /// 
        /// // 帶有快速回覆
        /// var quickReply = new QuickReply { Items = new List&lt;QuickReplyItem&gt; { ... } };
        /// var message4 = new TextMessage("請選擇操作：", quickReply);
        /// 
        /// // 超過 2000 字元會自動截斷
        /// var longText = new string('A', 3000); // 3000 個 'A'
        /// var message5 = new TextMessage(longText); // 實際只會包含 2000 個 'A'
        /// </code>
        /// </example>
        public TextMessage(string text, QuickReply quickReply = null)
        {
            Text = text.Substring(0, Math.Min(text.Length, 2000));
            QuickReply = quickReply;
        }
    }
}
