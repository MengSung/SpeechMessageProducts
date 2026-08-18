using System;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 池化連線的完整分割鍵。只有四個邊界值全部相同的請求才可互換池內 client，
/// 以阻止不同產品、環境、組織或有效身分共用連線狀態。
/// </summary>
public readonly struct DataverseConnectionKey : IEquatable<DataverseConnectionKey>
{
    /// <summary>建立不可變的 Dataverse 連線分割鍵。</summary>
    public DataverseConnectionKey(string product, string environment, string organizationUrl, string effectiveIdentity)
    {
        Product = RequireValue(product, nameof(product));
        Environment = RequireValue(environment, nameof(environment));
        OrganizationUrl = RequireValue(organizationUrl, nameof(organizationUrl));
        EffectiveIdentity = RequireValue(effectiveIdentity, nameof(effectiveIdentity));
    }

    /// <summary>產品識別，例如 ChurchReport。</summary>
    public string Product { get; }

    /// <summary>部署環境識別，例如 Development 或 Production。</summary>
    public string Environment { get; }

    /// <summary>Dataverse 組織端點 URL。</summary>
    public string OrganizationUrl { get; }

    /// <summary>目前有效服務身分；未來啟用 impersonation 時仍是隔離邊界。</summary>
    public string EffectiveIdentity { get; }

    /// <inheritdoc />
    public bool Equals(DataverseConnectionKey other)
    {
        return StringComparer.Ordinal.Equals(Product, other.Product) &&
            StringComparer.Ordinal.Equals(Environment, other.Environment) &&
            StringComparer.Ordinal.Equals(OrganizationUrl, other.OrganizationUrl) &&
            StringComparer.Ordinal.Equals(EffectiveIdentity, other.EffectiveIdentity);
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is DataverseConnectionKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(Product),
            StringComparer.Ordinal.GetHashCode(Environment),
            StringComparer.Ordinal.GetHashCode(OrganizationUrl),
            StringComparer.Ordinal.GetHashCode(EffectiveIdentity));
    }

    /// <summary>值相等運算子。</summary>
    public static bool operator ==(DataverseConnectionKey left, DataverseConnectionKey right) => left.Equals(right);

    /// <summary>值不等運算子。</summary>
    public static bool operator !=(DataverseConnectionKey left, DataverseConnectionKey right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        $"{Product}|{Environment}|{OrganizationUrl}|{EffectiveIdentity}";

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("連線分割鍵不得為空白。", name);
        return value;
    }
}
