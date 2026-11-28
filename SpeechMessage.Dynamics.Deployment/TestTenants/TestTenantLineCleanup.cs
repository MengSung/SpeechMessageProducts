/*
檔案整體說明
目的與定位：F12 部署 CLI 的測試租戶 LINE 宮格清理元件，只處理精確 code 加中線前綴的 alias 及其 provider menu ID，其他租戶身份不得由名稱猜測或合併。
主要功能與流程：先唯讀取得最多一千筆完整 alias 清單，驗證非空唯一 alias 身份、宮格身份與前綴外共用衝突；產生不可變計畫後重驗目前清單及全部使用者查詢。每次解除前再次查詢當前 menu，只解除仍屬目標集合的綁定；每個 menu 先取得 richmenu-delete 預算，再依序刪除其 alias、重驗清單、刪除宮格。
依賴與輸出：client 與 budget 都由 CLI owner 借入，本類不 Dispose、不建立 HTTP client、連線、timer、訂閱或背景工作；每個 await 都等依賴實際完成，取消後才停止後續操作，不用額外逾時遺棄 SDK 工作。SDK owner 必須維持既有有界 HTTP timeout，並在本工作真正排空後才釋放依賴。
使用邊界與維護：產品與 Channel 必須先由 CLI 驗證實際憑證及測試租戶 manifest，並在整個計畫／執行期間持有適用的共用寫入鎖、阻止同 Channel 的自有配置與綁定寫入；本類的字串檢查不是遠端授權。LINE 沒有條件式使用者 unlink，重查無法阻止不受該鎖管理的外部寫入，呼叫端不能把此 helper 宣稱為跨供應商交易。
資料保留與續跑：每次操作只持有最多一千個使用者 ID 的獨立副本，finally 清空陣列；不 log 使用者、token 或原始回應。Plan 只含有界產品／Channel／代碼及 alias→menu ID，CLI 必須在首次副作用前以可信持久化 owner 保存，成功後刪除；可從可信保存資料重建 Plan，跨程序續跑仍重驗目前 alias。沒有原計畫的孤立 menu 不具租戶證據，必須保留並交人工／owner 診斷，不能以 menu 名稱推斷。
失敗處理與驗證：身份衝突與範圍漂移 fail closed，只有精確 SDK HTTP 404 可視為已不存在；配額不足拋固定可續跑型別，不 sleep。其他例外保留交 CLI 共用告警 owner，部分成功不回滾遠端刪除。純合成測試驗證前綴隔離、完整預檢、配額、計畫續跑與取消排空，不代表正式或測試實機已清理。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using SpeechMessage.Scheduling;

namespace SpeechMessage.Dynamics.Deployment.TestTenants;

/// <summary>配額拒絕的固定分類，讓 CLI 分辨可續跑停止與真正的供應商故障。</summary>
internal sealed class TestTenantLineCleanupBudgetExceededException : InvalidOperationException
{
    /// <summary>建立不含租戶、使用者或遠端本文的固定診斷訊息。</summary>
    internal TestTenantLineCleanupBudgetExceededException() : base("Test tenant LINE cleanup rate budget exhausted.") { }
}

/// <summary>在 CLI 已驗證的單一產品／Channel 範圍內，先驗證完整候選再依穩定 ID 清理；不擁有借入依賴。</summary>
internal sealed class TestTenantLineCleanup
{
    private const int MaximumRows = 1000;
    private readonly ILineMessagingClient client;
    private readonly IChannelRateBudget budget;
    private readonly string product;
    private readonly string channelKey;

    /// <summary>借入已通過 CLI 憑證及測試環境守門的依賴；只保存固定 scope，不保存使用者清單。</summary>
    /// <param name="client">具既有有界 timeout 的 Channel client，CLI 必須在清理排空後才 Dispose。</param>
    /// <param name="budget">同一產品／Channel 的持久化速率預算，生命週期由 CLI owner 管理。</param>
    /// <param name="product">三大產品的固定鍵；呼叫端必須重驗實際 Channel 屬於此產品。</param>
    /// <param name="channelKey">已驗證且與 client 相符的 Channel 識別，不是 caller 的授權憑證。</param>
    internal TestTenantLineCleanup(ILineMessagingClient client, IChannelRateBudget budget, string product, string channelKey)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(budget);
        ValidateScope(product, channelKey);
        this.client = client;
        this.budget = budget;
        this.product = product;
        this.channelKey = channelKey;
    }

    /// <summary>以當前 alias 建立單次計畫並清理；需要跨程序續跑的 CLI 必須改用先保存 Plan 的兩階段入口。</summary>
    /// <param name="code">manifest 已驗證的測試租戶代碼，與 alias 以 Ordinal 精確前綴比對。</param>
    /// <param name="userIds">只在本次操作保留的使用者候選；先複製、驗證及排序，結束後清空副本。</param>
    /// <param name="ct">取消阻止下一操作；已開始的 SDK／預算呼叫仍等到真完成。</param>
    /// <returns>所有已開始操作排空後才完成的 Task；失敗時不自動重試或遺棄工作。</returns>
    internal async Task CleanupAsync(string code, IReadOnlyList<string> userIds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var users = SnapshotUsers(userIds);
        try
        {
            var plan = await CreatePlanAsync(code, ct).ConfigureAwait(false);
            await ExecuteAsync(plan, users, ct).ConfigureAwait(false);
        }
        finally { Array.Clear(users); }
    }

    /// <summary>只讀取並驗證完整 alias 清單，建立可供 CLI 在任何副作用前持久化的最小不可變計畫。</summary>
    /// <param name="code">與 manifest 相同的有界租戶代碼，不接受路徑、空白或大小寫近似。</param>
    /// <param name="ct">讀取前及真實 SDK 呼叫排空後檢查取消，不遺棄 HTTP 工作。</param>
    /// <returns>只含穩定 alias 與 menu ID 的計畫；不包含 client、使用者、token 或 API body。</returns>
    internal async Task<Plan> CreatePlanAsync(string code, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateCode(code);
        var aliases = await ReadAliasesAsync(ct).ConfigureAwait(false);
        var prefix = code + "-";
        var plan = new Plan(product, channelKey, code, aliases.Where(row =>
            row.Key.StartsWith(prefix, StringComparison.Ordinal)).ToArray());
        ValidateCurrentAliases(plan, aliases);
        return plan;
    }

    /// <summary>使用可信持久化計畫續跑；alias 已不存在時仍以原 provider menu ID 判斷目標，並重驗目前共用衝突。</summary>
    /// <param name="plan">CLI 在第一次副作用前保存的計畫；不能接受外部 request 任意聲稱的宮格歸屬。</param>
    /// <param name="userIds">本次從已授權來源取得的候選；不從 Plan 還原或保留使用者個資。</param>
    /// <param name="ct">取消只阻止下一操作，所有已開始操作先排空。</param>
    /// <returns>完整排空的清理結果；部分刪除失敗後由 owner 以同一計畫重試。</returns>
    internal async Task CleanupAsync(Plan plan, IReadOnlyList<string> userIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();
        var users = SnapshotUsers(userIds);
        try { await ExecuteAsync(plan, users, ct).ConfigureAwait(false); }
        finally { Array.Clear(users); }
    }

    /// <summary>重驗 scope、完整 alias 與全部使用者可查詢性後，逐操作等待真完成；不排程背景重試。</summary>
    private async Task ExecuteAsync(Plan plan, string[] users, CancellationToken ct)
    {
        if (!string.Equals(plan.Product, product, StringComparison.Ordinal) ||
            !string.Equals(plan.ChannelKey, channelKey, StringComparison.Ordinal))
            throw new InvalidDataException("Test tenant LINE cleanup plan scope does not match.");

        var aliases = await ReadAliasesAsync(ct).ConfigureAwait(false);
        ValidateCurrentAliases(plan, aliases);
        // 多個不同 alias 合法地指向同一 provider menu；保留所有 alias 身份，只有實際刪宮格去除重複工作。
        var menus = new HashSet<string>(plan.Aliases.Select(row => row.Value), StringComparer.Ordinal);
        if (menus.Count == 0) return;

        foreach (var user in users)
            await ReadUserMenuAsync(user, ct).ConfigureAwait(false);

        foreach (var user in users)
        {
            var currentMenu = await ReadUserMenuAsync(user, ct).ConfigureAwait(false);
            if (currentMenu is null || !menus.Contains(currentMenu)) continue;
            ct.ThrowIfCancellationRequested();
            try { await client.UnLinkRichMenuFromUserAsync(user).ConfigureAwait(false); }
            catch (LineResponseException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
            ct.ThrowIfCancellationRequested();
        }

        foreach (var menu in menus.Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var acquired = await budget.TryAcquireAsync(product, channelKey, "richmenu-delete", ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!acquired) throw new TestTenantLineCleanupBudgetExceededException();

            // 預算可非同步等待；取得後重讀，以免等待期間發生的新共用關係被舊快照掩蓋。
            aliases = await ReadAliasesAsync(ct).ConfigureAwait(false);
            ValidateCurrentAliases(plan, aliases);
            foreach (var alias in plan.Aliases)
            {
                if (!string.Equals(alias.Value, menu, StringComparison.Ordinal) || !aliases.ContainsKey(alias.Key)) continue;
                ct.ThrowIfCancellationRequested();
                try { await client.DeleteRichMenuAliasAsync(alias.Key).ConfigureAwait(false); }
                catch (LineResponseException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
                ct.ThrowIfCancellationRequested();
            }

            // 刪除途中若 alias 被重新建立，不能繼續刪宮格；保留計畫讓 owner 解決寫入衝突後續跑。
            aliases = await ReadAliasesAsync(ct).ConfigureAwait(false);
            ValidateCurrentAliases(plan, aliases);
            if (aliases.Values.Contains(menu, StringComparer.Ordinal))
                throw new InvalidDataException("Test tenant LINE cleanup menu still has an alias.");
            ct.ThrowIfCancellationRequested();
            try { await client.DeleteRichMenuAsync(menu).ConfigureAwait(false); }
            catch (LineResponseException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>等待 SDK 的完整 alias 回應後複製成操作私有字典；全清單身份唯一且最多一千筆才發布。</summary>
    private async Task<Dictionary<string, string>> ReadAliasesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = await client.GetRichMenuAliasListAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (response?.Aliases is null || response.Aliases.Count > MaximumRows)
            throw new InvalidDataException("Test tenant LINE cleanup alias list is invalid or oversized.");
        var result = new Dictionary<string, string>(response.Aliases.Count, StringComparer.Ordinal);
        foreach (var row in response.Aliases)
        {
            ct.ThrowIfCancellationRequested();
            if (row is null || !IsIdentity(row.RichMenuAliasId, 100) || !IsIdentity(row.RichMenuId, 128) ||
                !result.TryAdd(row.RichMenuAliasId, row.RichMenuId))
                throw new InvalidDataException("Test tenant LINE cleanup alias identity is invalid or duplicated.");
        }
        return result;
    }

    /// <summary>只將精確 HTTP 404 視為未綁定；空回應及查詢能力缺失一律拒絕，不以未知狀態解除使用者。</summary>
    private async Task<string?> ReadUserMenuAsync(string user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string? menu = null;
        try { menu = await client.GetRichMenuIdOfUserAsync(user).ConfigureAwait(false); }
        catch (LineResponseException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }
        ct.ThrowIfCancellationRequested();
        if (!IsIdentity(menu, 128))
            throw new InvalidDataException("Test tenant LINE cleanup user binding could not be verified.");
        return menu;
    }

    /// <summary>原計畫中的 alias 可因上次刪除而消失；轉指、新目標 alias 或任何外租戶共用都必須停止。</summary>
    private static void ValidateCurrentAliases(Plan plan, Dictionary<string, string> aliases)
    {
        var expected = plan.Aliases.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        var menus = new HashSet<string>(plan.Aliases.Select(row => row.Value), StringComparer.Ordinal);
        var prefix = plan.Code + "-";
        foreach (var alias in aliases)
        {
            if (alias.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (!expected.TryGetValue(alias.Key, out var menu) || !string.Equals(menu, alias.Value, StringComparison.Ordinal))
                    throw new InvalidDataException("Test tenant LINE cleanup alias scope changed.");
            }
            else if (menus.Contains(alias.Value))
                throw new InvalidDataException("Test tenant LINE cleanup menu is referenced outside the tenant prefix.");
        }
    }

    /// <summary>先完整複製及驗證有界使用者候選，排序後才交非同步操作；重複身份拒絕而非靜默去重。</summary>
    private static string[] SnapshotUsers(IReadOnlyList<string> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count > MaximumRows)
            throw new InvalidDataException("Test tenant LINE cleanup user list is oversized.");
        var users = new string[userIds.Count];
        var identities = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            for (var index = 0; index < users.Length; index++)
            {
                var user = userIds[index];
                if (!IsIdentity(user, 128) || !identities.Add(user))
                    throw new InvalidDataException("Test tenant LINE cleanup user identity is invalid or duplicated.");
                users[index] = user;
            }
            Array.Sort(users, StringComparer.Ordinal);
            return users;
        }
        catch { Array.Clear(users); throw; }
    }

    /// <summary>限制 provider 路徑身份為有界 ASCII 英數、中線及底線，避免空白、控制字元與路徑語意進入 SDK。</summary>
    private static bool IsIdentity(string? value, int maximumLength) => !string.IsNullOrEmpty(value) &&
        value.Length <= maximumLength && value.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
            or >= '0' and <= '9' or '-' or '_');

    /// <summary>產品採固定白名單，Channel 為有界非空鍵；此檢查不能代替 CLI 的憑證歸屬驗證。</summary>
    private static void ValidateScope(string product, string channelKey)
    {
        if (product is not "GoodShepherd2" and not "GoodButler" and not "GoodGuild" || !IsIdentity(channelKey, 128))
            throw new InvalidDataException("Test tenant LINE cleanup product or channel scope is invalid.");
    }

    /// <summary>與 manifest 一致限制代碼為一至三十二個小寫英數或中線，首字須為小寫英數。</summary>
    private static void ValidateCode(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 32 ||
            code[0] is not (>= 'a' and <= 'z' or >= '0' and <= '9') ||
            code.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new InvalidDataException("Test tenant LINE cleanup tenant code is invalid.");
    }

    /// <summary>與產品／Channel／代碼綁定的不可變清理證據，只保存 alias 與 provider menu ID，沒有使用者或依賴引用。</summary>
    internal sealed class Plan
    {
        /// <summary>建立或恢復可信計畫時綁定的產品鍵，執行時須與 helper scope 完全相同。</summary>
        internal string Product { get; }
        /// <summary>與實際 client 憑證相符的 Channel 鍵；不接受跨 Channel 重用計畫。</summary>
        internal string ChannelKey { get; }
        /// <summary>由可信 manifest 提供的精確租戶代碼，所有 alias 均須屬於此代碼前綴。</summary>
        internal string Code { get; }
        /// <summary>排序後的獨立唯讀 alias→menu ID 快照；值型別項目不暴露 SDK 可變模型。</summary>
        internal IReadOnlyList<KeyValuePair<string, string>> Aliases { get; }

        /// <summary>複製及驗證清理證據；恢復時只可使用 CLI 在首次副作用前保存的可信資料，不能由外部 request 建構。</summary>
        /// <param name="product">已驗證的固定產品鍵。</param>
        /// <param name="channelKey">已驗證的 Channel scope。</param>
        /// <param name="code">與 manifest 相同的租戶代碼。</param>
        /// <param name="aliases">最多一千筆唯一 alias→menu ID；空清單代表沒有已知目標，不授權刪除孤立 menu。</param>
        internal Plan(string product, string channelKey, string code, IReadOnlyList<KeyValuePair<string, string>> aliases)
        {
            ValidateScope(product, channelKey);
            ValidateCode(code);
            ArgumentNullException.ThrowIfNull(aliases);
            if (aliases.Count > MaximumRows)
                throw new InvalidDataException("Test tenant LINE cleanup plan is oversized.");
            var copy = new KeyValuePair<string, string>[aliases.Count];
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < copy.Length; index++)
            {
                var alias = aliases[index];
                if (!IsIdentity(alias.Key, 100) || !IsIdentity(alias.Value, 128) ||
                    !alias.Key.StartsWith(code + "-", StringComparison.Ordinal) || !identities.Add(alias.Key))
                    throw new InvalidDataException("Test tenant LINE cleanup plan identity is invalid or duplicated.");
                copy[index] = alias;
            }
            Product = product;
            ChannelKey = channelKey;
            Code = code;
            Aliases = Array.AsReadOnly(copy.OrderBy(row => row.Key, StringComparer.Ordinal).ToArray());
        }
    }
}
