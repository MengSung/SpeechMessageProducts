/*
 * 檔案整體說明
 *
 * 目的與定位：分類單一 RichMenu definition 的同步結果。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuSyncOutcome。
 * - 選單已存在且 fingerprint 相符，因此不需要重新建立。
 * - 選單同步失敗；workflow 仍可繼續處理後續 definitions。
 * 資料／選項欄位包含 LineRichMenuSyncOutcome，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 分類單一 RichMenu definition 的同步結果。
/// </summary>
public enum LineRichMenuSyncOutcome
{
    /// <summary>
    /// 選單原本不存在於 LINE，已在同步期間建立。
    /// </summary>
    Created,

    /// <summary>
    /// 選單已存在且 fingerprint 相符，因此不需要重新建立。
    /// </summary>
    UpToDate,

    /// <summary>
    /// 選單同步失敗；workflow 仍可繼續處理後續 definitions。
    /// </summary>
    Failed
}
