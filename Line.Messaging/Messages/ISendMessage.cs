// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/ISendMessage.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：interface ISendMessage
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
