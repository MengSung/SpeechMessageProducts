/*
 * 檔案整體說明
 *
 * 目的與定位：將既有 LineMessagingProcessorClass 包成 RichMenu 專用介面，讓共用 RichMenu 核心不用知道其他 LINE 功能。
 *
 * 主要功能與流程：
 * 本檔宣告 LineMessagingProcessorRichMenuAdapter。
 * - 既有 processor，已負責 token 設定與 LINE SDK 存取。
 * - 將 legacy processor 包裝在 RichMenu 專用抽象後方。
 * 主要操作入口包含 CreateRichMenuAsync、UploadRichMenuPngImageAsync、GetRichMenuListAsync、SetDefaultRichMenuAsync、
 * GetDefaultRichMenuIdAsync、CancelDefaultRichMenuAsync、GetRichMenuIdOfUserAsync、LinkRichMenuToUserAsync；
 * 其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs、
 * LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs、
 * SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs、
 * SpeechMessageProducts.ChurchReport/Tools/PushUtility.cs 追查目前工作樹中的合作／使用位置。
 */
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
