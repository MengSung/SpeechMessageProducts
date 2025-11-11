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
