/*
 * 檔案整體說明
 *
 * 目的與定位：產品端提供的 RichMenu 目錄。 未來產品只要實作這個介面，就能把自己的 RichMenu 圖片、版面與 alias 接到共用 provisioning workflow。
 *
 * 主要功能與流程：
 * 本檔宣告 ILineRichMenuCatalog。
 * - 載入所有應同步到 LINE 的 RichMenu 定義。
 * 資料／選項欄位包含 ILineRichMenuCatalog，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs、
 * LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 產品端提供的 RichMenu 目錄。
/// 未來產品只要實作這個介面，就能把自己的 RichMenu 圖片、版面與 alias 接到共用 provisioning workflow。
/// </summary>
public interface ILineRichMenuCatalog
{
    /// <summary>
    /// 載入所有應同步到 LINE 的 RichMenu 定義。
    /// </summary>
    /// <param name="cancellationToken">供需要 I/O 的 catalog 實作用的取消權杖。</param>
    /// <returns>
    /// 穩定的應用程式 RichMenu 定義清單，包含 menu key、alias、版面與圖片 stream factory。
    /// </returns>
    Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default);
}
