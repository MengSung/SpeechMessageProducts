namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 設定文字觸發的 RichMenu 切換。
/// dictionary 中每筆資料都將一段精確的 LINE inbound message 對應到應指派的應用程式 menu key。
/// </summary>
public sealed class LineRichMenuTextTriggerOptions
{
    /// <summary>
    /// 取得精確文字到 menu key 的對照表。
    /// 預設 comparer 不分大小寫；resolver 查詢前仍會先 trim 前後空白。
    /// </summary>
    public Dictionary<string, string> ExactTextToMenuKey { get; } = new(StringComparer.OrdinalIgnoreCase);
}
