namespace LineMessagingProcessor.RichMenus;

public sealed class LineRichMenuSyncReport
{
    public LineRichMenuSyncReport(
        IReadOnlyDictionary<string, string> menuIds,
        IReadOnlyList<string> createdMenuKeys,
        IReadOnlyList<string> reusedMenuKeys,
        IReadOnlyList<string> deletedRichMenuIds,
        IReadOnlyList<LineRichMenuSyncItem>? items = null)
    {
        MenuIds = menuIds ?? new Dictionary<string, string>();
        CreatedMenuKeys = createdMenuKeys ?? Array.Empty<string>();
        ReusedMenuKeys = reusedMenuKeys ?? Array.Empty<string>();
        DeletedRichMenuIds = deletedRichMenuIds ?? Array.Empty<string>();
        Items = items ?? Array.Empty<LineRichMenuSyncItem>();
    }

    public IReadOnlyDictionary<string, string> MenuIds { get; }

    public IReadOnlyList<string> CreatedMenuKeys { get; }

    public IReadOnlyList<string> ReusedMenuKeys { get; }

    public IReadOnlyList<string> DeletedRichMenuIds { get; }

    public IReadOnlyList<LineRichMenuSyncItem> Items { get; }
}
