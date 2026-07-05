// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/RichMenu/RichMenuAlias.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class RichMenuAlias、class RichMenuAliasList
// 主要成員：RichMenuAliasId、RichMenuId、Aliases
// 引用命名空間：Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
