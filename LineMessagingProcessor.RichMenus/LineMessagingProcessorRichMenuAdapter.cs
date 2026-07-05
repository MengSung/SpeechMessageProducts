// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineMessagingProcessorRichMenuAdapter
// 主要成員：CreateRichMenuAsync、UploadRichMenuPngImageAsync、GetRichMenuListAsync、SetDefaultRichMenuAsync、GetDefaultRichMenuIdAsync、CancelDefaultRichMenuAsync、GetRichMenuIdOfUserAsync、LinkRichMenuToUserAsync、UnlinkRichMenuFromUserAsync、DeleteRichMenuAsync
// 引用命名空間：Line.Messaging、LineMessagingProcessor
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
