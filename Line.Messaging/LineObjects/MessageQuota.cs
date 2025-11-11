using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace Line.Messaging
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum QuotaType
    {
        [EnumMember(Value = "none")]
        None,
        [EnumMember(Value = "limited")]
        Limited
    }

    /// <summary>
    /// Message quota response
    /// https://developers.line.biz/en/reference/messaging-api/#get-message-quota-information
    /// </summary>
    public class MessageQuota
    {
        /// <summary>
        /// Quota type.
        /// - none: No limit on the number of messages.
        /// - limited: There is a limit on the number of messages.
        /// </summary>
        [JsonProperty("type")]
        public QuotaType Type { get; set; }

        /// <summary>
        /// The target limit for sending messages in the current month. This property is returned only when the `type` property is `limited`.
        /// </summary>
        [JsonProperty("value")]
        public long? Value { get; set; }
    }

    /// <summary>
    /// Message quota consumption response
    /// https://developers.line.biz/en/reference/messaging-api/#get-consumption
    /// </summary>
    public class MessageQuotaConsumption
    {
        /// <summary>
        /// The number of sent messages in the current month
        /// </summary>
        [JsonProperty("totalUsage")]
        public long TotalUsage { get; set; }
    }
}
