using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 建立 LINE Quick reply 物件。Quick reply 是訊息的附加選項，不是獨立訊息。
/// </summary>
public static class LineQuickReplyFactory
{
    public static QuickReply Create(params QuickReplyButtonObject[] items)
        => Create((IEnumerable<QuickReplyButtonObject>)items);

    public static QuickReply Create(IEnumerable<QuickReplyButtonObject> items)
    {
        var list = LineMessageFactoryValidation.RequiredRange(items, nameof(items), 1, 13, "Quick reply item");
        return new QuickReply(list.ToList());
    }

    public static QuickReplyButtonObject MessageAction(string label, string text, string? imageUrl = null)
        => Button(LineTemplateActionFactory.Message(label, text), imageUrl);

    public static QuickReplyButtonObject PostbackAction(string label, string data, string? displayText = null, string? imageUrl = null)
        => Button(LineTemplateActionFactory.Postback(label, data, displayText), imageUrl);

    public static QuickReplyButtonObject UriAction(string label, string uri, string? imageUrl = null)
        => Button(LineTemplateActionFactory.Uri(label, uri), imageUrl);

    public static QuickReplyButtonObject CameraAction(string label, string? imageUrl = null)
        => Button(new CameraTemplateAction(LineMessageFactoryValidation.Required(label, nameof(label), "Action label is required.")), imageUrl);

    public static QuickReplyButtonObject CameraRollAction(string label, string? imageUrl = null)
        => Button(new CameraRollTemplateAction(LineMessageFactoryValidation.Required(label, nameof(label), "Action label is required.")), imageUrl);

    public static QuickReplyButtonObject LocationAction(string label, string? imageUrl = null)
        => Button(new LocationTemplateAction(LineMessageFactoryValidation.Required(label, nameof(label), "Action label is required.")), imageUrl);

    public static QuickReplyButtonObject Button(ITemplateAction action, string? imageUrl = null)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var normalizedImageUrl = imageUrl == null
            ? null
            : LineMessageFactoryValidation.HttpsUrl(imageUrl, nameof(imageUrl), "Quick reply image URL is required.");

        return new QuickReplyButtonObject(action, normalizedImageUrl!);
    }
}
