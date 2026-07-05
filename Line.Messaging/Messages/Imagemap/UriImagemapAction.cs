// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Imagemap/UriImagemapAction.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class UriImagemapAction
// 主要成員：Type、Area、LinkUri、Label
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
    /// Object which specifies the actions and tappable regions of an imagemap.
    /// When a region is tapped, the user is redirected to the URI specified in uri.
    /// https://developers.line.me/en/docs/messaging-api/reference/#imagemap-action-objects
    /// </summary>
    public class UriImagemapAction : IImagemapAction
    {
        public ImagemapActionType Type { get; } = ImagemapActionType.Uri;

        /// <summary>
        /// Defined tappable area
        /// </summary>
        public ImagemapArea Area { get; }

        /// <summary>
        /// Webpage URL
        /// Max: 1000 characters
        /// </summary>
        public string LinkUri { get; }

        /// <summary>
        /// Label for the action. Spoken when the accessibility feature is enabled on the client device.
        /// Max: 50 characters
        /// Supported on LINE iOS version 8.2.0 and later.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="area">
        /// Defined tappable area
        /// </param>
        /// <param name="linkUri">
        /// Label for the action. Spoken when the accessibility feature is enabled on the client device.
        /// Max: 50 characters
        /// Supported on LINE iOS version 8.2.0 and later.
        /// </param>
        /// <param name="label">
        /// Label for the action. Spoken when the accessibility feature is enabled on the client device.
        /// Max: 50 characters
        /// Supported on LINE iOS version 8.2.0 and later.
        /// </param>
        public UriImagemapAction(ImagemapArea area, string linkUri, string label = null)
        {
            Area = area;
            LinkUri = linkUri;
            Label = label?.Substring(Math.Min(label.Length, 50));
        }
    }
}
