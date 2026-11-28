/*
檔案整體說明
目的與定位：F11每日Channel健康工作註冊的離線契約，以真DI描述子及合成scheduler檢查03:00台北時區，避免使用主機本地時區或自行timer。
主要功能與流程：反射確認新增組合入口形成可編譯RED，提供已驗部署組織及Channel health descriptor，啟動唯一註冊服務後驗固定JobKey、null租戶scope、cron、時區與空payload。缺設定時不得註冊任何工作。
使用邊界與維護：測試scheduler僅保存本案純值，不開SQL／CRM／LINE，偽health checker工廠若被錯誤提前解析即失敗。配置由當案builder擁有並確定Dispose，沒有背景程序、timer或跨案例狀態；排程註冊不是實機執行結果。
*/
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Scheduling;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>以正式IHostedService啟動行為驗證持久化註冊，而不是只看cron常值出現。</summary>
public sealed class ChannelHealthScheduleTests
{
    /// <summary>固定組織級工作每日03:00 Asia/Taipei，只有完整前置才經F4入口註冊，沒有額外timer。</summary>
    [Fact]
    public async Task Scheduler_ChannelHealthJob_RegisteredDailyTaipei0300()
    {
        var type = typeof(Program).Assembly.GetType("SpeechMessageProducts.GoodShepherd2.Web.Observability.GoodShepherd2HealthSchedule");
        Assert.NotNull(type);
        var org = Guid.NewGuid();
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Scheduling:OrganizationId"] = org.ToString("D"), ["Channels:OrganizationId"] = org.ToString("D") });
        var scheduler = new CaptureScheduler();
        builder.Services.AddSingleton<IJobScheduler>(scheduler);
        builder.Services.AddSingleton<ChannelHealthChecker>(_ => throw new InvalidOperationException("Must not resolve health HTTP during registration."));
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        builder.Services.AddSingleton(diagnostics);
        try
        {
            type.GetMethod("AddGoodShepherd2HealthSchedule", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { builder });
            await using var provider = builder.Services.BuildServiceProvider();
            var service = Assert.Single(provider.GetServices<IHostedService>());
            await service.StartAsync(default);
            Assert.Equal("0 3 * * *", scheduler.Cron);
            Assert.Equal("Asia/Taipei", scheduler.TimeZone);
            Assert.Equal("GoodShepherd2:channel-health:daily", scheduler.Key?.ToString());
            Assert.Equal(new JobScope("GoodShepherd2", org, null), scheduler.Scope);
            Assert.Equal("{}", scheduler.Payload);
            await service.StopAsync(default);
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
    }

    /// <summary>缺任一部署前置不註冊，異組織或重複入口則在排程／HTTP之前拒絕；不解析健康client。</summary>
    [Theory]
    [InlineData("missingSchedule")]
    [InlineData("missingChannel")]
    [InlineData("missingChecker")]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    public void Scheduler_MissingOrConflictingPrerequisites_FailClosed(string scenario)
    {
        var type = typeof(Program).Assembly.GetType("SpeechMessageProducts.GoodShepherd2.Web.Observability.GoodShepherd2HealthSchedule")!;
        var method = type.GetMethod("AddGoodShepherd2HealthSchedule", BindingFlags.Static | BindingFlags.NonPublic)!;
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        var org = Guid.NewGuid().ToString("D");
        builder.Configuration["Scheduling:OrganizationId"] = scenario == "missingSchedule" ? null : org;
        builder.Configuration["Channels:OrganizationId"] = scenario == "missingChannel" ? null : scenario == "foreign" ? Guid.NewGuid().ToString("D") : org;
        if (scenario != "missingChecker") builder.Services.AddSingleton<ChannelHealthChecker>(_ => throw new InvalidOperationException("Must not resolve HTTP."));
        try
        {
            if (scenario == "foreign")
                Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { builder })).InnerException);
            else
            {
                method.Invoke(null, new object[] { builder });
                if (scenario == "duplicate")
                    Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { builder })).InnerException);
                else Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(IHostedService));
            }
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
    }

    /// <summary>F4註冊真失敗與caller取消不被吞掉或變成成功，唯一服務沒有自行建立重試或timer。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scheduler_RegistrationFailure_PreservesOriginal(bool callerCancelled)
    {
        var type = typeof(Program).Assembly.GetType("SpeechMessageProducts.GoodShepherd2.Web.Observability.GoodShepherd2HealthSchedule")!;
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        var org = Guid.NewGuid().ToString("D");
        builder.Configuration["Scheduling:OrganizationId"] = org;
        builder.Configuration["Channels:OrganizationId"] = org;
        using var cancellation = new CancellationTokenSource();
        if (callerCancelled) cancellation.Cancel();
        Exception failure = callerCancelled ? new OperationCanceledException(cancellation.Token) : new IOException("synthetic-register-canary");
        var scheduler = new CaptureScheduler { Failure = failure };
        builder.Services.AddSingleton<IJobScheduler>(scheduler);
        builder.Services.AddSingleton<ChannelHealthChecker>(_ => throw new InvalidOperationException("Must not resolve HTTP."));
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        builder.Services.AddSingleton(diagnostics);
        try
        {
            type.GetMethod("AddGoodShepherd2HealthSchedule", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { builder });
            await using var provider = builder.Services.BuildServiceProvider();
            var service = Assert.Single(provider.GetServices<IHostedService>());
            Assert.Same(failure, await Record.ExceptionAsync(() => service.StartAsync(cancellation.Token)));
            await service.StopAsync(default);
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
    }

    /// <summary>純合成排程sink，不執行callback或建立外部資源，只保存單筆固定註冊供本案檢查。</summary>
    private sealed class CaptureScheduler : IJobScheduler
    {
        /// <summary>當案合成故障，僅在排程註冊呼叫返回，不建立外部工作。</summary>
        internal Exception? Failure { get; init; }
        internal string? Cron { get; private set; }
        internal string? TimeZone { get; private set; }
        internal JobKey? Key { get; private set; }
        internal JobScope? Scope { get; private set; }
        internal string? Payload { get; private set; }
        public Task ScheduleAsync(JobKey key, DateTimeOffset runAtUtc, JobScope scope, string payloadJson, CancellationToken ct) => throw new InvalidOperationException();
        public Task CancelAsync(JobKey key, CancellationToken ct) => throw new InvalidOperationException();
        public Task ScheduleRecurringAsync(JobKey key, string cron, string timeZoneId, JobScope scope, string payloadJson, CancellationToken ct)
        {
            if (Failure is not null) return Task.FromException(Failure);
            ct.ThrowIfCancellationRequested(); Key = key; Cron = cron; TimeZone = timeZoneId; Scope = scope; Payload = payloadJson;
            return Task.CompletedTask;
        }
    }
}
