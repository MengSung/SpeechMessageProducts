// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Action/PostbackTemplateAction.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class PostbackTemplateAction
// 主要成員：CreateFrom、Type、Label、Data、Text、DisplayText
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace Line.Messaging
{
    /// <summary>
    /// When a control associated with this action is tapped, a postback event is returned via webhook with the specified string in the data field.
    /// If you have included the text field, the string in the text field is sent as a message from the user.
    /// https://developers.line.me/en/docs/messaging-api/reference/#postback-action
    /// </summary>
    public class PostbackTemplateAction : ITemplateAction
    {
        public TemplateActionType Type { get; } = TemplateActionType.Postback;

        /// <summary>
        /// Label for the action
        /// Required for templates other than image carousel.Max: 20 characters
        /// Optional for image carousel templates.Max: 12 characters.
        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// String returned via webhook in the postback.data property of the postback event
        /// Max: 300 characters
        /// </summary>
        public string Data { get; }

        /// <summary>
        /// Deprecated. Text displayed in the chat as a message sent by the user when the action is performed. Returned from the server through a webhook.
        /// Max: 300 characters
        /// The displayText and text fields cannot both be used at the same time.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Text displayed in the chat as a message sent by the user when the action is performed.
        /// Max: 300 characters
        /// The displayText and text fields cannot both be used at the same time.
        /// </summary>
        public string DisplayText { get; }

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
        /// <param name="data">
        /// String returned via webhook in the postback.data property of the postback event
        /// Max: 300 characters
        /// </param>
        /// <param name="text">
        /// Text displayed in the chat as a message sent by the user when the action is performed.
        /// And only when <paramref name="useDisplayText"/> is false, returned from the server through a webhook.
        /// <para>Max: 300 characters</para>
        /// </param>
        /// <param name="useDisplayText">
        /// If set to true, <paramref name="text"/> parameter is set to DisplayText property.
        /// (Deprecated) If set to false, <paramref name="text"/> parameter is set to Text property. However text property is deprecated.
        /// </param>
        public PostbackTemplateAction(string label, string data, string text = null, bool useDisplayText = true)
        {
            Data = data.Substring(0, Math.Min(data.Length, 300));
            Label = label?.Substring(0, Math.Min(label.Length, 20));

            if (useDisplayText)
            {
                DisplayText = text?.Substring(0, Math.Min(text.Length, 300));
            }
            else
            {
                Text = text?.Substring(0, Math.Min(text.Length, 300));
            }

        }

        internal static PostbackTemplateAction CreateFrom(dynamic dynamicObject)
        {
            bool useDisplayText = true;
            string text = dynamicObject?.displayText;
            if (text == null)
            {
                text = dynamicObject?.text;
                useDisplayText = false;
            }
            return new PostbackTemplateAction((string)dynamicObject?.label, (string)dynamicObject?.data, text, useDisplayText);
        }
    }
}
