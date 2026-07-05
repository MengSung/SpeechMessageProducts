using Line.Messaging;

namespace LineMessagingProcessor.RichMenus.Tests.Support;

/// <summary>
/// 測試專用的 RichMenu processor 假物件。
///
/// 這個類別不會真的連到 LINE，而是把每一次呼叫記錄在 <see cref="Calls"/>，
/// 讓測試可以確認 workflow 是否真的送出 create / link / unlink / alias 等命令。
///
/// 設計重點：
/// 1. 測試只關心「共用流程有沒有呼叫正確的 processor 方法」，不碰外部網路。
/// 2. 透過 <see cref="LinkException"/> 與 <see cref="UnlinkException"/> 可以模擬 LINE 拒絕、
///    網路失敗、timeout 或程式錯誤，進而測試 workflow 的錯誤分類邊界。
/// 3. <see cref="LinkedUsers"/>、<see cref="ExistingRichMenus"/> 與 <see cref="Aliases"/>
///    是記憶體狀態，方便驗證 processor 呼叫後是否產生預期副作用。
///
/// 這種假物件讓 RichMenu 共用核心的測試保持單純、穩定、快速，
/// 也避免把 LINE 官方服務、產品資料庫或任何特定產品流程混進單元測試。
/// </summary>
internal sealed class CapturingRichMenuProcessor : ILineRichMenuProcessor
{
    // 此假物件刻意同時保存 provider 狀態與呼叫順序，讓 RichMenu tests 能驗證 workflow 是否真的走到 LINE 邊界。
    /// <summary>
    /// 建立 RichMenu 時要回傳的 richMenuId 佇列。
    ///
    /// 測試若需要固定 ID，可以先呼叫 <see cref="EnqueueCreatedRichMenuId"/>；
    /// 若沒有預先指定，這個假物件會依照目前清單數量自動產生穩定 ID。
    /// </summary>
    private readonly Queue<string> _createdRichMenuIds = new();

    /// <summary>
    /// 所有 processor 方法的呼叫紀錄。
    ///
    /// 測試通常透過這個集合確認 workflow 是否真的執行到外部平台邊界，
    /// 例如解除 RichMenu 時即使 state store 沒資料，也仍然要出現 <c>unlink:U123</c>。
    /// </summary>
    public List<string> Calls { get; } = new();

    /// <summary>
    /// 模擬 LINE 平台上已存在的 RichMenu 清單。
    ///
    /// workflow 在 cache miss 時會呼叫 <see cref="GetRichMenuListAsync"/> 尋找線上 RichMenu；
    /// 測試可先把資料放進這裡，驗證 menu key 能否被解析為實際 richMenuId。
    /// </summary>
    public List<ResponseRichMenu> ExistingRichMenus { get; } = new();

    /// <summary>
    /// 模擬 LINE RichMenu alias 對應表。
    ///
    /// key 是 alias id，value 是 richMenuId；用來測試 provisioning / alias 更新流程，
    /// 不需要真的呼叫 LINE 官方 alias API。
    /// </summary>
    public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 模擬使用者目前綁定的 RichMenu。
    ///
    /// key 是 LINE user id，value 是 richMenuId；link / unlink 測試會檢查這個集合，
    /// 以確認 workflow 呼叫 processor 後是否留下正確狀態。
    /// </summary>
    public Dictionary<string, string> LinkedUsers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 模擬 LINE 頻道預設 RichMenu id。
    /// </summary>
    public string? DefaultRichMenuId { get; private set; }

    /// <summary>
    /// 指定 link 使用者 RichMenu 時要丟出的例外。
    ///
    /// 測試用它模擬 LINE provider 拒絕、網路中斷、timeout 或非預期程式錯誤；
    /// workflow 必須只把 LINE / HTTP / timeout 這類 provider 邊界錯誤轉成標準結果，
    /// 其他例外則保留原樣往外拋。
    /// </summary>
    public Exception? LinkException { get; set; }

    /// <summary>
    /// 指定 unlink 使用者 RichMenu 時要丟出的例外。
    ///
    /// 這與 <see cref="LinkException"/> 成對使用，用來驗證解除綁定流程的錯誤分類。
    /// </summary>
    public Exception? UnlinkException { get; set; }

    /// <summary>
    /// 指定查詢線上 RichMenu 清單時要丟出的例外。
    ///
    /// workflow 在 cache miss 時會透過 <see cref="GetRichMenuListAsync"/> 到 LINE 平台查詢既有 RichMenu；
    /// 這個屬性讓測試可以驗證「線上清單查詢失敗」也會被視為 provider 邊界錯誤，
    /// 但不會影響 catalog、cache、state store 這些本機流程的錯誤分類。
    /// </summary>
    public Exception? ListException { get; set; }

    /// <summary>
    /// 記錄 RichMenu PNG 圖檔被上傳的次數。
    /// </summary>
    public int UploadedImageCount { get; private set; }

    /// <summary>
    /// 記錄建立 alias 的次數，方便測試 provisioning 是否避免不必要的重複建立。
    /// </summary>
    public int CreateAliasCount { get; private set; }

    /// <summary>
    /// 記錄更新 alias 的次數，方便測試 provisioning 是否只在目標 richMenuId 改變時更新。
    /// </summary>
    public int UpdateAliasCount { get; private set; }

    /// <summary>
    /// 預先指定下一次 <see cref="CreateRichMenuAsync"/> 要回傳的 richMenuId。
    /// </summary>
    public void EnqueueCreatedRichMenuId(string richMenuId)
    {
        _createdRichMenuIds.Enqueue(richMenuId);
    }

