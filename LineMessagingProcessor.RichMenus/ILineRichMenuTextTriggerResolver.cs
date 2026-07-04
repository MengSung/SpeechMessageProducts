namespace LineMessagingProcessor.RichMenus;

public interface ILineRichMenuTextTriggerResolver
{
    string? ResolveMenuKey(string? receivedText);

    bool TryResolve(string? receivedText, out string menuKey);
}
