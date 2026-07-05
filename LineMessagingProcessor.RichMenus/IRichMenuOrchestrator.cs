namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 協調單次使用者互動中的 policy 評估與 RichMenu 指派。
/// </summary>
public interface IRichMenuOrchestrator
{
    /// <summary>
    /// 依傳入 context 套用最佳 RichMenu decision。
    /// </summary>
    /// <param name="context">policy 評估所需的 LINE 使用者 context 與訊息事實。</param>
    /// <param name="cancellationToken">傳入 policies 與 assignment workflows 的取消權杖。</param>
    Task<LineRichMenuAssignmentResult> ApplyAsync(RichMenuContext context, CancellationToken cancellationToken = default);
}
