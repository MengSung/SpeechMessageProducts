/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述貼圖訊息的套件ID及貼圖ID。
 *
 * 主要功能與流程：
 * 本檔宣告 StickerMessage。
 * 資料／選項欄位包含 Type、QuickReply、PackageId、StickerId，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs、
 * Line.Messaging/Messages/ISendMessage.cs、LineMessagingProcessor.Workflows/LineNotificationContent.cs、
 * SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs、
 * SpeechMessageProducts.ChurchReport/Tools/PushUtility.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    /// <summary>
    /// Sticker. For a list of the sticker IDs for stickers that can be sent with the Messaging API, see Sticker list.
    /// </summary>
    public class StickerMessage : ISendMessage
    {
        public MessageType Type { get; } = MessageType.Sticker;

        /// <summary>
        /// These properties are used for the quick reply feature
        /// </summary>
        public QuickReply QuickReply { get; set; }

        /// <summary>
        /// Package ID for a set of stickers. For information on package IDs, see the Sticker list.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Sticker ID. For a list of sticker IDs for stickers that can be sent with the Messaging API, see the Sticker list.
        /// </summary>
        public string StickerId { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="packageId">
        /// Package ID for a set of stickers. For information on package IDs, see the Sticker list.
        /// </param>
        /// <param name="stickerId">
        /// Sticker ID. For a list of sticker IDs for stickers that can be sent with the Messaging API, see the Sticker list.
        /// </param>
        /// <param name="quickReply">
        /// QuickReply
        /// </param>
        public StickerMessage(string packageId, string stickerId, QuickReply quickReply = null)
        {
            PackageId = packageId;
            StickerId = stickerId;
            QuickReply = quickReply;
        }
    }
}
