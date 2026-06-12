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
        public IReadOnlyList<RelationGoalItem> RelationGoals { get; set; } = new List<RelationGoalItem>();
    }

    public class RelationGoalItem
    {
        public string Role { get; set; }
        public string TargetName { get; set; }
    }
}
