using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 將產品提供的 RichMenu catalog 同步到 LINE。
/// 每個定義彼此獨立；單一選單建立、上傳、alias 或 default 設定失敗時，只記錄 Failed item，
/// 不讓整批同步中斷，讓管理端可以一次看見完整同步結果。
/// </summary>
/// <remarks>
/// 保母級說明：
/// 這個 workflow 可以想成「把產品宣告的 RichMenu 藍圖，真正佈建到 LINE 後台」的流程。
/// 產品端只要提供 <see cref="ILineRichMenuCatalog"/>，也就是一份選單清單，這裡會負責：
/// 1. 讀取目前 LINE channel 上已存在的 RichMenu。
/// 2. 依照 RichMenu 版面與 PNG 圖片計算 fingerprint，產生可重複比對的 versioned name。
/// 3. 如果 LINE 上已有同名選單，就重用既有 richMenuId，避免重複建立。
/// 4. 如果 LINE 上沒有同名選單，就建立新 RichMenu、上傳圖片、維護 alias、設定 default。
/// 5. 將 menuKey 與 richMenuId 寫入快取，讓後續 assignment 可以直接依 menuKey 指派使用者。
///
/// 設計原則：
/// - LINE 平台是遠端真相來源；本類別不引入資料庫，也不保存產品身分資料。
/// - catalog 是產品提供的宣告式設定；共用層只讀取，不知道任何特定產品的業務語意。
/// - 每個 definition 獨立同步，避免一個壞選單讓整批佈建全部失敗。
/// - 不自動刪除 LINE 上未知的舊 RichMenu，避免誤刪仍在使用中的線上選單。
/// </remarks>
public sealed class LineRichMenuProvisioningWorkflow : ILineRichMenuProvisioningWorkflow
{
    // 產品提供的 RichMenu 目錄。它只描述「有哪些選單」與「圖片從哪裡來」，
    // 不負責實際呼叫 LINE API。
    private readonly ILineRichMenuCatalog _catalog;

    // 對 LINE RichMenu API 的抽象包裝。所有 Create / Upload / Alias / Default 操作
    // 都透過這個介面送到 LINE，方便測試時替換成 capture/fake processor。
    private readonly ILineRichMenuProcessor _processor;

    // channel 層級快取：menuKey -> richMenuId。
    // 注意這不是使用者狀態快取，只保存「某個邏輯選單目前對應 LINE 哪個 richMenuId」。
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
        // 第一步：從產品 catalog 取得所有要佈建的 RichMenu 定義。
        // catalog 可能從 appsettings、程式碼、內嵌資源或產品自己的設定來源組出來。
        var definitions = await _catalog.GetDefinitionsAsync(cancellationToken).ConfigureAwait(false);

        // 第二步：向 LINE 查詢目前 channel 內已存在的 RichMenu。
        // 後面會用「選單名稱」比對，而不是每次都盲目建立新選單。
        var existingMenus = await _processor.GetRichMenuListAsync().ConfigureAwait(false);

