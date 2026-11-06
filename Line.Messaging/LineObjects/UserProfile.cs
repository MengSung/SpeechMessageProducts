/*
 * 檔案整體說明
 *
 * 目的與定位：本檔接收單一LINE使用者的顯示名稱、識別、照片與狀態文字；profile屬目前授權操作，不可跨使用者誤用。
 *
 * 主要功能與流程：
 * 本檔宣告 UserProfile。
 * 資料／選項欄位包含 UserProfile、DisplayName、UserId、PictureUrl、StatusMessage，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/ChurchReportLineBindingNotificationServiceTests.cs、
 * Line.Messaging/ILineMessagingClient.cs、Line.Messaging/LineMessagingClient.cs、
 * LineMessagingProcessor/LineMessagingProcessorClass.cs、
 * SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    /// <summary>
    /// Response from Get User Profile API.
    /// https://developers.line.me/en/docs/messaging-api/reference/#get-profile
    /// </summary>
    public class UserProfile
    {
        /// <summary>
        /// Display name
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// User ID
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Image URL
        /// </summary>
        public string PictureUrl { get; set; }

        /// <summary>
        /// Status message
        /// </summary>
        public string StatusMessage { get; set; }
    }
}
