using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 定義一組可重複佈署的 LINE RichMenu。
/// 共用核心只保存選單 key、alias、LINE RichMenu 版面與 PNG 來源；
/// 呼叫端可以用自己的 catalog 組合不同選單，但不需要重寫底層建立、上傳與連結流程。
/// </summary>
public sealed class LineRichMenuDefinition
{
    private string _menuKey;
    private string _aliasId;
    private RichMenu _richMenu;

    public LineRichMenuDefinition()
    {
        _menuKey = string.Empty;
        _aliasId = string.Empty;
        _richMenu = new RichMenu();
        PngImageStreamFactory = _ => Task.FromResult<Stream>(Stream.Null);
    }

    public LineRichMenuDefinition(
        string menuKey,
        string aliasId,
        RichMenu richMenu,
        Func<CancellationToken, Task<Stream>> pngImageStreamFactory)
    {
        _menuKey = NormalizeRequired(menuKey, nameof(menuKey));
        _aliasId = NormalizeRequired(aliasId, nameof(aliasId));
        _richMenu = richMenu ?? throw new ArgumentNullException(nameof(richMenu));
        PngImageStreamFactory = pngImageStreamFactory ?? throw new ArgumentNullException(nameof(pngImageStreamFactory));
    }

    public string MenuKey => _menuKey;

    public string AliasId => _aliasId;

    public RichMenu RichMenu => _richMenu;

    public Func<CancellationToken, Task<Stream>> PngImageStreamFactory { get; init; }

    public bool IsDefault { get; init; }

    public string? Description { get; init; }

    public string Key
    {
        get => _menuKey;
        init => _menuKey = NormalizeRequired(value, nameof(Key));
    }

    public string Alias
    {
        get => _aliasId;
        init => _aliasId = NormalizeRequired(value, nameof(Alias));
    }

    public RichMenu Layout
    {
        get => _richMenu;
        init => _richMenu = value ?? throw new ArgumentNullException(nameof(Layout));
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim();
    }
}
