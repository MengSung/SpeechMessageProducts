using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// Group summary information
    /// https://developers.line.biz/en/reference/messaging-api/#get-group-summary
    /// </summary>
    public class GroupSummary
    {
        /// <summary>
        /// Group ID
        /// </summary>
        [JsonProperty("groupId")]
        public string GroupId { get; set; }

        /// <summary>
        /// Group name
        /// </summary>
        [JsonProperty("groupName")]
        public string GroupName { get; set; }

        /// <summary>
        /// Group icon URL. "https" image URL. Not included in the response if the group doesn't have an icon.
        /// </summary>
        [JsonProperty("pictureUrl")]
        public string PictureUrl { get; set; }
    }
}
