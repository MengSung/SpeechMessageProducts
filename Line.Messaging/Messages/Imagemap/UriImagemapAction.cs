/*
 * 檔案整體說明
 *
 * 目的與定位：本檔定義點擊imagemap指定區域後開啟URI的動作。
 *
 * 主要功能與流程：
 * 本檔宣告 UriImagemapAction。
 * 資料／選項欄位包含 Type、Area、LinkUri、Label，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 LineMessagingProcessor.Workflows/LineImagemapActionFactory.cs 追查目前工作樹中的合作／使用位置。
 */
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
