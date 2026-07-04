using Line.Messaging;

namespace LineMessagingProcessor.RichMenus.Tests.Support;

internal static class RichMenuTestFactory
{
    public static RichMenu CreateMenu(string name = "member-main")
    {
        return new RichMenu
        {
            Size = ImagemapSize.RichMenuLong,
            Selected = false,
            Name = name,
            ChatBarText = "open",
            Areas = new List<ActionArea>
            {
                new()
                {
                    Bounds = new ImagemapArea(0, 0, ImagemapSize.RichMenuLong.Width, ImagemapSize.RichMenuLong.Height),
                    Action = new MessageTemplateAction("Open", "OPEN")
                }
            }
        };
    }

    public static Func<CancellationToken, Task<Stream>> CreatePngFactory(byte seed = 1)
    {
        return _ => Task.FromResult<Stream>(new MemoryStream(CreatePngBytes(seed)));
    }

    public static byte[] CreatePngBytes(byte seed = 1)
        => new[] { seed, (byte)(seed + 1), (byte)(seed + 2) };
}
