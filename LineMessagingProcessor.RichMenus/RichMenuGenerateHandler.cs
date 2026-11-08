/*
 * 檔案整體說明
 *
 * 目的與定位：P2-08的單筆多語宮格待辦處理器及純值版面契約；由Hosting worker的工作scope
 * 注入已授權的完整文字範圍、正式教會代碼、啟用語系與八個產品版面，不以語言代表授權。
 * 主要功能與流程：先完成全部取字、alias、chatBar及版面候選，再讀provider正式ID與容量；
 * 容量不足或身分衝突不得建立或上傳。逐張產圖、上傳完成才發布alias，失敗補償本次自有資料。
 * 資源擁有權：handler只存續於單筆scope，不共用教會資料；借用的catalog、processor與renderer
 * 由scope擁有，不在本檔Dispose。每張PNG由handler確定性Dispose；不設cache、timer或背景Task。
 * 現有processor無取消參數，scope必須設定有界HTTP timeout；呼叫一律await完成再清理，
 * 取消在API邊界檢查，不用WaitAsync遺棄傳輸。真正失敗原樣交worker共用告警，不在這裡另建通知器。
 * 驗證：離線同名測試保護啟用語系、字典覆寫、容量、偽語系alias、重複正式ID與故障清理，
 * 不把離線圖檔當成LINE或D365驗收。產品仍負責版面業務內容與既有使用者的換綁生命週期。
 */
using Line.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using SpeechMessage.Hosting.Outbound;
using SpeechMessage.Localization;
using System.Security.Cryptography;
using System.Text;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 一個產品格子的不可變配置；位置只代表版面，不是資料庫列身分。
/// 工廠由已授權產品提供，只在本次同步準備時呼叫，產出的動作會由handler複製後才發布。
/// </summary>
/// <param name="X">左界像素。</param><param name="Y">上界像素。</param>
/// <param name="Width">格寬。</param><param name="Height">格高。</param>
/// <param name="TextKey">圖面取字鍵。</param>
/// <param name="CreateAction">以該語系完整文字建立本次操作自有動作。</param>
public sealed record RichMenuCellTemplate(int X, int Y, int Width, int Height,
    TextKey TextKey, Func<string, ITemplateAction> CreateAction);

/// <summary>單一穩定組合／頁籤配置；handler會複製Cells，不持有caller可變清單。</summary>
/// <param name="Combination">伺服器組合代碼。</param><param name="Tab">伺服器頁籤代碼。</param>
/// <param name="ChatBarKey">選單列文字鍵。</param><param name="Long">是否2500×1686長版。</param>
/// <param name="Cells">一至二十格的產品版面。</param>
public sealed record RichMenuPageTemplate(string Combination, string Tab, TextKey ChatBarKey,
    bool Long, IReadOnlyList<RichMenuCellTemplate> Cells);

/// <summary>單張已取字的純值繪圖格；不保存SDK或request參照。</summary>
/// <param name="X">左界。</param><param name="Y">上界。</param>
/// <param name="Width">寬度。</param><param name="Height">高度。</param>
/// <param name="Text">只在此次繪圖持有的完整文字。</param>
public sealed record RichMenuImageLabel(int X, int Y, int Width, int Height, string Text);

/// <summary>單張PNG的產生邊界；成功的stream所有權移交handler，失敗必須自行清理。</summary>
public interface IRichMenuImageRenderer
{
    /// <summary>固定字型、配色及繪圖版本識別；必須隨輸出政策修改，不得包含教會或使用者資料。</summary>
    string ProfileKey { get; }

    /// <summary>產生單張有界PNG；呼叫端必須Dispose回傳stream，不保存輸入或啟動背景工作。</summary>
    Task<Stream> RenderAsync(ImagemapSize size, IReadOnlyList<RichMenuImageLabel> labels,
        CancellationToken cancellationToken);
}

/// <summary>每筆scope專用的多語產生處理器，真正失敗由OutboundTaskWorker統一處理。</summary>
public sealed class RichMenuGenerateHandler : IOutboundTaskHandler
{
    private readonly TextContext _context;
    private readonly string _churchCode;
    private readonly string[] _cultures;
    private readonly RichMenuPageTemplate[] _pages;
    private readonly ITextCatalog _catalog;
    private readonly ILineRichMenuProcessor _processor;
    private readonly IRichMenuImageRenderer _renderer;

