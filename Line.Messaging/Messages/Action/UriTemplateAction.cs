// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Action/UriTemplateAction.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class UriTemplateAction
// 主要成員：CreateFrom、Type、Label、Uri、AltUri
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
    /// When a control associated with this action is tapped, the URI specified in the uri field is opened.
    /// https://developers.line.me/en/docs/messaging-api/reference/#uri-action
    /// </summary>
    public class UriTemplateAction : ITemplateAction
    {
        public TemplateActionType Type { get; } = TemplateActionType.Uri;

        /// <summary>
        /// Label for the action
        /// Required for templates other than image carousel.Max: 20 characters
        /// Optional for image carousel templates.Max: 12 characters.
        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// URI opened when the action is performed (Max: 1000 characters)
        /// Must start with http, https, or tel.
        /// </summary>
        public string Uri { get; }

        /// <summary>
        /// URI opened on LINE for macOS and Windows when the action is performed (Max: 1000 characters) If the altUri.desktop property is set,
        /// the uri property is ignored on LINE for macOS and Windows.<para>
        /// The available schemes are http, https, line, and tel.For more information about the LINE URL scheme, see Using the LINE URL scheme.
        /// This property is supported on the following version of LINE.
        /// LINE 5.12.0 or later for macOS and Windows</para>
        /// Note: The altUri.desktop property is supported only when you set URI actions in Flex Messages.
        /// </summary>
        public AltUri AltUri { get; }

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
        /// <param name="uri">
        /// URI opened when the action is performed (Max: 1000 characters)
        /// Must start with http, https, or tel.
        /// </param>
        /// <param name="altUri">
        /// URI opened on LINE for macOS and Windows when the action is performed (Max: 1000 characters) If the altUri.desktop property is set,
        /// the uri property is ignored on LINE for macOS and Windows.<para>
        /// The available schemes are http, https, line, and tel.For more information about the LINE URL scheme, see Using the LINE URL scheme.
        /// This property is supported on the following version of LINE.
        /// LINE 5.12.0 or later for macOS and Windows</para>
        /// Note: The altUri.desktop property is supported only when you set URI actions in Flex Messages.
        /// </param>
        public UriTemplateAction(string label, string uri, AltUri altUri = null)
        {
            Label = label?.Substring(0, Math.Min(label.Length, 20));
            Uri = uri;
            AltUri = altUri;
        }

        internal static UriTemplateAction CreateFrom(dynamic dynamicObject)
        {
            var desktopUri = (string)dynamicObject?.altUri?.desktop;
            var altUri = (desktopUri == null) ? null : new AltUri(desktopUri);
            return new UriTemplateAction((string)dynamicObject?.label, (string)dynamicObject?.uri, altUri);
        }
    }
}
