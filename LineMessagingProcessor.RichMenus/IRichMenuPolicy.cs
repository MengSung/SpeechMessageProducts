// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：interface IRichMenuPolicy
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 定義一條可判斷使用者 RichMenu 是否應改變的規則。
/// policy 刻意保持小而可組合；orchestrator 會評估所有 policy，並只套用強度最高的 decision。
/// </summary>
public interface IRichMenuPolicy
{
    /// <summary>
    /// 評估傳入 context，並回傳 RichMenu decision。
    /// </summary>
    /// <param name="context">policy 可使用的使用者與訊息 context。</param>
    /// <param name="cancellationToken">供需要非同步資料的 policy 使用的取消權杖。</param>
    Task<RichMenuDecision> DecideAsync(RichMenuContext context, CancellationToken cancellationToken = default);
}
