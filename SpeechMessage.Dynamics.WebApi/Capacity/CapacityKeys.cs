// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Capacity/CapacityKeys.cs
// 目的：定義容量命名空間，避免環境標籤或 profile 世代誤當獨立預算。
//
// 保母教學：
// - CanonicalOrganizationCapacityKey：同一個實體 Organization 只有一份總預算。
// - OrganizationAdmissionKey：queue / permit 命名空間，必須回指同一個 canonical key。
// - RuntimeHostSlotLeaseNamespace：Gateway/Embedded host 佔位租約命名空間。
// - 這三個 key 都不可包含使用者、LINE ID、token、密碼、session。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// 實體 Organization 的總容量鍵。
/// </summary>
public readonly record struct CanonicalOrganizationCapacityKey(
    Guid ExpectedOrganizationId,
    string NormalizedOrganizationBaseUri)
{
    public override string ToString()
        => $"{ExpectedOrganizationId:D}|{NormalizedOrganizationBaseUri}";
}

/// <summary>
/// queue/permit 命名空間。
/// </summary>
public readonly record struct OrganizationAdmissionKey(string AdmissionNamespaceId)
{
    public override string ToString() => AdmissionNamespaceId;
}

/// <summary>
/// runtime host slot 租約命名空間。
/// </summary>
public readonly record struct RuntimeHostSlotLeaseNamespace(string LeaseNamespaceId)
{
    public override string ToString() => LeaseNamespaceId;
}
