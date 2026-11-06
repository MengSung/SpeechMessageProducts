/*
 * 檔案整體說明
 *
 * 目的與定位：共用 RichMenu 核心呼叫 LINE RichMenu API 的抽象邊界。 未來產品只需要使用 catalog、provisioning、assignment 與 orchestration；底層 
 * LINE API 呼叫集中在此介面背後。
 *
 * 主要功能與流程：
 * 本檔宣告 ILineRichMenuProcessor。
 * - 建立 LINE RichMenu metadata record，並回傳 provider id。
 * - 上傳 LINE 顯示 RichMenu 前必須具備的 PNG 圖片。
 * - 列出目前 LINE channel 內已存在的 RichMenus。
 * - 取得目前直接連結到指定 LINE 使用者的 richMenuId。
 * 資料／選項欄位包含 ILineRichMenuProcessor，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs、
 * LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
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
