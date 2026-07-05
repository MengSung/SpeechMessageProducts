// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：interface ILineRichMenuProcessor
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：Line.Messaging
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 共用 RichMenu 核心呼叫 LINE RichMenu API 的抽象邊界。
/// 未來產品只需要使用 catalog、provisioning、assignment 與 orchestration；底層 LINE API 呼叫集中在此介面背後。
/// </summary>
public interface ILineRichMenuProcessor
{
    /// <summary>
    /// 建立 LINE RichMenu metadata record，並回傳 provider id。
    /// </summary>
    Task<string> CreateRichMenuAsync(RichMenu richMenu);

    /// <summary>
    /// 上傳 LINE 顯示 RichMenu 前必須具備的 PNG 圖片。
    /// </summary>
    Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream);

    /// <summary>
    /// 列出目前 LINE channel 內已存在的 RichMenus。
    /// </summary>
    Task<IList<ResponseRichMenu>> GetRichMenuListAsync();

    /// <summary>
    /// 設定 channel 預設 RichMenu。
    /// </summary>
    Task SetDefaultRichMenuAsync(string richMenuId);

    /// <summary>
    /// 取得目前 channel 預設 richMenuId。
    /// </summary>
    Task<string> GetDefaultRichMenuIdAsync();

    /// <summary>
    /// 清除 channel 預設 RichMenu。
    /// </summary>
    Task CancelDefaultRichMenuAsync();

    /// <summary>
    /// 取得目前直接連結到指定 LINE 使用者的 richMenuId。
    /// </summary>
    Task<string> GetRichMenuIdOfUserAsync(string userId);

    /// <summary>
    /// 將 LINE 使用者連結到指定 provider richMenuId。
    /// </summary>
    Task LinkRichMenuToUserAsync(string userId, string richMenuId);

    /// <summary>
    /// 移除 LINE 使用者的顯式 RichMenu 連結。
    /// </summary>
    Task UnlinkRichMenuFromUserAsync(string userId);

    /// <summary>
    /// 依 id 刪除 provider RichMenu。
    /// </summary>
    Task DeleteRichMenuAsync(string richMenuId);

    /// <summary>
    /// 建立指向 provider richMenuId 的 LINE RichMenu alias。
    /// </summary>
    Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId);

    /// <summary>
    /// 更新既有 RichMenu alias，讓它指向不同的 provider richMenuId。
    /// </summary>
    Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId);

    /// <summary>
    /// 刪除 LINE RichMenu alias。
    /// </summary>
    Task DeleteRichMenuAliasAsync(string richMenuAliasId);

    /// <summary>
    /// 依 alias id 取得單一 LINE RichMenu alias。
    /// </summary>
    Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId);

    /// <summary>
    /// 取得 channel 內所有 LINE RichMenu aliases。
    /// </summary>
    Task<RichMenuAliasList> GetRichMenuAliasListAsync();
}
