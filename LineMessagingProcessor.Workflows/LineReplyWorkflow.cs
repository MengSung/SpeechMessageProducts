// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.Workflows/LineReplyWorkflow.cs
// 所屬區塊：LINE 共用 workflow 模組與測試，放置可跨產品重用的訊息處理流程。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineReplyWorkflow
// 主要成員：ReplyAsync、ReplyOrThrowAsync、Validate
// 引用命名空間：Line.Messaging、LineMessagingProcessor
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
