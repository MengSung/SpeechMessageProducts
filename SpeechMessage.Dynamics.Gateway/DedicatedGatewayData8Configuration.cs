using Microsoft.Extensions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Connectors.Data8;
using SpeechMessage.Dynamics.ControlPlane.Configuration;

namespace SpeechMessage.Dynamics.Gateway;

/// <summary>
/// Dedicated Gateway 的 deployment-owned Data8 組態快照。
/// 此類別只在啟動期讀取組態並建立不可變 profile/catalog 與 connection settings；不訂閱 reload、
/// 不寫入 static cache，也不將密碼或 service URI 放入 HTTP request、log、pool key 或 response。
/// </summary>
internal sealed class DedicatedGatewayData8Configuration
{
    /// <summary>取得 runtime 唯一可解析的 profile snapshot。</summary>
    public required IReadOnlyDictionary<string, DynamicsProfileOptions> Profiles { get; init; }

    /// <summary>取得 runtime 唯一可使用的 organization catalog snapshot。</summary>
    public required IReadOnlyDictionary<string, OrganizationCatalogEntry> Catalog { get; init; }

    /// <summary>取得選取 profile 的 Data8 factory 設定；僅在 host-owned factory 內保存密碼。</summary>
    public required Data8OnPremiseConnectionSettings ConnectionSettings { get; init; }

    /// <summary>
    /// 讀取並 fail-closed 驗證 Dedicated Data8 設定。
    /// 只接受 Data8 connector、啟用 organization、精確的 credential reference 與 HTTPS Organization.svc URI；
    /// 任一條件不成立時不建立 pool 或 client，避免錯誤 profile 共享資源。
    /// </summary>
    public static DedicatedGatewayData8Configuration Load(
        IConfiguration configuration,
        DedicatedGatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        var profiles = configuration.GetSection("DynamicsConnectionManagement:Profiles")
            .GetChildren()
            .ToDictionary(
                static section => section.Key,
                static section => section.Get<DynamicsProfileOptions>() ?? new DynamicsProfileOptions(),
                StringComparer.OrdinalIgnoreCase);
        var catalog = configuration.GetSection("DynamicsConnectionManagement:OrganizationCatalog")
            .GetChildren()
            .ToDictionary(
                static section => section.Key,
                static section => section.Get<OrganizationCatalogEntry>() ?? new OrganizationCatalogEntry(),
                StringComparer.OrdinalIgnoreCase);

        var resolver = new ConfigurationProfileResolver(profiles, catalog, generationId: 1);
        if (!resolver.TryResolve(options.ProfileAlias, out var profile, out var error) || profile is null ||
            profile.ConnectorKind != ConnectorKind.Data8)
        {
            throw new InvalidOperationException("The Dedicated Gateway profile must resolve to an active Data8 profile: " + error);
        }

        if (!catalog.TryGetValue(profile.OrganizationAlias, out var organization) || organization is null ||
            organization.State != OrganizationState.Enabled ||
            !Uri.TryCreate(organization.ServiceUri, UriKind.Absolute, out var serviceUri) ||
            !string.Equals(serviceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Dedicated Gateway organization catalog entry is invalid.");
        }

        var settings = new Data8OnPremiseConnectionSettings(
            profile.CredentialReference,
            serviceUri.AbsoluteUri,
            Required(configuration, "DedicatedGateway:Data8:Username"),
            Required(configuration, "DedicatedGateway:Data8:Password"));
        return new DedicatedGatewayData8Configuration
        {
            Profiles = profiles,
            Catalog = catalog,
            ConnectionSettings = settings
        };
    }

    /// <summary>讀取不含前後空白的必要 scalar；不接受 placeholder 或 request-time 覆寫。</summary>
    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.Equals(value, "REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT", StringComparison.Ordinal))
        {
            value = Environment.GetEnvironmentVariable("CRM_PASSWORD");
        }
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            string.Equals(value, "REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{key} is required for the Dedicated Gateway Data8 host.");
        }

        return value;
    }
}
