/*
 * 檔案整體說明
 *
 * 目的與定位：RichMenu workflows 回傳的標準化狀態值。 這些值讓呼叫端不必直接依賴 LINE SDK exception 類型。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuStatus。
 * - 在嘗試呼叫 LINE API 前，本機 request 驗證已失敗。
 * - LINE 以 provider response 拒絕 request，例如 payload 錯誤或授權失敗。
 * - LINE 或網路路徑無法使用、逾時，或在取得可信 provider response 前失敗。
 * - 已知 provider 或 validation 分類以外的非預期應用程式錯誤。
 * 資料／選項欄位包含 LineRichMenuStatus，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuAssignmentResult.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuResult.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu workflows 回傳的標準化狀態值。
/// 這些值讓呼叫端不必直接依賴 LINE SDK exception 類型。
/// </summary>
public enum LineRichMenuStatus
{
    /// <summary>
    /// workflow 已成功完成。
    /// </summary>
    Succeeded,

    /// <summary>
    /// 在嘗試呼叫 LINE API 前，本機 request 驗證已失敗。
    /// </summary>
    ValidationFailed,

    /// <summary>
    /// LINE 以 provider response 拒絕 request，例如 payload 錯誤或授權失敗。
    /// </summary>
    ProviderRejected,

    /// <summary>
    /// LINE 或網路路徑無法使用、逾時，或在取得可信 provider response 前失敗。
    /// </summary>
    ProviderUnavailable,

    /// <summary>
    /// 已知 provider 或 validation 分類以外的非預期應用程式錯誤。
    /// </summary>
    UnexpectedError
}
