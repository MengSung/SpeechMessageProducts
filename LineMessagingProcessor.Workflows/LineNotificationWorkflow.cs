/*
 * 檔案整體說明
 *
 * 目的與定位：共用LINE相容通知工作流，驗證請求、組SDK訊息並標準化結果；依PROP-005，
 * 任意推播統一委派LineLegacySendWorkflow，不另走Core或建立平行傳輸。CRM、付款語意與會員授權留產品。
 *
 * 主要功能與流程：
 * 本檔宣告 LineNotificationWorkflow。
 * 主要操作入口包含 SendAsync、SendOrThrowAsync、Validate、BuildMessages。
 *
 * 使用邊界與維護：
 * workflow只保留綁定同一頻道的借用Legacy入口，processor／SDK／HttpClient仍由Host或呼叫端
 * 唯一擁有並設定有限timeout；每次request、結果與訊息副本僅保留至await排空，不跨作業共用。
 * 不建立queue、cache、timer或背景工作，不以呼叫參數當授權，失敗結果保存真正Exception供最終Host共用上報。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs 追查目前工作樹中的合作／使用位置。
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
    private readonly LineLegacySendWorkflow legacy;

    /// <summary>借用頻道owner的processor並建立唯一legacy委派，不取得任何傳輸資源所有權。</summary>
    /// <param name="processor">已由Host完成頻道解析與生命週期管理的processor。</param>
    public LineNotificationWorkflow(LineMessagingProcessorClass processor)
    {
        legacy = new LineLegacySendWorkflow(processor ?? throw new ArgumentNullException(nameof(processor)));
    }

    /// <summary>在I/O前驗請求，將訊息及原來源鍵交唯一Legacy入口；失敗轉明確結果，不吞成成功。</summary>
    /// <param name="request">只屬當次操作的請求，不構成會員／教會授權。</param>
    /// <returns>已等待傳輸排空的結果；真正失敗仍帶原Exception供最終owner處理。</returns>
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
            await legacy.SendAsync(recipientId, messages, request.RetryKey).ConfigureAwait(false);
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

    /// <summary>沿同一入口完成通知；失敗保留標準結果並拋出給最終產品共用例外入口。</summary>
    /// <param name="request">當次已由產品授權的相容請求。</param>
    /// <returns>完成並排空的通知作業，沒有fire-and-forget。</returns>
    public async Task SendOrThrowAsync(LineNotificationRequest request)
    {
        var result = await SendAsync(request).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new LineNotificationException(result);
        }
    }

    /// <summary>純值驗證且不保留請求，明拒多收件者以免只送第一位；不進行授權或I/O。</summary>
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

    /// <summary>建立當次集合副本，SDK在首個await前序列化；不發布可變共享訊息集合。</summary>
    private static IList<ISendMessage> BuildMessages(LineNotificationContent content)
    {
        if (content.SdkMessages != null && content.SdkMessages.Count > 0)
        {
            return content.SdkMessages.ToList();
        }

        return new List<ISendMessage> { new TextMessage(content.Text!) };
    }
}
