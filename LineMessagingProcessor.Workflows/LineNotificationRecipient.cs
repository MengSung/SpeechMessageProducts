namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用 LINE 通知收件者模型。未來產品只需要給 LINE ID，不需要依賴特定產品的 contact 或 group 實體。
/// </summary>
public sealed class LineNotificationRecipient
{
    private LineNotificationRecipient(LineNotificationRecipientKind kind, IReadOnlyList<string> ids)
    {
        Kind = kind;
        Ids = ids;
    }

    public LineNotificationRecipientKind Kind { get; }

    public IReadOnlyList<string> Ids { get; }

    public string? PrimaryId => Ids.Count == 0 ? null : Ids[0];

    public static LineNotificationRecipient User(string lineUserId)
        => new(LineNotificationRecipientKind.User, new[] { lineUserId });

    public static LineNotificationRecipient Users(IEnumerable<string> lineUserIds)
        => new(LineNotificationRecipientKind.Users, lineUserIds?.ToArray() ?? Array.Empty<string>());

    public static LineNotificationRecipient Group(string groupId)
        => new(LineNotificationRecipientKind.Group, new[] { groupId });

    public static LineNotificationRecipient Room(string roomId)
        => new(LineNotificationRecipientKind.Room, new[] { roomId });
}
