using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 建立 LINE Template / Quick reply / Flex 可共用的 action 物件。
/// 產品程式呼叫這裡即可取得常用 action，不需要散落 LINE SDK 建構子細節。
/// </summary>
public static class LineTemplateActionFactory
{
    public static ITemplateAction Message(string label, string text)
        => new MessageTemplateAction(
            LineMessageFactoryValidation.Required(label, nameof(label), "Action label is required."),
            LineMessageFactoryValidation.Required(text, nameof(text), "Action text is required."));

    public static ITemplateAction Postback(string label, string data, string? displayText = null)
        => new PostbackTemplateAction(
            LineMessageFactoryValidation.Required(label, nameof(label), "Action label is required."),
            LineMessageFactoryValidation.Required(data, nameof(data), "Action data is required."),
            displayText!);

    public static ITemplateAction Uri(string label, string uri)
        => new UriTemplateAction(
            LineMessageFactoryValidation.Required(label, nameof(label), "Action label is required."),
            LineMessageFactoryValidation.ActionUri(uri, nameof(uri), "Action URI is required."));
}
