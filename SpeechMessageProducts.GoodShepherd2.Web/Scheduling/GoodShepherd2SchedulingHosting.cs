/*
檔案整體說明
目的與定位：F4產品排程的唯一組合入口，讓Program只加一行並把持久化SQL、Hangfire server及排空所有權交回共用Scheduling核心。
主要功能與流程：未提供Scheduling區段時註冊拒絕排程及拒絕重驗插槽，不開SQL或CRM；既有共用ConnectionStrings:Platform單獨存在不代表啟用。啟用只接受Scheduling:OrganizationId及同一Platform連線，部分／未知設定拒絕啟動。註冊早於核心的Host lifecycle守門先用同一manager的WhoAmI驗組織，再讓核心啟動；每次執行另以scoped validator即時查租戶。
使用邊界與維護：固定產品GoodShepherd2不取自HTTP或設定，不註冊工作、每日健康工作或dashboard。設定快照活至Host釋放；沒有角色、token、授權結果cache或自行建立背景工作。組織驗證只在啟動及工作執行發生，不增加一般HTTP成本。manager、SQL與server各有既有唯一owner，生命週期守門只借短期lease並同步完整排空，沒有需要Stop另釋放的資源；真正啟動故障交Main共用診斷。離線測試只證明組合及合成WhoAmI，不代表SQL或D365實機可用。
*/
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeechMessage.Scheduling;
using ToolUtilityNameSpace.Dataverse;

namespace SpeechMessageProducts.GoodShepherd2.Web.Scheduling;

/// <summary>受信任產品組合根的F4註冊入口，不接受caller選產品、組織或連線。</summary>
internal static class GoodShepherd2SchedulingHosting
{
    /// <summary>驗部署設定後註冊一次產品重驗與核心owner，不在註冊時建線或建立隱藏容器。</summary>
    /// <param name="builder">已通過F2環境及端點守門的同一產品builder。</param>
    internal static void AddGoodShepherd2Scheduling(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var section = builder.Configuration.GetSection("Scheduling");
        if (!section.Exists())
        {
            builder.Services.TryAddSingleton<IJobScheduler, UnconfiguredGoodShepherd2Scheduler>();
            builder.Services.TryAddScoped<IScheduledJobScopeValidator, UnconfiguredGoodShepherd2ScopeValidator>();
            return;
        }
        if (section.Value is not null ||
            section.GetChildren().Any(child => !string.Equals(child.Key, "OrganizationId", StringComparison.OrdinalIgnoreCase)) ||
            !Guid.TryParseExact(section["OrganizationId"], "D", out var organizationId) || organizationId == Guid.Empty)
            throw new InvalidOperationException("Scheduling:OrganizationId must be a non-empty deployment organization GUID.");
        var connection = builder.Configuration.GetConnectionString("Platform");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Scheduling requires ConnectionStrings:Platform.");

        var options = new SchedulingOptions(GoodShepherd2Product.Name, organizationId, connection);
        builder.Services.AddScoped<IScheduledJobScopeValidator, GoodShepherd2ScheduledJobScopeValidator>();
        // lifecycle StartingAsync全部完成後Host才呼叫任一StartAsync，避免核心server在組織核對前開始處理工作。
        builder.Services.AddSingleton<IHostedService>(provider =>
            new GoodShepherd2SchedulingOrganizationStartup(provider.GetRequiredService<IDataverseConnectionManager>(), options));
        builder.Services.AddProductScheduling(options);
    }
}

/// <summary>無背景資源的啟動組織守門，在任何HostedService.StartAsync前完成權威組織驗證。</summary>
/// <param name="manager">Host既有固定產品manager；本服務不關閉或長期租用連線。</param>
/// <param name="options">固定部署快照，組織GUID仍以當次WhoAmI為準。</param>
internal sealed class GoodShepherd2SchedulingOrganizationStartup(
    IDataverseConnectionManager manager, SchedulingOptions options) : IHostedLifecycleService
{
    /// <summary>只借單次lease做WhoAmI，不讀租戶、不啟動SQL；故障完整排空後阻擋主機啟動。</summary>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        GoodShepherd2SchedulingCrmLease.Execute(manager, options.OrganizationId, static _ => true, cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>所有StartingAsync已完成，沒有第二次建線或常駐工作。</summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>不保存啟動結果或註冊回呼，因此沒有啟動後工作。</summary>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>沒有本服務擁有的資源，核心及既有manager各依自身owner停止。</summary>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>所有短期lease已在同步驗證內歸還，不建立關機I/O或補償工作。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <summary>關機後不保留身分、租約或排程伺服器參照。</summary>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>缺排程設定時的明確拒絕入口；沒有SQL、字典、timer或可釋放資源。</summary>
internal sealed class UnconfiguredGoodShepherd2Scheduler : IJobScheduler
{
    /// <summary>缺設定不保存工作；呼叫端正常取消優先，其他呼叫拋固定組態失敗。</summary>
    public Task ScheduleAsync(JobKey key, DateTimeOffset runAtUtc, JobScope scope, string payloadJson, CancellationToken ct) =>
        Refuse(ct);
    /// <summary>缺設定不建立週期工作，不以記憶體排程冒稱持久化。</summary>
    public Task ScheduleRecurringAsync(JobKey key, string cron, string timeZoneId, JobScope scope, string payloadJson, CancellationToken ct) =>
        Refuse(ct);
    /// <summary>缺設定不能宣稱已取消持久化工作，必須明確回傳失敗。</summary>
    public Task CancelAsync(JobKey key, CancellationToken ct) => Refuse(ct);
    /// <summary>純拒絕，不接觸工作內容、scope、秘密或任何外部服務。</summary>
    private static Task Refuse(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("GoodShepherd2 scheduling is not configured.");
    }
}

/// <summary>缺部署組織時即使被直接呼叫亦拒絕全部scope，不解析manager或建立外部資源。</summary>
internal sealed class UnconfiguredGoodShepherd2ScopeValidator : IScheduledJobScopeValidator
{
    /// <summary>正常取消優先，其餘固定false；不是允許以null租戶繞過組織驗證的插槽。</summary>
    public Task<bool> CanExecuteAsync(JobScope scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
