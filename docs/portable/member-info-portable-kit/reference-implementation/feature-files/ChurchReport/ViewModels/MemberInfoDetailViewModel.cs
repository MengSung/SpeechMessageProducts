using System;
using System.Collections.Generic;

namespace ChurchReport.ViewModels
{
    /// <summary>
    /// 會友詳細彈窗的讀取契約。文字欄位維持既有編輯／顯示用途；性別與生日只提供唯讀資訊，
    /// 不加入更新端點的輸入，避免新增欄位意外擴張既有上傳行為。
    /// </summary>
    public class MemberInfoDetailViewModel
    {
        public string ContactId { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        // Gender 是 CRM OptionSet 解析後的顯示文字；BirthDate 已將 Year<=1 的 CRM 哨兵值正規化為 null。
        public string Gender { get; set; }
        public DateTime? BirthDate { get; set; }
        public string MembershipStatus { get; set; }
        public string SpiritualIdentity { get; set; }
        public string AvatarSource { get; set; }

        // 會員身分(customertypecode) / 信仰狀態(new_spiriitual_identity) 改為下拉編輯所需：
        // *Value = 目前選中的 OptionSet 整數值(無值為 null)；*Options = 全部可選項(文字+值)。
        public int? MembershipStatusValue { get; set; }
        public int? SpiritualIdentityValue { get; set; }
        public IReadOnlyList<OptionItem> MembershipStatusOptions { get; set; } = new List<OptionItem>();
        public IReadOnlyList<OptionItem> SpiritualIdentityOptions { get; set; } = new List<OptionItem>();

        public IReadOnlyList<RelationGoalItem> RelationGoals { get; set; } = new List<RelationGoalItem>();
    }

    /// <summary>OptionSet 單一選項：以整數值送回後台，顯示文字給使用者看。</summary>
    public class OptionItem
    {
        public int Value { get; set; }
        public string Text { get; set; }
    }

    public class RelationGoalItem
    {
        public string Role { get; set; }
        public string TargetName { get; set; }
    }
}
