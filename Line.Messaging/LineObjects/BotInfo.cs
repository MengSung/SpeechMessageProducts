using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// Bot information
    /// https://developers.line.biz/en/reference/messaging-api/#get-bot-info
    /// </summary>
    public class BotInfo
    {
        /// <summary>
        /// Bot's user ID
        /// </summary>
        [JsonProperty("userId")]
        public string UserId { get; set; }

        /// <summary>
        /// Bot's basic ID
        /// </summary>
        [JsonProperty("basicId")]
        public string BasicId { get; set; }

        /// <summary>
        /// Bot's premium ID. Not included in the response if the premium ID isn't set.
        /// </summary>
        [JsonProperty("premiumId")]
        public string PremiumId { get; set; }

        /// <summary>
        /// Bot's display name
        /// </summary>
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        /// <summary>
        /// Profile image URL. "https" image URL. Not included in the response if the bot doesn't have a profile image.
        /// </summary>
        [JsonProperty("pictureUrl")]
        public string PictureUrl { get; set; }

        /// <summary>
        /// Chat settings set in the LINE Official Account Manager. One of:
        /// - chat: Chat is set to "On".
        /// - bot: Chat is set to "Off".
        /// </summary>
        [JsonProperty("chatMode")]
        public string ChatMode { get; set; }

        /// <summary>
        /// Automatic read setting for messages. If the chat is set to "Off", auto is returned.
        /// If the chat is set to "On", manual is returned.
        /// - auto: Auto read setting is enabled.
        /// - manual: Auto read setting is disabled.
        /// </summary>
        [JsonProperty("markAsReadMode")]
        public string MarkasreadMode { get; set; }
    }
}
