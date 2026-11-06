/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述圖片、影片或音訊訊息的內容供應來源及媒體長度，保留既有型別拼字相容性。
 *
 * 主要功能與流程：
 * 本檔宣告 MediaEventMessage。
 * 資料／選項欄位包含 ContentProvider、Duration，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/EventMessage.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Media event message (Image, Video or Audio)
    /// </summary>
    public class MediaEventMessage : EventMessage
    {
        /// <summary>
        /// ContentProvider
        /// </summary>
        public ContentProvider ContentProvider { get; }

        /// <summary>
        /// Length of media file (milliseconds)
        /// </summary>
        public int? Duration { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="type">Event Message Type</param>
        /// <param name="id">Message ID</param>
        /// <param name="contentProvider">ContentProvider Object</param>
        /// <param name="duration">Duration</param>
        public MediaEventMessage(EventMessageType type, string id, ContentProvider contentProvider = null, int? duration = null) : base(type, id)
        {
            ContentProvider = contentProvider;
            Duration = duration;
        }
    }
}
