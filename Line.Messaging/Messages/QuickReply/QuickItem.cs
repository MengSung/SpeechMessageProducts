/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述快速回覆按鈕的動作及可選圖示。
 *
 * 主要功能與流程：
 * 本檔宣告 QuickReplyButtonObject。
 * 資料／選項欄位包含 QuickReplyButtonObject、Type、ImageUrl、Action，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkGoldenTests.cs、Line.Messaging/Messages/QuickReply/QuickReply.cs、
 * LineMessagingProcessor.Workflows/LineQuickReplyFactory.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    /// <summary>
    /// This is a quick reply option that is displayed as a button.
    /// https://developers.line.me/en/reference/messaging-api/#quick-reply-button-object
    /// </summary>
    public class QuickReplyButtonObject
    {
        public string Type = "action";

        /// <summary>
        /// URL of the icon that is displayed at the beginning of the button
        /// Max: 1000 characters
        /// URL scheme: https
        /// Image format: PNG
        /// Aspect ratio: 1:1
        /// Data size: Up to 1 MB
        /// There is no limit on the image size.
        /// If the action property has a camera action, camera roll action, or location action, and the imageUrl property is not set, the default icon is displayed.
        /// </summary>
        public string ImageUrl { get; set; }

        public ITemplateAction Action { get; set; }

        public QuickReplyButtonObject(ITemplateAction action, string imageUrl = null)
        {
            Action = action;
            ImageUrl = imageUrl;
        }
    }
}
