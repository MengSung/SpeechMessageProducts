namespace LineMessagingProcessor.RichMenus;

public sealed class StaticLineRichMenuCatalog : ILineRichMenuCatalog
{
    private readonly IReadOnlyList<LineRichMenuDefinition> _definitions;

    public StaticLineRichMenuCatalog(IEnumerable<LineRichMenuDefinition> definitions)
    {
        _definitions = (definitions ?? throw new ArgumentNullException(nameof(definitions))).ToList();
    }

    public Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_definitions);
}
