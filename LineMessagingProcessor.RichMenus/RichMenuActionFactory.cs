using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

public static class RichMenuActionFactory
{
    public static RichMenuSwitchTemplateAction SwitchToAlias(string aliasId, string data, string? label = null)
        => Switch(aliasId, data, label);

    public static RichMenuSwitchTemplateAction Switch(string aliasId, string data, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(aliasId))
        {
            throw new ArgumentException("Alias id is required.", nameof(aliasId));
        }

        if (string.IsNullOrWhiteSpace(data))
        {
            throw new ArgumentException("Postback data is required.", nameof(data));
        }

        return new RichMenuSwitchTemplateAction(aliasId.Trim(), data.Trim(), label ?? string.Empty);
    }
}
