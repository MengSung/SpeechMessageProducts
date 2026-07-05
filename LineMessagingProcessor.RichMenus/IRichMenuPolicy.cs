namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 定義一條可判斷使用者 RichMenu 是否應改變的規則。
/// policy 刻意保持小而可組合；orchestrator 會評估所有 policy，並只套用強度最高的 decision。
/// </summary>
public interface IRichMenuPolicy
{
    /// <summary>
    /// 評估傳入 context，並回傳 RichMenu decision。
    /// </summary>
    /// <param name="context">policy 可使用的使用者與訊息 context。</param>
    /// <param name="cancellationToken">供需要非同步資料的 policy 使用的取消權杖。</param>
    Task<RichMenuDecision> DecideAsync(RichMenuContext context, CancellationToken cancellationToken = default);
}
