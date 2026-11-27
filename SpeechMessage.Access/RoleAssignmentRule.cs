/*
 * 檔案整體說明
 * 目的與定位：F8最後管理員保護的純值判斷，依正式指派快照計算，不執行資料庫刪除。
 * 主要功能與流程：消費伺服器取得的當前有效性快照與欲移除的正式指派，不保存集合或比對顯示名稱。
 * 使用邊界與維護：所有輸入都僅存在當次判斷；本helper沒有交易、鎖或跨程序保證，寫入端必須在同一資料庫交易重新驗證並刪除。資料列必須有唯一正式ID及一致租戶，不能以顯示名或重複列湊足管理員。
 */
namespace SpeechMessage.Access;

/// <summary>伺服器解析的指派快照；有效性包含指派、角色及Contact都active。</summary>
/// <param name="AssignmentId">資料庫指派主鍵，不能用顯示名取代。</param>
/// <param name="ContactId">被指派的正式Contact主鍵。</param>
/// <param name="TenantId">已驗證的租戶主鍵。</param>
/// <param name="IsAdministrator">角色有管理權限，不由角色顯示名稱推測。</param>
/// <param name="IsActive">指派、角色及Contact是否全部有效。</param>
public sealed record RoleAssignment(Guid AssignmentId, Guid ContactId, Guid TenantId, bool IsAdministrator, bool IsActive);

/// <summary>不負責交易或持久化的最後有效管理員判斷。</summary>
public static class RoleAssignmentRule
{
    /// <summary>驗證完整候選及欲移除列精確相同，移除後仍有一個有效管理員才允許；不執行交易或刪除。</summary>
    public static bool CanRemoveAssignment(IReadOnlyList<RoleAssignment> current, RoleAssignment removing)
    {
        if (current is null || removing is null || current.Count > 4096 || removing.AssignmentId == Guid.Empty ||
            removing.ContactId == Guid.Empty || removing.TenantId == Guid.Empty) return false;
        var ids = new HashSet<Guid>();
        bool found = false;
        bool remaining = false;
        foreach (RoleAssignment row in current)
        {
            if (row is null || row.AssignmentId == Guid.Empty || row.ContactId == Guid.Empty || row.TenantId != removing.TenantId || !ids.Add(row.AssignmentId)) return false;
            if (row.AssignmentId == removing.AssignmentId)
            {
                if (row != removing) return false;
                found = true;
            }
            else if (row.IsAdministrator && row.IsActive) remaining = true;
        }
        return found && remaining;
    }
}
