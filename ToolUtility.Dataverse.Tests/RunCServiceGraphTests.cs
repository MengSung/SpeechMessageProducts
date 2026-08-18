using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Dataverse.Tests;

/// <summary>
/// 驗證 Run C 切換後的完整服務圖、scope 隔離、故障淘汰與 raw client 封裝。
/// 測試只建立假的 <see cref="IOrganizationService"/>，不連線真實 Dataverse；
/// 每一項斷言都直接保護 Singleton pool、Scoped gateway 與 per-operation lease 的生命週期契約。
/// </summary>
public sealed class RunCServiceGraphTests
{
    /// <summary>
    /// 驗證服務圖在 ValidateScopes 與 ValidateOnBuild 同時開啟時可建立，且五個池參數皆由組態繫結。
    /// 故障注入為假的連線服務；決定性斷言是所有新服務可解析且組態值逐一相等。
    /// </summary>
    [Fact]
    public void Service_graph_validates_and_binds_all_pool_options()
    {
        var services = CreateServices(CreateHealthyConnectionService());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        Assert.NotNull(provider.GetRequiredService<IDataverseConnectionManager>());
        Assert.NotNull(provider.GetRequiredService<IBoundedClientPool>());
        using (var scope = provider.CreateScope())
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDataverseGateway>());
            Assert.IsType<GatewayOrganizationService>(scope.ServiceProvider.GetRequiredService<IOrganizationService>());
        }

        var options = provider.GetRequiredService<DataversePoolOptions>();
        Assert.Equal(2, options.MinSize);
        Assert.Equal(4, options.MaxN);
        Assert.Equal(TimeSpan.FromSeconds(7), options.AcquireTimeout);
        Assert.Equal(TimeSpan.FromSeconds(13), options.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(17), options.HealthInterval);
    }

    /// <summary>
    /// 驗證三個並行 request scope 各執行十次操作後租約歸零，且 keyed pool 不會超過 MaxN。
    /// 故障注入為可計數的假的服務；決定性斷言是 scope 結束後 Leased=0、Idle 不超過上限且只有一個子池。
    /// </summary>
    [Fact]
    public async Task Concurrent_scopes_return_all_leases_and_respect_maximum()
    {
        var connection = CreateHealthyConnectionService();
        var services = CreateServices(connection, minSize: 1, maxN: 2);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        var manager = provider.GetRequiredService<IDataverseConnectionManager>();
        var workers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
            for (var operation = 0; operation < 10; operation++)
            {
                service.Execute(new WhoAmIRequest());
            }
        })).ToArray();

        await Task.WhenAll(workers);

        var metrics = manager.GetMetrics();
        Assert.Equal(0, metrics.Leased);
        Assert.InRange(metrics.Idle, 0, 2);
        Assert.Equal(1, metrics.SubPoolCount);
    }

    /// <summary>
    /// 驗證操作例外會讓當前 client 進入 Faulted 並淘汰，而下一次操作仍能取得替代 client。
    /// 故障注入為第一個假的 service 在 Retrieve 時擲出例外；決定性斷言是 Faulted/Discarded 計數增加且第二次操作成功。
    /// </summary>
    [Fact]
    public void Operation_failure_evicts_faulted_client_and_allows_followup_acquire()
    {
        var created = new ConcurrentQueue<Mock<IOrganizationService>>();
        var connection = new Mock<ICrmConnectionService>(MockBehavior.Strict);
        connection
            .Setup(x => x.CreateOnPremiseClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(() =>
            {
                var mock = new Mock<IOrganizationService>(MockBehavior.Loose);
                mock.Setup(x => x.Execute(It.IsAny<OrganizationRequest>()))
                    .Returns((OrganizationRequest request) => request is WhoAmIRequest
                        ? new WhoAmIResponse()
                        : new OrganizationResponse());
                if (created.IsEmpty)
                {
                    mock.Setup(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Microsoft.Xrm.Sdk.Query.ColumnSet>()))
                        .Throws(new InvalidOperationException("injected operation failure"));
                }
                created.Enqueue(mock);
                return mock.Object;
            });

        using var provider = CreateServices(connection.Object, minSize: 1, maxN: 1)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        var manager = scope.ServiceProvider.GetRequiredService<IDataverseConnectionManager>();

        Assert.Throws<InvalidOperationException>(() =>
            service.Retrieve("account", Guid.NewGuid(), new Microsoft.Xrm.Sdk.Query.ColumnSet("name")));

        var afterFailure = manager.GetMetrics();
        Assert.Equal(0, afterFailure.Leased);
        Assert.Equal(1, afterFailure.Faulted);
        Assert.Equal(1, afterFailure.Discarded);

        var replacement = service.Retrieve("account", Guid.NewGuid(), new Microsoft.Xrm.Sdk.Query.ColumnSet("name"));
        Assert.Null(replacement);
        Assert.Equal(2, created.Count);
    }

    /// <summary>
    /// 驗證 DI 注入給 ToolUtilityClass 的服務是 gateway 代理而非 OnPremiseClient raw client。
    /// 這保護應用程式無法繞過 manager 取得長命通道；scope 結束時由 gateway/pool 確定歸還。
    /// </summary>
    [Fact]
    public void ToolUtilityClass_receives_gateway_proxy_instead_of_raw_on_premise_client()
    {
        using var provider = CreateServices(CreateHealthyConnectionService(), minSize: 1, maxN: 1)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        using var scope = provider.CreateScope();

        var utility = scope.ServiceProvider.GetRequiredService<ToolUtilityClass>();

        Assert.IsNotType<PowerPlatform.Dataverse.Client.OnPremiseClient>(utility.m_Crm2011OrganizationService);
        Assert.IsType<GatewayOrganizationService>(utility.m_Crm2011OrganizationService);
    }

    private static ServiceCollection CreateServices(
        ICrmConnectionService connectionService,
        int minSize = 2,
        int maxN = 4)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Test",
                ["CrmConnection:ServerUrl"] = "https://org.test/XRMServices/2011/Organization.svc",
                ["CrmConnection:Username"] = "service-user",
                ["CrmConnection:Password"] = "secret",
                ["Dataverse:Pool:MinSize"] = minSize.ToString(),
                ["Dataverse:Pool:MaxN"] = maxN.ToString(),
                ["Dataverse:Pool:AcquireTimeout"] = "00:00:07",
                ["Dataverse:Pool:IdleTimeout"] = "00:00:13",
                ["Dataverse:Pool:HealthInterval"] = "00:00:17"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IToolUtilityTracer>(new Mock<IToolUtilityTracer>(MockBehavior.Loose).Object);
        services.AddSingleton(connectionService);
        services.AddToolUtility();
        return services;
    }

    private static ICrmConnectionService CreateHealthyConnectionService()
    {
        var connection = new Mock<ICrmConnectionService>(MockBehavior.Strict);
        connection
            .Setup(x => x.CreateOnPremiseClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(() =>
            {
                var service = new Mock<IOrganizationService>(MockBehavior.Loose);
                service.Setup(x => x.Execute(It.IsAny<OrganizationRequest>()))
                    .Returns((OrganizationRequest request) => request is WhoAmIRequest
                        ? new WhoAmIResponse()
                        : new OrganizationResponse());
                return service.Object;
            });
        return connection.Object;
    }
}
