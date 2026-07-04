namespace LineMessagingProcessor.Workflows;

/// <summary>
/// RichMenu 共用 workflow 的標準結果。
/// SendAsync 類流程回傳結果而非直接吞掉例外，讓產品端可以依重要性決定是否中斷流程。
/// </summary>
public sealed class LineRichMenuResult
{
    private LineRichMenuResult(
        bool succeeded,
        LineNotificationStatus status,
        string? userId,
        string? richMenuId,
        string? errorCode,
        string? errorMessage,
        Exception? exception,
        IReadOnlyDictionary<string, string> metadata)
    {
        Succeeded = succeeded;
        Status = status;
        UserId = userId;
        RichMenuId = richMenuId;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Exception = exception;
        Metadata = metadata;
    }

    public bool Succeeded { get; }

    public LineNotificationStatus Status { get; }

    public string? UserId { get; }

    public string? RichMenuId { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public Exception? Exception { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static LineRichMenuResult Success(string userId, string? richMenuId, IReadOnlyDictionary<string, string> metadata)
        => new(true, LineNotificationStatus.Succeeded, userId, richMenuId, null, null, null, metadata);

    public static LineRichMenuResult Failure(
        string? userId,
        string? richMenuId,
        LineNotificationStatus status,
        string errorCode,
        string errorMessage,
        Exception? exception,
        IReadOnlyDictionary<string, string>? metadata)
        => new(false, status, userId, richMenuId, errorCode, errorMessage, exception, metadata ?? new Dictionary<string, string>());
}
