/*
檔案整體說明
目的與定位：驗證 F2 真實 ASP.NET Core 組合根與 D365 就緒端點的離線契約，不連線任何外部組織、SQL 或 LINE。
主要功能與流程：每案使用獨立暫存 content root、合成 SDK 與真實 Kestrel loopback 埠；以 WhoAmI 正常／失敗、取消及 Dispose 故障注入，確認只回安全狀態、故障先標記再歸還租約。空 Webhook 接收無效 JSON 仍回 200 且不進事件管線。重複啟停證明程序級診斷 owner 能解除後重新綁定。
使用邊界與維護：測試 collection 禁止與其他會綁定靜態診斷 owner 的案例並行；Host、HttpClient、組態與暫存資料由當案 finally／await using 清理。例外訊息使用合成 canary，實際 Exception.log 必須不含它。此測試只證明本機合成依賴及資源釋放，不代表實機 WhoAmI 或 LINE webhook/test 成功。
*/
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Crm.Sdk.Messages;
using Moq;
using SpeechMessageProducts.GoodShepherd2.Web.Health;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests;

/// <summary>每個真實主機依序啟停，避免程序相容診斷入口被兩個測試同時擁有。</summary>
[CollectionDefinition("F2Host", DisableParallelization = true)]
public sealed class F2HostGroup;

/// <summary>透過真正路由及短期租約確認 F2 安全回應與確定清理，所有外部 SDK 由合成替身接管。</summary>
[Collection("F2Host")]
public sealed class HostTests
{
    /// <summary>連續建立三個 Host，逐次確認 live、ready 與空 webhook，並證明 shutdown 解除上一個診斷 owner。</summary>
    [Fact]
    public async Task Host_HealthAndEmptyWebhook_RestartsWithoutRetainedOwner()
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var directory = Path.Combine(Path.GetTempPath(), "3p-f2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var lease = new Mock<IClientLease>(MockBehavior.Strict);
                var service = new Mock<IOrganizationService>(MockBehavior.Strict);
                service.Setup(value => value.Execute(It.IsAny<WhoAmIRequest>())).Returns(Identity());
                lease.SetupGet(value => value.Service).Returns(service.Object);
                lease.Setup(value => value.Dispose());
                var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
                manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
                var builder = Builder(directory);
                builder.Services.AddSingleton(manager.Object);
                await using var app = Program.BuildApplication(builder);
                await app.StartAsync();
                try
                {
                    var server = app.Services.GetRequiredService<IServer>();
                    var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
                    using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
                    using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
                    Assert.Equal(HttpStatusCode.OK, live.StatusCode);
                    using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
                    Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
                    using var content = new StringContent("not-json-and-not-a-line-event", Encoding.UTF8, "application/json");
                    using var webhook = await client.PostAsync(new Uri("/webhook/1234567890", UriKind.Relative), content);
                    Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
                    lease.Verify(value => value.Dispose(), Times.Once);
                    lease.Verify(value => value.MarkFaulted(), Times.Never);
                }
                finally { await app.StopAsync(); }
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }

    /// <summary>SDK 真正故障時先淘汰再歸還，安全 503 與共用日誌均不得複製原始 canary 訊息。</summary>
    [Fact]
    public async Task Ready_SdkFailure_MarksBeforeReleaseAndWritesSafeLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "3p-f2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var diagnostics = new ExceptionDiagnostics(directory, outputOptions: new ExceptionOutputOptions(true, false));
            var order = new List<string>();
            var lease = new Mock<IClientLease>(MockBehavior.Strict);
            var service = new Mock<IOrganizationService>(MockBehavior.Strict);
            service.Setup(value => value.Execute(It.IsAny<WhoAmIRequest>())).Throws(new InvalidOperationException("synthetic-sensitive-canary"));
            lease.SetupGet(value => value.Service).Returns(service.Object);
            lease.Setup(value => value.MarkFaulted()).Callback(() => order.Add("fault"));
            lease.Setup(value => value.Dispose()).Callback(() => order.Add("release"));
            var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
            manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
            var result = HealthEndpoints.CheckReady(manager.Object, diagnostics, CancellationToken.None);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.Collection(order,
                value => Assert.Equal("fault", value),
                value => Assert.Equal("release", value));
            var text = await File.ReadAllTextAsync(Path.Combine(directory, "Exception.log"));
            Assert.Contains("GoodShepherd2.Health.Ready", text, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-sensitive-canary", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>SDK 回應缺少真實身分或租約歸還失敗，都不得將候選 ready 發布為成功。</summary>
    /// <param name="releaseFails">是否在歸還注入錯誤；否則注入缺少身分的 SDK 回應。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ready_IncompleteIdentityOrReleaseFailure_Returns503(bool releaseFails)
    {
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new ExceptionOutputOptions(false, false));
        var lease = new Mock<IClientLease>(MockBehavior.Loose);
        var service = new Mock<IOrganizationService>(MockBehavior.Strict);
        service.Setup(value => value.Execute(It.IsAny<WhoAmIRequest>())).Returns(releaseFails ? Identity() : new WhoAmIResponse());
        lease.SetupGet(value => value.Service).Returns(service.Object);
        if (releaseFails) lease.Setup(value => value.Dispose()).Throws(new IOException("synthetic-release"));
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        var result = HealthEndpoints.CheckReady(manager.Object, diagnostics, CancellationToken.None);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        lease.Verify(value => value.Dispose(), Times.Once);
    }

