// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Action/ClipboardTemplateAction.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class ClipboardTemplateAction
// 主要成員：CreateFrom、Type、Label、ClipboardText
// 引用命名空間：Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// Clipboard action
    /// When a control associated with this action is tapped, the string in the clipboardText property is copied to the user's clipboard.
    /// https://developers.line.biz/en/reference/messaging-api/#clipboard-action
    /// </summary>
    public class ClipboardTemplateAction : ITemplateAction
    {
        public TemplateActionType Type { get; } = TemplateActionType.Clipboard;

        /// <summary>
        /// Action label.
        /// Max: 20 characters
        /// </summary>
        [JsonProperty("label")]
        public string Label { get; set; }

        /// <summary>
        /// Text to be copied to the clipboard when the action is performed.
        /// Max: 1000 characters
        /// </summary>
        [JsonProperty("clipboardText")]
        public string ClipboardText { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="clipboardText">Text to copy to clipboard</param>
        /// <param name="label">Action label (optional)</param>
        public ClipboardTemplateAction(string clipboardText, string label = null)
        {
            ClipboardText = clipboardText;
            Label = label;
        }

        internal static ClipboardTemplateAction CreateFrom(dynamic dynamicObject)
        {
            if (dynamicObject == null) return null;
            return new ClipboardTemplateAction(
                (string)dynamicObject?.clipboardText,
                (string)dynamicObject?.label
            );
        }
    }
}
