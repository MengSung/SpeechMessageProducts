/*
 * 檔案整體說明
 *
 * 目的與定位：保存單一 LINE 使用者的 RichMenu 指派狀態。 此狀態讓暫時性選單指派可在稍後還原或移除， 不必向 LINE 查詢只有應用程式才知道的業務 context。
 *
 * 主要功能與流程：
 * 本檔宣告 RichMenuUserState。
 * - 建立描述使用者目前 RichMenu 與選填到期時間的 state record。
 * - 取得 LINE Messaging API link/unlink 操作使用的 LINE userId。
 * - 取得應用程式目前視為此使用者作用中的 menu key。 此 key 稍後會透過 ILineRichMenuIdCache 解析成 provider richMenuId。
 * - 取得 ExpiresAt 經過後應還原的 menu key。 null 代表應解除使用者連結，讓使用者回到 LINE 預設行為。
 * 資料／選項欄位包含 RichMenuUserState、LineUserId、CurrentMenuKey、PreviousMenuKey、ExpiresAt、UpdatedAt，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuExpirationSweepWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs、
 * LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 保存單一 LINE 使用者的 RichMenu 指派狀態。
/// 此狀態讓暫時性選單指派可在稍後還原或移除，
/// 不必向 LINE 查詢只有應用程式才知道的業務 context。
/// </summary>
public sealed class RichMenuUserState
{
    /// <summary>
    /// 建立描述使用者目前 RichMenu 與選填到期時間的 state record。
    /// </summary>
    /// <param name="lineUserId">擁有此 state record 的 LINE userId。</param>
    /// <param name="currentMenuKey">目前指派給使用者的應用程式層級 menu key。</param>
    /// <param name="previousMenuKey">到期後要還原的應用程式層級 menu key；若無則為 null。</param>
    /// <param name="expiresAt">目前指派到期的 UTC-aware 時間點。</param>
    /// <param name="updatedAt">此 state record 最後寫入的時間。</param>
    public RichMenuUserState(
        string lineUserId,
        string currentMenuKey,
        string? previousMenuKey,
        DateTimeOffset? expiresAt,
        DateTimeOffset updatedAt)
    {
        LineUserId = lineUserId;
        CurrentMenuKey = currentMenuKey;
        PreviousMenuKey = previousMenuKey;
        ExpiresAt = expiresAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// 取得 LINE Messaging API link/unlink 操作使用的 LINE userId。
    /// </summary>
    public string LineUserId { get; }

    /// <summary>
    /// 取得應用程式目前視為此使用者作用中的 menu key。
    /// 此 key 稍後會透過 <see cref="ILineRichMenuIdCache"/> 解析成 provider richMenuId。
    /// </summary>
    public string CurrentMenuKey { get; }

    /// <summary>
    /// 取得 <see cref="ExpiresAt"/> 經過後應還原的 menu key。
    /// null 代表應解除使用者連結，讓使用者回到 LINE 預設行為。
    /// </summary>
    public string? PreviousMenuKey { get; }

    /// <summary>
    /// 取得暫時性指派的到期時間。
    /// 永久指派會將此值保留為 null，並被 expiration sweeps 忽略。
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    /// 取得此指派狀態最後更新時間。
    /// 此欄位可供 audit logs 使用，也可給需要可預期排序欄位的 store 使用。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }
}
