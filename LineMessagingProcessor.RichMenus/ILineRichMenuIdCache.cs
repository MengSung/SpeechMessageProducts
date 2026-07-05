// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/ILineRichMenuIdCache.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：interface ILineRichMenuIdCache
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 保存產品內部 menu key 與 LINE richMenuId 的對照。
/// 呼叫端只依賴這個抽象，實際儲存可由 in-memory、資料庫或 Redis 實作。
/// </summary>
public interface ILineRichMenuIdCache
{
    /// <summary>
    /// 嘗試取得應用程式 menu key 已解析出的 LINE richMenuId。
    /// </summary>
    /// <param name="menuKey">應用程式層級的 menu key。</param>
    /// <param name="richMenuId">方法回傳 true 時，代表已快取的 LINE richMenuId。</param>
    bool TryGet(string menuKey, out string richMenuId);

    /// <summary>
    /// 儲存或取代某個應用程式 menu key 對應的 LINE richMenuId。
    /// </summary>
    /// <param name="menuKey">應用程式層級的 menu key。</param>
    /// <param name="richMenuId">provisioning 過程中建立或發現的 LINE provider id。</param>
    void Set(string menuKey, string richMenuId);

    /// <summary>
    /// 移除已快取的應用程式 menu key 對照。
    /// </summary>
    /// <param name="menuKey">要移除的應用程式層級 menu key。</param>
    void Remove(string menuKey);

    /// <summary>
    /// 回傳目前所有應用程式 menu key 到 LINE richMenuId 對照的時間點快照。
    /// </summary>
    IReadOnlyDictionary<string, string> Snapshot();

    /// <summary>
    /// 以新的對照集合取代整份 cache。
    /// </summary>
    /// <param name="values">要保留的 menu key 到 richMenuId 對照。</param>
    void SetSnapshot(IReadOnlyDictionary<string, string> values);
}
