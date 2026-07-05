namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 保存產品內部 menu key 與 LINE richMenuId 的對照。
/// 呼叫端只依賴這個抽象，實際儲存可由 in-memory、資料庫或 Redis 實作。
/// </summary>
public interface ILineRichMenuIdCache
{
    /// <summary>
    /// 嘗試取得應用程式 menu key 已解析出的 LINE richMenuId。
    /// </summary>
    /// <param name="menuKey">應用程式層級的 menu key。</param>
    /// <param name="richMenuId">方法回傳 true 時，代表已快取的 LINE richMenuId。</param>
    bool TryGet(string menuKey, out string richMenuId);

    /// <summary>
    /// 儲存或取代某個應用程式 menu key 對應的 LINE richMenuId。
    /// </summary>
    /// <param name="menuKey">應用程式層級的 menu key。</param>
    /// <param name="richMenuId">provisioning 過程中建立或發現的 LINE provider id。</param>
    void Set(string menuKey, string richMenuId);

    /// <summary>
    /// 移除已快取的應用程式 menu key 對照。
    /// </summary>
    /// <param name="menuKey">要移除的應用程式層級 menu key。</param>
    void Remove(string menuKey);

    /// <summary>
    /// 回傳目前所有應用程式 menu key 到 LINE richMenuId 對照的時間點快照。
    /// </summary>
    IReadOnlyDictionary<string, string> Snapshot();

    /// <summary>
    /// 以新的對照集合取代整份 cache。
    /// </summary>
    /// <param name="values">要保留的 menu key 到 richMenuId 對照。</param>
    void SetSnapshot(IReadOnlyDictionary<string, string> values);
}
