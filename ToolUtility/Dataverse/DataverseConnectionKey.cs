/*
 * 檔案整體說明
 *
 * 目的與定位：池化連線的完整分割鍵。只有四個邊界值全部相同的請求才可互換池內 client， 以阻止不同產品、環境、組織或有效身分共用連線狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 DataverseConnectionKey。
 * - 部署環境識別，例如 Development 或 Production。
 * - 目前有效服務身分；未來啟用 impersonation 時仍是隔離邊界。
 * 主要操作入口包含 Equals、GetHashCode、ToString、RequireValue。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/DependencyInjection/ProductRegistrationTests.cs、ToolUtility/Dataverse/BoundedClientPool.cs、
 * ToolUtility/Dataverse/DataverseConnectionManager.cs、ToolUtility/Dataverse/IBoundedClientPool.cs、
 * ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
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
