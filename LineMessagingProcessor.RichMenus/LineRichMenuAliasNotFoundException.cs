namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 表示 LINE 沒有回傳指定 alias id 的 RichMenu alias。
/// 專用例外讓 provisioning 程式可以分辨「可建立的 missing alias」與「應回報為同步錯誤的其他 provider failure」。
/// </summary>
public sealed class LineRichMenuAliasNotFoundException : Exception
{
    /// <summary>
    /// 建立指定 LINE RichMenu alias id 不存在的例外。
    /// </summary>
    /// <param name="richMenuAliasId">向 LINE 查詢的 alias id。</param>
    public LineRichMenuAliasNotFoundException(string richMenuAliasId)
        : base($"RichMenu alias '{richMenuAliasId}' was not found.")
    {
        RichMenuAliasId = richMenuAliasId;
    }

    /// <summary>
    /// 取得不存在的 LINE RichMenu alias id。
    /// </summary>
    public string RichMenuAliasId { get; }
}
