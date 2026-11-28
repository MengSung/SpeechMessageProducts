/*
檔案整體說明
目的與定位：F11好牧人2.0每日Channel健康工作的唯一組合入口，使用F4持久化排程器呼叫F3已授權目錄及健康服務，不自行建立timer或LINE client。
主要功能與流程：只在Scheduling與Channels固定同一組織且已註冊F3 checker時建立scoped handler及Host註冊服務；Start透過ScheduleRecurringAsync以穩定組織級JobKey設定每日03:00 Asia/Taipei，真正註冊失敗交共用診斷後原樣阻擋啟動。每次handler重驗固定產品、組織、null租戶及空payload，再依已載入可服務Channel逐筆呼叫健康服務。
使用邊界與維護：所有外部呼叫仍由F3／F4原owner負責取消、限速與確定清理；本檔只有部署GUID與借用服務，沒有憑證、request、Session或長命結果cache。停止不刪持久化週期定義，只由F4停止並排空worker；重新啟動覆寫同一鍵而不累加工作。缺配置不排程，不假稱每日檢查已執行。只能由已授權測試環境部署設定啟用，不能藉路由選產品或載入舊Channel。
*/
using SpeechMessage.Scheduling;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Observability;

/// <summary>依現有F3／F4組合前置註冊，不建立第二個scheduler或資源owner。</summary>
internal static class GoodShepherd2HealthSchedule
{
    /// <summary>只有固定部署前置同時齊全才發布描述子；相同Host重複組合屬錯誤，不新增重複工作。</summary>
    internal static void AddGoodShepherd2HealthSchedule(WebApplicationBuilder builder)
    {
        var schedule = builder.Configuration["Scheduling:OrganizationId"];
        var channels = builder.Configuration["Channels:OrganizationId"];
        if (schedule is null || channels is null || !builder.Services.Any(d => d.ServiceType == typeof(ChannelHealthChecker))) return;
        if (!Guid.TryParseExact(schedule, "D", out var organization) || organization == Guid.Empty ||
            !Guid.TryParseExact(channels, "D", out var channelOrganization) || channelOrganization != organization)
            throw new InvalidOperationException("Channel health scheduling deployment boundary is invalid.");
        if (builder.Services.Any(d => d.ServiceType == typeof(HealthScheduleRegistration)))
            throw new InvalidOperationException("Channel health scheduling requires one registration owner.");
        builder.Services.AddScoped<IScheduledJobHandler>(provider => new ChannelHealthJob(organization,
            provider.GetRequiredService<DataverseChannelDirectory>(), provider.GetRequiredService<ChannelHealthChecker>()));
        builder.Services.AddSingleton(provider => new HealthScheduleRegistration(organization,
            provider.GetRequiredService<IJobScheduler>(), provider.GetRequiredService<ExceptionDiagnostics>()));
        builder.Services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<HealthScheduleRegistration>());
    }

    /// <summary>只執行啟動時單次持久化註冊，沒有自有背景工作或Dispose資源。</summary>
    private sealed class HealthScheduleRegistration(Guid organization, IJobScheduler scheduler,
        ExceptionDiagnostics diagnostics) : IHostedService
    {
        /// <summary>由既有核心保證同鍵覆寫，真正失敗先安全告警再傳播原例外，正常Host取消不通知。</summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await scheduler.ScheduleRecurringAsync(new JobKey(GoodShepherd2Product.Name, "channel-health", "daily"),
                    "0 3 * * *", "Asia/Taipei", new JobScope(GoodShepherd2Product.Name, organization, null),
                    "{}", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            { diagnostics.Report(exception, "GoodShepherd2.ChannelHealth.Register", cancellationToken); throw; }
        }
        /// <summary>本服務不擁有worker或scheduler；F4停止並排空，持久化定義保留供下次同鍵更新。</summary>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>一個排程scope內的短命執行器；沒有快取、token或自行持有的HTTP傳輸。</summary>
    private sealed class ChannelHealthJob(Guid organization, DataverseChannelDirectory directory,
        ChannelHealthChecker checker) : IScheduledJobHandler
    {
        /// <summary>固定處理器名稱，不允許資料庫payload反射或選任意程式型別。</summary>
        public string Name => "channel-health";
        /// <summary>F4已重驗scope後仍確認本handler界線，再對已驗同產品可服務集合執行，不讀舊產品Channel。</summary>
        public async Task ExecuteAsync(JobScope scope, string payloadJson, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (scope.Product != GoodShepherd2Product.Name || scope.OrganizationId != organization ||
                scope.TenantId is not null || payloadJson != "{}")
                throw new InvalidOperationException("Channel health job scope is invalid.");
            foreach (var channel in directory.ConfiguredChannelIds)
            {
                ct.ThrowIfCancellationRequested();
                await checker.CheckAsync(channel, ct).ConfigureAwait(false);
            }
        }
    }
}
