namespace LineMessagingProcessor.RichMenus;

public sealed class RichMenuDecision
{
    private RichMenuDecision(string? menuKey, bool unlink, RichMenuDecisionPriority priority, TimeSpan? ttl, string reason)
    {
        MenuKey = menuKey;
        Unlink = unlink;
        Priority = priority;
        Ttl = ttl;
        Reason = reason;
    }

    public string? MenuKey { get; }

    public bool Unlink { get; }

    public RichMenuDecisionPriority Priority { get; }

    public TimeSpan? Ttl { get; }

    public string Reason { get; }

    public static RichMenuDecision None { get; } = new(null, false, RichMenuDecisionPriority.None, null, "none");

    public static RichMenuDecision Assign(string menuKey, RichMenuDecisionPriority priority, string reason, TimeSpan? ttl = null)
    {
        if (string.IsNullOrWhiteSpace(menuKey))
        {
            throw new ArgumentException("Menu key is required.", nameof(menuKey));
        }

        return new RichMenuDecision(menuKey.Trim(), false, priority, ttl, string.IsNullOrWhiteSpace(reason) ? "assign" : reason.Trim());
    }

    public static RichMenuDecision Remove(RichMenuDecisionPriority priority, string reason)
        => new(null, true, priority, null, string.IsNullOrWhiteSpace(reason) ? "unlink" : reason.Trim());
}
