using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu 使用者指派工作流。
///
/// 這個類別是未來多個產品共用 RichMenu 能力的核心入口之一。
/// 產品層只需要傳入「想指派的邏輯選單代號」<c>menuKey</c>，
/// 共用層會負責把它解析成 LINE 平台實際使用的 <c>richMenuId</c>，
/// 再透過 <see cref="ILineRichMenuProcessor"/> 呼叫 LINE RichMenu API。
///
/// 設計邊界：
/// 1. 這裡只處理 RichMenu 指派與解除指派，不處理特定產品的身分資料、業務流程、畫面或通知文字。
/// 2. <see cref="ILineRichMenuCatalog"/> 由產品提供，負責描述產品有哪些 RichMenu；共用層只讀取目錄。
/// 3. <see cref="IRichMenuStateStore"/> 只是本流程的輔助紀錄，不是 LINE 平台狀態的唯一真相來源。
/// 4. LINE / HTTP / timeout 錯誤會轉成標準 <see cref="LineRichMenuAssignmentResult"/>，
///    讓產品層用一致方式判斷失敗，而不是被迫捕捉各種底層例外。
///
/// 這樣切割後，建設公司維修系統、協會會員系統、發票收款系統等未來產品，
/// 只要提供自己的 catalog / policy / state store，就能共用同一套 RichMenu 指派流程。
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

        var richMenuResolution = await ResolveRichMenuIdAsync(key, cancellationToken).ConfigureAwait(false);
        if (richMenuResolution.ProviderFailure != null)
        {
            return richMenuResolution.ProviderFailure;
        }

        var richMenuId = richMenuResolution.RichMenuId;
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
            return LineRichMenuAssignmentResult.Linked(
                previous?.PreviousMenuKey,
                key,
                richMenuId,
                changed: false);
        }

        // 只有真正跨出本機流程、送到 LINE 平台的 link 呼叫，才可以被轉成 provider failure。
        //
        // 這個邊界非常重要：state store 是產品可以替換的本機輔助儲存，
        // 例如記憶體、資料庫、Redis 或任何未來產品自己的實作。
        // 如果 state store 寫入失敗，代表本機資料一致性出問題，不是 LINE provider 拒絕或斷線；
        // 因此不能包在同一個 try/catch 裡偽裝成 ProviderUnavailable。
        var providerFailure = await TryExecuteProviderActionAsync(
            () => _processor.LinkRichMenuToUserAsync(userId, richMenuId)).ConfigureAwait(false);
        if (providerFailure != null)
        {
            return providerFailure;
        }

        await _stateStore.SetAsync(
            new RichMenuUserState(
                userId,
                key,
                previous?.CurrentMenuKey,
                expiresAt: null,
                updatedAt: DateTimeOffset.UtcNow),
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

        // LINE 平台才是使用者目前 RichMenu 綁定狀態的唯一真相來源。
        //
        // 如果 state store 查不到資料就直接回傳 no-op，真實產品會有狀態漂移風險：
        // - 應用程式重啟後，InMemory state store 可能已清空。
        // - 未來產品可能用多台主機、背景服務或不同 state store 實作。
        // - LINE 端可能仍保留舊 RichMenu 綁定，但本機輔助紀錄已不存在。
        //
        // 因此解除綁定一律呼叫 LINE unlink，再清除本機輔助紀錄。
        // changed=true 表示本流程已向 LINE 發出解除命令，不表示本機事前一定有紀錄。
        var providerFailure = await TryExecuteProviderActionAsync(
            () => _processor.UnlinkRichMenuFromUserAsync(userId)).ConfigureAwait(false);
        if (providerFailure != null)
        {
            return providerFailure;
        }

        await _stateStore.RemoveAsync(userId, cancellationToken).ConfigureAwait(false);
        return LineRichMenuAssignmentResult.Unlinked(previous?.CurrentMenuKey, changed: true);
    }

    public async Task UnassignOrThrowAsync(string lineUserId, CancellationToken cancellationToken = default)
    {
        var result = await UnassignAsync(lineUserId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineRichMenuException(result);
        }
    }

    private async Task<(string? RichMenuId, LineRichMenuAssignmentResult? ProviderFailure)> ResolveRichMenuIdAsync(
        string menuKey,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGet(menuKey, out var cachedRichMenuId))
        {
            return (cachedRichMenuId, null);
        }

        if (_catalog == null)
        {
            return (null, null);
        }

        var definitions = await _catalog.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var definition = definitions.FirstOrDefault(item =>
            string.Equals(item.MenuKey, menuKey, StringComparison.OrdinalIgnoreCase));
        if (definition == null)
        {
            return (null, null);
        }

        await using var imageStream = await definition.PngImageStreamFactory(cancellationToken).ConfigureAwait(false);
        if (imageStream == null)
        {
            return (null, null);
        }

        var imageBytes = await ReadAllBytesAsync(imageStream, cancellationToken).ConfigureAwait(false);
        var expectedName = LineRichMenuFingerprint.BuildName(definition, imageBytes);

        // cache miss 時需要向 LINE 查詢線上 RichMenu 清單，這也是 provider 邊界。
        //
        // 這裡只包住 GetRichMenuListAsync 這個真正跨平台的呼叫；
        // catalog 讀取、圖片 stream 產生、指紋計算與 cache 寫入都仍然是本機流程，
        // 如果它們失敗，應該直接往外拋，避免把產品自己的目錄或檔案錯誤誤判成 LINE 失敗。
        var onlineMenusResult = await TryExecuteProviderQueryAsync(
            () => _processor.GetRichMenuListAsync()).ConfigureAwait(false);
        if (onlineMenusResult.Failure != null)
        {
            return (null, onlineMenusResult.Failure);
        }

        var onlineMenus = onlineMenusResult.Value ?? Array.Empty<ResponseRichMenu>();
        var matched = onlineMenus.FirstOrDefault(menu =>
            string.Equals(menu.Name, expectedName, StringComparison.OrdinalIgnoreCase));
        if (matched == null || string.IsNullOrWhiteSpace(matched.RichMenuId))
        {
            return (null, null);
        }

        _cache.Set(menuKey, matched.RichMenuId);
        return (matched.RichMenuId, null);
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

    private static async Task<LineRichMenuAssignmentResult?> TryExecuteProviderActionAsync(Func<Task> providerAction)
    {
        try
        {
            await providerAction().ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (TryMapProviderException(ex, out var result))
        {
            return result;
        }
    }

    private static async Task<(T? Value, LineRichMenuAssignmentResult? Failure)> TryExecuteProviderQueryAsync<T>(
        Func<Task<T>> providerQuery)
    {
        try
        {
            return (await providerQuery().ConfigureAwait(false), null);
        }
        catch (Exception ex) when (TryMapProviderException(ex, out var result))
        {
            return (default, result);
        }
    }

    private static bool TryMapProviderException(Exception exception, out LineRichMenuAssignmentResult result)
    {
        switch (exception)
        {
            case LineResponseException lineResponseException:
                result = LineRichMenuAssignmentResult.Failure(
                    LineRichMenuStatus.ProviderRejected,
                    "line-richmenu-provider-rejected",
                    lineResponseException.Message);
                return true;

            case HttpRequestException httpRequestException:
                result = LineRichMenuAssignmentResult.Failure(
                    LineRichMenuStatus.ProviderUnavailable,
                    "line-richmenu-provider-unavailable",
                    httpRequestException.Message);
                return true;

            case TaskCanceledException taskCanceledException
                when !taskCanceledException.CancellationToken.IsCancellationRequested:
                result = LineRichMenuAssignmentResult.Failure(
                    LineRichMenuStatus.ProviderUnavailable,
                    "line-richmenu-provider-timeout",
                    taskCanceledException.Message);
                return true;

            case TimeoutException timeoutException:
                result = LineRichMenuAssignmentResult.Failure(
                    LineRichMenuStatus.ProviderUnavailable,
                    "line-richmenu-provider-timeout",
                    timeoutException.Message);
                return true;

            default:
                // 這裡刻意不把所有 Exception 都轉成失敗結果。
                //
                // RichMenu 共用層只應該把「LINE 平台或網路傳輸」這類產品可處理的外部錯誤
                // 標準化成 LineRichMenuAssignmentResult；程式錯誤、資料流錯誤、未知狀態
                // 必須直接往外拋，讓測試、監控與呼叫端能看見真正的 bug。
                result = null!;
                return false;
        }
    }
}
