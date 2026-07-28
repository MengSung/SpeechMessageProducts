// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs
// 目的：註冊 transport、admission、client、executor。
//
// 保母教學：
// - 產品不要直接呼叫這個 DI 擴充；請走 Gateway 或 Embedded。
// - admission manager 是 singleton：同一個 host 共用一份 bounded queue。
// - 這裡不做 per-user session pool。
// ============================================================================

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WebApi.Capacity;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.WebApi.DependencyInjection;

/// <summary>
/// WebApi 層 DI 擴充方法。
/// </summary>
public static class WebApiServiceCollectionExtensions
{
    /// <summary>
    /// 註冊私有 Dynamics Web API 連線器、admission 與受控操作執行器。
    /// </summary>
    public static IServiceCollection AddSpeechMessageDynamicsWebApi(
        this IServiceCollection services,
        Action<DynamicsWebApiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<DynamicsWebApiOptions>()
            .Configure(configure)
            .Validate(options =>
            {
                if (!ApprovedWebApiRootFactory.TryCreate(options, out _, out _))
                {
                    return false;
                }

                if (options.AuthMode == DynamicsAuthMode.Windows &&
                    options.CredentialSource == DynamicsCredentialSource.SecretReference &&
                    (string.IsNullOrWhiteSpace(options.UserNameSecretName) ||
                     string.IsNullOrWhiteSpace(options.PasswordSecretName)))
                {
                    return false;
                }

                if (options.AuthMode == DynamicsAuthMode.AdfsOAuth)
                {
                    var hasBearer = !string.IsNullOrWhiteSpace(options.CredentialReferenceName);
                    var hasAuthority =
                        !string.IsNullOrWhiteSpace(options.AuthorityUri) ||
                        !string.IsNullOrWhiteSpace(options.AuthoritySecretName);
                    var hasClientId =
                        !string.IsNullOrWhiteSpace(options.ClientId) ||
                        !string.IsNullOrWhiteSpace(options.ClientIdSecretName);
                    var hasPasswordGrantSecrets =
                        options.AllowLocalDevPasswordGrant &&
                        !string.IsNullOrWhiteSpace(options.UserNameSecretName) &&
                        !string.IsNullOrWhiteSpace(options.PasswordSecretName);

                    // 合法：預發 bearer，或 authority+clientId 且（password grant 或另有 secret reference）
                    if (!hasBearer && !(hasAuthority && hasClientId && (hasPasswordGrantSecrets || !string.IsNullOrWhiteSpace(options.SecretReference))))
                    {
                        return false;
                    }
                }

                return OrganizationAdmissionPlan.TryCreate(options, options.Admission, out _, out _);
            }, "DynamicsWebApi options failed validation.")
            .ValidateOnStart();

        services.TryAddSingleton<ISecretResolver, EnvironmentSecretResolver>();
        services.AddHttpClient("dynamics-adfs-token")
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.None,
                PreAuthenticate = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        services.TryAddSingleton<IAdfsOAuthTokenProvider, AdfsOAuthTokenProvider>();
        services.TryAddSingleton<IRuntimeHostSlotCoordinator, InMemoryRuntimeHostSlotCoordinator>();

        services.AddSingleton<IOrganizationAdmissionManager>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DynamicsWebApiOptions>>().Value;
            if (!OrganizationAdmissionPlan.TryCreate(options, options.Admission, out var plan, out var error) ||
                plan is null)
            {
                throw new InvalidOperationException(
                    error?.ErrorMessage ?? "Invalid organization admission plan.");
            }

            var coordinator = sp.GetRequiredService<IRuntimeHostSlotCoordinator>();
            var logger = sp.GetRequiredService<ILogger<OrganizationAdmissionManager>>();
            return new OrganizationAdmissionManager(plan, coordinator, logger);
        });

        services.AddSingleton<IDynamicsHttpTransport, DynamicsHttpTransport>();
        services.AddSingleton<IDynamicsWebApiClient, DynamicsWebApiClient>();
        services.AddSingleton<IDynamicsOperationExecutor, ControlledOperationExecutor>();
        return services;
    }
}
