/*
檔案整體說明
目的與定位：F11正式主機四項就緒adapter的離線邊界測試，使用真產品方法、合成Async2 SDK與Channel來源，不接實機或讀部署秘密。
主要功能與流程：針對WhoAmI完整身分、錯組織、SDK失敗、取消及釋放故障，斷言故障標記先於歸還、所有租約計數歸零。Channel矩陣保留不同正式ID並拒絕重複ID、異產品與停用；來源只讀metadata且任何路徑確定釋放。SQL僅測缺設定與預先取消，不能冒稱select1已實機通過。
使用邊界與維護：每案擁有唯一Meter、診斷、取消來源，Moq替身沒有外部連線；反射只呼叫既有internal核心，不複製產品判斷。真實owner的釋放順序由callback觀察，不以假counter歸零替代SDK停止證據。沒有背景程序或跨案例狀態，測試後全數Dispose。
*/
using System.Reflection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Moq;
using SpeechMessage.Hosting.Observability;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>真正產品probe的合成故障矩陣，不依賴SQL、網路或主機時區。</summary>
public sealed class ReadinessAdapterTests
{
    /// <summary>任何已取得SDK租約皆完整釋放；錯身分、傳輸故障與取消先fault，歸還失敗也不得健康。</summary>
    [Theory]
    [InlineData("valid")]
    [InlineData("incomplete")]
    [InlineData("foreign")]
    [InlineData("sdk")]
    [InlineData("release")]
    [InlineData("cancel")]
    public async Task D365_ValidatesIdentityAndDrainsBeforePublishing(string scenario)
    {
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        using var meters = new HostingMeters();
        using var cancellation = new CancellationTokenSource();
        var org = Guid.NewGuid();
        var order = new List<string>();
        var failure = new IOException("synthetic-secret-canary");
        var sdk = new Mock<IOrganizationServiceAsync2>(MockBehavior.Strict);
        sdk.Setup(s => s.ExecuteAsync(It.IsAny<WhoAmIRequest>(), It.IsAny<CancellationToken>()))
            .Returns((OrganizationRequest _, CancellationToken token) =>
            {
                Assert.Equal(1, meters.GetSnapshot().DataverseLeasesActive);
                if (scenario == "sdk") throw failure;
                if (scenario == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                return Task.FromResult<OrganizationResponse>(new WhoAmIResponse
                {
                    Results = new ParameterCollection
                    {
                        ["UserId"] = scenario == "incomplete" ? Guid.Empty : Guid.NewGuid(),
                        ["OrganizationId"] = scenario == "foreign" ? Guid.NewGuid() : org,
                        ["BusinessUnitId"] = Guid.NewGuid()
                    }
                });
            });
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        lease.SetupGet(l => l.Service).Returns(sdk.Object);
        lease.Setup(l => l.MarkFaulted()).Callback(() => order.Add("fault"));
        lease.Setup(l => l.Dispose()).Callback(() =>
        {
            Assert.Equal(1, meters.GetSnapshot().DataverseLeasesActive);
            order.Add("release");
            if (scenario == "release") throw failure;
        });
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(m => m.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        var observerType = typeof(Program).Assembly.GetType("SpeechMessageProducts.GoodShepherd2.Web.Observability.ObservedDataverseConnectionManager")!;
        using var observed = Assert.IsAssignableFrom<IDataverseConnectionManager>(Activator.CreateInstance(observerType, manager.Object, meters, diagnostics));
        Task<bool> Run() => Invoke("CheckD365Async", observed, diagnostics, org, cancellation.Token);
        if (scenario == "valid") Assert.True(await Run());
        else
        {
            var actual = await Record.ExceptionAsync(Run);
            Assert.NotNull(actual);
            if (scenario is "sdk" or "release") Assert.Same(failure, actual);
            if (scenario == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(actual);
        }
        if (scenario is "valid" or "release") Assert.Collection(order, value => Assert.Equal("release", value));
        else Assert.Collection(order, value => Assert.Equal("fault", value), value => Assert.Equal("release", value));
        Assert.Equal(0, meters.GetSnapshot().DataverseLeasesActive);
        lease.Verify(l => l.Dispose(), Times.Once);
    }

    /// <summary>正式ID不同的資料完整保留；同ID、同Channel鍵或外產品拒絕，停用為預期未就緒且來源仍釋放。</summary>
    [Theory]
    [InlineData("valid", true)]
    [InlineData("disabled", false)]
    [InlineData("empty", false)]
    [InlineData("duplicate", null)]
    [InlineData("duplicateKey", null)]
    [InlineData("foreign", null)]
    [InlineData("fault", null)]
    public async Task Channels_MetadataOnlyAndLeaseDrained(string scenario, bool? expected)
    {
        var id = Guid.NewGuid();
        var rows = scenario == "empty" ? Array.Empty<ChannelDirectoryRecord>() : new[]
        {
            new ChannelDirectoryRecord(id, "GoodShepherd2", "2012804957", DirectoryChannelStatus.Operating, "1", DateTime.UtcNow),
            new ChannelDirectoryRecord(scenario == "duplicate" ? id : Guid.NewGuid(), scenario == "foreign" ? "GoodGuild" : "GoodShepherd2",
                scenario == "duplicateKey" ? "2012804957" : "2012804966", scenario == "disabled" ? DirectoryChannelStatus.Disabled : DirectoryChannelStatus.Full,
                "2", DateTime.UtcNow)
        };
        var lease = new Mock<IChannelDirectorySourceLease>(MockBehavior.Strict);
        lease.Setup(l => l.ReadAsync(false, It.IsAny<CancellationToken>())).Returns(() => scenario == "fault"
            ? Task.FromException<IReadOnlyList<ChannelDirectoryRecord>>(new IOException("synthetic-read"))
            : Task.FromResult<IReadOnlyList<ChannelDirectoryRecord>>(rows));
        lease.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var source = new Mock<IChannelDirectorySource>(MockBehavior.Strict);
        source.SetupGet(s => s.Product).Returns("GoodShepherd2");
        source.SetupGet(s => s.OrganizationId).Returns(Guid.NewGuid());
        source.Setup(s => s.OpenAsync(It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult(lease.Object));
        if (expected.HasValue) Assert.Equal(expected.Value, await Invoke("CheckChannelsAsync", source.Object, CancellationToken.None));
        else Assert.NotNull(await Record.ExceptionAsync(() => Invoke("CheckChannelsAsync", source.Object, CancellationToken.None)));
        lease.Verify(l => l.ReadAsync(false, It.IsAny<CancellationToken>()), Times.Once);
        lease.Verify(l => l.ReadAsync(true, It.IsAny<CancellationToken>()), Times.Never);
        lease.Verify(l => l.DisposeAsync(), Times.Once);
    }

    /// <summary>metadata讀取及歸還同時失敗時兩個真實例外都保留；不得以Dispose錯取代主要讀取錯。</summary>
    [Fact]
    public async Task Channels_ReadAndCleanupFail_BothCausesRemainDiagnosable()
    {
        var primary = new IOException("synthetic-read-canary");
        var cleanup = new InvalidOperationException("synthetic-release-canary");
        var lease = new Mock<IChannelDirectorySourceLease>(MockBehavior.Strict);
        lease.Setup(l => l.ReadAsync(false, It.IsAny<CancellationToken>())).ThrowsAsync(primary);
        lease.Setup(l => l.DisposeAsync()).Returns(() => ValueTask.FromException(cleanup));
        var source = new Mock<IChannelDirectorySource>(MockBehavior.Strict);
        source.SetupGet(s => s.Product).Returns("GoodShepherd2");
        source.SetupGet(s => s.OrganizationId).Returns(Guid.NewGuid());
        source.Setup(s => s.OpenAsync(It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult(lease.Object));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => Invoke("CheckChannelsAsync", source.Object, CancellationToken.None));
        Assert.Collection(failure.InnerExceptions, value => Assert.Same(primary, value), value => Assert.Same(cleanup, value));
        lease.Verify(l => l.DisposeAsync(), Times.Once);
    }

    /// <summary>缺SQL設定不建線且為未就緒；預先取消在解析連線字串前傳播，不能假作SQL已可用。</summary>
    [Fact]
    public async Task Sql_MissingConfigurationAndCancellation_FailClosed()
    {
        Assert.False(await Invoke("CheckSqlAsync", null, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke("CheckSqlAsync", "invalid=canary", new CancellationToken(true)));
        Assert.False(await Invoke("CheckChannelsAsync", null, CancellationToken.None));
    }

    /// <summary>只取得產品組件內既有方法；非同步原錯誤由Task保留，不將反射包裝當產品行為。</summary>
    private static Task<bool> Invoke(string name, params object?[] args)
    {
        var type = typeof(Program).Assembly.GetType("SpeechMessageProducts.GoodShepherd2.Web.Health.GoodShepherd2Readiness");
        Assert.NotNull(type);
        var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task<bool>>(method.Invoke(null, args));
    }
}
