/*
 * 檔案整體說明
 *
 * 目的與定位：建立 LINE Template / Quick reply / Flex 可共用的 action 物件。 產品程式呼叫這裡即可取得常用 action，不需要散落 LINE SDK 建構子細節。
 *
 * 主要功能與流程：
 * 本檔宣告 LineTemplateActionFactory。
 * 主要操作入口包含 Message、Postback、Uri。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs、
 * LineMessagingProcessor.Workflows/LineQuickReplyFactory.cs 追查目前工作樹中的合作／使用位置。
 */
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
