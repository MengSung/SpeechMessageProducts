/*
 * 檔案整體說明
 *
 * 目的與定位：本檔表示群組或聊天室有成員加入的事件。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberJoinEvent。
 * 資料／選項欄位包含 Joined，名稱、型別與序列化規約須與消費端保持一致。
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
    /// Event object for when a user joins a group or room that the bot is in.
    /// </summary>
    public class MemberJoinEvent : ReplyableEvent
    {
        /// <summary>
        /// User ID of users who joined
        /// </summary>
        public Moved Joined { get; }

        public MemberJoinEvent(WebhookEventSource source, long timestamp, string replyToken, IList<WebhookEventSource> members)
            : base(WebhookEventType.MemberJoined, source, timestamp, replyToken)
        {
            Joined = new Moved(members);
        }
    }

}