        // 將 LINE 回傳的選單依 Name 建成 dictionary，讓每個 catalog definition
        // 可以用 O(1) 方式查詢是否已經佈建過同版選單。
        //
        // 若 LINE 上剛好有重名選單，取第一個即可；正常流程下 versioned name 應該唯一。
        var existingByName = existingMenus
            .Where(menu => !string.IsNullOrWhiteSpace(menu.Name))
            .GroupBy(menu => menu.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        // 本次同步完成後要回報給呼叫端的結果集合。
        // menuIds：所有成功處理的 menuKey -> richMenuId。
        // created：本次新建立的 menuKey。
        // reused：LINE 上已存在、直接重用的 menuKey。
        // items：逐項同步結果，包含成功、已存在、失敗與錯誤訊息。
        var menuIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var created = new List<string>();
        var reused = new List<string>();
        var items = new List<LineRichMenuSyncItem>();

        foreach (var definition in definitions)
        {
            // 讓呼叫端可以中斷整批同步，例如管理端取消、背景工作停止或應用程式關閉。
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // 每個 definition 都獨立進入同步流程。
                // 這樣即使 A 選單圖片壞掉，B/C 選單仍然可以繼續佈建。
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
                // 取消是呼叫端明確要求停止，不應該被包成單一選單 Failed。
                throw;
            }
            catch (Exception ex)
            {
                // 這裡採「報告模式」而不是「丟出例外中斷整批」。
                // 管理端可以看到哪個 menuKey 失敗與失敗原因，再決定要修圖、修設定或重跑。
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
        // 讀取產品提供的 PNG 圖片資料。
        // 這裡使用 await using，確保不管後續成功或失敗，stream 都會被釋放。
        await using var imageStream = await definition.PngImageStreamFactory(cancellationToken).ConfigureAwait(false);
        if (imageStream == null)
        {
            // 圖片是 RichMenu 的必要素材；沒有圖片就不能建立 LINE RichMenu。
            throw new InvalidOperationException($"RichMenu '{definition.MenuKey}' image stream factory returned null.");
        }

        // LINE RichMenu 是否「同一版」不能只看 menuKey，因為同一個 menuKey 的版面或圖片可能改版。
        // 因此先把圖片讀成 bytes，再把版面 JSON + 圖片內容做 fingerprint。
        var imageBytes = await ReadAllBytesAsync(imageStream, cancellationToken).ConfigureAwait(false);
        var fingerprint = LineRichMenuFingerprint.Create(definition.RichMenu, imageBytes);

        // versionedName 是佈建冪等性的關鍵：
        // - 版面與圖片都沒變：名稱相同，重跑同步會 UpToDate。
        // - 版面或圖片有變：名稱不同，會建立新版 RichMenu。
        var versionedName = LineRichMenuFingerprint.BuildName(definition, fingerprint);

        if (existingByName.TryGetValue(versionedName, out var existing))
        {
            // LINE 上已經有同版選單，不需要重新 create/upload。
            // 但 alias/default/cache 仍要補齊，因為它們可能被手動改掉或快取剛重啟。
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

        // LINE 上沒有同版選單，建立新選單。
        // 不直接改動 catalog 內的 RichMenu 物件，而是 clone 一份把 Name 換成 versionedName；
        // 這樣產品宣告物件可重用，也避免 workflow 對輸入物件造成副作用。
        var richMenu = CloneForProvisioning(definition.RichMenu, versionedName);
        var richMenuId = await _processor.CreateRichMenuAsync(richMenu).ConfigureAwait(false);

        // LINE 建立 RichMenu 後，還需要另外上傳 PNG 圖片。
        // 這是 LINE Messaging API 的分步設計，不是本專案任意拆分。
        await using var uploadStream = new MemoryStream(imageBytes, writable: false);
        await _processor.UploadRichMenuPngImageAsync(richMenuId, uploadStream).ConfigureAwait(false);

        // alias 讓 RichMenu 內的分頁切換按鈕可以指向穩定名稱，
        // 即使 richMenuId 因改版而改變，按鈕仍可透過 alias 連到新版。
        await UpsertAliasAsync(definition.AliasId, richMenuId).ConfigureAwait(false);

        if (definition.IsDefault)
        {
            // default RichMenu 是 LINE channel 層級設定。
            // 若產品指定此選單為預設，未被個別綁定的使用者會看到它。
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
            // alias 已存在時，不管它目前指向舊版或新版，都統一 update 到目前 richMenuId。
            // 這能讓改版後的分頁按鈕保持穩定，不需要產品端知道新 richMenuId。
            await _processor.GetRichMenuAliasAsync(aliasId).ConfigureAwait(false);
            await _processor.UpdateRichMenuAliasAsync(aliasId, richMenuId).ConfigureAwait(false);
        }
        catch (LineRichMenuAliasNotFoundException)
        {
            // 測試用 adapter 或本專案包裝層可能用本地例外表示 alias 不存在。
            await _processor.CreateRichMenuAliasAsync(richMenuId, aliasId).ConfigureAwait(false);
        }
        catch (LineResponseException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 真實 LINE API 可能以 HTTP 404 表示 alias 不存在，這也視為可建立的正常情境。
            await _processor.CreateRichMenuAliasAsync(richMenuId, aliasId).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 複製 RichMenu 版面並覆寫 Name。
    /// </summary>
    /// <remarks>
    /// catalog 內的 RichMenu 是產品宣告資料，workflow 不應直接修改它。
    /// 這裡建立新物件可以避免「同步一次後輸入物件被改名」這類隱性副作用。
    /// </remarks>
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
            // MemoryStream 已在記憶體中，直接 ToArray 即可。
            return memoryStream.ToArray();
        }

        // 一般 Stream 可能來自檔案、內嵌資源或網路來源；
        // 統一複製到 MemoryStream 後計算 fingerprint 與重新上傳。
        await using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        return copy.ToArray();
    }
}
