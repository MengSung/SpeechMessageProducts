namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 分類單一 RichMenu definition 的同步結果。
/// </summary>
public enum LineRichMenuSyncOutcome
{
    /// <summary>
    /// 選單原本不存在於 LINE，已在同步期間建立。
    /// </summary>
    Created,

    /// <summary>
    /// 選單已存在且 fingerprint 相符，因此不需要重新建立。
    /// </summary>
    UpToDate,

    /// <summary>
    /// 選單同步失敗；workflow 仍可繼續處理後續 definitions。
    /// </summary>
    Failed
}
