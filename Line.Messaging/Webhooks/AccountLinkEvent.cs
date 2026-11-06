/*
 * 檔案整體說明
 *
 * 目的與定位：本檔表示LINE帳號連結服務帳號後的事件及連結結果。
 *
 * 主要功能與流程：
 * 本檔宣告 AccountLinkEvent。
 * 資料／選項欄位包含 Link，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/WebhookApplication.cs、Line.Messaging/Webhooks/WebhookEvent.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Event object for when a user has linked his/her LINE account with a provider's service account. You can reply to account link events.
    /// If the link token has expired or has already been used, no webhook event will be sent and the user will be shown an error.
    /// </summary>
    public class AccountLinkEvent : ReplyableEvent
    {
        /// <summary>
        /// Link Object
        /// </summary>
        public Link Link { get; }

        public AccountLinkEvent(WebhookEventSource source, long timestamp, string replyToken, Link link)
            : base(WebhookEventType.AccountLink, source, timestamp, replyToken)
        {
            Link = link;
        }
    }
}
