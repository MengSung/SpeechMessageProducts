using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用 LINE reply-token workflow 的輸入模型。
/// 這裡只描述 LINE reply 所需的 protocol 資料：reply token、SDK message 清單、
/// 以及診斷用 metadata；不得放入 ChurchReport 的 CRM、Controller 或產品流程狀態。
/// </summary>
public sealed class LineReplyRequest
{
    public string? ReplyToken { get; init; }

    public IReadOnlyList<ISendMessage>? Messages { get; init; }

    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
