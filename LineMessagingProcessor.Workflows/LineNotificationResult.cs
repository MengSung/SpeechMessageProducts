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
