/*
 * 檔案整體說明
 *
 * 目的與定位：LINE reply-token workflow 的標準化結果。 產品層可以依狀態判斷是否要記錄、重拋或回報錯誤； 共用層只負責保留 LINE API 呼叫結果與錯誤分類。
 *
 * 主要功能與流程：
 * 本檔宣告 LineReplyResult。
 * 主要操作入口包含 Success、Failure。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Workflows/ILineReplyWorkflow.cs、
 * LineMessagingProcessor.Workflows/LineReplyException.cs、LineMessagingProcessor.Workflows/LineReplyWorkflow.cs
 * 追查目前工作樹中的合作／使用位置。
 */
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
