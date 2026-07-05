// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/LineObjects/NarrowcastProgress.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class NarrowcastProgress
// 主要成員：Phase、SuccessCount、FailureCount、TargetCount、FailedDescription、ErrorCode、AcceptedTime、CompletedTime
// 引用命名空間：Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
