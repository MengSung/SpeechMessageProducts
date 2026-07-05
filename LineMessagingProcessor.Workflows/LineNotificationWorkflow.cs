// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs
// 所屬區塊：LINE 共用 workflow 模組與測試，放置可跨產品重用的訊息處理流程。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineNotificationWorkflow
// 主要成員：SendAsync、SendOrThrowAsync、Validate、BuildMessages
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
/// 共用 LINE 通知工作流。這一層只負責驗證共用請求、轉成 SDK 訊息、呼叫 processor、標準化結果。
/// CRM 更新、付款語意與會員資料不允許放進這裡，必須留在各產品自己的工作流。
/// </summary>
public sealed class LineNotificationWorkflow : ILineNotificationWorkflow
{
    private readonly LineMessagingProcessorClass _processor;

    public LineNotificationWorkflow(LineMessagingProcessorClass processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    }

    public async Task<LineNotificationResult> SendAsync(LineNotificationRequest request)
    {
        var validation = Validate(request);
        if (validation != null)
        {
            return validation;
        }

        try
        {
            var recipientId = request.Recipient.PrimaryId!;
            var messages = BuildMessages(request.Content);
            await _processor.SendMessagesAsync(recipientId, messages, request.RetryKey).ConfigureAwait(false);
            return LineNotificationResult.Success(request);
        }
        catch (LineResponseException ex)
        {
            return LineNotificationResult.Failure(
                request,
                LineNotificationStatus.ProviderRejected,
                "line-provider-rejected",
                ex.Message,
                ex);
        }
        catch (HttpRequestException ex)
        {
            return LineNotificationResult.Failure(
                request,
                LineNotificationStatus.ProviderUnavailable,
                "line-provider-unavailable",
                ex.Message,
                ex);
        }
        catch (TaskCanceledException ex)
        {
            return LineNotificationResult.Failure(
                request,
                LineNotificationStatus.ProviderUnavailable,
                "line-provider-timeout",
                ex.Message,
                ex);
        }
        catch (Exception ex)
        {
            return LineNotificationResult.Failure(
                request,
                LineNotificationStatus.UnexpectedError,
                "line-unexpected-error",
                ex.Message,
                ex);
        }
    }

    public async Task SendOrThrowAsync(LineNotificationRequest request)
    {
        var result = await SendAsync(request).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineNotificationException(result);
        }
    }

    private static LineNotificationResult? Validate(LineNotificationRequest? request)
    {
        if (request == null)
        {
            return LineNotificationResult.Failure(
                null,
                LineNotificationStatus.ValidationFailed,
                "line-request-required",
                "Line notification request is required.");
        }

        if (request.Recipient == null)
        {
            return LineNotificationResult.Failure(
                request,
                LineNotificationStatus.ValidationFailed,
                "line-recipient-required",
                "Line notification recipient is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Recipient.PrimaryId))
        {
            return LineNotificationResult.Failure(
                request,
                LineNotificationStatus.ValidationFailed,
                "line-recipient-id-required",
                "Line notification recipient id is required.");
        }

        if (request.Recipient.Kind == LineNotificationRecipientKind.Users && request.Recipient.Ids.Count != 1)
        {
            return LineNotificationResult.Failure(
                request,
                LineNotificationStatus.ValidationFailed,
                "line-recipient-users-not-supported",
                "Line notification workflow currently supports exactly one user recipient. Use one request per user.");
        }

        if (request.Content == null)
        {
            return LineNotificationResult.Failure(
                request,
                LineNotificationStatus.ValidationFailed,
                "line-content-required",
                "Line notification content is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Content.Text) &&
            (request.Content.SdkMessages == null || request.Content.SdkMessages.Count == 0))
        {
            return LineNotificationResult.Failure(
                request,
                LineNotificationStatus.ValidationFailed,
                "line-content-empty",
                "Line notification content is empty.");
        }

        return null;
    }

    private static IList<ISendMessage> BuildMessages(LineNotificationContent content)
    {
        if (content.SdkMessages != null && content.SdkMessages.Count > 0)
        {
            return content.SdkMessages.ToList();
        }

        return new List<ISendMessage> { new TextMessage(content.Text!) };
    }
}
