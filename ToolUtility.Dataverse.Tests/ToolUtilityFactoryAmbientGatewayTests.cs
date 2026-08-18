// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs
// 檔案責任：驗證 legacy ToolUtilityFactory 單例透過 ambient gateway 取得目前 scope 的
//           Dataverse 操作邊界，而不保存 request scope、client 或 lease。
// 資源生命週期：測試建立的 ServiceProvider、scope、pool 與假 CRM service 都由 using
//               明確釋放；測試會斷言 ambient fallback scope 在操作完成後立即釋放，
//               並確認 100 個跨 scope 操作後沒有保留 lease 或成長中的 client。
// 跨 request 隔離：AsyncLocal 僅模擬單一執行流程的目前 request services；Factory 單例
//                 僅保存不含 request 狀態的 AmbientGatewayOrganizationService，因此不同
//                 scope 不可能共享 mutable gateway 或租約。
// 編碼要求：本檔案維持 UTF-8 無 BOM、CRLF，並以 final CRLF 結尾。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using ToolUtilityNameSpace.Factory;
using Xunit;

namespace ToolUtility.Dataverse.Tests;

/// <summary>
/// 驗證 legacy Factory 使用 ambient gateway 的跨 scope 安全性與資源回收契約。
/// </summary>
public sealed class ToolUtilityFactoryAmbientGatewayTests
{
    /// <summary>
    /// 保護 Factory 單例不捕獲 request scope、也不在背景操作遺漏 scope 釋放的契約。
    /// 故障注入為可計數的 scope factory 和不連線網路的 CRM service；決定性斷言依序是：
    /// 有 request 時不自建 scope、無 request 時建立且釋放一個短命 scope、以及連續一百個
    /// 不同 request scope 操作後 pool 的已建立 client 數維持熱身基線且 lease 歸零。
    /// </summary>
    [Fact]
    public void Factory_singleton_resolves_current_gateway_and_releases_ambient_scopes_without_pool_growth()
    {
        ResetFactory();
        var currentRequestServices = new AsyncLocal<IServiceProvider?>();
        var createdServices = new List<IOrganizationService>();
        var configuration = CreateConfiguration();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IToolUtilityTracer>(new Mock<IToolUtilityTracer>(MockBehavior.Loose).Object);
        services.AddSingleton(CreateConnectionService(createdServices));
        services.AddToolUtility();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        var trackingScopeFactory = new TrackingScopeFactory(
            provider.GetRequiredService<IServiceScopeFactory>());
        var ambient = new AmbientGatewayOrganizationService(
            () => currentRequestServices.Value,
            trackingScopeFactory);

        ToolUtilityFactory.SetConfiguration(configuration);
        ToolUtilityFactory.SetTracer(provider.GetRequiredService<IToolUtilityTracer>());
        ToolUtilityFactory.SetAmbientService(ambient);
        var legacySingleton = ToolUtilityFactory.GetInstance();

        try
        {
            using (var requestScope = provider.CreateScope())
            {
                currentRequestServices.Value = requestScope.ServiceProvider;
                legacySingleton.m_Crm2011OrganizationService.Execute(new WhoAmIRequest());
                Assert.Equal(0, trackingScopeFactory.CreatedCount);
            }

            currentRequestServices.Value = null;
            legacySingleton.m_Crm2011OrganizationService.Execute(new WhoAmIRequest());
            var manager = provider.GetRequiredService<IDataverseConnectionManager>();
            Assert.Equal(1, trackingScopeFactory.CreatedCount);
            Assert.Equal(1, trackingScopeFactory.DisposedCount);
            Assert.Equal(0, manager.GetMetrics().Leased);

            var createdAfterWarmup = manager.GetMetrics().Created;
            for (var iteration = 0; iteration < 100; iteration++)
            {
                using var requestScope = provider.CreateScope();
                currentRequestServices.Value = requestScope.ServiceProvider;
                legacySingleton.m_Crm2011OrganizationService.Execute(new WhoAmIRequest());
            }

            currentRequestServices.Value = null;
            var finalMetrics = manager.GetMetrics();
            Assert.Equal(createdAfterWarmup, finalMetrics.Created);
            Assert.Equal(0, finalMetrics.Leased);
            Assert.Single(createdServices);
        }
        finally
        {
            currentRequestServices.Value = null;
            ResetFactory();
        }
    }

