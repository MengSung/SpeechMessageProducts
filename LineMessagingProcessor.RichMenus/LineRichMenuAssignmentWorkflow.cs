namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu 指派工作流。
/// 這裡只負責把產品給的 menu key 解析成 LINE richMenuId，再執行 link / unlink。
/// 產品端的角色判斷、資料更新、畫面流程都不放在這裡，避免共用核心被任一產品綁死。
/// </summary>
public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWorkflow
{
    private readonly ILineRichMenuProcessor _processor;
    private readonly ILineRichMenuIdCache _cache;
    private readonly IRichMenuStateStore _stateStore;
    private readonly ILineRichMenuCatalog? _catalog;

    public LineRichMenuAssignmentWorkflow(
        ILineRichMenuProcessor processor,
        ILineRichMenuIdCache cache,
        IRichMenuStateStore stateStore,
        ILineRichMenuCatalog? catalog = null)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _catalog = catalog;
    }

    public LineRichMenuAssignmentWorkflow(
        ILineRichMenuProcessor processor,
        ILineRichMenuIdCache cache)
        : this(processor, cache, new InMemoryRichMenuStateStore())
    {
    }

    public async Task<LineRichMenuAssignmentResult> AssignAsync(
        string lineUserId,
        string menuKey,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeRequired(lineUserId, nameof(lineUserId));
        var key = NormalizeRequired(menuKey, nameof(menuKey));

        var richMenuId = await ResolveRichMenuIdAsync(key, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(richMenuId))
        {
            return LineRichMenuAssignmentResult.Failure(
                LineRichMenuStatus.ValidationFailed,
                "line-richmenu-menu-key-not-found",
                $"RichMenu id for menu key '{key}' was not provisioned or could not be found online.");
        }

        var previous = await _stateStore.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (string.Equals(previous?.CurrentMenuKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return LineRichMenuAssignmentResult.Linked(previous?.PreviousMenuKey, key, richMenuId, changed: false);
        }

        await _processor.LinkRichMenuToUserAsync(userId, richMenuId).ConfigureAwait(false);
        await _stateStore.SetAsync(
            new RichMenuUserState(userId, key, previous?.CurrentMenuKey, null, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        return LineRichMenuAssignmentResult.Linked(previous?.CurrentMenuKey, key, richMenuId, changed: true);
    }

    public async Task AssignOrThrowAsync(
        string lineUserId,
        string menuKey,
        CancellationToken cancellationToken = default)
    {
        var result = await AssignAsync(lineUserId, menuKey, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineRichMenuException(result);
        }
    }

    public async Task<LineRichMenuAssignmentResult> UnassignAsync(
        string lineUserId,
        CancellationToken cancellationToken = default)
    {
        var userId = NormalizeRequired(lineUserId, nameof(lineUserId));
        var previous = await _stateStore.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (previous == null)
        {
            return LineRichMenuAssignmentResult.Unlinked(null, changed: false);
        }

        await _processor.UnlinkRichMenuFromUserAsync(userId).ConfigureAwait(false);
        await _stateStore.RemoveAsync(userId, cancellationToken).ConfigureAwait(false);
        return LineRichMenuAssignmentResult.Unlinked(previous.CurrentMenuKey, changed: true);
    }

    public async Task UnassignOrThrowAsync(string lineUserId, CancellationToken cancellationToken = default)
    {
        var result = await UnassignAsync(lineUserId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineRichMenuException(result);
        }
    }

    private async Task<string?> ResolveRichMenuIdAsync(string menuKey, CancellationToken cancellationToken)
    {
        if (_cache.TryGet(menuKey, out var cachedRichMenuId))
        {
            return cachedRichMenuId;
        }

        if (_catalog == null)
        {
            return null;
        }

        var definitions = await _catalog.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var definition = definitions.FirstOrDefault(item =>
            string.Equals(item.MenuKey, menuKey, StringComparison.OrdinalIgnoreCase));
        if (definition == null)
        {
            return null;
        }

        await using var imageStream = await definition.PngImageStreamFactory(cancellationToken).ConfigureAwait(false);
        if (imageStream == null)
        {
            return null;
        }

        var imageBytes = await ReadAllBytesAsync(imageStream, cancellationToken).ConfigureAwait(false);
        var expectedName = LineRichMenuFingerprint.BuildName(definition, imageBytes);
        var onlineMenus = await _processor.GetRichMenuListAsync().ConfigureAwait(false);
        var matched = onlineMenus.FirstOrDefault(menu =>
            string.Equals(menu.Name, expectedName, StringComparison.OrdinalIgnoreCase));
        if (matched == null || string.IsNullOrWhiteSpace(matched.RichMenuId))
        {
            return null;
        }

        _cache.Set(menuKey, matched.RichMenuId);
        return matched.RichMenuId;
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim();
    }

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
