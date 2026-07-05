namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 還原或解除已到期的暫時性 RichMenu 指派。
/// </summary>
public interface IRichMenuExpirationSweepWorkflow
{
    /// <summary>
    /// 處理已到期的 RichMenu 使用者狀態紀錄。
    /// </summary>
    /// <param name="now">用來判斷哪些指派已到期的目前時間。</param>
    /// <param name="cancellationToken">傳入 state store 與 assignment workflow 的取消權杖。</param>
    Task<RichMenuExpirationSweepReport> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
