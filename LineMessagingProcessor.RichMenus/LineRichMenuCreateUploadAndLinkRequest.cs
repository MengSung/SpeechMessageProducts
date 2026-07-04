using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 建立 RichMenu、上傳 PNG 圖片並綁定到單一 LINE 使用者的共用請求。
/// RichMenu 的版面與圖片內容由產品端提供；workflow 只負責照 LINE 官方 API 順序執行。
/// </summary>
public sealed class LineRichMenuCreateUploadAndLinkRequest
{
    public required string UserId { get; init; }

    public required RichMenu RichMenu { get; init; }

    public required Func<Stream> PngImageStreamFactory { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
