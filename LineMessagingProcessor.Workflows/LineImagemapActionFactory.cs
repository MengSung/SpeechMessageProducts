using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 建立 imagemap 可點擊區域 action。imagemap 本身仍由 LINE SDK message object 序列化。
/// </summary>
public static class LineImagemapActionFactory
{
    public static IImagemapAction Message(string text, int x, int y, int width, int height, string? label = null)
        => new MessageImagemapAction(
            LineMessageFactoryValidation.Area(x, y, width, height),
            LineMessageFactoryValidation.Required(text, nameof(text), "Imagemap message text is required."),
            label!);

    public static IImagemapAction Uri(string linkUri, int x, int y, int width, int height, string? label = null)
        => new UriImagemapAction(
            LineMessageFactoryValidation.Area(x, y, width, height),
            LineMessageFactoryValidation.ActionUri(linkUri, nameof(linkUri), "Imagemap link URI is required."),
            label!);
}
