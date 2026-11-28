/*
檔案整體說明
目的與定位：F11主機全域SDK租約觀測decorator的離線生命週期契約，保護健康以外F3、F4、F6、F8及F12同樣透過manager時被精確計入。
主要功能與流程：以一百次真decorator租借包裝合成manager，確認底層歸還前仍計在途，重入Dispose不重扣、傳輸fault原樣委派，底層歸還拋錯仍扣回。Acquire失敗不增計數；wrapper關閉不得關閉借用manager或Meter，原owner仍唯一負責排空與Dispose。
使用邊界與維護：每案唯一擁有Meter、diagnostics與替身；替身不建網路或保存任何使用者資料，反射建立產品internal型別以保留正式API邊界。所有租約在finally確定釋放後才關Meter，沒有背景task或static計數；本案不代表實機SDK性能驗收。
*/
using Moq;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Hosting.Observability;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>manager對所有產品consumer的相同租約計數契約。</summary>
public sealed class DataverseObservationTests
{
    /// <summary>一百次Acquire、fault及釋放完整對稱；底層釋放故障不遮原錯、不留計數，重入無副作用。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllLeases_CountedUntilUnderlyingRelease_AndReturnToZero(bool releaseFails)
    {
        using var meters = new HostingMeters();
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        var released = 0;
        var failure = new IOException("synthetic-release-canary");
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var leases = new List<Mock<IClientLease>>();
        manager.Setup(m => m.Acquire(It.IsAny<CancellationToken>())).Returns(() =>
        {
            var lease = new Mock<IClientLease>(MockBehavior.Strict);
            lease.SetupGet(l => l.Service).Returns(sdk.Object);
            lease.Setup(l => l.MarkFaulted());
            lease.Setup(l => l.Dispose()).Callback(() =>
            {
                Assert.True(meters.GetSnapshot().DataverseLeasesActive > 0);
                released++;
                if (releaseFails) throw failure;
            });
            leases.Add(lease);
            return lease.Object;
        });
        using var wrapper = Create(manager.Object, meters, diagnostics);
        var observations = Enumerable.Range(0, 100).Select(_ => wrapper.Acquire()).ToArray();
        try
        {
            Assert.Equal(100, meters.GetSnapshot().DataverseLeasesActive);
            foreach (var observation in observations)
            {
                Assert.Same(sdk.Object, observation.Service);
                observation.MarkFaulted();
                if (releaseFails) Assert.Same(failure, Assert.Throws<IOException>(observation.Dispose));
                else observation.Dispose();
                observation.Dispose();
            }
            Assert.Equal(100, released);
            Assert.Equal(0, meters.GetSnapshot().DataverseLeasesActive);
            foreach (var lease in leases)
            {
                lease.Verify(l => l.MarkFaulted(), Times.Once);
                lease.Verify(l => l.Dispose(), Times.Once);
            }
        }
        finally { foreach (var observation in observations) observation.Dispose(); }
        wrapper.Dispose();
        Assert.Throws<ObjectDisposedException>(() => wrapper.Acquire());
        manager.Verify(m => m.Dispose(), Times.Never);
    }

    /// <summary>底層借用或Meter建立失敗皆不可漏lease：前者零取得，後者先fault並釋放且保留主要例外。</summary>
    [Fact]
    public async Task AcquisitionOrObservationFailure_DoesNotRetainResources()
    {
        using var meters = new HostingMeters();
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        var failure = new IOException("synthetic-acquire");
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(m => m.Acquire(It.IsAny<CancellationToken>())).Throws(failure);
        using var wrapper = Create(manager.Object, meters, diagnostics);
        Assert.Same(failure, Assert.Throws<IOException>(() => wrapper.Acquire()));
        Assert.Equal(0, meters.GetSnapshot().DataverseLeasesActive);
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        lease.Setup(l => l.MarkFaulted());
        lease.Setup(l => l.Dispose());
        manager.Setup(m => m.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        meters.Dispose();
        Assert.Throws<ObjectDisposedException>(() => wrapper.Acquire());
        lease.Verify(l => l.MarkFaulted(), Times.Once);
        lease.Verify(l => l.Dispose(), Times.Once);
    }

    /// <summary>僅建立產品內真正decorator；其不存在時是明確行為RED，不以編譯失敗當證據。</summary>
    private static IDataverseConnectionManager Create(IDataverseConnectionManager manager, HostingMeters meters, ExceptionDiagnostics diagnostics)
    {
        var type = typeof(Program).Assembly.GetType("SpeechMessageProducts.GoodShepherd2.Web.Observability.ObservedDataverseConnectionManager");
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<IDataverseConnectionManager>(Activator.CreateInstance(type, manager, meters, diagnostics));
    }
}
