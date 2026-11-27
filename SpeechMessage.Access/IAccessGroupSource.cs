/*
 * 檔案整體說明
 * 目的與定位：F8跨產品群組範圍的權威來源契約；既有組織樹尚待F0實機確認，因此共用引擎不猜測實體名稱。
 * 主要功能與流程：產品adapter使用當次借用service讀取主體節點及同租戶樹候選，policy再驗身分、產品、租戶、唯一節點、祖先與循環；缺來源時Group一律拒絕。
 * 使用邊界與維護：來源只能由Host提供，不能接受瀏覽器自報祖先；候選最多4096節點，每次重讀，不跨請求快取，不保存service或SDK列。所有作業返回前完成，租約由policy唯一歸還。離線候選不代表實機組織樹已驗證。
 */
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Access;

/// <summary>產品提供群組候選，不以呼叫端自報GroupNodeId當權威祖先證明。</summary>
public interface IAccessGroupSource
{
    /// <summary>在當次租約內完整讀出有界候選；沒有群組回null，SDK故障則拋出，不冒充業務拒絕。</summary>
    AccessGroupSnapshot? Read(IOrganizationService service, Actor actor, CancellationToken ct);
}

/// <summary>正式節點純值；顯示名稱不是身分，ParentId只能指向同租戶正式主鍵。</summary>
/// <param name="Id">資料庫節點主鍵。</param>
/// <param name="TenantId">節點已驗證租戶。</param>
/// <param name="ParentId">父節點，根為null。</param>
public sealed record AccessGroupNode(Guid Id, Guid TenantId, Guid? ParentId);

/// <summary>產品讀取的完整候選，policy會再複製驗證，不能跨請求保存。</summary>
/// <param name="ContactId">已驗證群組主體。</param>
/// <param name="TenantId">候選租戶。</param>
/// <param name="Product">固定產品。</param>
/// <param name="MemberNodeId">主體目前權威所屬節點。</param>
/// <param name="Nodes">最多4096筆正式節點，不可缺父、重複ID或循環。</param>
public sealed record AccessGroupSnapshot(Guid ContactId, Guid TenantId, string Product, Guid MemberNodeId, IReadOnlyList<AccessGroupNode> Nodes);
