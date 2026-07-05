namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 保存 LINE 使用者的應用程式層級 RichMenu 狀態。
/// 實作可以使用記憶體、資料庫或分散式快取，但必須保留足夠狀態，讓 assignment workflow 與到期 sweep 能可預期地還原前一個選單。
/// </summary>
public interface IRichMenuStateStore
{
    /// <summary>
    /// 取得單一 LINE 使用者已保存的狀態。
    /// </summary>
    /// <param name="lineUserId">要查詢的 LINE userId。</param>
    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
    Task<RichMenuUserState?> GetAsync(string lineUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 儲存或取代單一 LINE 使用者的 RichMenu 狀態。
    /// </summary>
    /// <param name="state">要保存的完整狀態紀錄。</param>
    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
    Task SetAsync(RichMenuUserState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除單一 LINE 使用者已保存的 RichMenu 狀態。
    /// </summary>
    /// <param name="lineUserId">要移除狀態的 LINE userId。</param>
    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
    Task RemoveAsync(string lineUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 回傳所有已達到期時間的狀態紀錄。
    /// </summary>
    /// <param name="now">用於到期比較的目前時間。</param>
    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
    Task<IReadOnlyList<RichMenuUserState>> GetExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
