/*
 * 檔案整體說明
 *
 * 目的與定位：使用精確文字對照，將 LINE 傳入文字解析成應用程式 RichMenu key。 resolver 會先 trim 使用者輸入，再依 options dictionary 的 comparer
 * 決定是否區分大小寫。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuTextTriggerResolver。
 * - 將 trigger text 對應到應用程式 menu key 的設定。
 * - 使用傳入的 trigger options 建立 resolver。
 * - 回傳 received text 對應的 menu key；若沒有 trigger 命中則回傳 null。
 * - 嘗試解析 received text；沒有對照時透過 out 參數回傳空字串。
 * 主要操作入口包含 ResolveMenuKey、TryResolve。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs 追查目前工作樹中的合作／使用位置。
 */
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
