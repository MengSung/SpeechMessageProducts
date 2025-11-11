using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// Rich menu alias
    /// https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias
    /// </summary>
    public class RichMenuAlias
    {
        /// <summary>
        /// Rich menu alias ID
        /// </summary>
        [JsonProperty("richMenuAliasId")]
        public string RichMenuAliasId { get; set; }

        /// <summary>
        /// Rich menu ID
        /// </summary>
        [JsonProperty("richMenuId")]
        public string RichMenuId { get; set; }
    }

    /// <summary>
    /// Rich menu alias list
    /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-list
    /// </summary>
    public class RichMenuAliasList
    {
        /// <summary>
        /// Array of rich menu alias objects
        /// </summary>
        [JsonProperty("aliases")]
        public System.Collections.Generic.List<RichMenuAlias> Aliases { get; set; }
    }
}
