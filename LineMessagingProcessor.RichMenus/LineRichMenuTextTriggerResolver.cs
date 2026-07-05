// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineRichMenuTextTriggerResolver
// 主要成員：ResolveMenuKey、TryResolve
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 使用精確文字對照，將 LINE 傳入文字解析成應用程式 RichMenu key。
/// resolver 會先 trim 使用者輸入，再依 options dictionary 的 comparer 決定是否區分大小寫。
/// </summary>
public sealed class LineRichMenuTextTriggerResolver : ILineRichMenuTextTriggerResolver
{
    /// <summary>
    /// 將 trigger text 對應到應用程式 menu key 的設定。
    /// </summary>
    private readonly LineRichMenuTextTriggerOptions _options;

    /// <summary>
    /// 使用傳入的 trigger options 建立 resolver。
    /// </summary>
    /// <param name="options">解析時使用的精確文字到 menu key 對照。</param>
    public LineRichMenuTextTriggerResolver(LineRichMenuTextTriggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 回傳 received text 對應的 menu key；若沒有 trigger 命中則回傳 null。
    /// </summary>
    /// <param name="receivedText">LINE 收到的原始文字。</param>
    public string? ResolveMenuKey(string? receivedText)
    {
        if (string.IsNullOrWhiteSpace(receivedText))
        {
            return null;
        }

        var text = receivedText.Trim();
        return _options.ExactTextToMenuKey.TryGetValue(text, out var menuKey) && !string.IsNullOrWhiteSpace(menuKey)
            ? menuKey.Trim()
            : null;
    }

    /// <summary>
    /// 嘗試解析 received text；沒有對照時透過 out 參數回傳空字串。
    /// </summary>
    /// <param name="receivedText">LINE 收到的原始文字。</param>
    /// <param name="menuKey">方法回傳 true 時為解析出的 menu key；否則為空字串。</param>
    public bool TryResolve(string? receivedText, out string menuKey)
    {
        menuKey = ResolveMenuKey(receivedText) ?? string.Empty;
        return menuKey.Length > 0;
    }
}
