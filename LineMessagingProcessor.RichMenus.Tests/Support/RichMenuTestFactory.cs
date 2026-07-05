// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus.Tests/Support/RichMenuTestFactory.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class RichMenuTestFactory
// 主要成員：CreateMenu、CreatePngBytes
// 引用命名空間：Line.Messaging
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
