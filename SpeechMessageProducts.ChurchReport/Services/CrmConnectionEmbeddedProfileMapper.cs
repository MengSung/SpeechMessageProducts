// ============================================================================
// 檔案：SpeechMessageProducts.ChurchReport/Services/CrmConnectionEmbeddedProfileMapper.cs
// 用途：在 ChurchReport 啟動組合根，將既有 CrmConnection 設定轉為受控的 Dynamics Profile 與 Organization Catalog。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace ChurchReport.Services;

/// <summary>
/// 將 ChurchReport 既有的 <c>CrmConnection</c> 組態限制在啟動期，轉換成 ControlPlane 所需的唯一
/// <see cref="DynamicsProfileOptions"/> 與 <see cref="OrganizationCatalogEntry"/>。
/// 這是組合根的單向邊界：產品執行期間只傳遞 <c>ProfileAlias</c>，不會把 OrganizationId、服務端點、
/// ConnectorKind、密碼或其他 credential 放入 controller、request、session 或 operation payload。
/// </summary>
/// <remarks>
/// Mapper 本身不建立 Data8 client、WCF channel、admission permit、背景工作、計時器或取消權杖來源；它只建立
/// 短生命週期且無秘密值的設定快照。因此沒有可歸還的連線資源，也不會把 password 保留在靜態欄位或跨 request
/// 快取。真正的連線、permit 與 generation drain/dispose 所有權仍由後續 ControlPlane/Data8 pool 持有。
/// </remarks>
public static class CrmConnectionEmbeddedProfileMapper
{
    /// <summary>
    /// 將目前主機的單一 <c>CrmConnection</c> 設定映射為同 alias 的 Data8 Profile 與已啟用的 Organization Catalog。
    /// 在輸入遺漏、格式不正確、非 Embedded mode 或產品 alias 與 CrmConnection organization 不一致時，方法會
    /// fail closed 並回傳空字典；這可保證尚未通過 profile/organization 邊界的 request 永遠無法取得 permit 或 client。
    /// </summary>
    /// <param name="configuration">只在應用程式啟動組合根讀取的設定來源；本方法絕不讀取 Password 欄位。</param>
    /// <param name="options">產品層僅含連線模式與 ProfileAlias 的選項。</param>
    /// <param name="profiles">成功時唯一的受控 Data8 profile；失敗時為空集合。</param>
    /// <param name="catalog">成功時唯一的 enabled organization catalog entry；失敗時為空集合。</param>
    /// <param name="errorCode">可安全記錄且不含端點、GUID、帳號或秘密的失敗代碼。</param>
    /// <returns>只有所有啟動期不變量都成立時才為 <see langword="true"/>。</returns>
    public static bool TryCreate(
        IConfiguration configuration,
        ProductDynamicsOptions options,
        out IReadOnlyDictionary<string, DynamicsProfileOptions> profiles,
        out IReadOnlyDictionary<string, OrganizationCatalogEntry> catalog,
        out string errorCode)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        profiles = EmptyProfiles;
        catalog = EmptyCatalog;
        errorCode = string.Empty;

        if (options.ConnectionMode != ConnectionMode.Embedded)
        {
            errorCode = "embedded.connection-mode-required";
            return false;
        }

        var profileAlias = NormalizeAlias(options.ProfileAlias);
        if (profileAlias.Length == 0 ||
            !TryResolveOrganization(
                configuration,
                profileAlias,
                out var friendlyName,
                out var uniqueName,
                out var organizationId,
                out var ceVersion,
                out var serviceUri,
                out errorCode))
        {
            return false;
        }

        if (!TryReadPositiveInt(configuration, "CrmConnection:MinPoolSize", fallback: 0, out var minPoolSize) ||
            !TryReadPositiveInt(configuration, "CrmConnection:MaxPoolSize", fallback: 20, out var maxPoolSize) ||
            minPoolSize > maxPoolSize ||
            !TryReadPositiveInt(configuration, "CrmConnection:ConnectionTimeoutSeconds", fallback: 35, out var operationTimeoutSeconds) ||
            !TryReadPositiveInt(configuration, "CrmConnection:IdleTimeoutMinutes", fallback: 10, out var idleTimeoutMinutes))
        {
            errorCode = "embedded.pool-policy-invalid";
            return false;
        }

