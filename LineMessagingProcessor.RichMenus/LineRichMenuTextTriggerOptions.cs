namespace LineMessagingProcessor.RichMenus;

public sealed class LineRichMenuTextTriggerOptions
{
    public Dictionary<string, string> ExactTextToMenuKey { get; } = new(StringComparer.OrdinalIgnoreCase);
}
