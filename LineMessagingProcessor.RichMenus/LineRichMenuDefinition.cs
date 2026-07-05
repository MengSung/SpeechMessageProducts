using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 定義一組可重複佈署的 LINE RichMenu。
/// 共用核心只保存選單 key、alias、LINE RichMenu 版面與 PNG 來源；
/// 呼叫端可以用自己的 catalog 組合不同選單，但不需要重寫底層建立、上傳與連結流程。
/// </summary>
public sealed class LineRichMenuDefinition
{
    /// <summary>
    /// catalog 內部使用的穩定 menu key。
    /// </summary>
    private string _menuKey;

    /// <summary>
    /// LINE RichMenu alias id，供 RichMenu switch action 與 provisioning workflow 使用。
    /// </summary>
    private string _aliasId;

    /// <summary>
    /// LINE RichMenu 版面設定；包含尺寸、chat bar text 與所有可點擊 action areas。
    /// </summary>
    private RichMenu _richMenu;

    /// <summary>
    /// 建立可供 object initializer 使用的空白 definition。
    /// </summary>
    public LineRichMenuDefinition()
    {
        _menuKey = string.Empty;
        _aliasId = string.Empty;
        _richMenu = new RichMenu();
        PngImageStreamFactory = _ => Task.FromResult<Stream>(Stream.Null);
    }

    /// <summary>
    /// 以完整必要欄位建立 RichMenu definition。
    /// </summary>
    /// <param name="menuKey">產品端穩定識別這份選單的 menu key。</param>
    /// <param name="aliasId">LINE RichMenu alias id。</param>
    /// <param name="richMenu">要建立到 LINE 的 RichMenu 版面。</param>
    /// <param name="pngImageStreamFactory">可依 cancellation token 開啟 PNG stream 的 factory。</param>
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

    /// <summary>
    /// 取得產品端用來指派 RichMenu 的穩定 menu key。
    /// </summary>
    public string MenuKey => _menuKey;

    /// <summary>
    /// 取得 LINE RichMenu alias id。
    /// </summary>
    public string AliasId => _aliasId;

    /// <summary>
    /// 取得 LINE RichMenu 版面設定。
    /// </summary>
    public RichMenu RichMenu => _richMenu;

    /// <summary>
    /// 取得 PNG 圖片 stream factory。
    /// provisioning 會用它讀取圖片內容、計算 fingerprint，並上傳到 LINE。
    /// </summary>
    public Func<CancellationToken, Task<Stream>> PngImageStreamFactory { get; init; }

    /// <summary>
    /// 取得這份選單是否應設定為 LINE channel default RichMenu。
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// 取得產品端提供的描述文字，供管理畫面或日誌顯示。
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// object initializer 友善別名，對應 <see cref="MenuKey"/>。
    /// </summary>
    public string Key
    {
        get => _menuKey;
        init => _menuKey = NormalizeRequired(value, nameof(Key));
    }

    /// <summary>
    /// object initializer 友善別名，對應 <see cref="AliasId"/>。
    /// </summary>
    public string Alias
    {
        get => _aliasId;
        init => _aliasId = NormalizeRequired(value, nameof(Alias));
    }

    /// <summary>
    /// object initializer 友善別名，對應 <see cref="RichMenu"/>。
    /// </summary>
    public RichMenu Layout
    {
        get => _richMenu;
        init => _richMenu = value ?? throw new ArgumentNullException(nameof(Layout));
    }

    /// <summary>
    /// 正規化必要字串欄位，避免 catalog 以空白 key 或 alias 進入 provisioning。
    /// </summary>
    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim();
    }
}
