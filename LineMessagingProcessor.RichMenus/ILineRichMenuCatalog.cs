namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 產品端提供的 RichMenu 目錄。
/// 未來產品只要實作這個介面，就能把自己的 RichMenu 圖片、版面與 alias 接到共用 provisioning workflow。
/// </summary>
public interface ILineRichMenuCatalog
{
    /// <summary>
    /// 載入所有應同步到 LINE 的 RichMenu 定義。
    /// </summary>
    /// <param name="cancellationToken">供需要 I/O 的 catalog 實作用的取消權杖。</param>
    /// <returns>
    /// 穩定的應用程式 RichMenu 定義清單，包含 menu key、alias、版面與圖片 stream factory。
    /// </returns>
    Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default);
}
