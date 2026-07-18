using System;
using System.Collections.Generic;

namespace ChurchReport.ViewModels.MemberInfoTree
{
    public class GroupNodeViewModel
    {
        public string ListId { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string LeaderName { get; set; } = string.Empty;
        public string GroupTime { get; set; } = string.Empty;
        public string GroupPlace { get; set; } = string.Empty;
        public int MemberCount { get; set; }
    }

    public class DistrictNodeViewModel
    {
        public string RaceLeaderKey { get; set; } = string.Empty;
        public string AreaName { get; set; } = string.Empty;
        public string RaceLeaderName { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public int GroupCount { get; set; }
        public List<GroupNodeViewModel> Groups { get; set; } = new List<GroupNodeViewModel>();
    }

    public class UngroupedNodeViewModel
    {
        public int MemberCount { get; set; }
    }

    public class DistrictTreeViewModel
    {
        public List<DistrictNodeViewModel> Districts { get; set; } = new List<DistrictNodeViewModel>();
        public UngroupedNodeViewModel? Ungrouped { get; set; }
        public string Scope { get; set; } = string.Empty;
    }

    /// <summary>
    /// 小組、未分組與樹搜尋共用的成員列契約。BirthDate 保留 nullable，讓前端能區分有效日期與未設定；
    /// Gender、SpiritualIdentity、MembershipStatus 均為 OptionSet 顯示文字，而非可直接回寫 CRM 的整數值。
    /// </summary>
    public class GroupMemberRowViewModel
    {
        public string ContactId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string SpiritualIdentity { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        /// <summary>
        /// contact.customertypecode 在 Dynamics 客製化選項集合中的零起始順位。
        /// 這不是 OptionSet 原始整數值；null 代表 metadata 未列出或欄位未填。
        /// </summary>
        public int? MembershipStatusOrder { get; set; }
        /// <summary>
        /// 表示 CRM 欄位實際有 OptionSet 值，用來區分 metadata 未知舊值與真正空白。
        /// </summary>
        public bool HasMembershipStatusValue { get; set; }
        /// <summary>
        /// 顯示給使用者的本地化會員身份文字；排序不得使用這個文字欄位。
        /// </summary>
        public string MembershipStatus { get; set; } = string.Empty;
        public string RelationGoals { get; set; } = string.Empty;
    }

    /// <summary>
    /// MatchingListIds 與 HasUngrouped 保留為相容欄位及節點命中 metadata；現行前端主要使用 Rows
    /// 直接替換搜尋結果表格，不需按每個命中節點再發請求。Rows 必須由 allowed ids 過濾並去重。
    /// </summary>
    public class MemberInfoTreeSearchResultViewModel
    {
        public List<string> MatchingListIds { get; set; } = new List<string>();
        public bool HasUngrouped { get; set; }
        public List<GroupMemberRowViewModel> Rows { get; set; } = new List<GroupMemberRowViewModel>();
    }
}
