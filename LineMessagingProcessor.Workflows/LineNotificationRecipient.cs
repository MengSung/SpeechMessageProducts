/*
 * 檔案整體說明
 *
 * 目的與定位：共用 LINE 通知收件者模型。未來產品只需要給 LINE ID，不需要依賴特定產品的 contact 或 group 實體。
 *
 * 主要功能與流程：
 * 本檔宣告 LineNotificationRecipient。
 * 主要操作入口包含 User、Users、Group、Room。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs、
 * LineMessagingProcessor.Workflows/LineNotificationRequest.cs、
 * LineMessagingProcessor.Workflows/LineNotificationResult.cs 追查目前工作樹中的合作／使用位置。
 */
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
