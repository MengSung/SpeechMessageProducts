using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 建立 RichMenu、上傳 PNG 並連結到 LINE 使用者的標準請求。
/// RichMenu 版面與圖片來源由呼叫端提供，workflow 僅負責穩定串接 LINE RichMenu API。
/// </summary>
public sealed class LineRichMenuCreateUploadAndLinkRequest
{
    public required string UserId { get; init; }

    public required RichMenu RichMenu { get; init; }

    public required Func<Stream> PngImageStreamFactory { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

