namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用 LINE 通知工作流入口。SendAsync 給可容忍失敗的流程，SendOrThrowAsync 給付款指示等必須浮出錯誤的流程。
/// </summary>
public interface ILineNotificationWorkflow
{
    Task<LineNotificationResult> SendAsync(LineNotificationRequest request);

    Task SendOrThrowAsync(LineNotificationRequest request);
}
