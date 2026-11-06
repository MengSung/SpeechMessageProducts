/*
 * 檔案整體說明
 *
 * 目的與定位：開發與測試用的 RichMenu 使用者狀態儲存。 這個實作只把資料保存在目前應用程式行程的記憶體中，服務重啟、站台回收或多台主機部署時資料都不會保留或同步。 未來產品若需要正式使用 RichMenu 到期還原、
 * 角色切換或跨節點一致性，應實作 IRichMenuStateStore 並接到資料庫、Redis 或其他持久化儲存。
 *
 * 主要功能與流程：
 * 本檔宣告 InMemoryRichMenuStateStore。
 * - 以 LINE userId 為 key 的 thread-safe state table。 使用不分大小寫 comparer，讓呼叫端 userId 大小寫不同時仍能穩定查詢狀態。
 * - 正規化選填 key，讓查詢與移除路徑可容忍空值並安全 no-op。
 * - 正規化寫入路徑的 key；空值會破壞 store，因此必須拒絕。
 * 主要操作入口包含 GetAsync、SetAsync、RemoveAsync、GetExpiredAsync、Normalize、NormalizeRequired。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuExpirationSweepWorkflowTests.cs、
 * LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs、
 * SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs 追查目前工作樹中的合作／使用位置。
 */
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
