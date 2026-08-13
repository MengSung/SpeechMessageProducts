// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs
// 目的：產品端註冊 DynamicsAccess（Gateway 模式）與 Package 1 fee-read client。
//
// 保母教學：
// - 產品 JSON 的 DynamicsAccess.ConnectionMode=DedicatedGateway 或 CentralGateway 時用這個擴充。
// - Embedded 模式請先呼叫 Embedded 專案的 AddSpeechMessageDynamicsEmbedded，
//   再呼叫 AddSpeechMessageDynamicsPackage01FeeReads()。
// - 產品只能依賴 ProductClient 與 Gateway 契約，不得參考任何 Dynamics 傳輸實作。
// ============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Net;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.Authentication;
using SpeechMessage.Dynamics.ProductClient.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Gateway;
using SpeechMessage.Dynamics.ProductClient.ListCatalog;
using SpeechMessage.Dynamics.ProductClient.ListManagement;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;

namespace SpeechMessage.Dynamics.ProductClient.DependencyInjection;

/// <summary>
/// 產品端 Dynamics client DI 擴充。
/// </summary>
public static class ProductClientServiceCollectionExtensions
{
    /// <summary>
    /// 以 Gateway 模式註冊產品端 Dynamics 操作執行器 + Package 1 fee-read。
    /// </summary>
    public static IServiceCollection AddSpeechMessageDynamicsGatewayProductClient(
        this IServiceCollection services,
        Action<ProductDynamicsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<ProductDynamicsOptions>,
                GatewayProductDynamicsOptionsValidator>());

        services.AddOptions<ProductDynamicsOptions>()
            .Configure(configure)
            .ValidateOnStart();

        services.AddHttpClient<IDynamicsOperationExecutor, GatewayDynamicsOperationExecutor>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ProductDynamicsOptions>>().Value;
            if (options.Gateway is not null &&
                Uri.TryCreate(options.Gateway.Endpoint, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            client.Timeout = TimeSpan.FromSeconds(60);
        })
        .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            Credentials = CredentialCache.DefaultNetworkCredentials,
            PreAuthenticate = false,
            MaxConnectionsPerServer = 8,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        .SetHandlerLifetime(TimeSpan.FromMinutes(10));

