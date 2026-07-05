using Newtonsoft.Json;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// 將 RichMenu 批次連結到多位使用者的 request body。
    /// https://developers.line.biz/en/reference/messaging-api/#link-rich-menu-to-users
    /// 此 DTO 會直接序列化到 LINE bulk-link endpoint，因此屬性名稱必須對齊官方 JSON contract，
    /// 不能依本機 C# 命名偏好任意調整。
    /// </summary>
    public class RichMenuBulkLinkRequest
    {
        /// <summary>
        /// LINE 回傳的 provider richMenuId。
        /// 這裡不能填應用程式 menu key 或 alias id。
        /// </summary>
        [JsonProperty("richMenuId")]
        public string RichMenuId { get; set; }

        /// <summary>
        /// 使用者 ID 集合，必須使用 webhook event object 內回傳的 userId。
        /// 不可使用使用者自己看到的 LINE ID；LINE 最多接受 500 筆。
        /// 呼叫端應先將大量受眾切成小批次，避免超過 API 限制而被拒絕。
        /// </summary>
        [JsonProperty("userIds")]
        public List<string> UserIds { get; set; }
    }

    /// <summary>
    /// 批次解除多位使用者 RichMenu 連結的 request body。
    /// https://developers.line.biz/en/reference/messaging-api/#unlink-rich-menu-from-users
    /// 此 DTO 用於移除使用者與 RichMenu 的直接連結；受影響使用者會回到 channel 的 LINE 預設 RichMenu 行為。
    /// </summary>
    public class RichMenuBulkUnlinkRequest
    {
        /// <summary>
        /// 使用者 ID 集合，必須使用 webhook event object 內回傳的 userId。
        /// 不可使用使用者自己看到的 LINE ID；LINE 最多接受 500 筆。
        /// 此清單只能包含 LINE webhook userId，顯示名稱與 LINE ID 都不是有效值。
        /// </summary>
        [JsonProperty("userIds")]
        public List<string> UserIds { get; set; }
    }
}
