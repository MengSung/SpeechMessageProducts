namespace LineMessagingProcessor.Workflows;

/// <summary>
/// LINE reply-token 發送流程的共用介面。
/// reply-token 來自 webhook 事件，只能用來回覆該事件；
/// 它和 push notification 的 user id / group id / room id 是不同語意，
/// 因此獨立成 reply workflow，不混入 <see cref="ILineNotificationWorkflow"/>。
/// </summary>
public interface ILineReplyWorkflow
{
    Task<LineReplyResult> ReplyAsync(LineReplyRequest request);

    Task ReplyOrThrowAsync(LineReplyRequest request);
}
