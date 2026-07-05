namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 記錄單一應用程式 RichMenu definition 的同步結果。
/// </summary>
public sealed class LineRichMenuSyncItem
{
    /// <summary>
    /// 建立單一選單的同步結果項目。
    /// </summary>
    /// <param name="menuKey">catalog definition 中的應用程式層級 menu key。</param>
    /// <param name="richMenuId">已知的 LINE provider id；若同步失敗且尚未取得則可為空字串。</param>
    /// <param name="outcome">此 definition 的同步結果。</param>
    /// <param name="errorMessage">選填的 provider 或 validation 錯誤訊息。</param>
    public LineRichMenuSyncItem(
        string menuKey,
        string richMenuId,
        LineRichMenuSyncOutcome outcome,
        string? errorMessage = null)
    {
        MenuKey = menuKey;
        RichMenuId = richMenuId;
        Outcome = outcome;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 取得此項目代表的應用程式 menu key。
    /// </summary>
    public string MenuKey { get; }

    /// <summary>
    /// 取得此選單建立或重用的 LINE richMenuId；若無資料則為空字串。
    /// </summary>
    public string RichMenuId { get; }

    /// <summary>
    /// 取得此選單是新建、已最新或同步失敗。
    /// </summary>
    public LineRichMenuSyncOutcome Outcome { get; }

    /// <summary>
    /// 取得失敗項目的錯誤細節。
    /// </summary>
    public string? ErrorMessage { get; }
}
