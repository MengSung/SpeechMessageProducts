/*
檔案整體說明
目的與定位：保護F4在真實產品組合根的離線設定契約，確認未配置時保留拒絕執行的插槽，部分配置不能啟動。
主要功能與流程：每案建立只讀取合成記憶體組態的Development builder，呼叫真正Program入口。未配置時解析IJobScheduler並確認排程被拒絕；組織鍵無效或缺SQL時必須在Build前失敗。平台連線即使存在亦不單獨啟用排程，延續F6既有離線主機。
使用邊界與維護：不讀User Secrets、不連D365、SQL或LINE；沒有產品工作或dashboard。Build成功後Host唯一釋放組態，失敗時當案finally釋放尚未移交的builder組態；同F2 collection序列執行，避免程序診斷owner重疊。測試只證明離線組合與拒絕結果，不宣稱外部儲存可用。
*/
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Moq;
using SpeechMessage.Scheduling;
using SpeechMessageProducts.GoodShepherd2.Web.Scheduling;
using ToolUtilityNameSpace.Dataverse;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Scheduling;

/// <summary>透過產品真正Build入口驗證設定邊界；所有外部來源由單案記憶體設定取代。</summary>
[Collection("F2Host")]
public sealed class SchedulingRegistrationTests
{
    /// <summary>缺排程組織鍵時仍能建主機；即使共用平台連線存在，排程插槽也必須明確拒絕且不建立SQL owner。</summary>
    [Fact]
    public async Task UnconfiguredHost_RegistersFailClosedScheduler()
    {
        var builder = Builder();
        WebApplication? app = null;
        try
        {
            app = Program.BuildApplication(builder);
            var scheduler = app.Services.GetService<IJobScheduler>();
            Assert.NotNull(scheduler);
            Assert.Empty(app.Services.GetServices<IScheduledJobHandler>());
            using var scope = app.Services.CreateScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<IScheduledJobScopeValidator>().CanExecuteAsync(
                new JobScope("GoodShepherd2", Guid.NewGuid(), null), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.ScheduleAsync(
                new JobKey("GoodShepherd2", "synthetic", "one"), DateTimeOffset.UnixEpoch,
                new JobScope("GoodShepherd2", Guid.NewGuid(), Guid.NewGuid()), "{}", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.ScheduleRecurringAsync(
                new JobKey("GoodShepherd2", "synthetic", "one"), "0 9 * * *", "Asia/Taipei",
                new JobScope("GoodShepherd2", Guid.NewGuid(), null), "{}", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.CancelAsync(
                new JobKey("GoodShepherd2", "synthetic", "one"), CancellationToken.None));
            using var caller = new CancellationTokenSource();
            await caller.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduler.CancelAsync(
                new JobKey("GoodShepherd2", "synthetic", "one"), caller.Token));
        }
        finally
        {
            if (app is not null) await app.DisposeAsync();
            else ((IDisposable)builder.Configuration).Dispose();
        }
    }

    /// <summary>空白、非GUID或空GUID組織鍵都屬部分配置，不能靜默回退未啟用。</summary>
    /// <param name="organization">只屬本案的非法組織設定。</param>
    [Theory]
    [InlineData("")]
    [InlineData("synthetic-invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void PartialConfiguration_RejectsStartup(string organization)
    {
        var builder = Builder();
        WebApplication? unexpected = null;
        try
        {
            builder.Configuration["Scheduling:OrganizationId"] = organization;
            Assert.Throws<InvalidOperationException>(() => unexpected = Program.BuildApplication(builder));
        }
        finally
        {
            if (unexpected is not null) ((IDisposable)unexpected).Dispose();
            else ((IDisposable)builder.Configuration).Dispose();
        }
    }

    /// <summary>組織鍵存在但沒有平台連線時必須拒絕，不建立記憶體排程冒充持久化。</summary>
    [Fact]
    public void OrganizationWithoutPlatformConnection_RejectsStartup()
    {
        var builder = Builder();
        WebApplication? unexpected = null;
        try
        {
            builder.Configuration["Scheduling:OrganizationId"] = Guid.NewGuid().ToString("D");
            builder.Configuration["ConnectionStrings:Platform"] = null;
            Assert.Throws<InvalidOperationException>(() => unexpected = Program.BuildApplication(builder));
        }
        finally
        {
            if (unexpected is not null) ((IDisposable)unexpected).Dispose();
            else ((IDisposable)builder.Configuration).Dispose();
        }
    }

    /// <summary>未知排程鍵不能讓部署者誤以為產品或worker可由設定覆寫；拒絕時尚未建立任何資源。</summary>
    [Fact]
    public void UnknownSchedulingSetting_RejectsStartup()
    {
        var builder = Builder();
        try
        {
            builder.Configuration["Scheduling:OrganizationId"] = Guid.NewGuid().ToString("D");
            builder.Configuration["Scheduling:Product"] = "GoodButler";
            Assert.Throws<InvalidOperationException>(() =>
                GoodShepherd2SchedulingHosting.AddGoodShepherd2Scheduling(builder));
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
    }

    /// <summary>真實Program啟用排程時，錯誤WhoAmI組織必須在任何SQL／server啟動前拒絕；歸還租約後完整停止及釋放Host。</summary>
    [Fact]
    public async Task ConfiguredHost_RejectsActualOrganizationMismatchBeforeRuntimeStarts()
    {
        var builder = Builder();
        WebApplication? app = null;
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(value => value.Execute(It.IsAny<WhoAmIRequest>())).Returns(new WhoAmIResponse
        {
            Results = new ParameterCollection
            {
                ["OrganizationId"] = Guid.NewGuid(), ["UserId"] = Guid.NewGuid(), ["BusinessUnitId"] = Guid.NewGuid()
            }
        });
        var lease = new Mock<IClientLease>(MockBehavior.Strict);
        lease.SetupGet(value => value.Service).Returns(sdk.Object);
        lease.Setup(value => value.MarkFaulted());
        lease.Setup(value => value.Dispose());
        var manager = new Mock<IDataverseConnectionManager>(MockBehavior.Strict);
        manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(lease.Object);
        builder.Services.AddSingleton(manager.Object);
        builder.Configuration["Scheduling:OrganizationId"] = Guid.NewGuid().ToString("D");
        builder.Configuration["ConnectionStrings:Platform"] =
            "Server=synthetic.invalid;Database=GoodShepherd2Platform;Integrated Security=true;Encrypt=true;Enlist=false";
        try
        {
            app = Program.BuildApplication(builder);
            // 固定組織失配是產品守門的InvalidOperationException；不能走到SQL連線或Kestrel啟動。
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
            lease.Verify(value => value.MarkFaulted(), Times.Once);
            lease.Verify(value => value.Dispose(), Times.Once);
        }
        finally
        {
            if (app is not null)
            {
                try { await app.StopAsync(); }
                finally { await app.DisposeAsync(); }
            }
            else ((IDisposable)builder.Configuration).Dispose();
        }
    }

    /// <summary>只讀合成組態的唯一builder，不讀本機使用者秘密，不啟動HTTP或任何外部服務。</summary>
    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = Path.GetTempPath(),
            ApplicationName = typeof(SchedulingRegistrationTests).Assembly.FullName
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:ServerUrl"] = "https://goodshepherd20.example/XRMServices/2011/Organization.svc",
            ["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20",
            ["ExceptionNotifications:WriteExceptionLog"] = "false",
            ["ExceptionNotifications:SendLine"] = "false",
            ["ConnectionStrings:Platform"] = "Server=synthetic.invalid;Database=GoodShepherd2Platform;Integrated Security=true;Encrypt=true",
            ["LineWebhook:Queue:Capacity"] = "8",
            ["LineWebhook:Queue:PartitionCount"] = "1"
        });
        return builder;
    }
}
