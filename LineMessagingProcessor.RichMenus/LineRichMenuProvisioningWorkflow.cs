using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 將產品提供的 RichMenu catalog 同步到 LINE。
/// 每個定義彼此獨立；單一選單建立、上傳、alias 或 default 設定失敗時，只記錄 Failed item，
/// 不讓整批同步中斷，讓管理端可以一次看見完整同步結果。
/// </summary>
public sealed class LineRichMenuProvisioningWorkflow : ILineRichMenuProvisioningWorkflow
{
    private readonly ILineRichMenuCatalog _catalog;
    private readonly ILineRichMenuProcessor _processor;
    private readonly ILineRichMenuIdCache _cache;

    public LineRichMenuProvisioningWorkflow(
        ILineRichMenuCatalog catalog,
        ILineRichMenuProcessor processor,
        ILineRichMenuIdCache cache)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<LineRichMenuSyncReport> SyncAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _catalog.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var existingMenus = await _processor.GetRichMenuListAsync().ConfigureAwait(false);
        var existingByName = existingMenus
            .Where(menu => !string.IsNullOrWhiteSpace(menu.Name))
            .GroupBy(menu => menu.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var menuIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var created = new List<string>();
        var reused = new List<string>();
        var items = new List<LineRichMenuSyncItem>();

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await SyncDefinitionAsync(
                    definition,
                    existingByName,
                    menuIds,
                    created,
                    reused,
                    items,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                items.Add(new LineRichMenuSyncItem(
                    definition.MenuKey,
                    string.Empty,
                    LineRichMenuSyncOutcome.Failed,
                    ex.Message));
            }
        }

        return new LineRichMenuSyncReport(menuIds, created, reused, Array.Empty<string>(), items);
    }

    private async Task SyncDefinitionAsync(
        LineRichMenuDefinition definition,
        IReadOnlyDictionary<string, ResponseRichMenu> existingByName,
        IDictionary<string, string> menuIds,
        ICollection<string> created,
        ICollection<string> reused,
        ICollection<LineRichMenuSyncItem> items,
        CancellationToken cancellationToken)
    {
        await using var imageStream = await definition.PngImageStreamFactory(cancellationToken).ConfigureAwait(false);
        if (imageStream == null)
        {
            throw new InvalidOperationException($"RichMenu '{definition.MenuKey}' image stream factory returned null.");
        }

        var imageBytes = await ReadAllBytesAsync(imageStream, cancellationToken).ConfigureAwait(false);
        var fingerprint = LineRichMenuFingerprint.Create(definition.RichMenu, imageBytes);
        var versionedName = LineRichMenuFingerprint.BuildName(definition, fingerprint);

        if (existingByName.TryGetValue(versionedName, out var existing))
        {
            await UpsertAliasAsync(definition.AliasId, existing.RichMenuId).ConfigureAwait(false);
            if (definition.IsDefault)
            {
                await _processor.SetDefaultRichMenuAsync(existing.RichMenuId).ConfigureAwait(false);
            }

            _cache.Set(definition.MenuKey, existing.RichMenuId);
            menuIds[definition.MenuKey] = existing.RichMenuId;
            reused.Add(definition.MenuKey);
            items.Add(new LineRichMenuSyncItem(definition.MenuKey, existing.RichMenuId, LineRichMenuSyncOutcome.UpToDate));
            return;
        }

        var richMenu = CloneForProvisioning(definition.RichMenu, versionedName);
        var richMenuId = await _processor.CreateRichMenuAsync(richMenu).ConfigureAwait(false);
        await using var uploadStream = new MemoryStream(imageBytes, writable: false);
        await _processor.UploadRichMenuPngImageAsync(richMenuId, uploadStream).ConfigureAwait(false);
        await UpsertAliasAsync(definition.AliasId, richMenuId).ConfigureAwait(false);

        if (definition.IsDefault)
        {
            await _processor.SetDefaultRichMenuAsync(richMenuId).ConfigureAwait(false);
        }

        _cache.Set(definition.MenuKey, richMenuId);
        menuIds[definition.MenuKey] = richMenuId;
        created.Add(definition.MenuKey);
        items.Add(new LineRichMenuSyncItem(definition.MenuKey, richMenuId, LineRichMenuSyncOutcome.Created));
    }

    private async Task UpsertAliasAsync(string aliasId, string richMenuId)
    {
        try
        {
            await _processor.GetRichMenuAliasAsync(aliasId).ConfigureAwait(false);
            await _processor.UpdateRichMenuAliasAsync(aliasId, richMenuId).ConfigureAwait(false);
        }
        catch (LineRichMenuAliasNotFoundException)
        {
            await _processor.CreateRichMenuAliasAsync(richMenuId, aliasId).ConfigureAwait(false);
        }
        catch (LineResponseException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await _processor.CreateRichMenuAliasAsync(richMenuId, aliasId).ConfigureAwait(false);
        }
    }

    private static RichMenu CloneForProvisioning(RichMenu source, string name)
        => new()
        {
            Size = source.Size,
            Selected = source.Selected,
            Name = name,
            ChatBarText = source.ChatBarText,
            Areas = source.Areas
        };

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream memoryStream)
        {
            return memoryStream.ToArray();
        }

        await using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        return copy.ToArray();
    }
}
