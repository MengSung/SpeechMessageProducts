// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/DependencyInjection/OfficialWorkerServiceCollectionExtensions.cs
// 目的：註冊中立 ControlPlane runtime、官方 Worker supervisor 與唯一 Organization admission ownership graph。
// ============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Connectors;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.ControlPlane.DependencyInjection;

/// <summary>
/// 官方 NuGet Worker ControlPlane 的 DI 擴充方法。
/// 此 composition root 只接受呼叫端已建立的 immutable <see cref="DynamicsProfileDefinition"/> 集合，
/// 不依賴 IConfiguration，也不註冊 direct HTTP transport、token provider、CRM SDK client 或 caller-selected fallback。
/// </summary>
public static class OfficialWorkerServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 Local／Central Gateway 共用的官方 Worker multi-profile runtime graph。
    /// 每個 immutable Profile Generation 擁有自己的 Worker process、Pipe、request gate、retirement CTS 與
    /// admission registration；只有 canonical Organization admission manager 可由相容 generations 共用。
    /// </summary>
    /// <param name="services">Host 擁有並在 shutdown 時反向 Dispose singleton graph 的服務集合。</param>
    /// <param name="profiles">
    /// 部署程式建立的 immutable definitions。方法複製集合與驗證 alias 唯一性；不保存來源 collection，
    /// 也不解析 Credential、Token、endpoint secret 或 worker-profile.xml 內容。
    /// </param>
    /// <returns>同一服務集合，供 Host 繼續註冊 authentication、authorization 與 readiness。</returns>
    public static IServiceCollection AddSpeechMessageDynamicsOfficialWorkers(
        this IServiceCollection services,
        IReadOnlyCollection<DynamicsProfileDefinition> profiles)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0)
        {
            throw new ArgumentException(
                "At least one official Dynamics worker profile definition is required.",
                nameof(profiles));
        }

        var profileSnapshot = profiles.ToArray();
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profileSnapshot)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (!aliases.Add(profile.ProfileAlias))
            {
                throw new ArgumentException(
                    "Official Dynamics worker profile aliases must be unique using case-insensitive comparison.",
                    nameof(profiles));
            }
        }

        // In-memory coordinator 只作為單程序/Testing 預設；正式 Host 必須在 ServiceProvider 建立前
        // 呼叫 AddSqlRuntimeHostSlotCoordinator 取代它。TryAdd 可保留 caller 已先註冊的 durable owner。
        services.TryAddSingleton<IRuntimeHostSlotCoordinator, InMemoryRuntimeHostSlotCoordinator>();
        services.TryAddSingleton<IOrganizationAdmissionRegistry, OrganizationAdmissionRegistry>();
        services.TryAddSingleton<IDynamicsProfileRuntimeFactory, DynamicsProfileRuntimeFactory>();
        services.TryAddSingleton<IDynamicsProfileRuntimeManager>(serviceProvider =>
            new DynamicsProfileRuntimeManager(
                profileSnapshot,
                serviceProvider.GetRequiredService<IDynamicsProfileRuntimeFactory>()));
        services.TryAddSingleton<IActiveProfileGenerationResolver>(serviceProvider =>
            (IActiveProfileGenerationResolver)serviceProvider
                .GetRequiredService<IDynamicsProfileRuntimeManager>());
        services.TryAddSingleton<IProfileExecutionLeaseProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<IDynamicsProfileRuntimeManager>());
        services.TryAddSingleton<ProfileRoutedOperationExecutor>(serviceProvider =>
            new ProfileRoutedOperationExecutor(
                serviceProvider.GetRequiredService<IProfileExecutionLeaseProvider>()));

        // IDynamicsOperationExecutor 是產品/Gateway 唯一派送 seam。移除先前 descriptor 可避免同一 Host
        // 同時存在舊 transport 或 request-time fallback；不移除外部資源型 singleton，因本方法不擁有它們。
        services.RemoveAll<IDynamicsOperationExecutor>();
        services.AddSingleton<IDynamicsOperationExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<ProfileRoutedOperationExecutor>());
        return services;
    }

    /// <summary>
    /// 將已建立的 Official Worker runtime manager 接到 connector-oriented Gateway 執行路徑。
    /// 這個方法必須在 composition root 註冊 <see cref="IProfileResolver"/> 之後呼叫；
    /// registry 只擁有 Pool generation，不擁有 runtime manager，故 DI dispose 順序仍由宿主控制。
    /// Dedicated Gateway 不得呼叫此方法，Dedicated 的 Data8 pool 仍維持獨立 composition。
    /// </summary>
    /// <param name="services">Gateway host 的 DI collection。</param>
    /// <returns>同一個 collection，方便 composition root 串接設定。</returns>
    public static IServiceCollection AddSpeechMessageDynamicsOfficialWorkerConnectorRouting(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<OfficialWorkerConnectorPoolRegistry>();
        services.TryAddSingleton<IConnectorRouter>(serviceProvider =>
            serviceProvider.GetRequiredService<OfficialWorkerConnectorPoolRegistry>());
        services.RemoveAll<ProfileRoutedOperationExecutor>();
        services.AddSingleton<ProfileRoutedOperationExecutor>(serviceProvider =>
            new ProfileRoutedOperationExecutor(
                serviceProvider.GetRequiredService<IProfileResolver>(),
                serviceProvider.GetRequiredService<IConnectorRouter>()));
        services.RemoveAll<IDynamicsOperationExecutor>();
        services.AddSingleton<IDynamicsOperationExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<ProfileRoutedOperationExecutor>());
        return services;
    }

    /// <summary>
    /// 以已驗證 immutable options snapshot 取代單程序 coordinator，註冊耐久 SQL host-slot owner。
    /// 驗證在任何 SqlConnection、connection pool、transaction、timer 或 renewal task 建立前完成，
    /// 且只允許專用 control-plane database 與 Windows integrated authentication。
    /// </summary>
    /// <param name="services">Host 擁有 coordinator 與其所有 SQL/renewal 資源的服務集合。</param>
    /// <param name="configure">只在註冊期間同步填入 options 的 bounded delegate；不得保存 services 或啟動背景工作。</param>
    /// <returns>同一服務集合。</returns>
    public static IServiceCollection AddSqlRuntimeHostSlotCoordinator(
        this IServiceCollection services,
        Action<SqlRuntimeHostSlotCoordinatorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqlRuntimeHostSlotCoordinatorOptions();
        configure(options);
        options.Validate();

        // 替換必須發生在 ServiceProvider materialization 前；已建立的 Registry/Manager 不可原地更換 coordinator，
        // 否則既有 lease owner 可能同時連到兩個容量權威。
        services.RemoveAll<IRuntimeHostSlotCoordinator>();
        services.AddSingleton(options);
        services.AddSingleton<SqlRuntimeHostSlotCoordinator>();
        services.AddSingleton<IRuntimeHostSlotCoordinator>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlRuntimeHostSlotCoordinator>());
        return services;
    }
}
