// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.Workflows/LineQuickReplyFactory.cs
// 所屬區塊：LINE 共用 workflow 模組與測試，放置可跨產品重用的訊息處理流程。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineQuickReplyFactory
// 主要成員：Create、MessageAction、PostbackAction、UriAction、CameraAction、CameraRollAction、LocationAction、Button
// 引用命名空間：Line.Messaging
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
