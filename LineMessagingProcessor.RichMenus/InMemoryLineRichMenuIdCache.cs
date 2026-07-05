// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class InMemoryLineRichMenuIdCache
// 主要成員：TryGet、Set、Remove、SetSnapshot、Normalize
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
