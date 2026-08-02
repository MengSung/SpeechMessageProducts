// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Capacity/CapacityKeys.cs
// 目的：定義容量命名空間，避免環境標籤或 profile 世代誤當獨立預算。
//
// 保母教學：
// - CanonicalOrganizationCapacityKey：同一個實體 Organization 只有一份總預算。
// - OrganizationAdmissionKey：queue / permit 命名空間，必須回指同一個 canonical key。
// - RuntimeHostSlotLeaseNamespace：Gateway/Embedded host 佔位租約命名空間。
// - 這三個 key 都不可包含使用者、LINE ID、token、密碼、session。
// ============================================================================

namespace SpeechMessage.Dynamics.ControlPlane.Capacity;

/// <summary>
/// 實體 Organization 的總容量鍵。
/// </summary>
public readonly record struct CanonicalOrganizationCapacityKey(
    Guid ExpectedOrganizationId,
    string NormalizedOrganizationBaseUri)
{
    // SQL Server 的 nvarchar unique key 上限是 900 bytes；BIN2 下每個 UTF-16 code unit 仍需兩個 bytes，
    // 所以 canonical base URI 的 durable-store 表示固定限制為 450 個字元。此界限在設定階段執行，
    // 避免 runtime 才因資料庫索引失敗而留下半建立的 lease、transaction 或背景資源。
    internal const int MaximumNormalizedOrganizationBaseUriLength = 450;

    /// <summary>
    /// 從已驗證的 Dynamics Organization root 建立唯一的實體 Organization 容量鍵。
    /// 直接傳輸 endpoint 會被拒絕；容量鍵只接受不帶傳輸路徑的 Organization root，
    /// 並保留 Organization Virtual Directory 的大小寫與結構；Scheme 與 Host 則依 URI 規則正規化。
    /// 這個區分很重要：Host 不分大小寫，但 On-Premises 反向代理或 Virtual Directory 路徑可能區分大小寫，
    /// 若把整條 URI 一律轉小寫，可能把兩個不同部署錯誤合併成同一份容量與 Credential 邊界。
    /// </summary>
    /// <param name="expectedOrganizationId">已由 Discovery／WhoAmI 或部署證據確認的 Organization GUID。</param>
    /// <param name="approvedWebApiRoot">已通過 HTTPS、來源、路徑與版本驗證的完整 Web API Root。</param>
    /// <param name="apiVersionSegment">版本路徑片段，例如 <c>v8.2</c> 或 <c>v9.1</c>。</param>
    /// <param name="key">成功時回傳不含 Alias、Token、Credential 或 Session 的 Canonical Key。</param>
    /// <param name="error">失敗時回傳可安全顯示的設定錯誤，不含秘密值。</param>
    /// <returns>只有在 GUID、HTTPS URI 與版本尾端都有效時才回傳 <see langword="true"/>。</returns>
    public static bool TryCreate(
        Guid expectedOrganizationId,
        Uri organizationBaseUri,
        out CanonicalOrganizationCapacityKey key,
        out string error)
    {
        key = default;
        error = string.Empty;

        if (expectedOrganizationId == Guid.Empty)
        {
            error = "ExpectedOrganizationId is required.";
            return false;
        }

        if (organizationBaseUri is null ||
            !organizationBaseUri.IsAbsoluteUri ||
            !string.Equals(organizationBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(organizationBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(organizationBaseUri.Query) ||
            !string.IsNullOrEmpty(organizationBaseUri.Fragment) ||
            string.IsNullOrWhiteSpace(organizationBaseUri.Host))
        {
            error = "Organization base URI must be an absolute HTTPS URI without user-info, query, or fragment.";
            return false;
        }

        var organizationPath = organizationBaseUri.AbsolutePath.TrimEnd('/') + "/";
        if (organizationPath.Contains("/api/data/", StringComparison.OrdinalIgnoreCase) ||
            organizationPath.Contains("/XRMServices/", StringComparison.OrdinalIgnoreCase))
        {
            error = "Organization base URI must be an organization root, not a direct Dynamics transport endpoint.";
            return false;
        }

        // 只移除最尾端的 Web API 版本路徑；不可用 Replace，否則 Virtual Directory 中同名片段也會被誤刪。
        // UriBuilder 正確處理 IPv6 方括號與非預設 Port；只把不具大小寫語意的 Scheme／Host 正規化。
        var builder = new UriBuilder(
            organizationBaseUri.Scheme.ToLowerInvariant(),
            organizationBaseUri.Host.ToLowerInvariant(),
            organizationBaseUri.IsDefaultPort ? -1 : organizationBaseUri.Port,
            organizationPath);
        var normalizedBaseUri = builder.Uri.GetLeftPart(UriPartial.Path);
        if (!normalizedBaseUri.EndsWith("/", StringComparison.Ordinal))
        {
            normalizedBaseUri += "/";
        }

        if (normalizedBaseUri.Length > MaximumNormalizedOrganizationBaseUriLength)
        {
            error = $"Normalized organization base URI must not exceed {MaximumNormalizedOrganizationBaseUriLength} characters.";
            return false;
        }

        key = new CanonicalOrganizationCapacityKey(expectedOrganizationId, normalizedBaseUri);
        return true;
    }

    /// <summary>
    /// 產生僅供診斷的人類可讀字串；Durable Store 或跨程序 Key 必須使用另外版本化、長度前綴的編碼，
    /// 不可依賴這個顯示格式，避免分隔符號或未來格式變更造成鍵值碰撞。
    /// </summary>
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
