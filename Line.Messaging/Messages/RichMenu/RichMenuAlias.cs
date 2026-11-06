/*
 * 檔案整體說明
 *
 * 目的與定位：RichMenu 別名。 https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias alias 提供穩定識別碼，
 * 讓 action 在 provisioning 輪替底層 provider richMenuId 後仍能引用同一個邏輯選單。
 *
 * 主要功能與流程：
 * 本檔宣告 RichMenuAlias、RichMenuAliasList。
 * - RichMenu 別名 ID。 此值由應用程式 catalog 控制，跨佈署應維持穩定。
 * - alias 目前指向的 LINE provider richMenuId。
 * - RichMenu alias 物件集合。 LINE 會在此集合中回傳 channel 目前的 alias 對照表。
 * 資料／選項欄位包含 RichMenuAlias、RichMenuAliasId、RichMenuId、RichMenuAliasList、Aliases，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/ILineMessagingClient.cs、Line.Messaging/LineMessagingClient.cs 追查目前工作樹中的合作／使用位置。
 */
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
