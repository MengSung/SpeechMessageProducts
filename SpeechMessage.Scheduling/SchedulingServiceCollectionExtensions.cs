/*
檔案整體說明
目的與定位：F4單一產品排程服務的唯一DI組合入口，讓主機以一行註冊持久化工作與速率預算。
主要功能與流程：先驗單一部署註冊，再建立唯一runtime、scheduler、固定dispatcher及scoped速率預算。借用Hosting既有ExceptionDiagnostics與Localization時鐘，產品另注入每次執行的scope validator及handler。
使用邊界與維護：註冊不做外部I/O、不建立隱藏容器或修改Hangfire全域設定。runtime只登錄一個DI owner，IHostedService借用該實例；重入停止共用同一Task。每次工作另建async scope，SQL及四worker在停止後真正排空再清專用池。
*/
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Localization;
using SpeechMessage.Scheduling.Hangfire;

namespace SpeechMessage.Scheduling;

/// <summary>排程器組合根入口；不接受HTTP指定的產品或連線。</summary>
public static class SchedulingServiceCollectionExtensions
{
    /// <summary>完整註冊單一部署owner；產品必須提供既有diagnostics及scope validator，缺少依賴時明確失敗。</summary>
    public static IServiceCollection AddProductScheduling(this IServiceCollection services, SchedulingOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (services.Any(s => s.ServiceType == typeof(SchedulingOptions) || s.ServiceType == typeof(IJobScheduler)))
            throw new InvalidOperationException("Scheduling is already registered for this host.");
        services.AddSingleton(options);
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IChurchClock, ChurchClock>();
        services.AddSingleton<HangfireSchedulingRuntime>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<HangfireSchedulingRuntime>());
        services.AddSingleton<IJobScheduler, HangfireJobScheduler>();
        services.AddSingleton(provider => provider.GetRequiredService<HangfireSchedulingRuntime>().CreateDispatcher());
        services.AddScoped<ScheduledJobExecution>();
        services.AddScoped<IChannelRateBudget, SqlChannelRateBudget>();
        return services;
    }
}
