namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 刪除已連結 RichMenu 的標準請求。
/// 呼叫端只提供 LINE user id 與必要追蹤資料；解除連結與刪除遠端選單由共用 workflow 負責。
/// </summary>
public sealed class LineRichMenuDeleteLinkedRequest
{
    public required string UserId { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

