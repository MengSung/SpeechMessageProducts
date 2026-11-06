/*
 * 檔案整體說明
 *
 * 目的與定位：定義會友樹組裝的輸入契約，將小組描述與群組成員關聯列分開，避免樹組裝依賴完整CRM物件；輸入必須先由呼叫端完成範圍驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroupDescriptor、GroupMembershipRow。
 * 資料／選項欄位包含 SmallGroupDescriptor、ListId、GroupName、AreaName、RaceLeaderName、RaceLeaderKey、LeaderName、GroupTime、
 * GroupPlace、GroupMembershipRow、ContactId、IsCurrent，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs、
 * ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs、
 * ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs、
 * SpeechMessageProducts.ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs 追查目前工作樹中的合作／使用位置。
 */
namespace ChurchReport.Services.MemberInfo
{
    public class SmallGroupDescriptor
    {
        public string ListId { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string AreaName { get; set; } = string.Empty;
        public string RaceLeaderName { get; set; } = string.Empty;
        public string RaceLeaderKey { get; set; } = string.Empty;
        public string LeaderName { get; set; } = string.Empty;
        public string GroupTime { get; set; } = string.Empty;
        public string GroupPlace { get; set; } = string.Empty;
    }

    public class GroupMembershipRow
    {
        public string ListId { get; set; } = string.Empty;
        public string ContactId { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
    }
}
