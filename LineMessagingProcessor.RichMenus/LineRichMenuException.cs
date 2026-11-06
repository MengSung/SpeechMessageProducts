/*
 * 檔案整體說明
 *
 * 目的與定位：RichMenu 共用流程使用的標準例外。 共用層同時保留標準化結果，讓產品端可以用同一種方式記錄錯誤、回覆使用者或中斷必要流程。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuException。
 * - 以低階 RichMenu workflow 的失敗結果建立例外。
 * - 取得低階 RichMenu workflow 的標準化結果。 即使例外源自 assignment workflow，也會轉成這個通用結果以維持舊呼叫端相容。
 * - 取得原始 assignment workflow 結果；只有指派/解除綁定流程失敗時會有值。
 * 資料／選項欄位包含 Result、AssignmentResult，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs、
 * LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
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
