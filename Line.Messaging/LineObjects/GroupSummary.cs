/*
 * 檔案整體說明
 *
 * 目的與定位：本檔接收群組識別、群名及圖片資訊，供群組摘要查詢結果使用。
 *
 * 主要功能與流程：
 * 本檔宣告 GroupSummary。
 * 資料／選項欄位包含 GroupSummary、GroupId、GroupName、PictureUrl，名稱、型別與序列化規約須與消費端保持一致。
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
    /// Group summary information
    /// https://developers.line.biz/en/reference/messaging-api/#get-group-summary
    /// </summary>
    public class GroupSummary
    {
        /// <summary>
        /// Group ID
        /// </summary>
        [JsonProperty("groupId")]
        public string GroupId { get; set; }

        /// <summary>
        /// Group name
        /// </summary>
        [JsonProperty("groupName")]
        public string GroupName { get; set; }

        /// <summary>
        /// Group icon URL. "https" image URL. Not included in the response if the group doesn't have an icon.
        /// </summary>
        [JsonProperty("pictureUrl")]
        public string PictureUrl { get; set; }
    }
}
