namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 必達 reply-token 流程失敗時拋出的例外。
/// 呼叫端若使用 ReplyOrThrowAsync，代表該回覆不能被靜默吞掉，
/// 因此例外會攜帶完整 <see cref="LineReplyResult"/> 供產品層記錄與診斷。
/// </summary>
public sealed class LineReplyException : Exception
{
    public LineReplyException(LineReplyResult result)
        : base(result?.ErrorMessage ?? "LINE reply failed.")
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public LineReplyResult Result { get; }
}
