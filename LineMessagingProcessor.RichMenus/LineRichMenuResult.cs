namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu workflow 的標準化執行結果。
/// 透過固定欄位表達成功、驗證失敗、LINE 拒絕、服務不可用或未預期錯誤，避免各產品自行解析例外。
/// </summary>
public sealed class LineRichMenuResult
{
    private LineRichMenuResult(
        bool succeeded,
        LineRichMenuStatus status,
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

    public LineRichMenuStatus Status { get; }

    public string? UserId { get; }

    public string? RichMenuId { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public Exception? Exception { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static LineRichMenuResult Success(string userId, string? richMenuId, IReadOnlyDictionary<string, string> metadata)
        => new(true, LineRichMenuStatus.Succeeded, userId, richMenuId, null, null, null, metadata);

    public static LineRichMenuResult Failure(
        string? userId,
        string? richMenuId,
        LineRichMenuStatus status,
        string errorCode,
        string errorMessage,
        Exception? exception,
        IReadOnlyDictionary<string, string>? metadata)
        => new(false, status, userId, richMenuId, errorCode, errorMessage, exception, metadata ?? new Dictionary<string, string>());
}

