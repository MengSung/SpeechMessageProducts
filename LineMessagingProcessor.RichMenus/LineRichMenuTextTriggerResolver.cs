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
