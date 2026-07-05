// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/QuickReply/QuickItem.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class QuickReplyButtonObject
// 主要成員：ImageUrl、Action
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace Line.Messaging
{
    /// <summary>
    /// This is a quick reply option that is displayed as a button.
    /// https://developers.line.me/en/reference/messaging-api/#quick-reply-button-object
    /// </summary>
    public class QuickReplyButtonObject
    {
        public string Type = "action";

        /// <summary>
        /// URL of the icon that is displayed at the beginning of the button
        /// Max: 1000 characters
        /// URL scheme: https
        /// Image format: PNG
        /// Aspect ratio: 1:1
        /// Data size: Up to 1 MB
        /// There is no limit on the image size.
        /// If the action property has a camera action, camera roll action, or location action, and the imageUrl property is not set, the default icon is displayed.
        /// </summary>
        public string ImageUrl { get; set; }

        public ITemplateAction Action { get; set; }

        public QuickReplyButtonObject(ITemplateAction action, string imageUrl = null)
        {
            Action = action;
            ImageUrl = imageUrl;
        }
    }
}