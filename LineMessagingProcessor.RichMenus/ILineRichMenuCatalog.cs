namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 產品端提供的 RichMenu 目錄。
/// 未來產品只要實作這個介面，就能把自己的 RichMenu 圖片、版面與 alias 接到共用 provisioning workflow。
/// </summary>
public interface ILineRichMenuCatalog
{
    Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default);
}
