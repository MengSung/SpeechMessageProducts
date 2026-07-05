namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu 共用流程使用的標準例外。
/// 共用層同時保留標準化結果，讓產品端可以用同一種方式記錄錯誤、回覆使用者或中斷必要流程。
/// </summary>
public sealed class LineRichMenuException : Exception
{
    /// <summary>
    /// 以低階 RichMenu workflow 的失敗結果建立例外。
    /// </summary>
    /// <param name="result">包含狀態、錯誤碼與錯誤訊息的標準化結果。</param>
    public LineRichMenuException(LineRichMenuResult result)
        : base(result?.ErrorMessage ?? "LINE RichMenu workflow failed.")
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    /// <summary>
    /// 以使用者指派 workflow 的失敗結果建立例外。
    /// </summary>
    /// <param name="result">包含指派狀態、錯誤碼、richMenuId 與錯誤訊息的標準化結果。</param>
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

    /// <summary>
    /// 取得低階 RichMenu workflow 的標準化結果。
    /// 即使例外源自 assignment workflow，也會轉成這個通用結果以維持舊呼叫端相容。
    /// </summary>
    public LineRichMenuResult Result { get; }

    /// <summary>
    /// 取得原始 assignment workflow 結果；只有指派/解除綁定流程失敗時會有值。
    /// </summary>
    public LineRichMenuAssignmentResult? AssignmentResult { get; }
}
