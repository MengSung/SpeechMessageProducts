/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述音訊訊息的來源網址、長度與快速回覆資料。
 *
 * 主要功能與流程：
 * 本檔宣告 AudioMessage。
 * 資料／選項欄位包含 Type、QuickReply、OriginalContentUrl、Duration，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/ISendMessage.cs、Line.Messaging/Messages/VideoMessage.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging
{
    /// <summary>
    /// Audio
    /// https://developers.line.me/en/docs/messaging-api/reference/#audio
    /// </summary>
    public class AudioMessage : ISendMessage
    {
        public MessageType Type { get; } = MessageType.Audio;

        /// <summary>
        /// These properties are used for the quick reply feature
        /// </summary>
        public QuickReply QuickReply { get; set; }

        /// <summary>
        /// URL of audio file (Max: 1000 characters)
        /// HTTPS
        /// m4a
        /// Less than 1 minute
        /// Max 10 MB
        /// </summary>
        public string OriginalContentUrl { get; }

        /// <summary>
        /// Length of audio file (milliseconds)
        /// </summary>
        public long Duration { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="originalContentUrl">
        /// URL of audio file (Max: 1000 characters)
        /// HTTPS
        /// m4a
        /// Less than 1 minute
        /// Max 10 MB
        /// </param>
        /// <param name="duration">
        /// Length of audio file (milliseconds)
        /// </param>
        /// <param name="quickReply">
        /// QuickReply
        /// </param>
        public AudioMessage(string originalContentUrl, long duration, QuickReply quickReply = null)
        {
            OriginalContentUrl = originalContentUrl;
            Duration = duration;
            QuickReply = quickReply;
        }
    }
}