    /// <summary>呼叫端已取消時不取得租約、不宣稱 ready，也不把正常取消寫成基礎設施告警。</summary>
    [Fact]
    public async Task Ready_CallerCancelled_DoesNotAcquireOrNotify()
    {
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new ExceptionOutputOptions(false, false));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        Assert.Throws<OperationCanceledException>(() => HealthEndpoints.CheckReady(manager.Object, diagnostics, cancellation.Token));
        manager.Verify(value => value.Acquire(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>同步 SDK 正在執行時 caller 取消不能提前歸還租約；SDK 真正返回後才淘汰、釋放並傳回正常取消。</summary>
    [Fact]
    public async Task Ready_CancelDuringSdk_DrainsBeforeLeaseRelease()
    {
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(),
            outputOptions: new ExceptionOutputOptions(false, false));
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var released = 0;
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        var service = new Mock<IOrganizationService>(MockBehavior.Strict);
        service.Setup(value => value.Execute(It.IsAny<WhoAmIRequest>())).Returns(() =>
        {
            entered.Set();
            if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic SDK drain deadline.");
            return Identity();
        });
        lease.SetupGet(value => value.Service).Returns(service.Object);
        lease.Setup(value => value.MarkFaulted());
        lease.Setup(value => value.Dispose()).Callback(() => Interlocked.Increment(ref released));
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        var operation = Task.Run(() => HealthEndpoints.CheckReady(manager.Object, diagnostics, cancellation.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            await cancellation.CancelAsync();
            Assert.False(operation.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref released));
        }
        finally
        {
            finish.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await operation.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.Equal(1, Volatile.Read(ref released));
        lease.Verify(value => value.MarkFaulted(), Times.Once);
    }

    /// <summary>F3 前誤放任何產品 Channel 或啟用例外 LINE 必須在建置主機前拒絕，不建立 sender 或共用舊產品驗簽。</summary>
    /// <param name="key">注入的部署鍵名。</param>
    /// <param name="value">足以啟用不允許功能的合成值。</param>
    [Theory]
    [InlineData("ExceptionNotifications:SendLine", "true")]
    [InlineData("LineWebhook:Channels:0:ChannelId", "synthetic-channel")]
    [InlineData("LineWebhook:EnableLegacyCallback", "true")]
    public void Host_F2RejectsChannelsAndLineBeforeBuild(string key, string value)
    {
        var builder = Builder(Path.GetTempPath());
        try
        {
            builder.Configuration[key] = value;
            Assert.Throws<InvalidOperationException>(() => Program.BuildApplication(builder));
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
    }

    /// <summary>只建立與本案分離的 Deployment 快照；避免讀真實 User Secrets 或修改程序環境。</summary>
    private static WebApplicationBuilder Builder(string directory)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development, ContentRootPath = directory, ApplicationName = typeof(HostTests).Assembly.FullName });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:ServerUrl"] = "https://goodshepherd20.example/XRMServices/2011/Organization.svc",
            ["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20",
            ["ExceptionNotifications:WriteExceptionLog"] = "true",
            ["ExceptionNotifications:SendLine"] = "false",
            ["ConnectionStrings:Platform"] = "Server=(localdb)\\MSSQLLocalDB;Database=GoodShepherd2Platform;Integrated Security=true;Encrypt=true",
            ["LineWebhook:Queue:Capacity"] = "8", ["LineWebhook:Queue:PartitionCount"] = "1"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    /// <summary>建立只含三個合成 GUID 的 WhoAmI 成功回應，不使用真實組織／使用者識別。</summary>
    private static WhoAmIResponse Identity() => new() { Results = new ParameterCollection { ["UserId"] = Guid.NewGuid(), ["BusinessUnitId"] = Guid.NewGuid(), ["OrganizationId"] = Guid.NewGuid() } };
}
