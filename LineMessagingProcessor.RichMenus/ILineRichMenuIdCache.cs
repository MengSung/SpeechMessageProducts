namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 保存產品內部 menu key 與 LINE richMenuId 的對照。
/// 呼叫端只依賴這個抽象，實際儲存可由 in-memory、資料庫或 Redis 實作。
/// </summary>
public interface ILineRichMenuIdCache
{
    bool TryGet(string menuKey, out string richMenuId);

    void Set(string menuKey, string richMenuId);

    void Remove(string menuKey);

    IReadOnlyDictionary<string, string> Snapshot();

    void SetSnapshot(IReadOnlyDictionary<string, string> values);
}
