using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用 LINE 通知流程的訊息內容包裝器。
/// 這一層刻意只處理「要送什麼訊息」，不處理收件者、重試鍵、HTTP 呼叫或產品流程。
/// 未來產品如果只要發送文字、圖片或 Flex 訊息，可以直接使用這裡的工廠方法；
/// 若遇到尚未包裝的 LINE SDK 訊息型別，仍可透過 <see cref="SdkMessagesList"/> 保留擴充出口。
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

    public static LineNotificationContent ImageMessage(string originalContentUrl, string previewImageUrl)
    {
        EnsureHttpsUrl(originalContentUrl, nameof(originalContentUrl), "Original image URL is required.");
        EnsureHttpsUrl(previewImageUrl, nameof(previewImageUrl), "Preview image URL is required.");

        return new(null, new ISendMessage[] { new Line.Messaging.ImageMessage(originalContentUrl, previewImageUrl) });
    }

    public static LineNotificationContent FlexMessage(Line.Messaging.FlexMessage message)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return new(null, new ISendMessage[] { message });
    }

    public static LineNotificationContent SdkMessagesList(IReadOnlyList<ISendMessage> messages)
        => new(null, messages);

    private static void EnsureHttpsUrl(string value, string parameterName, string requiredMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(requiredMessage, parameterName);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("LINE image URL must be an absolute HTTPS URL.", parameterName);
        }
    }
}
