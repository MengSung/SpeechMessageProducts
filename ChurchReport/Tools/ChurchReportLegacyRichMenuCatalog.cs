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
    public const string LegacyAuthMenuKey = "legacy-auth";

    private const string LegacyAuthAliasId = "churchreport-legacy-auth";

    private const string LegacyImagePath = @"D:\暫存區\richmenu.PNG";

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
