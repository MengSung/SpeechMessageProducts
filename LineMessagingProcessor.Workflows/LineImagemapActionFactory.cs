/*
 * 檔案整體說明
 *
 * 目的與定位：建立 imagemap 可點擊區域 action。imagemap 本身仍由 LINE SDK message object 序列化。
 *
 * 主要功能與流程：
 * 本檔宣告 LineImagemapActionFactory。
 * 主要操作入口包含 Message、Uri。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs 追查目前工作樹中的合作／使用位置。
 */
using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 建立 imagemap 可點擊區域 action。imagemap 本身仍由 LINE SDK message object 序列化。
/// </summary>
public static class LineImagemapActionFactory
{
    public static IImagemapAction Message(string text, int x, int y, int width, int height, string? label = null)
        => new MessageImagemapAction(
            LineMessageFactoryValidation.Area(x, y, width, height),
            LineMessageFactoryValidation.Required(text, nameof(text), "Imagemap message text is required."),
            label!);

    public static IImagemapAction Uri(string linkUri, int x, int y, int width, int height, string? label = null)
        => new UriImagemapAction(
            LineMessageFactoryValidation.Area(x, y, width, height),
            LineMessageFactoryValidation.ActionUri(linkUri, nameof(linkUri), "Imagemap link URI is required."),
            label!);
}
