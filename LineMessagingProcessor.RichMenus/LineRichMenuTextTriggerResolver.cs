namespace LineMessagingProcessor.RichMenus;

public sealed class LineRichMenuTextTriggerResolver : ILineRichMenuTextTriggerResolver
{
    private readonly LineRichMenuTextTriggerOptions _options;

    public LineRichMenuTextTriggerResolver(LineRichMenuTextTriggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string? ResolveMenuKey(string? receivedText)
    {
        if (string.IsNullOrWhiteSpace(receivedText))
        {
            return null;
        }

        var text = receivedText.Trim();
        return _options.ExactTextToMenuKey.TryGetValue(text, out var menuKey) && !string.IsNullOrWhiteSpace(menuKey)
            ? menuKey.Trim()
            : null;
    }

    public bool TryResolve(string? receivedText, out string menuKey)
    {
        menuKey = ResolveMenuKey(receivedText) ?? string.Empty;
        return menuKey.Length > 0;
    }
}
