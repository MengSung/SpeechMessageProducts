/*
 * 檔案整體說明
 *
 * 目的與定位：記錄單一應用程式 RichMenu definition 的同步結果。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuSyncItem。
 * - 取得此選單建立或重用的 LINE richMenuId；若無資料則為空字串。
 * 資料／選項欄位包含 LineRichMenuSyncItem、MenuKey、RichMenuId、Outcome、ErrorMessage，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 記錄單一應用程式 RichMenu definition 的同步結果。
/// </summary>
public sealed class LineRichMenuSyncItem
{
    /// <summary>
    /// 建立單一選單的同步結果項目。
    /// </summary>
    /// <param name="menuKey">catalog definition 中的應用程式層級 menu key。</param>
    /// <param name="richMenuId">已知的 LINE provider id；若同步失敗且尚未取得則可為空字串。</param>
    /// <param name="outcome">此 definition 的同步結果。</param>
    /// <param name="errorMessage">選填的 provider 或 validation 錯誤訊息。</param>
    public LineRichMenuSyncItem(
        string menuKey,
        string richMenuId,
        LineRichMenuSyncOutcome outcome,
        string? errorMessage = null)
    {
        MenuKey = menuKey;
        RichMenuId = richMenuId;
        Outcome = outcome;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 取得此項目代表的應用程式 menu key。
    /// </summary>
    public string MenuKey { get; }

    /// <summary>
    /// 取得此選單建立或重用的 LINE richMenuId；若無資料則為空字串。
    /// </summary>
    public string RichMenuId { get; }

    /// <summary>
    /// 取得此選單是新建、已最新或同步失敗。
    /// </summary>
    public LineRichMenuSyncOutcome Outcome { get; }

    /// <summary>
    /// 取得失敗項目的錯誤細節。
    /// </summary>
    public string? ErrorMessage { get; }
}
