namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 解除使用者目前 RichMenu 並刪除該 RichMenu 的共用請求。
/// 這保留舊 ChurchReport 行為：先查使用者目前綁定的 RichMenu ID，再 unlink，最後 delete。
/// 若未來產品需要「只解除不刪除」或「刪除共用模板」這類不同策略，應新增明確方法，不在這裡塞旗標。
/// </summary>
public sealed class LineRichMenuDeleteLinkedRequest
{
    public required string UserId { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
