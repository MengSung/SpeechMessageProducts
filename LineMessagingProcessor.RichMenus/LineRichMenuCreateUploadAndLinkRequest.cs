/*
 * 檔案整體說明
 *
 * 目的與定位：建立 RichMenu、上傳 PNG 並連結到 LINE 使用者的標準請求。 RichMenu 版面與圖片來源由呼叫端提供，workflow 僅負責穩定串接 LINE RichMenu API。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuCreateUploadAndLinkRequest。
 * - 要連結新 RichMenu 的 LINE 使用者 id。
 * - 要建立到 LINE 的 RichMenu 版面、尺寸、chat bar 文字與 action area 設定。
 * - 開啟 PNG 圖片 stream 的 factory。 每次呼叫 workflow 時都應回傳可讀取的新 stream，讓上傳流程能完整讀取圖片內容。
 * 資料／選項欄位包含 LineRichMenuCreateUploadAndLinkRequest、UserId、RichMenu、PngImageStreamFactory、Metadata，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
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
