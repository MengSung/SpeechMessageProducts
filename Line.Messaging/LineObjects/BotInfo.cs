/*
 * 檔案整體說明
 *
 * 目的與定位：本檔接收機器人帳號ID、名稱、圖片與聊天模式等資訊，供查詢頻道帳號資料。
 *
 * 主要功能與流程：
 * 本檔宣告 BotInfo。
 * 資料／選項欄位包含 BotInfo、UserId、BasicId、PremiumId、DisplayName、PictureUrl、ChatMode、MarkasreadMode，名稱、型別與序列化規約須與消費端保持一致。
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
