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
    /// 註冊 Local／Central Gateway 共用的 Multi-Profile Runtime ownership graph。
    /// 每個 Profile Generation 由 Factory 建立獨立 Client、Transport、Token Provider 與 Handler；
    /// 程序內只透過 <see cref="IOrganizationAdmissionRegistry"/> 共享 Canonical Organization 容量。
    /// </summary>
    /// <param name="services">Gateway Host 的服務集合。</param>
    /// <param name="profiles">
    /// 部署設定建立的不可變 Profile Definitions；集合會在註冊時複製與驗證，後續呼叫端修改原集合不會改變 Catalog。
    /// </param>
    /// <returns>同一服務集合，供 Host 繼續註冊驗證、授權與 ASP.NET 端點。</returns>
    /// <remarks>
    /// 此 overload 刻意不註冊全域 <see cref="IDynamicsWebApiClient"/>、<see cref="IDynamicsHttpTransport"/>
    /// 或 <see cref="IAdfsOAuthTokenProvider"/>，避免 crm82／crm91 共用可變連線、Token 或 Credential 狀態。
    /// 既有 <see cref="AddSpeechMessageDynamicsWebApi"/> 保持不變，供單一 Profile Embedded／Smoke Test 相容路徑使用。
    /// </remarks>
    public static IServiceCollection AddSpeechMessageDynamicsProfiles(
        this IServiceCollection services,
        IReadOnlyCollection<DynamicsProfileDefinition> profiles)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0)
        {
            throw new ArgumentException(
                "At least one Dynamics profile definition is required.",
                nameof(profiles));
        }

        var profileSnapshot = profiles.ToArray();
        foreach (var profile in profileSnapshot)
        {
            ArgumentNullException.ThrowIfNull(profile);
            var options = profile.CreateOptionsSnapshot();
            if (!ValidateDynamicsOptions(options))
            {
                throw new ArgumentException(
                    $"Dynamics profile '{profile.ProfileAlias}' failed configuration validation.",
                    nameof(profiles));
            }
        }

        // Runtime Factory 直接建立兩組 Generation-local HTTP ownership graph（CRM 與 ADFS Token），
        // 因此這裡不使用 IHttpClientFactory 的 named handler pool，也不建立可跨 Generation 共用的 Client singleton。
        services.TryAddSingleton<ISecretResolver, EnvironmentSecretResolver>();
        services.TryAddSingleton<IRuntimeHostSlotCoordinator, InMemoryRuntimeHostSlotCoordinator>();
        services.TryAddSingleton<IOrganizationAdmissionRegistry, OrganizationAdmissionRegistry>();
        services.TryAddSingleton<IDynamicsProfileRuntimeFactory, DynamicsProfileRuntimeFactory>();
        services.TryAddSingleton<IDynamicsProfileRuntimeManager>(serviceProvider =>
            new DynamicsProfileRuntimeManager(
                profileSnapshot,
                serviceProvider.GetRequiredService<IDynamicsProfileRuntimeFactory>()));
        services.TryAddSingleton<IProfileExecutionLeaseProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<IDynamicsProfileRuntimeManager>());
        services.TryAddSingleton<ProfileRoutedOperationExecutor>(serviceProvider =>
            new ProfileRoutedOperationExecutor(
                serviceProvider.GetRequiredService<IProfileExecutionLeaseProvider>()));

        // Gateway 的正式執行邊界必須指向 Profile-aware Executor；先移除可能由其他單一 Profile
        // scaffolding 留下的 executor descriptor，但不移除其 Client／Transport，以免此方法暗中接管外部資源 ownership。
        services.RemoveAll<IDynamicsOperationExecutor>();
        services.AddSingleton<IDynamicsOperationExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<ProfileRoutedOperationExecutor>());
        return services;
    }

    public static IServiceCollection AddSqlRuntimeHostSlotCoordinator(
        this IServiceCollection services,
        Action<SqlRuntimeHostSlotCoordinatorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqlRuntimeHostSlotCoordinatorOptions();
        configure(options);
        options.Validate();

        services.RemoveAll<IRuntimeHostSlotCoordinator>();
        services.AddSingleton(options);
        services.AddSingleton<SqlRuntimeHostSlotCoordinator>();
        services.AddSingleton<IRuntimeHostSlotCoordinator>(sp =>
            sp.GetRequiredService<SqlRuntimeHostSlotCoordinator>());
        return services;
    }

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
            .Validate(ValidateDynamicsOptions, "DynamicsWebApi options failed validation.")
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

    /// <summary>
    /// 驗證單一 Profile 的 Web API root、Authentication tagged union 與 Admission Plan。
    /// 方法只檢查非秘密設定形狀與 Secret Reference 是否存在，不解析或記錄秘密值，也不建立 Client、Handler 或 Host Slot。
    /// 單一 Profile 與 Multi-Profile DI 共用此判斷，避免兩條註冊路徑對相同設定產生漂移。
    /// </summary>
    private static bool ValidateDynamicsOptions(DynamicsWebApiOptions options)
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
            // ROPC/password grant 與未經實機證明的 client-secret token exchange 都是明確禁止的設定形狀。
            // 在 Options validation 階段 fail closed，可保證 Host 尚未建立 Token Provider、Handler、Socket 或
            // secret-resolution 工作就停止，避免錯誤設定形成跨 Generation credential retention。
            if (options.AllowLocalDevPasswordGrant ||
                !string.IsNullOrWhiteSpace(options.ClientSecretName))
            {
                return false;
            }

            var hasBearer = !string.IsNullOrWhiteSpace(options.CredentialReferenceName);
            var hasAuthority =
                !string.IsNullOrWhiteSpace(options.AuthorityUri) ||
                !string.IsNullOrWhiteSpace(options.AuthoritySecretName);
            var hasClientId =
                !string.IsNullOrWhiteSpace(options.ClientId) ||
                !string.IsNullOrWhiteSpace(options.ClientIdSecretName);
            var hasRefreshTokenReference =
                !string.IsNullOrWhiteSpace(options.RefreshTokenSecretName);

            // 合法形狀只有預先核發 bearer，或 authority＋clientId＋refresh-token secret reference。
            // 這裡只驗證參考名稱，不解析 Token，也不建立共用 cache；實際秘密只在 generation-owned provider
            // 的單次 acquisition 內解析，並由 provider Dispose 統一清除 access-token cache。
            if (!hasBearer &&
                !(hasAuthority &&
                  hasClientId &&
                  hasRefreshTokenReference))
            {
                return false;
            }
        }

        return OrganizationAdmissionPlan.TryCreate(
            options,
            options.Admission,
            out _,
            out _);
    }
}
