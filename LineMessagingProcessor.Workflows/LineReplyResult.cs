namespace LineMessagingProcessor.Workflows;

/// <summary>
/// LINE reply-token workflow 的標準化結果。
/// 產品層可以依狀態判斷是否要記錄、重拋或回報錯誤；
/// 共用層只負責保留 LINE API 呼叫結果與錯誤分類。
/// </summary>
public sealed class LineReplyResult
{
    private LineReplyResult(
        LineReplyRequest? request,
        LineNotificationStatus status,
        string? errorCode,
        string? errorMessage,
        Exception? exception)
    {
        Request = request;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    public LineReplyRequest? Request { get; }

    public LineNotificationStatus Status { get; }

    public bool Succeeded => Status == LineNotificationStatus.Succeeded;

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public Exception? Exception { get; }

    public static LineReplyResult Success(LineReplyRequest request)
        => new(request, LineNotificationStatus.Succeeded, null, null, null);

    public static LineReplyResult Failure(
        LineReplyRequest? request,
        LineNotificationStatus status,
        string errorCode,
        string errorMessage,
        Exception? exception = null)
        => new(request, status, errorCode, errorMessage, exception);
}
