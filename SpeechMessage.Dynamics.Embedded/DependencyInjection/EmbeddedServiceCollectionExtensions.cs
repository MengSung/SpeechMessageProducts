// ============================================================================
// 檔案：SpeechMessage.Dynamics.Embedded/DependencyInjection/EmbeddedServiceCollectionExtensions.cs
// 目的：讓單一產品（Visual Studio / 隔離部署）啟用 Embedded 受控操作。
//
// 保姆級教學：
// - 產品只參考 Embedded + Abstractions，不要直接參考 WebApi。
// - Embedded 內部會接上 IDynamicsOperationExecutor，語意與 Gateway 相同。
// - CredentialSource:
//     HostIdentity    = 用目前 Windows 進程身分（IIS AppPool / gMSA）
//     SecretReference = 用秘密名稱解析帳密
// - AuthMode:
//     Windows   = NTLM/Negotiate（純 AD 環境）
//     AdfsOAuth = IFD/claims 環境的 Bearer token（jesus 需要這個）
// - additionalSecrets：本機 local-dev 可傳入「秘密名稱 -> 值」對照表（值來自既有 CrmConnection），
//   但產品 JSON 仍只存秘密名稱，不存密碼。
// ============================================================================

using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.WebApi.DependencyInjection;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Embedded.DependencyInjection;

/// <summary>
/// Embedded 模式 DI 註冊。
/// </summary>
public static class EmbeddedServiceCollectionExtensions
{
    /// <summary>
    /// 依產品 DynamicsAccess 設定啟用 Embedded 受控操作執行器。
    /// </summary>
    /// <param name="additionalSecrets">
    /// 可選：本機開發用秘密對照表（key=秘密名稱，value=秘密值）。
    /// 正式環境應改走環境變數 / KeyVault，不要依賴這張表。
    /// </param>
    public static IServiceCollection AddSpeechMessageDynamicsEmbedded(
        this IServiceCollection services,
        ProductDynamicsOptions productOptions,
        IReadOnlyDictionary<string, string>? additionalSecrets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(productOptions);

        if (productOptions.ExecutionMode != DynamicsExecutionMode.Embedded)
        {
            throw new InvalidOperationException(
                "AddSpeechMessageDynamicsEmbedded requires ExecutionMode=Embedded.");
        }

        if (productOptions.Embedded is null)
        {
            throw new InvalidOperationException(
                "Embedded options are required when ExecutionMode=Embedded.");
        }

        var embedded = productOptions.Embedded;
        var credentialSource = ParseCredentialSource(embedded.CredentialSource);

        if (credentialSource == DynamicsCredentialSource.SecretReference)
        {
            if (string.IsNullOrWhiteSpace(embedded.UserNameSecretName) ||
                string.IsNullOrWhiteSpace(embedded.PasswordSecretName))
            {
                throw new InvalidOperationException(
                    "Embedded SecretReference requires UserNameSecretName and PasswordSecretName.");
            }
        }

        // 先註冊 secret resolver，讓 WebApi 的 TryAddSingleton 不會蓋掉。
        if (additionalSecrets is { Count: > 0 })
        {
            services.AddSingleton<ISecretResolver>(_ =>
                new ChainedSecretResolver(
                    new EnvironmentSecretResolver(),
                    new DictionarySecretResolver(additionalSecrets)));
        }

        var authMode = ParseAuthMode(embedded.AuthMode);
        var isLocalDevManifest = string.Equals(
            embedded.ManifestOrRegistrySource,
            "local-dev-manifest",
            StringComparison.OrdinalIgnoreCase);

        // 本機 local-dev-manifest + AdfsOAuth：允許用 CrmConnection 橋接帳密做 password grant。
        // 正式環境必須 false，改走 bearer 服務流程。
        var allowLocalDevPasswordGrant =
            authMode == DynamicsAuthMode.AdfsOAuth &&
            isLocalDevManifest &&
            (embedded.AllowLocalDevPasswordGrant ||
             string.IsNullOrWhiteSpace(embedded.CredentialReferenceName));

        if (authMode == DynamicsAuthMode.AdfsOAuth)
        {
            // 兩條合法路：
            // A) 已有 bearer token 秘密（CredentialReferenceName）
            // B) 走 ADFS token endpoint：AuthorityUri + ClientId（本機可再加 password grant）
            var hasBearerRef = !string.IsNullOrWhiteSpace(embedded.CredentialReferenceName);
            var hasAuthority = !string.IsNullOrWhiteSpace(embedded.AuthorityUri);
            var hasClientId =
                !string.IsNullOrWhiteSpace(embedded.ClientId) ||
                !string.IsNullOrWhiteSpace(embedded.ClientIdSecretName);

            if (!hasBearerRef && !(hasAuthority && hasClientId))
            {
                throw new InvalidOperationException(
                    "Embedded AdfsOAuth requires either CredentialReferenceName (pre-issued bearer), " +
                    "or AuthorityUri + ClientId/ClientIdSecretName for ADFS token acquisition.");
            }
        }

        services.AddSpeechMessageDynamicsWebApi(options =>
        {
            options.OrganizationWebApiBaseUri = embedded.OrganizationWebApiBaseUri;
            options.CeVersion = embedded.CeVersion;
            options.AuthMode = authMode;
            options.CredentialSource = credentialSource;
            options.SecretReference = embedded.SecretReference;
            options.UserNameSecretName = embedded.UserNameSecretName;
            options.PasswordSecretName = embedded.PasswordSecretName;
            options.DomainSecretName = embedded.DomainSecretName;
            options.AuthorityUri = embedded.AuthorityUri;
            options.ResourceUri = embedded.ResourceUri;
            options.ClientId = embedded.ClientId;
            options.ClientIdSecretName = embedded.ClientIdSecretName;
            options.ClientSecretName = embedded.ClientSecretName;
            options.CredentialReferenceName = embedded.CredentialReferenceName;
            options.AllowLocalDevPasswordGrant = allowLocalDevPasswordGrant;
            options.RefreshTokenSecretName = embedded.RefreshTokenSecretName;
            options.LocalDevTokenStorePath = embedded.LocalDevTokenStorePath;
            options.RedirectUri = embedded.RedirectUri;
            options.TimeoutSeconds = 30;
            options.MaxConnectionsPerServer = 4;
            options.MaxResponseBytes = 2_097_152;
            options.MaxRetryAttempts = 2;
            options.MaxRetryDelaySeconds = 5;
            options.Admission.ExpectedOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            options.Admission.AggregateMaxInFlight = 24;
            options.Admission.MaximumRuntimeHosts = 6;
            options.Admission.LocalQueueCapacity = 48;
            options.Admission.MaxInFlightAndQueuedPerWorkload = 8;
            options.Admission.AdmissionNamespaceId = "embedded-local-admission";
            options.Admission.LeaseNamespaceId = "embedded-local-host-lease";
            options.Admission.RequireDurableHostCoordinator = false;
        });

        return services;
    }

    private static DynamicsAuthMode ParseAuthMode(string? raw)
    {
        if (string.Equals(raw, "AdfsOAuth", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicsAuthMode.AdfsOAuth;
        }

        return DynamicsAuthMode.Windows;
    }

    private static DynamicsCredentialSource ParseCredentialSource(string? raw)
    {
        if (string.Equals(raw, "SecretReference", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicsCredentialSource.SecretReference;
        }

        return DynamicsCredentialSource.HostIdentity;
    }
}
