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
