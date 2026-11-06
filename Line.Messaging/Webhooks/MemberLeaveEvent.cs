/*
 * 檔案整體說明
 *
 * 目的與定位：本檔表示群組或聊天室有成員離開的事件。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberLeaveEvent。
 * 資料／選項欄位包含 Left，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/WebhookApplication.cs、Line.Messaging/Webhooks/WebhookEvent.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Collections.Generic;

namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Event object for when a user leaves a group or room that the bot is in.
    /// </summary>
    public class MemberLeaveEvent : WebhookEvent
    {
        /// <summary>
        /// User ID of users who left
        /// </summary>
        public Moved Left { get; }

        public MemberLeaveEvent(WebhookEventSource source, long timestamp, IList<WebhookEventSource> members)
            : base(WebhookEventType.MemberLeft, source, timestamp)
        {
            Left = new Moved(members);
        }
    }


}