    /// <summary>
    /// 建立只供測試使用的 Dataverse 組態；五個 pool 值均為小且明確的範圍，
    /// 以避免測試等待真實網路或留下長時間計時器。
    /// </summary>
    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Test",
                ["CrmConnection:ServerUrl"] = "https://org.test/XRMServices/2011/Organization.svc",
                ["CrmConnection:Username"] = "service-user",
                ["CrmConnection:Password"] = "test-secret",
                ["Dataverse:Pool:MinSize"] = "1",
                ["Dataverse:Pool:MaxN"] = "2",
                ["Dataverse:Pool:AcquireTimeout"] = "00:00:02",
                ["Dataverse:Pool:IdleTimeout"] = "00:05:00",
                ["Dataverse:Pool:HealthInterval"] = "00:05:00"
            })
            .Build();
    }

    /// <summary>
    /// 建立不連線 Dynamics 的嚴格連線服務替身。每次底層 pool 要建立 client 時才新增
    /// 一個可計數的假的組織服務，WhoAmI 回應用來通過首次健康檢查。
    /// </summary>
    private static ICrmConnectionService CreateConnectionService(List<IOrganizationService> createdServices)
    {
        var connection = new Mock<ICrmConnectionService>(MockBehavior.Strict);
        connection
            .Setup(service => service.CreateOnPremiseClient(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(() =>
            {
                var service = new Mock<IOrganizationService>(MockBehavior.Loose);
                service.Setup(candidate => candidate.Execute(It.IsAny<OrganizationRequest>()))
                    .Returns((OrganizationRequest _) => new WhoAmIResponse());
                createdServices.Add(service.Object);
                return service.Object;
            });
        return connection.Object;
    }

    /// <summary>
    /// 透過既有 internal reset 清空程序級 Factory 狀態，使每個測試不把設定、單例或
    /// ambient 代理殘留到其他測試。反射只用於測試隔離，不改變產品公開 API。
    /// </summary>
    private static void ResetFactory()
    {
        var reset = typeof(ToolUtilityFactory).GetMethod(
            "ResetInstance",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(reset);
        reset!.Invoke(null, null);
    }

    /// <summary>
    /// 包裝真正的 scope factory 以記錄 fallback scope 的建立與釋放。包裝器不保存
    /// request services 或 client；每個回傳 scope 仍由呼叫端的 using 擁有。
    /// </summary>
    private sealed class TrackingScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceScopeFactory _inner;

        /// <summary>
        /// 建立可計數包裝器；內層 factory 仍是根 DI 容器唯一的 scope 建立者。
        /// </summary>
        public TrackingScopeFactory(IServiceScopeFactory inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>已建立的 fallback scope 數量。</summary>
        public int CreatedCount { get; private set; }

        /// <summary>已確定釋放的 fallback scope 數量。</summary>
        public int DisposedCount { get; private set; }

        /// <summary>
        /// 建立一個由呼叫端負責 Dispose 的短命 scope，並以包裝器在 Dispose 時累計。
        /// </summary>
        public IServiceScope CreateScope()
        {
            CreatedCount++;
            return new TrackingScope(_inner.CreateScope(), this);
        }

        /// <summary>
        /// 裝飾一個 scope，確保 Dispose 冪等且只將釋放數計一次。
        /// </summary>
        private sealed class TrackingScope : IServiceScope
        {
            private readonly IServiceScope _inner;
            private readonly TrackingScopeFactory _owner;
            private int _disposed;

            /// <summary>
            /// 建立 scope 包裝器；服務解析仍完全交給內層 scope。
            /// </summary>
            public TrackingScope(IServiceScope inner, TrackingScopeFactory owner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            /// <summary>內層 scope 的服務提供者。</summary>
            public IServiceProvider ServiceProvider => _inner.ServiceProvider;

            /// <summary>
            /// 釋放內層 scope 並只記錄一次，防止重複 Dispose 讓測試誤判生命週期。
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                _inner.Dispose();
                _owner.DisposedCount++;
            }
        }
    }
}
