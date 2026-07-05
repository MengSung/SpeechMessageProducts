// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：interface IRichMenuExpirationSweepWorkflow
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 還原或解除已到期的暫時性 RichMenu 指派。
/// </summary>
public interface IRichMenuExpirationSweepWorkflow
{
    /// <summary>
    /// 處理已到期的 RichMenu 使用者狀態紀錄。
    /// </summary>
    /// <param name="now">用來判斷哪些指派已到期的目前時間。</param>
    /// <param name="cancellationToken">傳入 state store 與 assignment workflow 的取消權杖。</param>
    Task<RichMenuExpirationSweepReport> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
