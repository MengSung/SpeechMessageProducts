// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineRichMenuCreateUploadAndLinkRequest
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
/// 建立 RichMenu、上傳 PNG 並連結到 LINE 使用者的標準請求。
/// RichMenu 版面與圖片來源由呼叫端提供，workflow 僅負責穩定串接 LINE RichMenu API。
/// </summary>
public sealed class LineRichMenuCreateUploadAndLinkRequest
{
    /// <summary>
    /// 要連結新 RichMenu 的 LINE 使用者 id。
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// 要建立到 LINE 的 RichMenu 版面、尺寸、chat bar 文字與 action area 設定。
    /// </summary>
    public required RichMenu RichMenu { get; init; }

    /// <summary>
    /// 開啟 PNG 圖片 stream 的 factory。
    /// 每次呼叫 workflow 時都應回傳可讀取的新 stream，讓上傳流程能完整讀取圖片內容。
    /// </summary>
    public required Func<Stream> PngImageStreamFactory { get; init; }

    /// <summary>
    /// 呼叫端提供的追蹤資料；結果成功或失敗時都會保留。
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

