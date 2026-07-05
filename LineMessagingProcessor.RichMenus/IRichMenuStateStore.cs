// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：interface IRichMenuStateStore
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 保存 LINE 使用者的應用程式層級 RichMenu 狀態。
/// 實作可以使用記憶體、資料庫或分散式快取，但必須保留足夠狀態，讓 assignment workflow 與到期 sweep 能可預期地還原前一個選單。
/// </summary>
public interface IRichMenuStateStore
{
    /// <summary>
    /// 取得單一 LINE 使用者已保存的狀態。
    /// </summary>
    /// <param name="lineUserId">要查詢的 LINE userId。</param>
    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
    Task<RichMenuUserState?> GetAsync(string lineUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 儲存或取代單一 LINE 使用者的 RichMenu 狀態。
    /// </summary>
    /// <param name="state">要保存的完整狀態紀錄。</param>
    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
    Task SetAsync(RichMenuUserState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除單一 LINE 使用者已保存的 RichMenu 狀態。
    /// </summary>
    /// <param name="lineUserId">要移除狀態的 LINE userId。</param>
    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
    Task RemoveAsync(string lineUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 回傳所有已達到期時間的狀態紀錄。
    /// </summary>
    /// <param name="now">用於到期比較的目前時間。</param>
    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
    Task<IReadOnlyList<RichMenuUserState>> GetExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
