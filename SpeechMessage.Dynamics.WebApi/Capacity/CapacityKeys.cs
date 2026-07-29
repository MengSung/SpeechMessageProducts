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
    /// <summary>
    /// 從已驗證的 Dynamics Web API Root 建立唯一的實體 Organization 容量鍵。
    /// 此方法只移除路徑尾端精確匹配的 <c>/api/data/v8.2|v9.1/</c> 片段，
    /// 保留 Organization Virtual Directory 的大小寫與結構；Scheme 與 Host 則依 URI 規則正規化。
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
        Uri approvedWebApiRoot,
        string apiVersionSegment,
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

        if (approvedWebApiRoot is null ||
            !approvedWebApiRoot.IsAbsoluteUri ||
            !string.Equals(approvedWebApiRoot.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(approvedWebApiRoot.UserInfo) ||
            !string.IsNullOrEmpty(approvedWebApiRoot.Query) ||
            !string.IsNullOrEmpty(approvedWebApiRoot.Fragment))
        {
            error = "Approved Web API root must be an absolute HTTPS URI without user-info, query, or fragment.";
            return false;
        }

        var expectedSuffix = $"/api/data/{apiVersionSegment.Trim().Trim('/')}/";
        var approvedPath = approvedWebApiRoot.AbsolutePath.TrimEnd('/') + "/";
        if (!approvedPath.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Approved Web API root does not end with the configured API version path.";
            return false;
        }

        // 只移除最尾端的 Web API 版本路徑；不可用 Replace，否則 Virtual Directory 中同名片段也會被誤刪。
        var organizationPath = approvedPath[..^expectedSuffix.Length];
        if (string.IsNullOrEmpty(organizationPath))
        {
            organizationPath = "/";
        }
        else if (!organizationPath.EndsWith("/", StringComparison.Ordinal))
        {
            organizationPath += "/";
        }

        // UriBuilder 正確處理 IPv6 方括號與非預設 Port；只把不具大小寫語意的 Scheme／Host 正規化。
        var builder = new UriBuilder(
            approvedWebApiRoot.Scheme.ToLowerInvariant(),
            approvedWebApiRoot.Host.ToLowerInvariant(),
            approvedWebApiRoot.IsDefaultPort ? -1 : approvedWebApiRoot.Port,
            organizationPath);
        var normalizedBaseUri = builder.Uri.GetLeftPart(UriPartial.Path);
        if (!normalizedBaseUri.EndsWith("/", StringComparison.Ordinal))
        {
            normalizedBaseUri += "/";
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
