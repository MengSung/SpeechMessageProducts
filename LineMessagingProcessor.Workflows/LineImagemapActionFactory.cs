// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.Workflows/LineImagemapActionFactory.cs
// 所屬區塊：LINE 共用 workflow 模組與測試，放置可跨產品重用的訊息處理流程。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineImagemapActionFactory
// 主要成員：Message、Uri
// 引用命名空間：Line.Messaging
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
