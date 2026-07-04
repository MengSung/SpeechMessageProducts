namespace LineMessagingProcessor.RichMenus;

public sealed class LineRichMenuSyncItem
{
    public LineRichMenuSyncItem(
        string menuKey,
        string richMenuId,
        LineRichMenuSyncOutcome outcome,
        string? errorMessage = null)
    {
        MenuKey = menuKey;
        RichMenuId = richMenuId;
        Outcome = outcome;
        ErrorMessage = errorMessage;
    }

    public string MenuKey { get; }

    public string RichMenuId { get; }

    public LineRichMenuSyncOutcome Outcome { get; }

    public string? ErrorMessage { get; }
}
