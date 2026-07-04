namespace LineMessagingProcessor.RichMenus;

public sealed class RichMenuContext
{
    public RichMenuContext(
        string lineUserId,
        IReadOnlySet<string>? roles = null,
        string? receivedText = null,
        string? currentMenuKey = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(lineUserId))
        {
            throw new ArgumentException("LINE user id is required.", nameof(lineUserId));
        }

        LineUserId = lineUserId.Trim();
        Roles = roles ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReceivedText = receivedText;
        CurrentMenuKey = currentMenuKey;
        Attributes = attributes ?? new Dictionary<string, string>();
    }

    public string LineUserId { get; }

    public IReadOnlySet<string> Roles { get; }

    public string? ReceivedText { get; }

    public string? CurrentMenuKey { get; }

    public IReadOnlyDictionary<string, string> Attributes { get; }
}
