using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// Rich menu switch action
    /// When a control associated with this action is tapped, the rich menu switches to the rich menu specified in richMenuAliasId.
    /// https://developers.line.biz/en/reference/messaging-api/#richmenu-switch-action
    /// </summary>
    public class RichMenuSwitchTemplateAction : ITemplateAction
    {
        public TemplateActionType Type { get; } = TemplateActionType.RichMenuSwitch;

        /// <summary>
        /// Rich menu alias ID to switch to
        /// </summary>
        [JsonProperty("richMenuAliasId")]
        public string RichMenuAliasId { get; set; }

        /// <summary>
        /// Action label.
        /// Max: 20 characters
        /// Not displayed for rich menus. (Required for template messages, but not for rich menus)
        /// Supported on LINE 8.11.0 and later for iOS and Android.
        /// </summary>
        [JsonProperty("label")]
        public string Label { get; set; }

        /// <summary>
        /// String returned via webhook in the postback.data property of the postback event. Max: 300 characters.
        /// </summary>
        [JsonProperty("data")]
        public string Data { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="richMenuAliasId">Rich menu alias ID</param>
        /// <param name="data">Postback data</param>
        /// <param name="label">Action label (optional)</param>
        public RichMenuSwitchTemplateAction(string richMenuAliasId, string data, string label = null)
        {
            RichMenuAliasId = richMenuAliasId;
            Data = data;
            Label = label;
        }

        internal static RichMenuSwitchTemplateAction CreateFrom(dynamic dynamicObject)
        {
            if (dynamicObject == null) return null;
            return new RichMenuSwitchTemplateAction(
                (string)dynamicObject?.richMenuAliasId,
                (string)dynamicObject?.data,
                (string)dynamicObject?.label
            );
        }
    }
}
