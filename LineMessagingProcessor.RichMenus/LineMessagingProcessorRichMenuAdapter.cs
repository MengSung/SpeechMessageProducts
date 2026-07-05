using Line.Messaging;
using LineMessagingProcessor;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 將既有 LineMessagingProcessorClass 包成 RichMenu 專用介面，讓共用 RichMenu 核心不用知道其他 LINE 功能。
/// </summary>
public sealed class LineMessagingProcessorRichMenuAdapter : ILineRichMenuProcessor
{
    /// <summary>
    /// 既有 processor，已負責 token 設定與 LINE SDK 存取。
    /// </summary>
    private readonly LineMessagingProcessorClass _processor;

    /// <summary>
    /// 將 legacy processor 包裝在 RichMenu 專用抽象後方。
    /// </summary>
    /// <param name="processor">應用程式既有的 LINE messaging processor。</param>
    public LineMessagingProcessorRichMenuAdapter(LineMessagingProcessorClass processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    }

    /// <inheritdoc />
    public Task<string> CreateRichMenuAsync(RichMenu richMenu) => _processor.CreateRichMenuAsync(richMenu);

    /// <inheritdoc />
    public Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream) => _processor.UploadRichMenuPngImageAsync(richMenuId, imageStream);

    /// <inheritdoc />
    public Task<IList<ResponseRichMenu>> GetRichMenuListAsync() => _processor.GetRichMenuListAsync();

    /// <inheritdoc />
    public Task SetDefaultRichMenuAsync(string richMenuId) => _processor.SetDefaultRichMenuAsync(richMenuId);

    /// <inheritdoc />
    public Task<string> GetDefaultRichMenuIdAsync() => _processor.GetDefaultRichMenuIdAsync();

    /// <inheritdoc />
    public Task CancelDefaultRichMenuAsync() => _processor.CancelDefaultRichMenuAsync();

    /// <inheritdoc />
    public Task<string> GetRichMenuIdOfUserAsync(string userId) => _processor.GetRichMenuIdOfUserAsync(userId);

    /// <inheritdoc />
    public Task LinkRichMenuToUserAsync(string userId, string richMenuId) => _processor.LinkRichMenuToUserAsync(userId, richMenuId);

    /// <inheritdoc />
    public Task UnlinkRichMenuFromUserAsync(string userId) => _processor.UnlinkRichMenuFromUserAsync(userId);

    /// <inheritdoc />
    public Task DeleteRichMenuAsync(string richMenuId) => _processor.DeleteRichMenuAsync(richMenuId);

    /// <inheritdoc />
    public Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId) => _processor.CreateRichMenuAliasAsync(richMenuId, richMenuAliasId);

    /// <inheritdoc />
    public Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId) => _processor.UpdateRichMenuAliasAsync(richMenuAliasId, richMenuId);

    /// <inheritdoc />
    public Task DeleteRichMenuAliasAsync(string richMenuAliasId) => _processor.DeleteRichMenuAliasAsync(richMenuAliasId);

    /// <inheritdoc />
    public async Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId)
    {
        try
        {
            return await _processor.GetRichMenuAliasAsync(richMenuAliasId).ConfigureAwait(false);
        }
        catch (LineResponseException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new LineRichMenuAliasNotFoundException(richMenuAliasId);
        }
    }

    /// <inheritdoc />
    public Task<RichMenuAliasList> GetRichMenuAliasListAsync() => _processor.GetRichMenuAliasListAsync();
}
