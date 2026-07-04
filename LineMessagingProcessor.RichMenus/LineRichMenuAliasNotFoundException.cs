namespace LineMessagingProcessor.RichMenus;

public sealed class LineRichMenuAliasNotFoundException : Exception
{
    public LineRichMenuAliasNotFoundException(string richMenuAliasId)
        : base($"RichMenu alias '{richMenuAliasId}' was not found.")
    {
        RichMenuAliasId = richMenuAliasId;
    }

    public string RichMenuAliasId { get; }
}
