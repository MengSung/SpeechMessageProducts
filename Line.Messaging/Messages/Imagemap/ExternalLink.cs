/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述imagemap影片播放後的外部連結與顯示文字。
 *
 * 主要功能與流程：
 * 本檔宣告 ExternalLink。
 * 資料／選項欄位包含 ExternalLink、LinkUri、Label，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/Imagemap/Video.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    public class ExternalLink
    {
        /// <summary>
        /// Webpage URL. Called when the label displayed after the video is tapped.
        /// / Max: 1000 characters
        /// </summary>
        public string LinkUri { get; }

        /// <summary>
        /// Label.Displayed after the video is finished.
        /// / Max: 30 characters
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="linkUri">
        /// Webpage URL. Called when the label displayed after the video is tapped.
        /// / Max: 1000 characters
        /// </param>
        /// <param name="label">
        /// Label.Displayed after the video is finished.
        /// / Max: 30 characters
        /// </param>
        public ExternalLink(string linkUri, string label)
        {
            LinkUri = linkUri;
            Label = label;
        }
    }
}