    /// <summary>建立單筆已授權工作；不自行查找credential或推論呼叫端權限。</summary>
    public RichMenuGenerateHandler(TextContext context, string churchCode, IReadOnlyList<string> enabledCultures,
        IReadOnlyList<RichMenuPageTemplate> pages, ITextCatalog catalog,
        ILineRichMenuProcessor processor, IRichMenuImageRenderer renderer)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _churchCode = churchCode;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        ArgumentNullException.ThrowIfNull(enabledCultures);
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count != 8 || enabledCultures.Count is < 1 or > 64)
            throw new ArgumentException("Eight pages and bounded enabled cultures are required.");
        _cultures = enabledCultures.ToArray();
        _pages = pages.Select(page => page with { Cells = page.Cells.ToArray() }).ToArray();
        if (_cultures.Distinct(StringComparer.OrdinalIgnoreCase).Count() != _cultures.Length ||
            string.IsNullOrWhiteSpace(renderer.ProfileKey) || renderer.ProfileKey.Length > 256)
            throw new ArgumentException("Generation configuration is ambiguous.");
    }

    /// <summary>建立固定格式alias；超長拒絕，不能截斷造成跨教會或頁籤碰撞。</summary>
    public static string BuildAlias(string churchCode, string combination, string tab, string culture)
    {
        var suffix = string.Equals(culture, "qps-ploc", StringComparison.OrdinalIgnoreCase) ? "qps" : culture;
        foreach (var part in new[] { churchCode, combination, tab, suffix })
            if (string.IsNullOrEmpty(part) || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
                throw new ArgumentException("Rich menu alias components are invalid.");
        var result = string.Join("-", churchCode, combination, tab, suffix);
        if (result.Length > 32) throw new ArgumentException("Rich menu alias exceeds 32 characters.");
        return result;
    }

    /// <summary>完整await本次工作；正常取消仍須清理本次自有遠端候選與圖片。</summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plans = Prepare();
        var existing = await _processor.GetRichMenuListAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (existing is null || existing.Count > 1000)
            throw new InvalidOperationException("Provider menu collection is not bounded.");
        var byId = new Dictionary<string, ResponseRichMenu>(StringComparer.Ordinal);
        foreach (var menu in existing)
        {
            if (menu is null || !ValidProviderId(menu.RichMenuId) || !byId.TryAdd(menu.RichMenuId, menu))
                throw new InvalidOperationException("Provider menu identity is missing or duplicated.");
        }
        var response = await _processor.GetRichMenuAliasListAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        if (response?.Aliases is null || response.Aliases.Count > 1000)
            throw new InvalidOperationException("Provider alias collection is not bounded.");
        foreach (var alias in response.Aliases)
        {
            if (alias is null || string.IsNullOrEmpty(alias.RichMenuAliasId) || alias.RichMenuAliasId.Length > 32 ||
                !ValidProviderId(alias.RichMenuId) || !aliases.TryAdd(alias.RichMenuAliasId, alias.RichMenuId))
                throw new InvalidOperationException("Provider alias identity is missing or duplicated.");
        }

        // 只由alias指向的正式ID判斷已發布版本；不依顯示名稱GroupBy、取第一筆或合併合法記錄。
        // 既有不同版本需要產品換綁流程，不能在這個純產生包暗中換alias而遺留直接綁定的使用者。
        var pending = new List<Prepared>();
        foreach (var plan in plans)
        {
            if (!aliases.TryGetValue(plan.Alias, out var id)) pending.Add(plan);
            else if (!byId.TryGetValue(id, out var menu) || menu.Name != plan.Menu.Name)
                throw new InvalidOperationException("Existing rich menu alias requires the product migration workflow.");
        }
        if (existing.Count + pending.Count > 1000)
            throw new InvalidOperationException("Rich menu channel capacity is insufficient.");

        var created = new List<(string Id, string Alias, bool AliasAttempted)>(pending.Count);
        try
        {
            foreach (var plan in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var png = await _renderer.RenderAsync(plan.Menu.Size, plan.Labels, cancellationToken)
                    .ConfigureAwait(false);
                if (png is null || !png.CanSeek || png.Length is < 1 or > 1024 * 1024)
                    throw new InvalidOperationException("Rendered PNG exceeds its bounded contract.");
                png.Position = 0;
                var id = await _processor.CreateRichMenuAsync(plan.Menu).ConfigureAwait(false);
                if (!ValidProviderId(id) || byId.ContainsKey(id) || created.Any(item => item.Id == id))
                    throw new InvalidOperationException("Created provider identity is invalid or reused.");
                created.Add((id, plan.Alias, false));
                cancellationToken.ThrowIfCancellationRequested();
                await _processor.UploadRichMenuPngImageAsync(id, png).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                created[^1] = (id, plan.Alias, true);
                await _processor.CreateRichMenuAliasAsync(id, plan.Alias).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            // 補償不受已取消的caller token中斷；仍受scope的HTTP timeout約束，且只刪本次自有ID。
            foreach (var item in created.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.AliasAttempted)
                    {
                        try
                        {
                            var current = await _processor.GetRichMenuAliasAsync(item.Alias).ConfigureAwait(false);
                            if (current.RichMenuId != item.Id)
                                throw new InvalidOperationException("Alias ownership changed during compensation.");
                            await _processor.DeleteRichMenuAliasAsync(item.Alias).ConfigureAwait(false);
                        }
                        catch (LineRichMenuAliasNotFoundException) { /* 未發佈，無須刪除其他alias。 */ }
                    }
                    await _processor.DeleteRichMenuAsync(item.Id).ConfigureAwait(false);
                }
                catch (Exception cleanup) { errors.Add(cleanup); }
            }
            if (errors.Count > 1) throw new AggregateException("Rich menu compensation requires attention.", errors);
            throw;
        }
    }

    /// <summary>建立完整候選才開始I/O；長文字及格子越界在SDK靜默截斷前fail closed。</summary>
    private List<Prepared> Prepare()
    {
        var result = new List<Prepared>(_cultures.Length * 8);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in _cultures)
        {
            var context = _context.WithCultures(culture, culture == "qps-ploc" ? "en-US" : culture);
            foreach (var page in _pages)
            {
                var alias = BuildAlias(_churchCode, page.Combination, page.Tab, culture);
                if (!ids.Add(alias) || page.Cells.Count is < 1 or > 20)
                    throw new ArgumentException("Page identity or cell count is invalid.");
                var chat = _catalog.GetText(context, page.ChatBarKey);
                if (string.IsNullOrWhiteSpace(chat) || chat.Length > 14)
                    throw new ArgumentException("Rich menu chat bar text exceeds 14 characters.");
                var size = new ImagemapSize(2500, page.Long ? 1686 : 843);
                var areas = new List<ActionArea>(page.Cells.Count);
                var labels = new List<RichMenuImageLabel>(page.Cells.Count);
                foreach (var cell in page.Cells)
                {
                    if (cell.X < 0 || cell.Y < 0 || cell.Width <= 0 || cell.Height <= 0 ||
                        (long)cell.X + cell.Width > size.Width || (long)cell.Y + cell.Height > size.Height)
                        throw new ArgumentException("Rich menu cell is outside its canvas.");
                    var text = _catalog.GetText(context, cell.TextKey);
                    if (string.IsNullOrWhiteSpace(text) || text.Length > 256)
                        throw new ArgumentException("Rich menu label exceeds its bounded contract.");
                    var action = cell.CreateAction(text) ?? throw new ArgumentException("Cell action is required.");
                    var json = JsonConvert.SerializeObject(action, new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver(),
                        Converters = { new StringEnumConverter() }
                    });
                    if (json.Length > 8192) throw new ArgumentException("Cell action exceeds its bounded contract.");
                    // SDK模型可變，將factory輸出分離，不能把caller後續mutation帶入HTTP。
                    areas.Add(new ActionArea { Bounds = new ImagemapArea(cell.X, cell.Y, cell.Width, cell.Height),
                        Action = ActionArea.ParseTemplateAction(JObject.Parse(json)) });
                    labels.Add(new RichMenuImageLabel(cell.X, cell.Y, cell.Width, cell.Height, text));
                }
                var menu = new RichMenu { Size = size, ChatBarText = chat, Areas = areas, Selected = false };
                var version = JsonConvert.SerializeObject(new { alias, menu, labels, _renderer.ProfileKey });
                menu.Name = "p208:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version)));
                result.Add(new Prepared(alias, menu, labels));
            }
        }
        return result;
    }

    /// <summary>正式providerID必須符合LINE固定格式；位置、名稱、事件鍵都不能取代它。</summary>
    private static bool ValidProviderId(string? id) => id is { Length: 41 } &&
        id.StartsWith("richmenu-", StringComparison.Ordinal) && id.AsSpan(9).IndexOfAnyExcept("0123456789abcdef") < 0;

    /// <summary>完整驗證後的操作候選；只在本次Execute保存，不發布可變集合到快取。</summary>
    private sealed record Prepared(string Alias, RichMenu Menu, IReadOnlyList<RichMenuImageLabel> Labels);
}
