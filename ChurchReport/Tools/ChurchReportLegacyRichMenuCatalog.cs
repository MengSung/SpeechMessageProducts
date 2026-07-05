using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.RichMenus;

namespace ChurchReport.Tools;

/// <summary>
/// ChurchReport 既有單鈕認證 RichMenu 的產品目錄。
/// 共用 RichMenu 專案只負責「依 catalog 佈建與指派」，不應知道 ChurchReport 的舊圖檔位置、
/// 按鈕文字或認證用途；因此這份 catalog 留在 ChurchReport 產品層。
/// </summary>
public sealed class ChurchReportLegacyRichMenuCatalog : ILineRichMenuCatalog
{
    /// <summary>
    /// 共用 RichMenu 工作流用來代表既有認證選單的產品層 menu key。
    /// </summary>
    public const string LegacyAuthMenuKey = "legacy-auth";

    /// <summary>
    /// LINE RichMenu alias 的穩定識別碼，供切換動作與佈建流程共用。
    /// </summary>
    private const string LegacyAuthAliasId = "churchreport-legacy-auth";

    /// <summary>
    /// ChurchReport 既有 RichMenu 佈署使用的 PNG 檔案路徑。
    /// 將路徑集中在 catalog 內，未來改成內嵌資源或設定檔時，不必改動共用工作流。
    /// </summary>
    private const string LegacyImagePath = @"D:\暫存區\richmenu.PNG";

    /// <summary>
    /// 回傳單一既有 RichMenu 定義，讓舊 ChurchReport 選單能接到共用工作流。
    /// </summary>
    /// <param name="cancellationToken">
    /// 目前未使用；此 catalog 是靜態定義，只有 provisioning workflow 開啟 stream 時才讀取圖片。
    /// </param>
    public Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LineRichMenuDefinition> definitions = new[]
        {
            new LineRichMenuDefinition(
                LegacyAuthMenuKey,
                LegacyAuthAliasId,
                CreateLegacySingleButtonRichMenu(),
                _ => Task.FromResult<Stream>(File.OpenRead(LegacyImagePath)))
            {
                Description = "ChurchReport 既有單鈕認證選單；保留舊畫面與使用者可見行為。",
            },
        };

        return Task.FromResult(definitions);
    }

    /// <summary>
    /// 建立既有單一按鈕 RichMenu 版面。
    /// action 覆蓋整張長版 RichMenu 圖片，使用者點任意位置都會送出舊版 postback payload。
    /// </summary>
    private static RichMenu CreateLegacySingleButtonRichMenu()
    {
        return new RichMenu
        {
            Size = ImagemapSize.RichMenuLong,
            Selected = false,
            Name = "ChurchReport legacy auth",
            ChatBarText = "touch me",
            Areas = new List<ActionArea>
            {
                new()
                {
                    Bounds = new ImagemapArea(
                        0,
                        0,
                        ImagemapSize.RichMenuLong.Width,
                        ImagemapSize.RichMenuLong.Height),
                    Action = new PostbackTemplateAction("ButtonA", "Menu A", "Menu A"),
                },
            },
        };
    }
}
