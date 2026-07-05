// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class StaticLineRichMenuCatalog
// 主要成員：GetDefinitionsAsync
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 提供固定的記憶體 RichMenu definitions catalog。
/// 當應用程式在啟動時已知道所有選單，且希望 provisioning workflow 不必讀取資料庫、設定 provider 或遠端服務時，可使用此實作。
/// </summary>
public sealed class StaticLineRichMenuCatalog : ILineRichMenuCatalog
{
    /// <summary>
    /// 建構式傳入 definitions 的不可變時間點快照。
    /// 先複製成 list，可避免來源 enumerable 後續異動影響 provisioning workflow 要同步的選單。
    /// </summary>
    private readonly IReadOnlyList<LineRichMenuDefinition> _definitions;

    /// <summary>
    /// 從傳入的 RichMenu definitions 建立靜態 catalog。
    /// </summary>
    /// <param name="definitions">
    /// 要提供給同步 workflow 的完整 RichMenu definitions 集合。
    /// </param>
    public StaticLineRichMenuCatalog(IEnumerable<LineRichMenuDefinition> definitions)
    {
        _definitions = (definitions ?? throw new ArgumentNullException(nameof(definitions))).ToList();
    }

    /// <summary>
    /// 回傳預先設定的 RichMenu definitions。
    /// </summary>
    /// <param name="cancellationToken">
    /// 目前未使用；此實作沒有非同步 I/O，但保留此參數以符合會從外部來源載入選單的 catalog。
    /// </param>
    public Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_definitions);
}
