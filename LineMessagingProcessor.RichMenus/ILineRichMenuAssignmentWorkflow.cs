namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu 指派工作流入口。
/// 一般流程可使用回傳結果版本；必要流程可使用 OrThrow 版本，讓錯誤直接浮出給產品端處理。
/// </summary>
public interface ILineRichMenuAssignmentWorkflow
{
    Task<LineRichMenuAssignmentResult> AssignAsync(string lineUserId, string menuKey, CancellationToken cancellationToken = default);

    Task AssignOrThrowAsync(string lineUserId, string menuKey, CancellationToken cancellationToken = default);

    Task<LineRichMenuAssignmentResult> UnassignAsync(string lineUserId, CancellationToken cancellationToken = default);

    Task UnassignOrThrowAsync(string lineUserId, CancellationToken cancellationToken = default);
}
