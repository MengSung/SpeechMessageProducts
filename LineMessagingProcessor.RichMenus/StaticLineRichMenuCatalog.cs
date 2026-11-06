/*
 * 檔案整體說明
 *
 * 目的與定位：提供固定的記憶體 RichMenu definitions catalog。 當應用程式在啟動時已知道所有選單，且希望 provisioning workflow 不必讀取資料庫、設定 provider 
 * 或遠端服務時，可使用此實作。
 *
 * 主要功能與流程：
 * 本檔宣告 StaticLineRichMenuCatalog。
 * - 建構式傳入 definitions 的不可變時間點快照。 先複製成 list，可避免來源 enumerable 後續異動影響 provisioning workflow 要同步的選單。
 * - 從傳入的 RichMenu definitions 建立靜態 catalog。
 * - 回傳預先設定的 RichMenu definitions。
 * 主要操作入口包含 GetDefinitionsAsync。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs 追查目前工作樹中的合作／使用位置。
 */
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
