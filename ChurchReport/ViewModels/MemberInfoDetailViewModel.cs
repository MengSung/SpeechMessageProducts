using System.Collections.Generic;

namespace ChurchReport.ViewModels
{
    public class MemberInfoDetailViewModel
    {
        public string ContactId { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
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
