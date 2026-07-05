// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineRichMenuSyncReport
// 主要成員：CreatedMenuKeys、ReusedMenuKeys、DeletedRichMenuIds、Items
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 描述 RichMenu catalog 與 LINE 同步後的結果。
/// report 將 provider ids、新建/重用/刪除集合與逐選單 outcome 分開，
/// 讓呼叫端可記錄高階佈署狀態，同時保留調查單一選單失敗所需的資訊。
/// </summary>
public sealed class LineRichMenuSyncReport
{
    /// <summary>
    /// 建立 RichMenu 同步報告。
    /// </summary>
    /// <param name="menuIds">已解析的應用程式 menu key 到 LINE richMenuId 對照。</param>
    /// <param name="createdMenuKeys">本次同步中新建 LINE RichMenu 的應用程式 menu keys。</param>
    /// <param name="reusedMenuKeys">與既有 fingerprinted LINE RichMenu 相符並被重用的應用程式 menu keys。</param>
    /// <param name="deletedRichMenuIds">cleanup 期間刪除的 provider RichMenu ids。</param>
    /// <param name="items">選填的逐 definition 同步結果。</param>
    public LineRichMenuSyncReport(
        IReadOnlyDictionary<string, string> menuIds,
        IReadOnlyList<string> createdMenuKeys,
        IReadOnlyList<string> reusedMenuKeys,
        IReadOnlyList<string> deletedRichMenuIds,
        IReadOnlyList<LineRichMenuSyncItem>? items = null)
    {
        MenuIds = menuIds ?? new Dictionary<string, string>();
        CreatedMenuKeys = createdMenuKeys ?? Array.Empty<string>();
        ReusedMenuKeys = reusedMenuKeys ?? Array.Empty<string>();
        DeletedRichMenuIds = deletedRichMenuIds ?? Array.Empty<string>();
        Items = items ?? Array.Empty<LineRichMenuSyncItem>();
    }

    /// <summary>
    /// 取得已解析的應用程式 menu key 到 LINE richMenuId 對照。
    /// assignment workflows 會透過 <see cref="ILineRichMenuIdCache"/> 使用這些值。
    /// </summary>
    public IReadOnlyDictionary<string, string> MenuIds { get; }

    /// <summary>
    /// 取得本次需要新建並上傳 LINE RichMenu 的 menu keys。
    /// </summary>
    public IReadOnlyList<string> CreatedMenuKeys { get; }

    /// <summary>
    /// 取得 fingerprint 與既有 LINE RichMenu 相符、因此被重用的 menu keys。
    /// </summary>
    public IReadOnlyList<string> ReusedMenuKeys { get; }

    /// <summary>
    /// 取得已從 LINE 移除的 richMenuIds；這些選單已不再由目前 catalog 擁有。
    /// </summary>
    public IReadOnlyList<string> DeletedRichMenuIds { get; }

    /// <summary>
    /// 取得逐選單同步結果，包含未中止整體同步流程的單一選單失敗。
    /// </summary>
    public IReadOnlyList<LineRichMenuSyncItem> Items { get; }
}
