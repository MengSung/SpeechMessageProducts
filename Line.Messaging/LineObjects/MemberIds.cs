/*
 * 檔案整體說明
 *
 * 目的與定位：本檔接收群組成員ID的分頁清單及下一頁游標；有游標時不能把目前頁宣稱為完整名單。
 *
 * 主要功能與流程：
 * 本檔宣告 GroupMemberIds。
 * 資料／選項欄位包含 GroupMemberIds、MemberIds、Next，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/ILineMessagingClient.cs、Line.Messaging/LineMessagingClient.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Response from Get group member user IDs API
    /// </summary>
    public class GroupMemberIds
    {
        /// <summary>
        /// List of user IDs of the members in the group.
        /// Max: 100 user IDs
        /// </summary>
        public IList<string> MemberIds { get; set; }

        /// <summary>
        /// continuationToken
        /// Only returned when there are more user IDs remaining in memberIds
        /// </summary>
        public string Next { get; set; }
    }
}
