using Newtonsoft.Json;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Request body for linking rich menu to multiple users
    /// https://developers.line.biz/en/reference/messaging-api/#link-rich-menu-to-users
    /// </summary>
    public class RichMenuBulkLinkRequest
    {
        /// <summary>
        /// Rich menu ID
        /// </summary>
        [JsonProperty("richMenuId")]
        public string RichMenuId { get; set; }

        /// <summary>
        /// Array of user IDs. Use the userId values returned in webhook event objects. 
        /// Do not use the LINE ID found on LINE.
        /// Max: 500 user IDs
        /// </summary>
        [JsonProperty("userIds")]
        public List<string> UserIds { get; set; }
    }

    /// <summary>
    /// Request body for unlinking rich menu from multiple users
    /// https://developers.line.biz/en/reference/messaging-api/#unlink-rich-menu-from-users
    /// </summary>
    public class RichMenuBulkUnlinkRequest
    {
        /// <summary>
        /// Array of user IDs. Use the userId values returned in webhook event objects. 
        /// Do not use the LINE ID found on LINE.
        /// Max: 500 user IDs
        /// </summary>
        [JsonProperty("userIds")]
        public List<string> UserIds { get; set; }
    }
}
