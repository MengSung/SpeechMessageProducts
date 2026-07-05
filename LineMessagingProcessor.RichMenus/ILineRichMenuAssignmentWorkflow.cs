// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：interface ILineRichMenuAssignmentWorkflow
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu 指派工作流入口。
/// 一般流程可使用回傳結果版本；必要流程可使用 OrThrow 版本，讓錯誤直接浮出給產品端處理。
/// </summary>
public interface ILineRichMenuAssignmentWorkflow
{
    /// <summary>
    /// 將應用程式 menu key 指定的選單指派給 LINE 使用者。
    /// </summary>
    /// <param name="lineUserId">要接收選單的 LINE userId。</param>
    /// <param name="menuKey">應用程式層級的 menu key，會透過 RichMenu id cache 或 catalog 解析。</param>
    /// <param name="cancellationToken">供 cache、catalog、state store 與 provider 操作使用的取消權杖。</param>
    Task<LineRichMenuAssignmentResult> AssignAsync(string lineUserId, string menuKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指派選單；若標準化結果不成功，則丟出 <see cref="LineRichMenuException"/>。
    /// </summary>
    /// <param name="lineUserId">要接收選單的 LINE userId。</param>
    /// <param name="menuKey">要指派的應用程式層級 menu key。</param>
    /// <param name="cancellationToken">供下游操作使用的取消權杖。</param>
    Task AssignOrThrowAsync(string lineUserId, string menuKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除 LINE 使用者的顯式 RichMenu 連結，並清除本機保存的應用程式指派狀態。
    /// </summary>
    /// <param name="lineUserId">要移除顯式 RichMenu 連結的 LINE userId。</param>
    /// <param name="cancellationToken">供 provider 與 state store 操作使用的取消權杖。</param>
    Task<LineRichMenuAssignmentResult> UnassignAsync(string lineUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除使用者 RichMenu 連結；若結果不成功，則丟出 <see cref="LineRichMenuException"/>。
    /// </summary>
    /// <param name="lineUserId">要移除顯式 RichMenu 連結的 LINE userId。</param>
    /// <param name="cancellationToken">供下游操作使用的取消權杖。</param>
    Task UnassignOrThrowAsync(string lineUserId, CancellationToken cancellationToken = default);
}
