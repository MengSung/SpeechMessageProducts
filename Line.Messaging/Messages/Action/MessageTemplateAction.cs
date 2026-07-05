using System;

namespace Line.Messaging
{
    /// <summary>
    /// When a control associated with this action is tapped, the string in the text field is sent as a message from the user.
    /// https://developers.line.me/en/docs/messaging-api/reference/#datetime-picker-action
    /// </summary>
    public class MessageTemplateAction : ITemplateAction
    {
        public TemplateActionType Type { get; } = TemplateActionType.Message;

        /// <summary>
        /// Label for the action
        /// Required for templates other than image carousel.Max: 20 characters
        /// Optional for image carousel templates.Max: 12 characters.
        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Text sent when the action is performed
        /// Max: 300 characters
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="label">
        /// Label for the action
        /// Required for templates other than image carousel.Max: 20 characters
        /// Optional for image carousel templates.Max: 12 characters.
        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
        /// </param>
        /// <param name="text">
        /// Text sent when the action is performed
        /// Max: 300 characters
        /// </param>
        public MessageTemplateAction(string label, string text)
        {
            Label = label?.Substring(0, Math.Min(label.Length, 20));
            Text = text.Substring(0, Math.Min(text.Length, 300));
        }

        internal static MessageTemplateAction CreateFrom(dynamic dynamicObject)
        {
            return new MessageTemplateAction((string)dynamicObject?.label, (string)dynamicObject?.text);
        }
    }
}
