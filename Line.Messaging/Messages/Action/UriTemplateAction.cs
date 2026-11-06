/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述點擊後開啟URI及桌面替代URI的動作。
 *
 * 主要功能與流程：
 * 本檔宣告 UriTemplateAction。
 * - Label for the action Required for templates other than image carousel.Max: 20 characters Optional for image
 * carousel templates.Max: 12 characters. RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。 LINE iOS 8.2.0 以後支援 RichMenu
 * 上的此可及性 label。
 * 主要操作入口包含 CreateFrom。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/LineSchemeUrl.cs、Line.Messaging/Messages/RichMenu/ActionArea.cs 追查目前工作樹中的合作／使用位置。
 */
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
