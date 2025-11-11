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
