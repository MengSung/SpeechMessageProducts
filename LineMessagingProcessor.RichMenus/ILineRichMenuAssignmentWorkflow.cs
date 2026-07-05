namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu 指派工作流入口。
/// 一般流程可使用回傳結果版本；必要流程可使用 OrThrow 版本，讓錯誤直接浮出給產品端處理。
/// </summary>
public interface ILineRichMenuAssignmentWorkflow
{
    /// <summary>
    /// 將應用程式 menu key 指定的選單指派給 LINE 使用者。
    /// </summary>
    /// <param name="lineUserId">要接收選單的 LINE userId。</param>
    /// <param name="menuKey">應用程式層級的 menu key，會透過 RichMenu id cache 或 catalog 解析。</param>
    /// <param name="cancellationToken">供 cache、catalog、state store 與 provider 操作使用的取消權杖。</param>
    Task<LineRichMenuAssignmentResult> AssignAsync(string lineUserId, string menuKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指派選單；若標準化結果不成功，則丟出 <see cref="LineRichMenuException"/>。
    /// </summary>
    /// <param name="lineUserId">要接收選單的 LINE userId。</param>
    /// <param name="menuKey">要指派的應用程式層級 menu key。</param>
    /// <param name="cancellationToken">供下游操作使用的取消權杖。</param>
    Task AssignOrThrowAsync(string lineUserId, string menuKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除 LINE 使用者的顯式 RichMenu 連結，並清除本機保存的應用程式指派狀態。
    /// </summary>
    /// <param name="lineUserId">要移除顯式 RichMenu 連結的 LINE userId。</param>
    /// <param name="cancellationToken">供 provider 與 state store 操作使用的取消權杖。</param>
    Task<LineRichMenuAssignmentResult> UnassignAsync(string lineUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除使用者 RichMenu 連結；若結果不成功，則丟出 <see cref="LineRichMenuException"/>。
    /// </summary>
    /// <param name="lineUserId">要移除顯式 RichMenu 連結的 LINE userId。</param>
    /// <param name="cancellationToken">供下游操作使用的取消權杖。</param>
    Task UnassignOrThrowAsync(string lineUserId, CancellationToken cancellationToken = default);
}
