/*
 * 檔案整體說明
 *
 * 目的與定位：RichMenu workflow 的標準化執行結果。 透過固定欄位表達成功、驗證失敗、LINE 拒絕、服務不可用或未預期錯誤，避免各產品自行解析例外。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuResult。
 * - 建立標準化 workflow 結果。 透過 static factory 建立成功與失敗結果，讓呼叫端程式碼保持清楚可讀。
 * - 取得 workflow 涉及的 LINE userId；若無資料則為 null。
 * - 取得 workflow 建立、連結或刪除的 LINE richMenuId；若無資料則為 null。
 * - 當失敗由 provider 或非預期錯誤造成時，取得原始例外。
 * 主要操作入口包含 Success、Failure。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuException.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
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
