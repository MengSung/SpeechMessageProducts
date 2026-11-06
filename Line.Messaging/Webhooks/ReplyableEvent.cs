/*
 * 檔案整體說明
 *
 * 目的與定位：本檔為可回覆事件提供reply token；token只適用目前事件且不應進入共用狀態或日誌。
 *
 * 主要功能與流程：
 * 本檔宣告 ReplyableEvent。
 * 資料／選項欄位包含 ReplyToken，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/AccountLinkEvent.cs、Line.Messaging/Webhooks/BeaconEvent.cs、
 * Line.Messaging/Webhooks/FollowEvent.cs、Line.Messaging/Webhooks/JoinEvent.cs、
 * Line.Messaging/Webhooks/MemberJoinEvent.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    public abstract class ReplyableEvent : WebhookEvent
    {
        public string ReplyToken { get; }

        public ReplyableEvent(WebhookEventType eventType, WebhookEventSource source, long timestamp, string replyToken)
            : base(eventType, source, timestamp)
        {
            ReplyToken = replyToken;
        }
    }
}
