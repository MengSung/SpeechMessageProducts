// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineRichMenuSyncItem
// 主要成員：MenuKey、RichMenuId、Outcome、ErrorMessage
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
