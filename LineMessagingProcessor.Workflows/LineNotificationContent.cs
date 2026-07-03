using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用通知內容。一般產品用文字訊息；既有系統若需要 Flex、Template 等 SDK 型別，可透過 SDK escape hatch 傳入。
/// </summary>
public sealed class LineNotificationContent
{
    private LineNotificationContent(string? text, IReadOnlyList<ISendMessage>? sdkMessages)
    {
        Text = text;
        SdkMessages = sdkMessages;
    }

    public string? Text { get; }

    public IReadOnlyList<ISendMessage>? SdkMessages { get; }

    public static LineNotificationContent TextMessage(string message)
        => new(message, null);

    public static LineNotificationContent SdkMessagesList(IReadOnlyList<ISendMessage> messages)
        => new(null, messages);
}
