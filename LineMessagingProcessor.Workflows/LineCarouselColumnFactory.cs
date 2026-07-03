using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 建立 carousel / image carousel 欄位，避免產品程式直接處理欄位數與 HTTPS 圖片限制。
/// </summary>
public static class LineCarouselColumnFactory
{
    public static CarouselColumn Column(
        string title,
        string text,
        string? thumbnailImageUrl,
        IEnumerable<ITemplateAction> actions)
    {
        var actionList = LineMessageFactoryValidation.RequiredRange(actions, nameof(actions), 1, 3, "Carousel column action");
        var normalizedImageUrl = thumbnailImageUrl == null
            ? null
            : LineMessageFactoryValidation.HttpsUrl(thumbnailImageUrl, nameof(thumbnailImageUrl), "Carousel thumbnail image URL is required.");

        return new CarouselColumn(
            LineMessageFactoryValidation.Required(text, nameof(text), "Carousel column text is required."),
            normalizedImageUrl!,
            LineMessageFactoryValidation.Required(title, nameof(title), "Carousel column title is required."),
            actionList.ToList());
    }

    public static ImageCarouselColumn ImageColumn(string imageUrl, ITemplateAction action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        return new ImageCarouselColumn(
            LineMessageFactoryValidation.HttpsUrl(imageUrl, nameof(imageUrl), "Image carousel image URL is required."),
            action);
    }
}
