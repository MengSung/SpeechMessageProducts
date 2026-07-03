namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 產品端送 LINE 通知時唯一需要組出的共用請求。
/// Metadata 保留給產品端追蹤 order id、contact id 等資料，但 workflow 不解讀這些產品語意。
/// </summary>
public sealed class LineNotificationRequest
{
    public required LineNotificationRecipient Recipient { get; init; }

    public required LineNotificationContent Content { get; init; }

    public string? RetryKey { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
