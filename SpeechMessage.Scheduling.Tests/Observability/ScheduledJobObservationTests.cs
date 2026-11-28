/*
檔案整體說明
目的與定位：F11正式排程dispatcher的scope生命週期離線測試，不連SQL，直接驗證同一RunScopedAsync核心在handler及DisposeAsync期間的資源計數。
主要功能與流程：以真AddProductScheduling建立Lazy runtime但不Start，透過私有執行核心測試validator／handler／async scope釋放。正式SQL ledger與工作鎖仍由既有F4 LocalDB測試覆蓋，本案不以跳過SQL宣稱完整排程或實機驗收。
使用邊界與維護：當案DI容器、Meter及diagnostics各有唯一owner，所有handler純合成；停止先真實排空scope再關Meter。反射只為避免擴張正式API，不重寫dispatcher行為，也不保存payload或跨案狀態；scope Dispose時仍須計在途，完成後必須回零。
*/
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Hosting.Observability;
using SpeechMessage.Scheduling.Hangfire;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Scheduling.Tests.Observability;

/// <summary>不啟SQL或worker的真DI scoped執行生命週期測試。</summary>
public sealed class ScheduledJobObservationTests
{
    /// <summary>handler及其async Dispose均觀察到1，完整清理後0；不得在handler回傳就提前扣回。</summary>
    [Fact]
    public async Task Scheduler_Observation_CoversHandlerAndAsyncScopeCleanup()
    {
        using var meters = new HostingMeters();
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        var services = new ServiceCollection();
        services.AddSingleton(meters);
        services.AddSingleton(diagnostics);
        services.AddScoped<IScheduledJobScopeValidator, AllowScope>();
        services.AddScoped<IScheduledJobHandler>(provider => new Handler(provider.GetRequiredService<HostingMeters>()));
        services.AddProductScheduling(new SchedulingOptions("GoodShepherd2", Guid.NewGuid(),
            "Server=unopened.example;Database=synthetic;Integrated Security=true;Encrypt=true"));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var dispatcher = provider.GetRequiredService<ScheduledJobDispatcher>();
        var method = typeof(ScheduledJobDispatcher).GetMethod("RunScopedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var key = new JobKey("GoodShepherd2", "observed", "synthetic");
        var scope = new JobScope("GoodShepherd2", Guid.NewGuid(), null);
        var parameters = method.GetParameters().Length == 4
            ? new object[] { key, scope, "{}", CancellationToken.None }
            : new object[] { key, scope, "{}", "42", CancellationToken.None };
        await Assert.IsAssignableFrom<Task>(method.Invoke(dispatcher, parameters));
        Assert.Equal(0, meters.GetSnapshot().SchedulerJobsRunning);
    }

    /// <summary>本案純重驗替身；沒有外部資料，不在正式程式引入允許所有scope的預設。</summary>
    private sealed class AllowScope : IScheduledJobScopeValidator
    {
        public Task<bool> CanExecuteAsync(JobScope scope, CancellationToken ct) => Task.FromResult(true);
    }

    /// <summary>短命handler不保存使用者狀態，Dispose在同一正式async DI scope由dispatcher觸發。</summary>
    private sealed class Handler(HostingMeters meters) : IScheduledJobHandler, IAsyncDisposable
    {
        public string Name => "observed";
        public Task ExecuteAsync(JobScope scope, string payloadJson, CancellationToken ct)
        {
            Assert.Equal(1, meters.GetSnapshot().SchedulerJobsRunning);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Assert.Equal(1, meters.GetSnapshot().SchedulerJobsRunning);
            return ValueTask.CompletedTask;
        }
    }
}
