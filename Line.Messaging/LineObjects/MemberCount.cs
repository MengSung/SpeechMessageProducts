using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// Member count response
    /// https://developers.line.biz/en/reference/messaging-api/#get-members-group-count
    /// </summary>
    public class MemberCount
    {
        /// <summary>
        /// The count of members in the group or multi-person chat. 
        /// The number excludes users who have blocked the LINE Official Account.
        /// </summary>
        [JsonProperty("count")]
        public int Count { get; set; }
    }
}
