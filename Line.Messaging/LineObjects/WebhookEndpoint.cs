/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述頻道webhook接收網址與啟用狀態，供管理API查詢及設定。
 *
 * 主要功能與流程：
 * 本檔宣告 WebhookEndpoint。
 * 資料／選項欄位包含 WebhookEndpoint、Endpoint、Active，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/ILineMessagingClient.cs、Line.Messaging/LineMessagingClient.cs 追查目前工作樹中的合作／使用位置。
 */
using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// Webhook endpoint information
    /// https://developers.line.biz/en/reference/messaging-api/#get-webhook-endpoint-information
    /// </summary>
    public class WebhookEndpoint
    {
        /// <summary>
        /// Webhook URL
        /// </summary>
        [JsonProperty("endpoint")]
        public string Endpoint { get; set; }

        /// <summary>
        /// Webhook usage status. true if using webhook, false otherwise.
        /// </summary>
        [JsonProperty("active")]
        public bool Active { get; set; }
    }
}
