namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu 共用流程使用的標準例外。
/// 共用層同時保留標準化結果，讓產品端可以用同一種方式記錄錯誤、回覆使用者或中斷必要流程。
/// </summary>
public sealed class LineRichMenuException : Exception
{
    public LineRichMenuException(LineRichMenuResult result)
        : base(result?.ErrorMessage ?? "LINE RichMenu workflow failed.")
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public LineRichMenuException(LineRichMenuAssignmentResult result)
        : base(result?.ErrorMessage ?? "LINE RichMenu assignment failed.")
    {
        AssignmentResult = result ?? throw new ArgumentNullException(nameof(result));
        Result = LineRichMenuResult.Failure(
            null,
            result.RichMenuId,
            result.Status,
            result.ErrorCode ?? "line-richmenu-assignment-failed",
            result.ErrorMessage ?? "LINE RichMenu assignment failed.",
            null,
            new Dictionary<string, string>());
    }

    public LineRichMenuResult Result { get; }

    public LineRichMenuAssignmentResult? AssignmentResult { get; }
}