        // 所有值都是啟動時建構的新物件；不可變的字典不會外洩 CrmConnection password，也不會共用可變的 profile state。
        profiles = new Dictionary<string, DynamicsProfileOptions>(StringComparer.OrdinalIgnoreCase)
        {
            [profileAlias] = new DynamicsProfileOptions
            {
                OrganizationAlias = profileAlias,
                CeVersion = ceVersion,
                ConnectorKind = ConnectorKind.Data8,
                CredentialReference = "churchreport.crmconnection",
                Pool = new PoolPolicy
                {
                    MinSize = minPoolSize,
                    MaxSize = maxPoolSize,
                    IdleTimeoutMinutes = idleTimeoutMinutes
                },
                Operation = new OperationPolicy
                {
                    TimeoutSeconds = operationTimeoutSeconds
                }
            }
        };

        catalog = new Dictionary<string, OrganizationCatalogEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [profileAlias] = new OrganizationCatalogEntry
            {
                FriendlyName = friendlyName,
                UniqueName = uniqueName,
                OrganizationId = organizationId,
                State = OrganizationState.Enabled,
                ServiceUri = serviceUri.AbsoluteUri
            }
        };

        return true;
    }

    /// <summary>
    /// 在 host 啟動 composition root 讀取既有 <c>CrmConnection</c> 的唯一 credential 設定，建立只可交給
    /// Data8 Factory 的受控 settings owner。此方法與 <see cref="TryCreate"/> 分開，確保 resolver／catalog
    /// 映射本身永遠不必觸及 password；只有真正要建立 Embedded runtime 時才讀取一次。settings 不會被放入
    /// request、Session、Profile、OrganizationCatalog、Pool key、log 或 static 欄位，host Dispose 後其唯一
    /// Factory owner 與整個 composition graph 一同釋放。
    /// </summary>
    /// <param name="configuration">僅限啟動期的設定來源；不可由 controller 或 request 傳入。</param>
    /// <param name="expectedServiceUri">已由中央 Catalog 驗證的 HTTPS 組織服務 URI；它是 Embedded factory 的唯一 endpoint 來源。</param>
    /// <param name="settings">成功時交由 Data8 Factory 使用的設定 owner；失敗時為 null。</param>
    /// <param name="errorCode">不含 endpoint、帳號、密碼或 Organization GUID 的固定錯誤分類。</param>
    /// <returns>只有 HTTPS service URI、帳號與有效密碼均存在時為 <see langword="true"/>。</returns>
    public static bool TryCreateConnectionSettings(
        IConfiguration configuration,
        string expectedServiceUri,
        out Data8OnPremiseConnectionSettings? settings,
        out string errorCode)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        settings = null;
        errorCode = string.Empty;
        if (!Uri.TryCreate(expectedServiceUri, UriKind.Absolute, out var expectedUri) ||
            !string.Equals(expectedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(expectedUri.Host))
        {
            errorCode = "embedded.connection-service-uri-invalid";
            return false;
        }

        var password = configuration["CrmConnection:Password"];
        if (string.Equals(
                password,
                "REPLACE_WITH_USER_SECRET_OR_ENVIRONMENT",
                StringComparison.Ordinal))
        {
            // placeholder 不可作為真實 credential；只在啟動期讀取明確的環境覆寫，且不寫回設定或 static state。
            password = Environment.GetEnvironmentVariable("CRM_PASSWORD");
        }

        try
        {
            settings = new Data8OnPremiseConnectionSettings(
                "churchreport.crmconnection",
                expectedUri.AbsoluteUri,
                configuration["CrmConnection:Username"] ?? string.Empty,
                password ?? string.Empty);
            return true;
        }
        catch (ArgumentException)
        {
            settings = null;
            errorCode = "embedded.connection-credentials-invalid";
            return false;
        }
    }

    private static readonly IReadOnlyDictionary<string, DynamicsProfileOptions> EmptyProfiles =
        new Dictionary<string, DynamicsProfileOptions>(0, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, OrganizationCatalogEntry> EmptyCatalog =
        new Dictionary<string, OrganizationCatalogEntry>(0, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 從 <c>CrmConnection:OrganizationCatalog</c> 解析目前唯一選取的別名。中央 Catalog 是 Organization GUID、
    /// CE 版本與目標服務 URI 的唯一來源；產品層只提供 alias，不能自行夾帶這些值。尚未補齊服務 URI 的已知組織
    /// 會在此處拒絕，絕不可退回目前其他組織的 CrmConnection:ServerUrl 猜測連線，避免跨版本／跨租戶 session、
    /// pool 或 credential 誤用。為維持 P4 既有組態相容性，僅在中央 Catalog 完全不存在時才採用舊單一欄位，
    /// 且仍要求 alias 與 CrmConnection:Organization 精確一致。
    /// </summary>
    private static bool TryResolveOrganization(
        IConfiguration configuration,
        string profileAlias,
        out string friendlyName,
        out string uniqueName,
        out Guid organizationId,
        out CeVersion ceVersion,
        out Uri serviceUri,
        out string errorCode)
    {
        friendlyName = string.Empty;
        uniqueName = string.Empty;
        organizationId = Guid.Empty;
        ceVersion = default;
        serviceUri = null!;
        errorCode = string.Empty;

        var catalogRoot = configuration.GetSection("CrmConnection:OrganizationCatalog");
        IConfigurationSection? selected = null;
        var hasCentralCatalog = false;
        foreach (var candidate in catalogRoot.GetChildren())
        {
            hasCentralCatalog = true;
            if (string.Equals(candidate.Key, profileAlias, StringComparison.OrdinalIgnoreCase))
            {
                selected = candidate;
                break;
            }
        }

        if (selected is null)
        {
            if (hasCentralCatalog)
            {
                errorCode = "embedded.organization-alias-not-found";
                return false;
            }

            var legacyAlias = NormalizeAlias(configuration["CrmConnection:Organization"]);
            if (!string.Equals(profileAlias, legacyAlias, StringComparison.OrdinalIgnoreCase))
            {
                errorCode = "embedded.profile-alias-mismatch";
                return false;
            }

            friendlyName = legacyAlias;
            uniqueName = legacyAlias;
            if (!Guid.TryParse(configuration["CrmConnection:OrganizationId"], out organizationId) ||
                organizationId == Guid.Empty)
            {
                errorCode = "embedded.organization-id-invalid";
                return false;
            }

            if (!TryParseCeVersion(configuration["CrmConnection:CeVersion"], out ceVersion))
            {
                errorCode = "embedded.ce-version-unsupported";
                return false;
            }

            return TryParseHttpsServiceUri(configuration["CrmConnection:ServerUrl"], out serviceUri, out errorCode);
        }

        friendlyName = NormalizeAlias(selected["FriendlyName"]);
        uniqueName = NormalizeAlias(selected["UniqueName"]);
        if (friendlyName.Length == 0 || uniqueName.Length == 0 ||
            !Guid.TryParse(selected["OrganizationId"], out organizationId) || organizationId == Guid.Empty)
        {
            errorCode = "embedded.organization-catalog-entry-invalid";
            return false;
        }

        if (!string.Equals(selected["State"]?.Trim(), "Enabled", StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "embedded.organization-disabled";
            return false;
        }

        if (!TryParseCeVersion(selected["CeVersion"], out ceVersion))
        {
            errorCode = "embedded.ce-version-unsupported";
            return false;
        }

        return TryParseHttpsServiceUri(selected["ServiceUri"], out serviceUri, out errorCode);
    }

    /// <summary>
    /// 只接受具主機名稱的 HTTPS Organization Service URI。這個檢查在 factory、permit 或 client 建立前完成；
    /// URI 缺失代表目前只有組織身分而沒有經核准的連線目標，必須 fail closed，不能拿其他 profile 的 endpoint 補值。
    /// </summary>
    private static bool TryParseHttpsServiceUri(string? value, out Uri serviceUri, out string errorCode)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(candidate.Host))
        {
            serviceUri = candidate;
            errorCode = string.Empty;
            return true;
        }

        serviceUri = null!;
        errorCode = "embedded.organization-service-uri-invalid";
        return false;
    }

    /// <summary>
    /// <summary>
    /// 將設定 alias 正規化成不含前後空白的 key。alias 只在啟動組合根使用，並由後續 resolver 固定到同一個 organization；
    /// 不把此可識別資料存入跨使用者 session、靜態 request cache 或可變 singleton 欄位。
    /// </summary>
    private static string NormalizeAlias(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>
    /// 只接受本專案已承諾支援的 CE 8.2 與 9.1，未知版本立即拒絕而不退回其他 connector，避免跨版本或跨組織錯誤共用連線。
    /// </summary>
    private static bool TryParseCeVersion(string? value, out CeVersion ceVersion)
    {
        switch (value?.Trim())
        {
            case "8.2":
                ceVersion = CeVersion.Ce82;
                return true;
            case "9.1":
                ceVersion = CeVersion.Ce91;
                return true;
            default:
                ceVersion = default;
                return false;
        }
    }

    /// <summary>
    /// 讀取正整數設定；未設定時採用明確且有界的預設值，格式錯誤或小於零時拒絕。
    /// MinPoolSize 允許零以避免 Embedded F5 啟動就強制建立 client；client 的建立與釋放仍由 pool lease 管理。
    /// </summary>
    private static bool TryReadPositiveInt(
        IConfiguration configuration,
        string key,
        int fallback,
        out int value)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = fallback;
            return true;
        }

        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
    }
}
