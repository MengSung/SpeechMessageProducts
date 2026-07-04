namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 以記憶體保存 menu key 與 LINE richMenuId 的對照。
/// 這是預設輕量實作；正式產品可用資料庫、Redis 或其他持久化儲存替換同一個介面。
/// </summary>
public sealed class InMemoryLineRichMenuIdCache : ILineRichMenuIdCache
{
    private readonly object _gate = new();
    private IReadOnlyDictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
        }
    }

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

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
