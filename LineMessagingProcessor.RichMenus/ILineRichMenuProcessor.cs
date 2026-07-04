using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 共用 RichMenu 核心呼叫 LINE RichMenu API 的抽象邊界。
/// 未來產品只需要使用 catalog、provisioning、assignment 與 orchestration；底層 LINE API 呼叫集中在此介面背後。
/// </summary>
public interface ILineRichMenuProcessor
{
    Task<string> CreateRichMenuAsync(RichMenu richMenu);
    Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream);
    Task<IList<ResponseRichMenu>> GetRichMenuListAsync();
    Task SetDefaultRichMenuAsync(string richMenuId);
    Task<string> GetDefaultRichMenuIdAsync();
    Task CancelDefaultRichMenuAsync();
    Task<string> GetRichMenuIdOfUserAsync(string userId);
    Task LinkRichMenuToUserAsync(string userId, string richMenuId);
    Task UnlinkRichMenuFromUserAsync(string userId);
    Task DeleteRichMenuAsync(string richMenuId);
    Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId);
    Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId);
    Task DeleteRichMenuAliasAsync(string richMenuAliasId);
    Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId);
    Task<RichMenuAliasList> GetRichMenuAliasListAsync();
}