    /// <summary>
    /// 模擬建立 RichMenu：記錄呼叫、產生 richMenuId，並把 RichMenu 放進線上清單。
    /// </summary>
    public Task<string> CreateRichMenuAsync(RichMenu richMenu)
    {
        var richMenuId = _createdRichMenuIds.Count == 0
            ? $"rich-menu-{ExistingRichMenus.Count + 1:000}"
            : _createdRichMenuIds.Dequeue();

        Calls.Add($"create:{richMenu.Name}");
        ExistingRichMenus.Add(richMenu.ToResponseRichMenu(richMenuId));
        return Task.FromResult(richMenuId);
    }

    /// <summary>
    /// 模擬上傳 RichMenu 圖檔。
    ///
    /// 測試不需要讀取圖片內容，只需要確認 workflow 有呼叫到圖片上傳步驟。
    /// </summary>
    public Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream)
    {
        Calls.Add($"upload:{richMenuId}");
        UploadedImageCount++;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模擬取得 LINE 平台上的 RichMenu 清單。
    /// </summary>
    public Task<IList<ResponseRichMenu>> GetRichMenuListAsync()
    {
        if (ListException != null)
        {
            throw ListException;
        }

        Calls.Add("list");
        return Task.FromResult<IList<ResponseRichMenu>>(ExistingRichMenus);
    }

    /// <summary>
    /// 模擬設定頻道預設 RichMenu。
    /// </summary>
    public Task SetDefaultRichMenuAsync(string richMenuId)
    {
        Calls.Add($"default:{richMenuId}");
        DefaultRichMenuId = richMenuId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模擬讀取目前頻道預設 RichMenu。
    /// </summary>
    public Task<string> GetDefaultRichMenuIdAsync()
    {
        Calls.Add("get-default");
        return Task.FromResult(DefaultRichMenuId ?? string.Empty);
    }

    /// <summary>
    /// 模擬取消頻道預設 RichMenu。
    /// </summary>
    public Task CancelDefaultRichMenuAsync()
    {
        Calls.Add("cancel-default");
        DefaultRichMenuId = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模擬查詢某個使用者目前綁定的 RichMenu id。
    /// </summary>
    public Task<string> GetRichMenuIdOfUserAsync(string userId)
    {
        Calls.Add($"get-user:{userId}");
        return Task.FromResult(LinkedUsers.TryGetValue(userId, out var richMenuId) ? richMenuId : string.Empty);
    }

    /// <summary>
    /// 模擬把指定使用者綁定到指定 RichMenu。
    ///
    /// 如果 <see cref="LinkException"/> 有值，這裡會先丟出該例外；
    /// 這讓測試可以精準驗證 workflow 只把 provider 邊界錯誤轉成失敗結果，
    /// 不會把其他本機錯誤一起吞掉。
    /// </summary>
    public Task LinkRichMenuToUserAsync(string userId, string richMenuId)
    {
        if (LinkException != null)
        {
            throw LinkException;
        }

        Calls.Add($"link:{userId}:{richMenuId}");
        LinkedUsers[userId] = richMenuId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模擬解除指定使用者的 RichMenu 綁定。
    ///
    /// 如果 <see cref="UnlinkException"/> 有值，會直接丟出該例外；
    /// 否則移除 <see cref="LinkedUsers"/> 中的使用者紀錄。
    /// </summary>
    public Task UnlinkRichMenuFromUserAsync(string userId)
    {
        if (UnlinkException != null)
        {
            throw UnlinkException;
        }

        Calls.Add($"unlink:{userId}");
        LinkedUsers.Remove(userId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模擬刪除指定 RichMenu。
    /// </summary>
    public Task DeleteRichMenuAsync(string richMenuId)
    {
        Calls.Add($"delete:{richMenuId}");
        ExistingRichMenus.RemoveAll(menu => menu.RichMenuId == richMenuId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模擬建立 RichMenu alias。
    /// </summary>
    public Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId)
    {
        Calls.Add($"alias-create:{richMenuAliasId}:{richMenuId}");
        CreateAliasCount++;
        Aliases[richMenuAliasId] = richMenuId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模擬更新 RichMenu alias。
    /// </summary>
    public Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId)
    {
        Calls.Add($"alias-update:{richMenuAliasId}:{richMenuId}");
        UpdateAliasCount++;
        Aliases[richMenuAliasId] = richMenuId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模擬刪除 RichMenu alias。
    /// </summary>
    public Task DeleteRichMenuAliasAsync(string richMenuAliasId)
    {
        Calls.Add($"alias-delete:{richMenuAliasId}");
        Aliases.Remove(richMenuAliasId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模擬取得單一 RichMenu alias。
    ///
    /// alias 不存在時丟出 <see cref="LineRichMenuAliasNotFoundException"/>，
    /// 讓測試情境接近真實 processor 的行為。
    /// </summary>
    public Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId)
    {
        Calls.Add($"alias-get:{richMenuAliasId}");
        if (!Aliases.TryGetValue(richMenuAliasId, out var richMenuId))
        {
            throw new LineRichMenuAliasNotFoundException(richMenuAliasId);
        }

        return Task.FromResult(new RichMenuAlias
        {
            RichMenuAliasId = richMenuAliasId,
            RichMenuId = richMenuId
        });
    }

    /// <summary>
    /// 模擬取得所有 RichMenu alias。
    /// </summary>
    public Task<RichMenuAliasList> GetRichMenuAliasListAsync()
    {
        Calls.Add("alias-list");
        return Task.FromResult(new RichMenuAliasList
        {
            Aliases = Aliases
                .Select(item => new RichMenuAlias
                {
                    RichMenuAliasId = item.Key,
                    RichMenuId = item.Value
                })
                .ToList()
        });
    }
}
