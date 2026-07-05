using Newtonsoft.Json;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// 批次替換或解除使用者 RichMenu 連結的 request body。
    /// https://developers.line.biz/en/reference/messaging-api/#batch-control-rich-menus
    /// LINE 會非同步處理此請求；呼叫端應使用 progress endpoint 追蹤已送出的操作最後成功或失敗。
    /// </summary>
    public class RichMenuBatchRequest
    {
        /// <summary>
        /// operation 物件集合，LINE 最多接受 1000 筆。
        /// 每筆 operation 表示一個 link、unlink 或 unlink-all 指令；順序應保留為呼叫端希望 LINE 處理的順序。
        /// </summary>
        [JsonProperty("operations")]
        public List<RichMenuBatchOperation> Operations { get; set; }

        /// <summary>
        /// 用於恢復 batch control request 的 key。
        /// 呼叫端重試或恢復先前已被接受的批次操作時會提供此值。
        /// </summary>
        [JsonProperty("resumeRequestKey")]
        public string ResumeRequestKey { get; set; }
    }


    /// <summary>
    /// RichMenu 批次操作項目。
    /// 表示 <see cref="RichMenuBatchRequest"/> 中的一個指令；必要欄位會依 <see cref="Type"/> 改變。
    /// 呼叫端只能組出 LINE API 接受的欄位組合。
    /// </summary>
    public class RichMenuBatchOperation
    {
        /// <summary>
        /// 操作類型。
        /// - link：將 RichMenu 綁定到使用者。
        /// - unlink：解除使用者的 RichMenu 綁定。
        /// - unlinkAll：解除所有使用者的 RichMenu 綁定。
        /// 此字串會直接送進 JSON，必須保持 LINE API 要求的小寫格式。
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// RichMenu ID；<see cref="Type"/> 為 link 時必填。
        /// 這是 provider richMenuId，不是 alias id；若要用 alias-based request，請使用 <see cref="RichMenuAliasId"/>。
        /// </summary>
        [JsonProperty("richMenuId")]
        public string RichMenuId { get; set; }

        /// <summary>
        /// RichMenu 別名 ID。
        /// alias id 讓 client 端切換 action 維持穩定，同時允許 provisioning 輪替底層 richMenuId。
        /// </summary>
        [JsonProperty("richMenuAliasId")]
        public string RichMenuAliasId { get; set; }

        /// <summary>
        /// 使用者 ID 集合；<see cref="Type"/> 為 link 或 unlink 時必填。
        /// 必須使用 webhook event object 內的 userId，LINE 最多接受 500 筆。
        /// unlinkAll 是 channel-wide 操作，不應提供此欄位。
        /// </summary>
        [JsonProperty("userIds")]
        public List<string> UserIds { get; set; }
    }

    /// <summary>
    /// RichMenu 批次操作進度 response。
    /// https://developers.line.biz/en/reference/messaging-api/#get-batch-control-rich-menus-progress-status
    /// LINE 接受 batch-control request 並開始非同步處理後，會透過此物件回傳進度狀態。
    /// </summary>
    public class RichMenuBatchProgress
    {
        /// <summary>
        /// RichMenu batch control operation 目前狀態。
        /// - processing：處理中。
        /// - succeeded：處理成功。
        /// - failed：處理失敗。
        /// LINE 未來可能擴充狀態集合，消費端遇到未知值時應採防禦式處理。
        /// </summary>
        [JsonProperty("phase")]
        public string Phase { get; set; }

        /// <summary>
        /// batch control request 被 LINE 接受的時間，單位為毫秒。
        /// 格式為 Epoch time milliseconds；這是 provider 時間，可用於診斷與 polling log，不應作為本機業務排序依據。
        /// </summary>
        [JsonProperty("acceptedTime")]
        public long AcceptedTime { get; set; }

        /// <summary>
        /// RichMenu batch control 完成時間，單位為毫秒。
        /// 僅在 phase 為 succeeded 或 failed 時回傳；格式為 Epoch time milliseconds。
        /// null 代表 LINE 尚未完成非同步操作。
        /// </summary>
        [JsonProperty("completedTime")]
        public long? CompletedTime { get; set; }
    }
}