        services.TryAddSingleton<IPackage01FeeReadClient, Package01FeeReadClient>();
        services.TryAddSingleton<IPackage01DedicationBookingReadClient, Package01DedicationBookingReadClient>();
        services.TryAddSingleton<IAppNamedListCatalogReadClient, AppNamedListCatalogReadClient>();
        services.TryAddSingleton<ISmallGroupAppNamedListCatalogReadClient, SmallGroupAppNamedListCatalogReadClient>();
        services.TryAddSingleton<IAuthenticationContactReadClient, AuthenticationContactReadClient>();
        services.TryAddSingleton<IPackage02ContactBasicInfoUpdateClient, Package02ContactBasicInfoUpdateClient>();
        services.TryAddSingleton<IPackage02ContactProfileClient, Package02ContactProfileClient>();
        services.TryAddSingleton<IMemberInfoPresentRecordReadClient, MemberInfoPresentRecordReadClient>();
        services.TryAddSingleton<IPackage02ListManagementClient, Package02ListManagementClient>();
        services.TryAddSingleton<IPackage03SpecialResourceClient, Package03SpecialResourceClient>();
        return services;
    }

    /// <summary>
    /// 只註冊 Package 1 fee-read client（executor 已由 Embedded/Gateway 註冊時使用）。
    /// </summary>
    public static IServiceCollection AddSpeechMessageDynamicsPackage01FeeReads(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPackage01FeeReadClient, Package01FeeReadClient>();
        services.TryAddSingleton<IPackage01DedicationBookingReadClient, Package01DedicationBookingReadClient>();
        return services;
    }

    /// <summary>
    /// 註冊 ORG-CALL-00014 的 stateless app-named catalog ProductClient。
    /// 此註冊不建立 consumer、feature gate、cache、retry 或 CE 流量；executor 仍由既有 Gateway/Embedded composition
    /// 擁有。singleton 安全的前提是 client 不保留 profile、workload、request、DTO 或 response，所有這些資料都在
    /// 每次呼叫建立並於完成後由 GC 回收，外部 transport/lease 則由 executor 的既有 deterministic cleanup 擁有。
    /// </summary>
    /// <param name="services">要加入封閉 catalog client 的 composition root service collection。</param>
    /// <returns>同一個 service collection，供 composition root 繼續鏈結。</returns>
    public static IServiceCollection AddSpeechMessageDynamicsAppNamedListCatalogReads(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IAppNamedListCatalogReadClient, AppNamedListCatalogReadClient>();
        return services;
    }

    /// <summary>
    /// 註冊 ORG-CALL-00065 的 stateless small-group app-named catalog ProductClient。
    /// 此方法只加入 client descriptor，不建立 consumer、feature gate、CE traffic、cache、retry、background task 或
    /// connector；Gateway/Embedded executor 仍是 transport、lease、permit 與 cancellation/fault cleanup 的單一 owner。
    /// singleton 不保留 profile、workload、request、leader GUID、DTO 或 response，因此每次呼叫維持 request-local
    /// 隔離並在完成後不留下可變 state。
    /// </summary>
    /// <param name="services">要加入封閉 small-group catalog client 的 composition root service collection。</param>
    /// <returns>同一個 service collection，供 composition root 繼續鏈結。</returns>
    public static IServiceCollection AddSpeechMessageDynamicsSmallGroupAppNamedListCatalogReads(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISmallGroupAppNamedListCatalogReadClient, SmallGroupAppNamedListCatalogReadClient>();
        return services;
    }

    /// <summary>
    /// 只註冊 P7.4 authentication contact read 的 stateless typed client。這個方法不啟用 deployment gate、
    /// 不建立 executor、host、HttpClient、handler、pool、credential 或任何 CE I/O；caller 必須先由自己的
    /// disabled-by-default composition root 驗證 gate、deployment profile 與 server-side authorization，且不得把
    /// 這個 local-only registration 接入既有登入、Session、QR、付款或 legacy fallback 流程。
    /// </summary>
    /// <param name="services">目前 composition root 的 service collection。</param>
    /// <returns>已加入但尚未解析下游資源的同一 service collection。</returns>
    public static IServiceCollection AddSpeechMessageDynamicsAuthenticationContactReads(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IAuthenticationContactReadClient, AuthenticationContactReadClient>();
        return services;
    }

    /// <summary>
    /// 只註冊 P7.2 contact basic-info typed client；它不啟用 ChurchReport 流量，也不建立下游 transport。
    /// 呼叫端必須先註冊 Embedded 或 Gateway 的 <see cref="IDynamicsOperationExecutor"/>；真正的 profile、
    /// ConnectorKind、CE version、admission 與 Data8 lease 仍由該 executor 的 composition root 擁有。
    /// </summary>
    public static IServiceCollection AddSpeechMessageDynamicsPackage02ContactBasicInfoUpdates(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPackage02ContactBasicInfoUpdateClient, Package02ContactBasicInfoUpdateClient>();
        return services;
    }

    /// <summary>
    /// 只註冊 P7.2 Slice B contact profile typed client；它不啟用 ChurchReport 流量、不建立 transport，也不持有
    /// LINE token、session、profile state 或 connector resource。呼叫端必須先註冊唯一的 Embedded/Gateway executor。
    /// </summary>
    public static IServiceCollection AddSpeechMessageDynamicsPackage02ContactProfileOperations(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPackage02ContactProfileClient, Package02ContactProfileClient>();
        return services;
    }

    /// <summary>
    /// 註冊 ORG-CALL-00026 的獨立 MemberInfo 個人出席紀錄唯讀 ProductClient。
    /// 此方法只加入不持有 request/profile/contact/response 狀態的 singleton，刻意不註冊或取得
    /// <see cref="IPackage02ContactProfileClient"/>，以防 disabled-by-default present-read composition 意外取得
    /// LINE 寫入或 aggregate capability。executor、Gateway/Embedded transport、connector、lease、credential graph、
    /// timer 與 disposal 仍由既有 DI/process-host owner 管理；本 registration 不建立 provider、I/O、cache 或背景工作。
    /// </summary>
    /// <param name="services">要加入唯一 DTO-only read boundary 的 composition root service collection。</param>
    /// <returns>已加入無狀態 present-record read client 的同一 service collection。</returns>
    public static IServiceCollection AddSpeechMessageDynamicsMemberInfoPresentRecordReads(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IMemberInfoPresentRecordReadClient, MemberInfoPresentRecordReadClient>();
        return services;
    }

    /// <summary>
    /// 只註冊 P7.2 Slice C list-management typed client。它不啟用 ChurchReport 流量、不建立 transport，且不持有
    /// list/contact/owner fixture、credential、session 或 connector resource；呼叫端必須先註冊唯一的 Embedded/Gateway executor。
    /// </summary>
    public static IServiceCollection AddSpeechMessageDynamicsPackage02ListManagementOperations(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPackage02ListManagementClient, Package02ListManagementClient>();
        return services;
    }

    /// <summary>
    /// 只註冊 P7.3 image、metadata 與 weekly-statistics typed client。它不啟用 ChurchReport consumer、feature gate、
    /// CE mutation 或 shared metadata cache；呼叫端必須先由 Embedded/Gateway composition 註冊唯一 executor，
    /// 而 Data8 runtime generation、lease、drain 與 disposal 仍由下游 owner 確定管理。
    /// </summary>
    public static IServiceCollection AddSpeechMessageDynamicsPackage03SpecialResources(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPackage03SpecialResourceClient, Package03SpecialResourceClient>();
        return services;
    }
}
