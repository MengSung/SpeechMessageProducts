// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineRichMenuException.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineRichMenuException
// 主要成員：Result、AssignmentResult
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
