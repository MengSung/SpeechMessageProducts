/*
 * 檔案整體說明
 *
 * 目的與定位：本檔接收webhook連通測試的成功旗標、時間、狀態碼及理由，區分管理測試與業務事件。
 *
 * 主要功能與流程：
 * 本檔宣告 WebhookTestResult。
 * 資料／選項欄位包含 WebhookTestResult、Success、Timestamp、StatusCode、Reason、Detail，名稱、型別與序列化規約須與消費端保持一致。
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
    /// Webhook test result
    /// https://developers.line.biz/en/reference/messaging-api/#test-webhook-endpoint
    /// </summary>
    public class WebhookTestResult
    {
        /// <summary>
        /// Result of the webhook test. true if the test was successful, false otherwise.
        /// </summary>
        [JsonProperty("success")]
        public bool Success { get; set; }

        /// <summary>
        /// Time of the event in milliseconds
        /// </summary>
        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        /// <summary>
        /// HTTP status code. Not included if the webhook test fails.
        /// </summary>
        [JsonProperty("statusCode")]
        public int? StatusCode { get; set; }

        /// <summary>
        /// Reason for the HTTP status code. Not included if the webhook test fails.
        /// </summary>
        [JsonProperty("reason")]
        public string Reason { get; set; }

        /// <summary>
        /// Details of the error. Not included if the webhook test was successful.
        /// </summary>
        [JsonProperty("detail")]
        public string Detail { get; set; }
    }
}
