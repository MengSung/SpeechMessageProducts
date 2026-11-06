/*
 * 檔案整體說明
 *
 * 目的與定位：將應用程式 RichMenu catalog 與 LINE provider 狀態同步。
 *
 * 主要功能與流程：
 * 本檔宣告 ILineRichMenuProvisioningWorkflow。
 * - 依目前 catalog 建立、重用、設定 alias、設定預設值，並快取 RichMenu 對照。
 * 資料／選項欄位包含 ILineRichMenuProvisioningWorkflow，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 將應用程式 RichMenu catalog 與 LINE provider 狀態同步。
/// </summary>
public interface ILineRichMenuProvisioningWorkflow
{
    /// <summary>
    /// 依目前 catalog 建立、重用、設定 alias、設定預設值，並快取 RichMenu 對照。
    /// </summary>
    /// <param name="cancellationToken">供 LINE API 與 catalog 操作使用的取消權杖。</param>
    Task<LineRichMenuSyncReport> SyncAsync(CancellationToken cancellationToken = default);
}
