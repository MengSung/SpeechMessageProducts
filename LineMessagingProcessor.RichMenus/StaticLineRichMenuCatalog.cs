namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 提供固定的記憶體 RichMenu definitions catalog。
/// 當應用程式在啟動時已知道所有選單，且希望 provisioning workflow 不必讀取資料庫、設定 provider 或遠端服務時，可使用此實作。
/// </summary>
public sealed class StaticLineRichMenuCatalog : ILineRichMenuCatalog
{
    /// <summary>
    /// 建構式傳入 definitions 的不可變時間點快照。
    /// 先複製成 list，可避免來源 enumerable 後續異動影響 provisioning workflow 要同步的選單。
    /// </summary>
    private readonly IReadOnlyList<LineRichMenuDefinition> _definitions;

    /// <summary>
    /// 從傳入的 RichMenu definitions 建立靜態 catalog。
    /// </summary>
    /// <param name="definitions">
    /// 要提供給同步 workflow 的完整 RichMenu definitions 集合。
    /// </param>
    public StaticLineRichMenuCatalog(IEnumerable<LineRichMenuDefinition> definitions)
    {
        _definitions = (definitions ?? throw new ArgumentNullException(nameof(definitions))).ToList();
    }

    /// <summary>
    /// 回傳預先設定的 RichMenu definitions。
    /// </summary>
    /// <param name="cancellationToken">
    /// 目前未使用；此實作沒有非同步 I/O，但保留此參數以符合會從外部來源載入選單的 catalog。
    /// </param>
    public Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_definitions);
}
