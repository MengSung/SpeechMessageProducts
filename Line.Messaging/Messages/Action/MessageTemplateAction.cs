/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述點擊後以使用者身分送出指定文字的動作。
 *
 * 主要功能與流程：
 * 本檔宣告 MessageTemplateAction。
 * - Label for the action Required for templates other than image carousel.Max: 20 characters Optional for image
 * carousel templates.Max: 12 characters. RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。 LINE iOS 8.2.0 以後支援 RichMenu
 * 上的此可及性 label。
 * 主要操作入口包含 CreateFrom。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkGoldenTests.cs、Line.Messaging/LineMessagingClient.cs、
 * Line.Messaging/Messages/ISendMessage.cs、Line.Messaging/Messages/ImageMessage.cs、
 * Line.Messaging/Messages/RichMenu/ActionArea.cs 追查目前工作樹中的合作／使用位置。
 */
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
