using System;
using System.Collections;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.ConnectionOperations;
using Xunit;

namespace ToolUtility.Dataverse.Tests;

/// <summary>
/// 驗證 Dataverse 池化服務包裝器與連線池的資源生命週期。
/// 每個測試都使用無網路副作用的組織服務替身，保護的契約是同一請求取得的連線只會在
/// <see cref="IDisposable.Dispose"/> 時歸還或銷毀，且任何不確定的傳輸結果不會被下一個請求重用。
/// </summary>
public sealed class PooledOrganizationServiceTests
{
    /// <summary>
    /// 保護 DI scope 結束時的正常歸還契約：注入的服務被釋放後，租借的連線必須回到原本的閒置數量。
    /// 故障注入為無；決定性斷言為歸還後的 idle 數恰為租借前基線。
    /// </summary>
    [Fact]
    public void Dispose_ReturnsLeasedConnectionToItsPool()
    {
        using var pool = CreatePool(CreateService());
        var idleBaseline = pool.GetStats().IdleConnections;

        var service = new PooledOrganizationService(pool);
        service.Dispose();

        Assert.Equal(idleBaseline, pool.GetStats().IdleConnections);
    }

    /// <summary>
    /// 保護故障連線不得回池的契約：已標記故障的池內連線必須被銷毀而非重新加入 idle 集合。
    /// 故障注入為明確呼叫標記 API；決定性斷言為歸還後 idle 數為零且已租借的服務不再被追蹤。
    /// </summary>
    [Fact]
    public void ReleaseConnection_DiscardsExplicitlyFaultedConnection()
    {
        using var pool = CreatePool(CreateService());
        var leasedService = pool.AcquireConnection();

        pool.MarkConnectionFaulted(leasedService);
        pool.ReleaseConnection(leasedService);

        Assert.Equal(0, pool.GetStats().IdleConnections);
        Assert.Equal(0, GetLookupCount(pool));
    }

    /// <summary>
    /// 保護逾時後的傳輸隔離契約：轉送的 Dataverse 操作若丟出 <see cref="TimeoutException"/>，
    /// 包裝器必須標記該連線故障，並在 scope 結束時銷毀它。故障注入為 Retrieve 的逾時；
    /// 決定性斷言為同一服務不再位於 idle 或 lookup 中。
    /// </summary>
    [Fact]
    public void Retrieve_WhenTransportTimesOut_DiscardsConnectionInsteadOfReturningIt()
    {
        var crm = CreateService();
        crm.Setup(service => service.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
            .Throws<TimeoutException>();

        using var pool = CreatePool(crm);
        var service = new PooledOrganizationService(pool);

        Assert.Throws<TimeoutException>(() =>
        {
            service.Retrieve("account", Guid.NewGuid(), new ColumnSet());
        });
        service.Dispose();

        Assert.Equal(0, pool.GetStats().IdleConnections);
        Assert.Equal(0, GetLookupCount(pool));
    }

    /// <summary>
    /// 保護長時間服務的有界資源契約：重複 200 次借還不可遺失 semaphore 名額，
    /// 也不可讓 lookup 因相同池化連線而成長。故障注入為無；決定性斷言為 idle 與 lookup
    /// 均回到宣告的安全基線，避免跨 request 保留不受管理的服務參考。
    /// </summary>
    [Fact]
    public void RepeatedLeaseAndReturn_RestoresIdleAndLookupBaselines()
    {
        using var pool = CreatePool(CreateService());
        var idleBaseline = pool.GetStats().IdleConnections;
        var lookupBaseline = GetLookupCount(pool);

        Assert.Throws<InvalidOperationException>(() => pool.ReleaseConnection(CreateService().Object));
        Assert.Equal(lookupBaseline, GetLookupCount(pool));

        var explicitlyReleasedService = pool.AcquireConnection();
        pool.ReleaseConnection(explicitlyReleasedService);
        Assert.Throws<InvalidOperationException>(() => pool.ReleaseConnection(explicitlyReleasedService));
        Assert.Equal(idleBaseline, pool.GetStats().IdleConnections);

        for (var index = 0; index < 200; index++)
        {
            using var service = new PooledOrganizationService(pool);
        }

        Assert.Equal(idleBaseline, pool.GetStats().IdleConnections);
        Assert.Equal(lookupBaseline, GetLookupCount(pool));
    }

    /// <summary>
    /// 建立只供測試使用的單一連線池。服務工廠每次皆回傳同一個可控制的替身，
    /// 使測試可在不開啟 CRM 網路通道的情況下檢查池的擁有權與釋放結果。
    /// </summary>
    private static CrmConnectionPool CreatePool(Mock<IOrganizationService> service)
    {
        var connectionFactory = new Mock<ICrmConnectionService>(MockBehavior.Strict);
        connectionFactory
            .Setup(factory => factory.CreateOnPremiseClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(service.Object);

        return new CrmConnectionPool(
            connectionFactory.Object,
            "https://example.test/XRMServices/2011/Organization.svc",
            "test-user",
            "test-password",
            minPoolSize: 1,
            maxPoolSize: 1,
            connectionTimeout: TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 建立無副作用的 IOrganizationService 替身，僅由個別測試覆寫需要驗證的傳輸行為。
    /// </summary>
    private static Mock<IOrganizationService> CreateService()
    {
        return new Mock<IOrganizationService>(MockBehavior.Loose);
    }

    /// <summary>
    /// 讀取池內的擁有權字典數量。這是 T4 的白箱斷言，因為公開統計不暴露字典大小，
    /// 而本次修補的核心契約正是防止該私有字典因非池服務或遺失清理而無界成長。
    /// </summary>
    private static int GetLookupCount(CrmConnectionPool pool)
    {
        var field = typeof(CrmConnectionPool).GetField("_connectionLookup", BindingFlags.Instance | BindingFlags.NonPublic);
        var lookup = Assert.IsAssignableFrom<ICollection>(field?.GetValue(pool));
        return lookup.Count;
    }
}
