/*
 * 檔案整體說明
 *
 * 目的與定位：共用 LINE 通知工作流。這一層只負責驗證共用請求、轉成 SDK 訊息、呼叫 processor、標準化結果。 CRM 更新、付款語意與會員資料不允許放進這裡，必須留在各產品自己的工作流。
 *
 * 主要功能與流程：
 * 本檔宣告 LineNotificationWorkflow。
 * 主要操作入口包含 SendAsync、SendOrThrowAsync、Validate、BuildMessages。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs、
 * LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs、
 * LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs、
 * SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs、
 * SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs 追查目前工作樹中的合作／使用位置。
 */
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
