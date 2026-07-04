using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用 LINE 通知流程的訊息內容包裝器。
/// 這一層只處理「要送什麼訊息」，不處理收件者、重試鍵、HTTP 呼叫或產品流程。
/// 未來產品可透過這些產品友善工廠建立常用 LINE 訊息；若遇到尚未包裝的 SDK 訊息型別，
/// 仍可透過 <see cref="SdkMessagesList"/> 保留擴充出口。
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

    public static LineNotificationContent TextMessage(string message, QuickReply quickReply)
        => SdkMessagesList(new ISendMessage[] { new TextMessage(LineMessageFactoryValidation.Required(message, nameof(message), "Text message is required."), quickReply) });

    public static LineNotificationContent TextMessageV2(
        string message,
        IDictionary<string, object>? substitution = null,
        string? quoteToken = null,
        QuickReply? quickReply = null)
        => SdkMessagesList(new ISendMessage[]
        {
            new TextV2Message(
                LineMessageFactoryValidation.Required(message, nameof(message), "Text v2 message is required."),
                substitution == null ? null! : new Dictionary<string, object>(substitution),
                quoteToken!,
                quickReply!)
        });

    public static LineNotificationContent ImageMessage(string originalContentUrl, string previewImageUrl, QuickReply? quickReply = null)
        => SdkMessagesList(new ISendMessage[]
        {
            new ImageMessage(
                LineMessageFactoryValidation.HttpsUrl(originalContentUrl, nameof(originalContentUrl), "Original image URL is required."),
                LineMessageFactoryValidation.HttpsUrl(previewImageUrl, nameof(previewImageUrl), "Preview image URL is required."),
                quickReply!)
        });

    public static LineNotificationContent VideoMessage(string originalContentUrl, string previewImageUrl, QuickReply? quickReply = null)
        => SdkMessagesList(new ISendMessage[]
        {
            new VideoMessage(
                LineMessageFactoryValidation.HttpsUrl(originalContentUrl, nameof(originalContentUrl), "Original video URL is required."),
                LineMessageFactoryValidation.HttpsUrl(previewImageUrl, nameof(previewImageUrl), "Preview image URL is required."),
                quickReply!)
        });

    public static LineNotificationContent AudioMessage(string originalContentUrl, long durationMilliseconds, QuickReply? quickReply = null)
        => SdkMessagesList(new ISendMessage[]
        {
            new AudioMessage(
                LineMessageFactoryValidation.HttpsUrl(originalContentUrl, nameof(originalContentUrl), "Original audio URL is required."),
                LineMessageFactoryValidation.Positive(durationMilliseconds, nameof(durationMilliseconds), "Audio duration"),
                quickReply!)
        });

    public static LineNotificationContent LocationMessage(
        string title,
        string address,
        decimal latitude,
        decimal longitude,
        QuickReply? quickReply = null)
        => SdkMessagesList(new ISendMessage[]
        {
            new LocationMessage(
                LineMessageFactoryValidation.Required(title, nameof(title), "Location title is required."),
                LineMessageFactoryValidation.Required(address, nameof(address), "Location address is required."),
                LineMessageFactoryValidation.Latitude(latitude, nameof(latitude)),
                LineMessageFactoryValidation.Longitude(longitude, nameof(longitude)),
                quickReply!)
        });

    public static LineNotificationContent StickerMessage(string packageId, string stickerId, QuickReply? quickReply = null)
        => SdkMessagesList(new ISendMessage[]
        {
            new StickerMessage(
                LineMessageFactoryValidation.Required(packageId, nameof(packageId), "Sticker package ID is required."),
                LineMessageFactoryValidation.Required(stickerId, nameof(stickerId), "Sticker ID is required."),
                quickReply!)
        });

    public static LineNotificationContent CouponMessage(string couponId, string? deliveryTag = null, QuickReply? quickReply = null)
        => SdkMessagesList(new ISendMessage[]
        {
            new CouponMessage(
                LineMessageFactoryValidation.Required(couponId, nameof(couponId), "Coupon ID is required."),
                deliveryTag!,
                quickReply!)
        });

    public static LineNotificationContent FlexMessage(FlexMessage message)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return SdkMessagesList(new ISendMessage[] { message });
    }

    public static LineNotificationContent ConfirmTemplateMessage(
        string altText,
        string text,
        IEnumerable<ITemplateAction> actions,
        QuickReply? quickReply = null)
    {
        var actionList = LineMessageFactoryValidation.RequiredRange(actions, nameof(actions), 2, 2, "Confirm template action");
        return Template(
            altText,
            new ConfirmTemplate(LineMessageFactoryValidation.Required(text, nameof(text), "Confirm template text is required."), actionList.ToList()),
            quickReply);
    }

    public static LineNotificationContent ButtonsTemplateMessage(
        string altText,
        string text,
        string? title,
        string? thumbnailImageUrl,
        IEnumerable<ITemplateAction> actions,
        QuickReply? quickReply = null)
    {
        var actionList = LineMessageFactoryValidation.RequiredRange(actions, nameof(actions), 1, 4, "Buttons template action");
        var normalizedImageUrl = thumbnailImageUrl == null
            ? null
            : LineMessageFactoryValidation.HttpsUrl(thumbnailImageUrl, nameof(thumbnailImageUrl), "Buttons template thumbnail image URL is required.");

        return Template(
            altText,
            new ButtonsTemplate(
                LineMessageFactoryValidation.Required(text, nameof(text), "Buttons template text is required."),
                normalizedImageUrl!,
                title!,
                actionList.ToList()),
            quickReply);
    }

    public static LineNotificationContent CarouselTemplateMessage(
        string altText,
        IEnumerable<CarouselColumn> columns,
        QuickReply? quickReply = null)
    {
        var columnList = LineMessageFactoryValidation.RequiredRange(columns, nameof(columns), 1, 10, "Carousel column");
        return Template(altText, new CarouselTemplate(columnList.ToList()), quickReply);
    }

    public static LineNotificationContent ImageCarouselTemplateMessage(
        string altText,
        IEnumerable<ImageCarouselColumn> columns,
        QuickReply? quickReply = null)
    {
        var columnList = LineMessageFactoryValidation.RequiredRange(columns, nameof(columns), 1, 10, "Image carousel column");
        return Template(altText, new ImageCarouselTemplate(columnList.ToList()), quickReply);
    }

    public static LineNotificationContent ImagemapMessage(
        string baseUrl,
        string altText,
        int width,
        int height,
        IEnumerable<IImagemapAction> actions,
        QuickReply? quickReply = null)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Imagemap width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Imagemap height must be greater than zero.");
        }

        var actionList = LineMessageFactoryValidation.RequiredRange(actions, nameof(actions), 1, 50, "Imagemap action");
        return SdkMessagesList(new ISendMessage[]
        {
            new ImagemapMessage(
                LineMessageFactoryValidation.HttpsUrl(baseUrl, nameof(baseUrl), "Imagemap base URL is required."),
                LineMessageFactoryValidation.Required(altText, nameof(altText), "Imagemap alt text is required."),
                new ImagemapSize(width, height),
                actionList.ToList(),
                quickReply!)
        });
    }

    public static LineNotificationContent SdkMessagesList(IReadOnlyList<ISendMessage> messages)
    {
        if (messages == null)
        {
            throw new ArgumentNullException(nameof(messages));
        }

        if (messages.Count == 0)
        {
            throw new ArgumentException("At least one LINE SDK message is required.", nameof(messages));
        }

        return new(null, messages);
    }

    private static LineNotificationContent Template(string altText, ITemplate template, QuickReply? quickReply)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        return SdkMessagesList(new ISendMessage[]
        {
            new TemplateMessage(
                LineMessageFactoryValidation.Required(altText, nameof(altText), "Template alt text is required."),
                template,
                quickReply!)
        });
    }
}
