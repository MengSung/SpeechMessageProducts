/*
 * 檔案整體說明
 *
 * 目的與定位：本檔接收窄播非同步任務階段、成功／失敗計數及時間；送出已受理不等於完成。
 *
 * 主要功能與流程：
 * 本檔宣告 NarrowcastProgress。
 * 資料／選項欄位包含 NarrowcastProgress、Phase、SuccessCount、FailureCount、TargetCount、FailedDescription、ErrorCode、
 * AcceptedTime、CompletedTime，名稱、型別與序列化規約須與消費端保持一致。
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
    /// Narrowcast progress response
    /// https://developers.line.biz/en/reference/messaging-api/#get-narrowcast-progress-status
    /// </summary>
    public class NarrowcastProgress
    {
        /// <summary>
        /// The current status. One of:
        /// - waiting: Messages are not yet ready to be sent. They are currently being filtered or processed.
        /// - sending: Messages are currently being sent.
        /// - succeeded: Messages were sent successfully. This may not mean the messages were successfully received.
        /// - failed: Messages failed to be sent. Use the failedDescription property to find the cause of the failure.
        /// </summary>
        [JsonProperty("phase")]
        public string Phase { get; set; }

        /// <summary>
        /// The number of users who successfully received the message.
        /// </summary>
        [JsonProperty("successCount")]
        public long? SuccessCount { get; set; }

        /// <summary>
        /// The number of users who failed to send the message.
        /// </summary>
        [JsonProperty("failureCount")]
        public long? FailureCount { get; set; }

        /// <summary>
        /// The number of intended recipients of the message.
        /// </summary>
        [JsonProperty("targetCount")]
        public long? TargetCount { get; set; }

        /// <summary>
        /// The reason the message failed to be sent. This property is only included when phase is failed.
        /// </summary>
        [JsonProperty("failedDescription")]
        public string FailedDescription { get; set; }

        /// <summary>
        /// Error summary. This property is only included when phase is failed and some recipients failed to receive a message.
        /// </summary>
        [JsonProperty("errorCode")]
        public string ErrorCode { get; set; }

        /// <summary>
        /// Narrowcast message request accepted time in milliseconds.
        /// Format: milliseconds (Epoch time)
        /// </summary>
        [JsonProperty("acceptedTime")]
        public long? AcceptedTime { get; set; }

        /// <summary>
        /// Processing of narrowcast message request completion time in milliseconds.
        /// Returned when the phase property is succeeded or failed.
        /// Format: milliseconds (Epoch time)
        /// </summary>
        [JsonProperty("completedTime")]
        public long? CompletedTime { get; set; }
    }
}
