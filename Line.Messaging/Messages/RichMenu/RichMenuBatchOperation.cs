using Newtonsoft.Json;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Request body for replacing or unlinking the linked rich menus in batches
    /// https://developers.line.biz/en/reference/messaging-api/#batch-control-rich-menus
    /// </summary>
    public class RichMenuBatchRequest
    {
        /// <summary>
        /// Array of operation objects. Max: 1000 objects
        /// </summary>
        [JsonProperty("operations")]
        public List<RichMenuBatchOperation> Operations { get; set; }

        /// <summary>
        /// A key that is used to resume a batch control request.
        /// </summary>
        [JsonProperty("resumeRequestKey")]
        public string ResumeRequestKey { get; set; }
    }


    /// <summary>
    /// Rich menu batch operation
    /// </summary>
    public class RichMenuBatchOperation
    {
        /// <summary>
        /// Operation type. One of:
        /// - link: Link a rich menu to users
        /// - unlink: Unlink a rich menu from users
        /// - unlinkAll: Unlink rich menus from all users
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// Rich menu ID. Required when type is link.
        /// </summary>
        [JsonProperty("richMenuId")]
        public string RichMenuId { get; set; }

        /// <summary>
        /// Rich menu alias ID.
        /// </summary>
        [JsonProperty("richMenuAliasId")]
        public string RichMenuAliasId { get; set; }

        /// <summary>
        /// Array of user IDs. Required when type is link or unlink.
        /// Use the userId values returned in webhook event objects.
        /// Max: 500 user IDs
        /// </summary>
        [JsonProperty("userIds")]
        public List<string> UserIds { get; set; }
    }

    /// <summary>
    /// Rich menu batch progress response
    /// https://developers.line.biz/en/reference/messaging-api/#get-batch-control-rich-menus-progress-status
    /// </summary>
    public class RichMenuBatchProgress
    {
        /// <summary>
        /// The current status of the rich menu batch control operation. One of:
        /// - processing: Processing is in progress
        /// - succeeded: Processing has succeeded
        /// - failed: Processing has failed
        /// </summary>
        [JsonProperty("phase")]
        public string Phase { get; set; }

        /// <summary>
        /// The accepted time in milliseconds of the request of batch control the rich menu.
        /// Format: Epoch time (milliseconds)
        /// </summary>
        [JsonProperty("acceptedTime")]
        public long AcceptedTime { get; set; }

        /// <summary>
        /// The completed time in milliseconds of batch control the rich menu. 
        /// Returned only when phase is succeeded or failed.
        /// Format: Epoch time (milliseconds)
        /// </summary>
        [JsonProperty("completedTime")]
        public long? CompletedTime { get; set; }
    }
}
