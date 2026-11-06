/*
 * 檔案整體說明
 *
 * 目的與定位：本檔描述頻道訊息額度種類、上限及用量查詢結果。
 *
 * 主要功能與流程：
 * 本檔宣告 QuotaType、MessageQuota、MessageQuotaConsumption。
 * 資料／選項欄位包含 QuotaType、MessageQuota、Type、Value、MessageQuotaConsumption、TotalUsage，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/ILineMessagingClient.cs、Line.Messaging/LineMessagingClient.cs 追查目前工作樹中的合作／使用位置。
 */
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
