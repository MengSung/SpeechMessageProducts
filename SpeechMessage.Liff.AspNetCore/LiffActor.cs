/*
檔案整體說明
目的與定位：共通層F6在單次LIFF驗證後使用的純值actor，不包含產品資料服務、HTTP內容或登入token。
主要功能與流程：以權威ContactId、TenantId及角色代碼建立不可變身分快照，複製呼叫端角色集合，拒絕空ID、無界或重複角色；供filter與產品端點驗證資料範圍。
使用邊界與維護：本型別不查詢、不授權、不快取，也沒有可釋放資源；來源resolver必須每次重新驗證完整產品及Channel邊界。只在該次request存活，不能把此值當成下次請求的授權證明。測試須核對原角色清單被改動後快照仍獨立。
*/
namespace SpeechMessage.Liff.AspNetCore;

/// <summary>單次已驗證的權威Contact與Tenant，以及與輸入集合分離的唯讀角色快照；不持有連線或token。</summary>
public sealed record LiffActor
{
    /// <summary>建立僅屬本次解析的不可變快照；角色上限64筆，避免異常來源造成無界保留。</summary>
    /// <param name="contactId">資料庫的非空Contact主鍵。</param>
    /// <param name="tenantId">資料庫的非空租戶Account主鍵。</param>
    /// <param name="roleCodes">當次授權來源重新查得的角色，不接受前端輸入；會複製後發布。</param>
    public LiffActor(Guid contactId, Guid tenantId, IReadOnlyCollection<string> roleCodes)
    {
        if (contactId == Guid.Empty) throw new ArgumentException("Contact identity is required.", nameof(contactId));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant identity is required.", nameof(tenantId));
        ArgumentNullException.ThrowIfNull(roleCodes);
        if (roleCodes.Count > 64) throw new ArgumentException("Role collection exceeds its limit.", nameof(roleCodes));
        var roles = roleCodes.ToArray();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
            if (string.IsNullOrWhiteSpace(role) || role.Length > 128 || role.Any(char.IsControl) || !unique.Add(role))
                throw new ArgumentException("Role collection is invalid.", nameof(roleCodes));
        ContactId = contactId;
        TenantId = tenantId;
        RoleCodes = Array.AsReadOnly(roles);
    }

    /// <summary>本次已授權的Contact權威ID，不是路由或操作重試鍵。</summary>
    public Guid ContactId { get; }
    /// <summary>本次Contact所屬且Channel相符的租戶權威ID。</summary>
    public Guid TenantId { get; }
    /// <summary>每次解析獨立重建的唯讀角色集合；F8未接入時為空，不捏造管理員。</summary>
    public IReadOnlyCollection<string> RoleCodes { get; }
}
