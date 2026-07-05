// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/LineObjects/MessageQuota.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：enum QuotaType、class MessageQuota、class MessageQuotaConsumption
// 主要成員：Type、Value、TotalUsage
// 引用命名空間：Newtonsoft.Json、Newtonsoft.Json.Converters、System.Runtime.Serialization
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace Line.Messaging
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum QuotaType
    {
        [EnumMember(Value = "none")]
        None,
        [EnumMember(Value = "limited")]
        Limited
    }

    /// <summary>
    /// Message quota response
    /// https://developers.line.biz/en/reference/messaging-api/#get-message-quota-information
    /// </summary>
    public class MessageQuota
    {
        /// <summary>
        /// Quota type.
        /// - none: No limit on the number of messages.
        /// - limited: There is a limit on the number of messages.
        /// </summary>
        [JsonProperty("type")]
        public QuotaType Type { get; set; }

        /// <summary>
        /// The target limit for sending messages in the current month. This property is returned only when the `type` property is `limited`.
        /// </summary>
        [JsonProperty("value")]
        public long? Value { get; set; }
    }

    /// <summary>
    /// Message quota consumption response
    /// https://developers.line.biz/en/reference/messaging-api/#get-consumption
    /// </summary>
    public class MessageQuotaConsumption
    {
        /// <summary>
        /// The number of sent messages in the current month
        /// </summary>
        [JsonProperty("totalUsage")]
        public long TotalUsage { get; set; }
    }
}
