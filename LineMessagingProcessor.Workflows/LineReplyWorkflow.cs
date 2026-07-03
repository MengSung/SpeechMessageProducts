using Line.Messaging;
using LineMessagingProcessor;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用的 LINE reply-token workflow。
/// 這一層只做 reply token 與 SDK message 的驗證、呼叫 LineMessagingProcessor，
/// 並將 LINE API 例外轉成穩定的 workflow result；ChurchReport 的文案、
/// CRM 判斷、MVC Action 流程都必須留在 ChurchReport。
/// </summary>
public sealed class LineReplyWorkflow : ILineReplyWorkflow
{
    private readonly LineMessagingProcessorClass _processor;

    public LineReplyWorkflow(LineMessagingProcessorClass processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    }

    public async Task<LineReplyResult> ReplyAsync(LineReplyRequest request)
    {
        var validation = Validate(request);
        if (validation != null)
        {
            return validation;
        }

        try
        {
            await _processor.ReplyMessagesAsync(
                request.ReplyToken!,
                request.Messages!.ToList()).ConfigureAwait(false);

            return LineReplyResult.Success(request);
        }
        catch (LineResponseException ex)
        {
            return LineReplyResult.Failure(
                request,
                LineNotificationStatus.ProviderRejected,
                "line-reply-provider-rejected",
                ex.Message,
                ex);
        }
        catch (HttpRequestException ex)
        {
            return LineReplyResult.Failure(
                request,
                LineNotificationStatus.ProviderUnavailable,
                "line-reply-provider-unavailable",
                ex.Message,
                ex);
        }
        catch (TaskCanceledException ex)
        {
            return LineReplyResult.Failure(
                request,
                LineNotificationStatus.ProviderUnavailable,
                "line-reply-provider-timeout",
                ex.Message,
                ex);
        }
        catch (Exception ex)
        {
            return LineReplyResult.Failure(
                request,
                LineNotificationStatus.UnexpectedError,
                "line-reply-unexpected-error",
                ex.Message,
                ex);
        }
    }

    public async Task ReplyOrThrowAsync(LineReplyRequest request)
    {
        var result = await ReplyAsync(request).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineReplyException(result);
        }
    }

    private static LineReplyResult? Validate(LineReplyRequest? request)
    {
        if (request == null)
        {
            return LineReplyResult.Failure(
                null,
                LineNotificationStatus.ValidationFailed,
                "line-reply-request-required",
                "Line reply request is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ReplyToken))
        {
            return LineReplyResult.Failure(
                request,
                LineNotificationStatus.ValidationFailed,
                "line-reply-token-required",
                "Line reply token is required.");
        }

        if (request.Messages == null || request.Messages.Count == 0)
        {
            return LineReplyResult.Failure(
                request,
                LineNotificationStatus.ValidationFailed,
                "line-reply-message-required",
                "At least one LINE reply message is required.");
        }

        return null;
    }
}
