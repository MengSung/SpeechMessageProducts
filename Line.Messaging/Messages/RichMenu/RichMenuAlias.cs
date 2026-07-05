using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// RichMenu 別名。
    /// https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias
    /// alias 提供穩定識別碼，讓 action 在 provisioning 輪替底層 provider richMenuId 後仍能引用同一個邏輯選單。
    /// </summary>
    public class RichMenuAlias
    {
        /// <summary>
        /// RichMenu 別名 ID。
        /// 此值由應用程式 catalog 控制，跨佈署應維持穩定。
        /// </summary>
        [JsonProperty("richMenuAliasId")]
        public string RichMenuAliasId { get; set; }

        /// <summary>
        /// alias 目前指向的 LINE provider richMenuId。
        /// </summary>
        [JsonProperty("richMenuId")]
        public string RichMenuId { get; set; }
    }

    /// <summary>
    /// RichMenu alias 清單。
    /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-list
    /// provisioning workflow 會讀取此清單，判斷 alias 應建立、更新或保持不變。
    /// </summary>
    public class RichMenuAliasList
    {
        /// <summary>
        /// RichMenu alias 物件集合。
        /// LINE 會在此集合中回傳 channel 目前的 alias 對照表。
        /// </summary>
        [JsonProperty("aliases")]
        public System.Collections.Generic.List<RichMenuAlias> Aliases { get; set; }
    }
}
