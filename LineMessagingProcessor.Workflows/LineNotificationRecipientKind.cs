namespace LineMessagingProcessor.Workflows;

/// <summary>
/// LINE 通知收件目標的種類。這裡只描述 LINE 技術目標，不放會員、付款或 CRM 語意。
/// </summary>
public enum LineNotificationRecipientKind
{
    User,
    Users,
    Group,
    Room
}
