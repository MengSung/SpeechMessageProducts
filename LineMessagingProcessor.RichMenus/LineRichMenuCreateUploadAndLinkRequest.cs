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

