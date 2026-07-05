// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：interface ILineRichMenuWorkflow
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：Line.Messaging
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu 建立、上傳、連結與解除連結的共用流程介面。
/// 呼叫端只需要提供標準請求；實作會統一處理 LINE API 呼叫、錯誤轉換與結果包裝。
/// </summary>
public interface ILineRichMenuWorkflow
{
    /// <summary>
    /// 建立 LINE RichMenu、上傳圖片，並直接連結到一位使用者。
    /// </summary>
    /// <param name="request">此操作需要的 user id、選單版面、圖片 stream factory 與 metadata。</param>
    Task<LineRichMenuResult> CreateUploadAndLinkAsync(LineRichMenuCreateUploadAndLinkRequest request);

    /// <summary>
    /// 執行 <see cref="CreateUploadAndLinkAsync"/>；若失敗則丟出 <see cref="LineRichMenuException"/>。
    /// </summary>
    /// <param name="request">建立、上傳與連結的 request。</param>
    Task CreateUploadAndLinkOrThrowAsync(LineRichMenuCreateUploadAndLinkRequest request);

    /// <summary>
    /// 解除使用者目前 RichMenu 連結，並刪除該連結指向的 provider RichMenu。
    /// </summary>
    /// <param name="request">刪除與解除連結操作需要的 user id 與 metadata。</param>
    Task<LineRichMenuResult> DeleteLinkedRichMenuAsync(LineRichMenuDeleteLinkedRequest request);

    /// <summary>
    /// 執行 <see cref="DeleteLinkedRichMenuAsync"/>；若失敗則丟出 <see cref="LineRichMenuException"/>。
    /// </summary>
    /// <param name="request">刪除與解除連結 request。</param>
    Task DeleteLinkedRichMenuOrThrowAsync(LineRichMenuDeleteLinkedRequest request);
}

