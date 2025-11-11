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
