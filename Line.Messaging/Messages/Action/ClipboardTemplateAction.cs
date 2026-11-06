/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述點擊後複製指定文字到剪貼簿的動作。
 *
 * 主要功能與流程：
 * 本檔宣告 ClipboardTemplateAction。
 * 主要操作入口包含 CreateFrom。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/RichMenu/ActionArea.cs 追查目前工作樹中的合作／使用位置。
 */
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
