// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineRichMenuResult.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineRichMenuResult
// 主要成員：Success、Failure、Succeeded、Status、UserId、RichMenuId、ErrorCode、ErrorMessage、Exception
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu workflow 的標準化執行結果。
/// 透過固定欄位表達成功、驗證失敗、LINE 拒絕、服務不可用或未預期錯誤，避免各產品自行解析例外。
/// </summary>
public sealed class LineRichMenuResult
{
    /// <summary>
    /// 建立標準化 workflow 結果。
    /// 透過 static factory 建立成功與失敗結果，讓呼叫端程式碼保持清楚可讀。
    /// </summary>
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

    /// <summary>
    /// 取得 workflow 是否成功完成。
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// 取得標準化 workflow 狀態。
    /// </summary>
    public LineRichMenuStatus Status { get; }

    /// <summary>
    /// 取得 workflow 涉及的 LINE userId；若無資料則為 null。
    /// </summary>
    public string? UserId { get; }

    /// <summary>
    /// 取得 workflow 建立、連結或刪除的 LINE richMenuId；若無資料則為 null。
    /// </summary>
    public string? RichMenuId { get; }

    /// <summary>
    /// workflow 失敗時，取得穩定的應用程式錯誤代碼。
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// workflow 失敗時，取得可讀的失敗細節。
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 當失敗由 provider 或非預期錯誤造成時，取得原始例外。
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// 取得呼叫端提供且應隨成功或失敗結果一起流動的 metadata。
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// 建立成功的 RichMenu workflow 結果。
    /// </summary>
    /// <param name="userId">此操作涉及的 LINE userId。</param>
    /// <param name="richMenuId">此操作涉及的 provider richMenuId；若無資料則為 null。</param>
    /// <param name="metadata">要保留在結果中的呼叫端 metadata。</param>
    public static LineRichMenuResult Success(string userId, string? richMenuId, IReadOnlyDictionary<string, string> metadata)
        => new(true, LineRichMenuStatus.Succeeded, userId, richMenuId, null, null, null, metadata);

    /// <summary>
    /// 建立失敗的 RichMenu workflow 結果，並包含標準化狀態與診斷資訊。
    /// </summary>
    /// <param name="userId">失敗操作涉及的 LINE userId；若已知才提供。</param>
    /// <param name="richMenuId">失敗前已涉及的 provider richMenuId；若已知才提供。</param>
    /// <param name="status">標準化失敗狀態。</param>
    /// <param name="errorCode">穩定的應用程式錯誤代碼。</param>
    /// <param name="errorMessage">可讀的失敗細節。</param>
    /// <param name="exception">捕捉到的原始例外；若沒有則為 null。</param>
    /// <param name="metadata">要保留在結果中的呼叫端 metadata。</param>
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

