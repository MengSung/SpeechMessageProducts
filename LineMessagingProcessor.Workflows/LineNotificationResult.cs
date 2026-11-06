/*
 * 檔案整體說明
 *
 * 目的與定位：共用 LINE 通知結果。SendAsync 永遠回傳這個模型，讓呼叫端可以自行決定失敗是否阻斷產品流程。
 *
 * 主要功能與流程：
 * 本檔宣告 LineNotificationResult。
 * 主要操作入口包含 Success、Failure。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/ChurchReportLineBindingNotificationServiceTests.cs、
 * ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PaymentNotificationServiceWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs、
 * LineMessagingProcessor.Workflows/ILineNotificationWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用 LINE 通知結果。SendAsync 永遠回傳這個模型，讓呼叫端可以自行決定失敗是否阻斷產品流程。
/// </summary>
public sealed class LineNotificationResult
{
    private LineNotificationResult(
        bool succeeded,
        LineNotificationStatus status,
        LineNotificationRecipient? recipient,
        string? retryKey,
        string? errorCode,
        string? errorMessage,
        Exception? exception,
        IReadOnlyDictionary<string, string> metadata)
    {
        Succeeded = succeeded;
        Status = status;
        Recipient = recipient;
        RetryKey = retryKey;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Exception = exception;
        Metadata = metadata;
    }

    public bool Succeeded { get; }

    public LineNotificationStatus Status { get; }

    public LineNotificationRecipient? Recipient { get; }

    public string? RetryKey { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public Exception? Exception { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static LineNotificationResult Success(LineNotificationRequest request)
        => new(true, LineNotificationStatus.Succeeded, request.Recipient, request.RetryKey, null, null, null, request.Metadata);

    public static LineNotificationResult Failure(
        LineNotificationRequest? request,
        LineNotificationStatus status,
        string errorCode,
        string errorMessage,
        Exception? exception = null)
        => new(
            false,
            status,
            request?.Recipient,
            request?.RetryKey,
            errorCode,
            errorMessage,
            exception,
            request?.Metadata ?? new Dictionary<string, string>());
}
