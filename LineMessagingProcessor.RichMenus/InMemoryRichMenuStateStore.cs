// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class InMemoryRichMenuStateStore
// 主要成員：GetAsync、SetAsync、RemoveAsync、GetExpiredAsync、Normalize、NormalizeRequired
// 引用命名空間：System.Collections.Concurrent
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Collections.Concurrent;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 開發與測試用的 RichMenu 使用者狀態儲存。
/// 這個實作只把資料保存在目前應用程式行程的記憶體中，服務重啟、站台回收或多台主機部署時資料都不會保留或同步。
/// 未來產品若需要正式使用 RichMenu 到期還原、角色切換或跨節點一致性，應實作 <see cref="IRichMenuStateStore"/> 並接到資料庫、Redis 或其他持久化儲存。
/// </summary>
public sealed class InMemoryRichMenuStateStore : IRichMenuStateStore
{
    /// <summary>
    /// 以 LINE userId 為 key 的 thread-safe state table。
    /// 使用不分大小寫 comparer，讓呼叫端 userId 大小寫不同時仍能穩定查詢狀態。
    /// </summary>
    private readonly ConcurrentDictionary<string, RichMenuUserState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<RichMenuUserState?> GetAsync(string lineUserId, CancellationToken cancellationToken = default)
    {
        _states.TryGetValue(Normalize(lineUserId), out var state);
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task SetAsync(RichMenuUserState state, CancellationToken cancellationToken = default)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        _states[NormalizeRequired(state.LineUserId, nameof(state.LineUserId))] = state;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string lineUserId, CancellationToken cancellationToken = default)
    {
        var key = Normalize(lineUserId);
        if (key.Length > 0)
        {
            _states.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RichMenuUserState>> GetExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RichMenuUserState> expired = _states.Values
            .Where(state => state.ExpiresAt.HasValue && state.ExpiresAt.Value <= now)
            .ToList();
        return Task.FromResult(expired);
    }

    /// <summary>
    /// 正規化選填 key，讓查詢與移除路徑可容忍空值並安全 no-op。
    /// </summary>
    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>
    /// 正規化寫入路徑的 key；空值會破壞 store，因此必須拒絕。
    /// </summary>
    private static string NormalizeRequired(string value, string parameterName)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return normalized;
    }
}
