/*
 * 檔案整體說明
 *
 * 目的與定位：描述 RichMenu catalog 與 LINE 同步後的結果。 report 將 provider ids、新建/重用/刪除集合與逐選單 outcome 分開， 讓呼叫端可記錄高階佈署狀態，
 * 同時保留調查單一選單失敗所需的資訊。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuSyncReport。
 * - 取得已解析的應用程式 menu key 到 LINE richMenuId 對照。 assignment workflows 會透過 ILineRichMenuIdCache 使用這些值。
 * - 取得本次需要新建並上傳 LINE RichMenu 的 menu keys。
 * - 取得 fingerprint 與既有 LINE RichMenu 相符、因此被重用的 menu keys。
 * - 取得已從 LINE 移除的 richMenuIds；這些選單已不再由目前 catalog 擁有。
 * 資料／選項欄位包含 LineRichMenuSyncReport、MenuIds、CreatedMenuKeys、ReusedMenuKeys、DeletedRichMenuIds、Items，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus/ILineRichMenuProvisioningWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 描述 RichMenu catalog 與 LINE 同步後的結果。
/// report 將 provider ids、新建/重用/刪除集合與逐選單 outcome 分開，
/// 讓呼叫端可記錄高階佈署狀態，同時保留調查單一選單失敗所需的資訊。
/// </summary>
public sealed class LineRichMenuSyncReport
{
    /// <summary>
    /// 建立 RichMenu 同步報告。
    /// </summary>
    /// <param name="menuIds">已解析的應用程式 menu key 到 LINE richMenuId 對照。</param>
    /// <param name="createdMenuKeys">本次同步中新建 LINE RichMenu 的應用程式 menu keys。</param>
    /// <param name="reusedMenuKeys">與既有 fingerprinted LINE RichMenu 相符並被重用的應用程式 menu keys。</param>
    /// <param name="deletedRichMenuIds">cleanup 期間刪除的 provider RichMenu ids。</param>
    /// <param name="items">選填的逐 definition 同步結果。</param>
    public LineRichMenuSyncReport(
        IReadOnlyDictionary<string, string> menuIds,
        IReadOnlyList<string> createdMenuKeys,
        IReadOnlyList<string> reusedMenuKeys,
        IReadOnlyList<string> deletedRichMenuIds,
        IReadOnlyList<LineRichMenuSyncItem>? items = null)
    {
        MenuIds = menuIds ?? new Dictionary<string, string>();
        CreatedMenuKeys = createdMenuKeys ?? Array.Empty<string>();
        ReusedMenuKeys = reusedMenuKeys ?? Array.Empty<string>();
        DeletedRichMenuIds = deletedRichMenuIds ?? Array.Empty<string>();
        Items = items ?? Array.Empty<LineRichMenuSyncItem>();
    }

    /// <summary>
    /// 取得已解析的應用程式 menu key 到 LINE richMenuId 對照。
    /// assignment workflows 會透過 <see cref="ILineRichMenuIdCache"/> 使用這些值。
    /// </summary>
    public IReadOnlyDictionary<string, string> MenuIds { get; }

    /// <summary>
    /// 取得本次需要新建並上傳 LINE RichMenu 的 menu keys。
    /// </summary>
    public IReadOnlyList<string> CreatedMenuKeys { get; }

    /// <summary>
    /// 取得 fingerprint 與既有 LINE RichMenu 相符、因此被重用的 menu keys。
    /// </summary>
    public IReadOnlyList<string> ReusedMenuKeys { get; }

    /// <summary>
    /// 取得已從 LINE 移除的 richMenuIds；這些選單已不再由目前 catalog 擁有。
    /// </summary>
    public IReadOnlyList<string> DeletedRichMenuIds { get; }

    /// <summary>
    /// 取得逐選單同步結果，包含未中止整體同步流程的單一選單失敗。
    /// </summary>
    public IReadOnlyList<LineRichMenuSyncItem> Items { get; }
}
