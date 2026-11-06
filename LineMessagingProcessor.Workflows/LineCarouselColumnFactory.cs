/*
 * 檔案整體說明
 *
 * 目的與定位：建立 carousel / image carousel 欄位，避免產品程式直接處理欄位數與 HTTPS 圖片限制。
 *
 * 主要功能與流程：
 * 本檔宣告 LineCarouselColumnFactory。
 * 主要操作入口包含 Column、ImageColumn。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs 追查目前工作樹中的合作／使用位置。
 */
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
