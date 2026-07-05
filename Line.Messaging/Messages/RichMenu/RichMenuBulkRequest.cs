// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/RichMenu/RichMenuBulkRequest.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class RichMenuBulkLinkRequest、class RichMenuBulkUnlinkRequest
// 主要成員：RichMenuId、UserIds
// 引用命名空間：Newtonsoft.Json、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
