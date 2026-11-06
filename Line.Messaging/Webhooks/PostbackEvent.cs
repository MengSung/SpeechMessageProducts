/*
 * 檔案整體說明
 *
 * 目的與定位：本檔表示使用者操作互動元件後回傳postback資料的事件。
 *
 * 主要功能與流程：
 * 本檔宣告 PostbackEvent。
 * 資料／選項欄位包含 Postback，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/WebhookApplication.cs、Line.Messaging/Webhooks/WebhookEvent.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Event object for when a user performs an action on a template message which initiates a postback. You can reply to postback events.
    /// </summary>
    public class PostbackEvent : ReplyableEvent
    {
        /// <summary>
        /// Postback
        /// </summary>
        public Postback Postback { get; }

        public PostbackEvent(WebhookEventSource source, long timestamp, string replyToken, Postback postback)
            : base(WebhookEventType.Postback, source, timestamp, replyToken)
        {
            Postback = postback;
        }
    }
}
