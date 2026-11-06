/*
 * 檔案整體說明
 *
 * 目的與定位：表示可傳送至 LINE 平台的訊息介面 Represents a message that can be sent to LINE platform
 *
 * 主要功能與流程：
 * 本檔宣告 ISendMessage。
 * - 取得訊息類型 Gets the message type
 * - 取得或設定快速回覆選單（Quick Reply） Gets or sets the quick reply menu
 * 資料／選項欄位包含 ISendMessage，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs、Line.Messaging.Tests/LineSdkGoldenTests.cs、
 * Line.Messaging/ILineMessagingClient.cs、Line.Messaging/LineMessagingClient.cs、
 * Line.Messaging/Messages/AudioMessage.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    /// <summary>
    /// 表示可傳送至 LINE 平台的訊息介面
    /// Represents a message that can be sent to LINE platform
    /// </summary>
    /// <remarks>
    /// 此介面為所有 LINE 訊息類型的基礎介面，包括文字、圖片、影片、音訊、貼圖、位置、模板和 Flex 訊息等。
    /// 所有實作此介面的類別都可以透過 LINE Messaging API 傳送給使用者。
    /// <para>
    /// This interface is the base for all LINE message types including text, image, video, audio, sticker, location, template, and flex messages.
    /// All classes implementing this interface can be sent to users via LINE Messaging API.
    /// </para>
    /// </remarks>
    /// <seealso cref="TextMessage"/>
    /// <seealso cref="ImageMessage"/>
    /// <seealso cref="VideoMessage"/>
    /// <seealso cref="AudioMessage"/>
    /// <seealso cref="StickerMessage"/>
    /// <seealso cref="LocationMessage"/>
    /// <seealso cref="TemplateMessage"/>
    /// <seealso cref="FlexMessage"/>
    /// <seealso cref="ImagemapMessage"/>
    public interface ISendMessage
    {
        /// <summary>
        /// 取得訊息類型
        /// Gets the message type
        /// </summary>
        /// <value>
        /// 訊息類型列舉值，如 Text、Image、Video 等
        /// Message type enum value such as Text, Image, Video, etc.
        /// </value>
        /// <seealso cref="MessageType"/>
        MessageType Type { get; }

        /// <summary>
        /// 取得或設定快速回覆選單（Quick Reply）
        /// Gets or sets the quick reply menu
        /// </summary>
        /// <value>
        /// 快速回覆選單物件，包含多個快速回覆按鈕
        /// Quick reply object containing multiple quick reply buttons
        /// </value>
        /// <remarks>
        /// 快速回覆功能支援 LINE iOS 8.11.0 及 Android 8.11.0 以上版本。
        /// 當使用者收到訊息時，可以在輸入框上方看到快速回覆按鈕，點擊後即可快速傳送預設的訊息或動作。
        /// 最多可以設定 13 個快速回覆按鈕。
        /// <para>
        /// Quick reply feature is supported on LINE 8.11.0 and later for iOS and Android.
        /// When users receive a message, they can see quick reply buttons above the input box.
        /// Tapping a button quickly sends a preset message or action.
        /// Up to 13 quick reply buttons can be set.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/docs/messaging-api/using-quick-reply/
        /// Official documentation: https://developers.line.biz/en/docs/messaging-api/using-quick-reply/
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var message = new TextMessage("請選擇您的答案")
        /// {
        ///     QuickReply = new QuickReply
        ///     {
        ///         Items = new List&lt;QuickReplyItem&gt;
        ///         {
        ///             new QuickReplyItem(new MessageTemplateAction("選項 A", "A")),
        ///             new QuickReplyItem(new MessageTemplateAction("選項 B", "B"))
        ///         }
        ///     }
        /// };
        /// </code>
        /// </example>
        /// <seealso cref="QuickReply"/>
        QuickReply QuickReply { get; set; }
    }
}
