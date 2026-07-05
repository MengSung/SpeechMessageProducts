// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineRichMenuAssignmentWorkflow
// 主要成員：AssignAsync、AssignOrThrowAsync、UnassignAsync、UnassignOrThrowAsync、NormalizeRequired、ReadAllBytesAsync、TryExecuteProviderActionAsync、TryMapProviderException
// 引用命名空間：Line.Messaging
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
    /// <summary>
    /// 對 LINE RichMenu API 的抽象；所有 provider link / unlink / list 呼叫都從這裡出去。
    /// </summary>
    private readonly ILineRichMenuProcessor _processor;

    /// <summary>
    /// menuKey 到 LINE richMenuId 的快取，避免每次指派都查詢 LINE 遠端清單。
    /// </summary>
    private readonly ILineRichMenuIdCache _cache;

    /// <summary>
    /// 本機輔助狀態，用來記錄使用者目前與前一個 menuKey，支援未來還原與到期掃描。
    /// </summary>
    private readonly IRichMenuStateStore _stateStore;

    /// <summary>
    /// 選用 catalog。當快取找不到 menuKey 時，workflow 會用 catalog definition 推算 fingerprint，
    /// 再到 LINE 線上 RichMenu 清單尋找已佈建的同版選單。
    /// </summary>
    private readonly ILineRichMenuCatalog? _catalog;

    /// <summary>
    /// 建立完整指派工作流。
    /// </summary>
    /// <param name="processor">LINE RichMenu API 抽象。</param>
    /// <param name="cache">menuKey 到 richMenuId 的快取。</param>
    /// <param name="stateStore">使用者 RichMenu 狀態儲存。</param>
    /// <param name="catalog">可選 catalog，用於 cache miss 時嘗試線上解析 richMenuId。</param>
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

    /// <summary>
    /// 建立使用 in-memory state store 的簡化指派工作流。
    /// </summary>
    /// <param name="processor">LINE RichMenu API 抽象。</param>
    /// <param name="cache">menuKey 到 richMenuId 的快取。</param>
    public LineRichMenuAssignmentWorkflow(
        ILineRichMenuProcessor processor,
        ILineRichMenuIdCache cache)
        : this(processor, cache, new InMemoryRichMenuStateStore())
    {
    }

    /// <summary>
    /// 將指定 menuKey 對應的 RichMenu 指派給 LINE 使用者。
    /// </summary>
    /// <param name="lineUserId">LINE 使用者 id。</param>
    /// <param name="menuKey">產品端邏輯選單代號。</param>
    /// <param name="cancellationToken">取消權杖，用於 catalog 與 state store 操作。</param>
    /// <returns>標準化指派結果，包含 richMenuId、前一個選單與是否真的變更。</returns>
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

    /// <summary>
    /// 執行 <see cref="AssignAsync"/>，並在失敗時丟出 <see cref="LineRichMenuException"/>。
    /// </summary>
    /// <param name="lineUserId">LINE 使用者 id。</param>
    /// <param name="menuKey">產品端邏輯選單代號。</param>
    /// <param name="cancellationToken">取消權杖。</param>
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

    /// <summary>
    /// 解除使用者的 LINE RichMenu 個人綁定，並移除本機輔助狀態。
    /// </summary>
    /// <param name="lineUserId">LINE 使用者 id。</param>
    /// <param name="cancellationToken">取消權杖，用於 state store 操作。</param>
    /// <returns>標準化解除結果，包含前一個 menuKey 與 provider 錯誤分類。</returns>
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

    /// <summary>
    /// 執行 <see cref="UnassignAsync"/>，並在失敗時丟出 <see cref="LineRichMenuException"/>。
    /// </summary>
    /// <param name="lineUserId">LINE 使用者 id。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    public async Task UnassignOrThrowAsync(string lineUserId, CancellationToken cancellationToken = default)
    {
        var result = await UnassignAsync(lineUserId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineRichMenuException(result);
        }
    }

    /// <summary>
    /// 解析 menuKey 對應的 LINE richMenuId。
    /// 解析順序是快取優先，其次才用 catalog fingerprint 到 LINE 線上清單尋找已佈建選單。
    /// </summary>
    /// <param name="menuKey">產品端邏輯選單代號。</param>
    /// <param name="cancellationToken">取消權杖，用於 catalog 與圖片 stream 讀取。</param>
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

    /// <summary>
    /// 驗證並修剪必要字串，避免空白 userId 或 menuKey 進入 provider 呼叫。
    /// </summary>
    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim();
    }

    /// <summary>
    /// 將任意可讀取 stream 複製為 byte array，以便用於 fingerprint 計算。
    /// </summary>
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

    /// <summary>
    /// 包住單一 LINE provider action，並只把已知 provider 邊界錯誤轉成標準結果。
    /// </summary>
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

    /// <summary>
    /// 包住單一 LINE provider query，並在 provider 失敗時回傳標準失敗結果。
    /// </summary>
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

    /// <summary>
    /// 將 LINE / HTTP / timeout 例外轉成產品層可理解的標準指派結果。
    /// 非 provider 類型的未知例外會回傳 false，讓原始例外直接往外拋。
    /// </summary>
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
