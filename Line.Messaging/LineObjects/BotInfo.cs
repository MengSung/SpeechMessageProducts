// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/LineObjects/BotInfo.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class BotInfo
// 主要成員：UserId、BasicId、PremiumId、DisplayName、PictureUrl、ChatMode、MarkasreadMode
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
