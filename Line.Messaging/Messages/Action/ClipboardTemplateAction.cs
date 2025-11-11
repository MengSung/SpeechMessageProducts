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
