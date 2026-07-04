using Line.Messaging;
using LineMessagingProcessor;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 將既有 LineMessagingProcessorClass 包成 RichMenu 專用介面，讓共用 RichMenu 核心不用知道其他 LINE 功能。
/// </summary>
public sealed class LineMessagingProcessorRichMenuAdapter : ILineRichMenuProcessor
{
    private readonly LineMessagingProcessorClass _processor;

    public LineMessagingProcessorRichMenuAdapter(LineMessagingProcessorClass processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    }

    public Task<string> CreateRichMenuAsync(RichMenu richMenu) => _processor.CreateRichMenuAsync(richMenu);
    public Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream) => _processor.UploadRichMenuPngImageAsync(richMenuId, imageStream);
    public Task<IList<ResponseRichMenu>> GetRichMenuListAsync() => _processor.GetRichMenuListAsync();
    public Task SetDefaultRichMenuAsync(string richMenuId) => _processor.SetDefaultRichMenuAsync(richMenuId);
    public Task<string> GetDefaultRichMenuIdAsync() => _processor.GetDefaultRichMenuIdAsync();
    public Task CancelDefaultRichMenuAsync() => _processor.CancelDefaultRichMenuAsync();
    public Task<string> GetRichMenuIdOfUserAsync(string userId) => _processor.GetRichMenuIdOfUserAsync(userId);
    public Task LinkRichMenuToUserAsync(string userId, string richMenuId) => _processor.LinkRichMenuToUserAsync(userId, richMenuId);
    public Task UnlinkRichMenuFromUserAsync(string userId) => _processor.UnlinkRichMenuFromUserAsync(userId);
    public Task DeleteRichMenuAsync(string richMenuId) => _processor.DeleteRichMenuAsync(richMenuId);
    public Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId) => _processor.CreateRichMenuAliasAsync(richMenuId, richMenuAliasId);
    public Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId) => _processor.UpdateRichMenuAliasAsync(richMenuAliasId, richMenuId);
    public Task DeleteRichMenuAliasAsync(string richMenuAliasId) => _processor.DeleteRichMenuAliasAsync(richMenuAliasId);
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
    public Task<RichMenuAliasList> GetRichMenuAliasListAsync() => _processor.GetRichMenuAliasListAsync();
}
