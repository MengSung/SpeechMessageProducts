namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 將 LINE 傳入文字解析成應用程式 RichMenu key。
/// </summary>
public interface ILineRichMenuTextTriggerResolver
{
    /// <summary>
    /// 回傳 received text 對應的 menu key；若沒有 trigger 命中則回傳 null。
    /// </summary>
    /// <param name="receivedText">LINE 收到的原始文字。</param>
    string? ResolveMenuKey(string? receivedText);

    /// <summary>
    /// 嘗試將 received text 解析成 menu key。
    /// </summary>
    /// <param name="receivedText">LINE 收到的原始文字。</param>
    /// <param name="menuKey">方法回傳 true 時為解析出的 menu key；否則為空字串。</param>
    bool TryResolve(string? receivedText, out string menuKey);
}
