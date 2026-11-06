/*
 * 檔案整體說明
 *
 * 目的與定位：本檔接收群組或聊天室成員數查詢結果，不包含成員個別資料。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberCount。
 * 資料／選項欄位包含 MemberCount、Count，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs、
 * ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs、Line.Messaging/LineMessagingClient.cs、
 * Line.Messaging/LineObjects/CouponAndMembership.cs、
 * SpeechMessageProducts.ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs 追查目前工作樹中的合作／使用位置。
 */
using Newtonsoft.Json;

namespace Line.Messaging
{
    /// <summary>
    /// Member count response
    /// https://developers.line.biz/en/reference/messaging-api/#get-members-group-count
    /// </summary>
    public class MemberCount
    {
        /// <summary>
        /// The count of members in the group or multi-person chat.
        /// The number excludes users who have blocked the LINE Official Account.
        /// </summary>
        [JsonProperty("count")]
        public int Count { get; set; }
    }
}
