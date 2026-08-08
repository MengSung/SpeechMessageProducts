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
using SpeechMessage.Dynamics.ProductClient.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Gateway;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;

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
        services.TryAddSingleton<IPackage02ContactBasicInfoUpdateClient, Package02ContactBasicInfoUpdateClient>();
        services.TryAddSingleton<IPackage02ContactProfileClient, Package02ContactProfileClient>();
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
}
