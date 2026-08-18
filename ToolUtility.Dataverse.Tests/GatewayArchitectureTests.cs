using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using Xunit;

namespace ToolUtility.Dataverse.Tests;

/// <summary>
/// 驗證 Run B 新增的 Manager、Gateway、IOrganizationService 代理與 ambient fallback。
/// 所有測試均使用假的 CRM service，並檢查租約取得、故障標記與 scope 確定性釋放。
/// </summary>
public sealed class GatewayArchitectureTests
{
    /// <summary>巢狀三層 Execute 必須只取得一條 lease，最外層結束才歸還。</summary>
    [Fact]
    public void Gateway_reentrant_execute_uses_one_lease_for_three_nested_calls()
    {
        var service = new Mock<IOrganizationService>(MockBehavior.Loose).Object;
        var lease = new TestLease(service);
        var manager = new TestManager(lease);
        using var gateway = new DataverseGateway(manager);
        var depth = 0;

        gateway.Execute(_ =>
        {
            depth++;
            gateway.Execute(_ =>
            {
                depth++;
                gateway.Execute(__ => depth++);
            });
        });

        Assert.Equal(3, depth);
        Assert.Equal(1, manager.AcquireCount);
        Assert.Equal(1, lease.DisposeCount);
        Assert.Equal(0, lease.MarkFaultedCount);
    }

    /// <summary>工作委派擲例外時 Gateway 必須標記故障、釋放 lease 並保留原例外。</summary>
    [Fact]
    public void Gateway_marks_lease_faulted_and_rethrows_operation_exception()
    {
        var lease = new TestLease(new Mock<IOrganizationService>(MockBehavior.Loose).Object);
        using var gateway = new DataverseGateway(new TestManager(lease));

        Assert.Throws<InvalidOperationException>(() => gateway.Execute(_ => throw new InvalidOperationException("boom")));
        Assert.Equal(1, lease.MarkFaultedCount);
        Assert.Equal(1, lease.DisposeCount);
    }

    /// <summary>代理的八個 IOrganizationService 方法均透過同一個 Gateway 執行。</summary>
    [Fact]
    public void Gateway_organization_service_delegates_all_service_methods()
    {
        var service = new Mock<IOrganizationService>(MockBehavior.Loose);
        service.Setup(x => x.Create(It.IsAny<Entity>())).Returns(Guid.NewGuid());
        service.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection());
        service.Setup(x => x.Execute(It.IsAny<OrganizationRequest>())).Returns(new OrganizationResponse());
        var gateway = new DirectGateway(service.Object);
        var proxy = new GatewayOrganizationService(gateway);
        var id = Guid.NewGuid();
        var relationship = new Relationship("contact_customer_accounts");
        var related = new EntityReferenceCollection();

        proxy.Associate("account", id, relationship, related);
        proxy.Create(new Entity("account"));
        proxy.Delete("account", id);
        proxy.Disassociate("account", id, relationship, related);
        proxy.Execute(new OrganizationRequest("WhoAmI"));
        proxy.Retrieve("account", id, new ColumnSet("name"));
        proxy.RetrieveMultiple(new QueryExpression("account"));
        proxy.Update(new Entity("account") { Id = id });

        service.Verify(x => x.Associate("account", id, relationship, related), Times.Once);
        service.Verify(x => x.Create(It.IsAny<Entity>()), Times.Once);
        service.Verify(x => x.Delete("account", id), Times.Once);
        service.Verify(x => x.Disassociate("account", id, relationship, related), Times.Once);
        service.Verify(x => x.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        service.Verify(x => x.Retrieve("account", id, It.IsAny<ColumnSet>()), Times.Once);
        service.Verify(x => x.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        service.Verify(x => x.Update(It.IsAny<Entity>()), Times.Once);
    }

    /// <summary>無 HttpContext 時 ambient 代理會建立短命 scope，工作完成即釋放 scoped gateway。</summary>
    [Fact]
    public void Ambient_service_creates_and_releases_scope_without_http_context()
    {
        var service = new Mock<IOrganizationService>(MockBehavior.Loose).Object;
        TrackingGateway tracking = null!;
        using var root = new ServiceCollection()
            .AddScoped<IDataverseGateway>(_ => tracking = new TrackingGateway(service))
            .BuildServiceProvider();
        var ambient = new AmbientGatewayOrganizationService(
            () => null,
            root.GetRequiredService<IServiceScopeFactory>());

        ambient.Retrieve("account", Guid.NewGuid(), new ColumnSet("name"));

        Assert.NotNull(tracking);
        Assert.True(tracking.Disposed);
    }

    /// <summary>Manager 解析四段 Pool Key，並把建立 client 的唯一工作交給連線服務。</summary>
    [Fact]
    public void Connection_manager_builds_key_and_exposes_pool_metrics()
    {
        var service = new Mock<IOrganizationService>(MockBehavior.Loose);
        var connection = new Mock<ICrmConnectionService>(MockBehavior.Strict);
        connection.Setup(x => x.CreateOnPremiseClient(
            "https://org.test/XRMServices/2011/Organization.svc",
            "service-user",
            "secret")).Returns(service.Object);
        service.Setup(x => x.Execute(It.IsAny<OrganizationRequest>())).Returns(new Microsoft.Crm.Sdk.Messages.WhoAmIResponse());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:ServerUrl"] = "https://org.test/XRMServices/2011/Organization.svc",
            ["CrmConnection:Username"] = "service-user",
            ["CrmConnection:Password"] = "secret"
        }).Build();

        using var manager = new DataverseConnectionManager(
            connection.Object,
            configuration,
            "ChurchReport",
            "Test",
            new DataversePoolOptions { MinSize = 1, MaxN = 2 });
        using var lease = manager.Acquire();

        Assert.Same(service.Object, lease.Service);
        Assert.Equal(1, manager.GetMetrics().SubPoolCount);
        connection.VerifyAll();
    }

    private sealed class TestManager : IDataverseConnectionManager
    {
        private readonly TestLease _lease;

        internal TestManager(TestLease lease) => _lease = lease;

        public int AcquireCount { get; private set; }

        public IClientLease Acquire(System.Threading.CancellationToken cancellationToken = default)
        {
            AcquireCount++;
            return _lease;
        }

        public DataversePoolMetrics GetMetrics() => new();
        public void Dispose() { }
    }

    private sealed class TestLease : IClientLease
    {
        internal TestLease(IOrganizationService service) => Service = service;
        public IOrganizationService Service { get; }
        public int MarkFaultedCount { get; private set; }
        public int DisposeCount { get; private set; }
        public void MarkFaulted() => MarkFaultedCount++;
        public void Dispose() => DisposeCount++;
    }

    private sealed class DirectGateway : IDataverseGateway
    {
        private readonly IOrganizationService _service;
        internal DirectGateway(IOrganizationService service) => _service = service;
        public void Execute(Action<IOrganizationService> work) => work(_service);
        public T Execute<T>(Func<IOrganizationService, T> work) => work(_service);
    }

    private sealed class TrackingGateway : IDataverseGateway, IDisposable
    {
        private readonly IOrganizationService _service;
        internal TrackingGateway(IOrganizationService service) => _service = service;
        internal bool Disposed { get; private set; }
        public void Execute(Action<IOrganizationService> work) => work(_service);
        public T Execute<T>(Func<IOrganizationService, T> work) => work(_service);
        public void Dispose() => Disposed = true;
    }
}
