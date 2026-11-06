/*
 * 檔案整體說明
 *
 * 目的與定位：以記憶體保存 menu key 與 LINE richMenuId 的對照。 這是預設輕量實作；正式產品可用資料庫、Redis 或其他持久化儲存替換同一個介面。
 *
 * 主要功能與流程：
 * 本檔宣告 InMemoryLineRichMenuIdCache。
 * - 同步 snapshot 取代流程，避免讀取端看到只更新到一半的 dictionary。
 * - 目前 cache snapshot，以應用程式 menu key 作為索引。 透過整份 dictionary 取代而不是原地修改，讓 snapshot 讀取保持簡單且可預期。
 * - 修剪 cache key 與 value，確保 cache 保存的是 workflow 實際使用的邏輯識別碼。
 * 主要操作入口包含 TryGet、Set、Remove、Snapshot、SetSnapshot、Normalize。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 以記憶體保存 menu key 與 LINE richMenuId 的對照。
/// 這是預設輕量實作；正式產品可用資料庫、Redis 或其他持久化儲存替換同一個介面。
/// </summary>
public sealed class InMemoryLineRichMenuIdCache : ILineRichMenuIdCache
{
    /// <summary>
    /// 同步 snapshot 取代流程，避免讀取端看到只更新到一半的 dictionary。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 目前 cache snapshot，以應用程式 menu key 作為索引。
    /// 透過整份 dictionary 取代而不是原地修改，讓 snapshot 讀取保持簡單且可預期。
    /// </summary>
    private IReadOnlyDictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool TryGet(string menuKey, out string richMenuId)
    {
        richMenuId = string.Empty;
        var normalizedKey = Normalize(menuKey);
        if (normalizedKey.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_values.TryGetValue(normalizedKey, out var cachedRichMenuId))
            {
                return false;
            }

            richMenuId = cachedRichMenuId;
            return true;
        }
    }

    /// <inheritdoc />
    public void Set(string menuKey, string richMenuId)
    {
        var normalizedKey = Normalize(menuKey);
        var normalizedValue = Normalize(richMenuId);
        if (normalizedKey.Length == 0)
        {
            throw new ArgumentException("Menu key is required.", nameof(menuKey));
        }

        if (normalizedValue.Length == 0)
        {
            throw new ArgumentException("RichMenu id is required.", nameof(richMenuId));
        }

        lock (_gate)
        {
            var replacement = new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase)
            {
                [normalizedKey] = normalizedValue
            };
            _values = replacement;
        }
    }

    /// <inheritdoc />
    public void Remove(string menuKey)
    {
        var normalizedKey = Normalize(menuKey);
        if (normalizedKey.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_values.ContainsKey(normalizedKey))
            {
                return;
            }

            var replacement = new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
            replacement.Remove(normalizedKey);
            _values = replacement;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    public void SetSnapshot(IReadOnlyDictionary<string, string> values)
    {
        var replacement = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values != null)
        {
            foreach (var pair in values)
            {
                var key = Normalize(pair.Key);
                var value = Normalize(pair.Value);
                if (key.Length > 0 && value.Length > 0)
                {
                    replacement[key] = value;
                }
            }
        }

        lock (_gate)
        {
            _values = replacement;
        }
    }

    /// <summary>
    /// 修剪 cache key 與 value，確保 cache 保存的是 workflow 實際使用的邏輯識別碼。
    /// </summary>
    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
