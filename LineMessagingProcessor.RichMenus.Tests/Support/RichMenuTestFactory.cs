using Line.Messaging;

namespace LineMessagingProcessor.RichMenus.Tests.Support;

/// <summary>
/// 建立 RichMenu 測試資料的集中 factory。
/// 測試透過這個 helper 取得一致的版面、action area 與 PNG bytes，避免每個測試各自手寫不一致的 RichMenu payload。
/// </summary>
internal static class RichMenuTestFactory
{
    /// <summary>
    /// 建立一個可供 provisioning、assignment 與 workflow 測試共用的基本 RichMenu。
    /// </summary>
    /// <param name="name">RichMenu 名稱；測試會用它模擬一般名稱或 fingerprinted provider 名稱。</param>
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

    /// <summary>
    /// 建立 PNG stream factory，模擬 catalog definition 在 provisioning 時可重新開啟圖片來源。
    /// </summary>
    /// <param name="seed">用來產生穩定但可區分的測試 bytes。</param>
    public static Func<CancellationToken, Task<Stream>> CreatePngFactory(byte seed = 1)
    {
        return _ => Task.FromResult<Stream>(new MemoryStream(CreatePngBytes(seed)));
    }

    /// <summary>
    /// 建立穩定 PNG bytes，讓 fingerprint 測試可預期且不依賴真實圖片檔案。
    /// </summary>
    /// <param name="seed">第一個 byte，方便測試不同圖片內容會產生不同 fingerprint。</param>
    public static byte[] CreatePngBytes(byte seed = 1)
        => new[] { seed, (byte)(seed + 1), (byte)(seed + 2) };
}
