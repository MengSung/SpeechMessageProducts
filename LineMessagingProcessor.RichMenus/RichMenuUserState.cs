namespace LineMessagingProcessor.RichMenus;

public sealed class RichMenuUserState
{
    public RichMenuUserState(
        string lineUserId,
        string currentMenuKey,
        string? previousMenuKey,
        DateTimeOffset? expiresAt,
        DateTimeOffset updatedAt)
    {
        LineUserId = lineUserId;
        CurrentMenuKey = currentMenuKey;
        PreviousMenuKey = previousMenuKey;
        ExpiresAt = expiresAt;
        UpdatedAt = updatedAt;
    }

    public string LineUserId { get; }

    public string CurrentMenuKey { get; }

    public string? PreviousMenuKey { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public DateTimeOffset UpdatedAt { get; }
}
