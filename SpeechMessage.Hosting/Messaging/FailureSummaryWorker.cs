/*
 * 檔案整體說明
 * 目的與定位：P5-12 通用外送失敗彙總排程，不參考產品、Dataverse SDK 或 LINE；
 * 筆數由每輪 scoped source 供應，通知只交既有 ExceptionDiagnostics 唯一 owner。
 * 主要流程：缺省停用不發布任何服務；啟用以一至六十分鐘 UTC 對齊封閉窗查詢，
 * 每窗最多64間教會、逐教會超過門檻才發安全摘要。拒絕重複教會、負筆數或超量，
 * 先完整驗證再通知，不讓錯誤來源產生半套彙總。每輪只留純值至完成，不留歷史集合。
 * 資源與隔離：Host 擁有 worker；每輪 async DI scope 與30秒合作取消來源由 using
 * 唯一持有，等待 source 真正返回才 Dispose，不以 WaitAsync 遺棄仍存活的查詢。
 * 同步 SDK 的硬期限由 source 的傳輸 owner 保證，worker 不假稱能強制中斷它。
 * 啟動排程抑制 ExecutionContext 流入，避免啟動呼叫者的 AsyncLocal／身分被長期保留。
 * Stop 取消並等待排空，timer 在 Execute finally Dispose；沒有 Session、憑證或快取。
 * 單次啟動每窗最多嘗試一次，不保存跨程序／重啟通知帳本；不宣稱 exactly-once。
 * 重啟只讀當前最近封閉窗，不追補無界歷史，後續持久去重需求須另核定。
 */
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Hosting.Messaging;

/// <summary>已驗證來源所回的 detached 教會筆數；沒有活動原文、個資、SDK 或憑證。</summary>
public sealed record FailureSummaryCount(Guid ChurchId, long Count);

/// <summary>由產品組合根提供的 scoped 唯讀 adapter；不得回傳跨組織或未授權資料。</summary>
public interface IFailureSummarySource
{
    /// <summary>
    /// 讀 start 含、end 不含的封閉 UTC 窗，最多64間受信任教會，不讀內容或個資。
    /// 實作須遵守取消／傳輸期限，Task 完成前不得釋放 SDK，也不得遺棄背景查詢。
    /// </summary>
    Task<IReadOnlyList<FailureSummaryCount>> ReadAsync(DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc, CancellationToken ct);
}

/// <summary>受信任部署設定的不可變快照，不保存 configuration 或允許 request 改寫。</summary>
public sealed class FailureSummaryOptions
{
    /// <summary>週期一至六十分鐘，門檻缺省十且允許零；無效值在註冊前拒絕。</summary>
    public FailureSummaryOptions(int intervalMinutes = 5, int threshold = 10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(intervalMinutes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(intervalMinutes, 60);
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, 50000);
        Interval = TimeSpan.FromMinutes(intervalMinutes);
        Threshold = threshold;
    }
    /// <summary>對齊 UTC 的固定窗寬，不採用主機本地時間或 DST。</summary>
    public TimeSpan Interval { get; }
    /// <summary>筆數嚴格大於此值才通知，不跨教會加總。</summary>
    public long Threshold { get; }
}

/// <summary>啟動時一次性註冊，停用時連 timer、來源、診斷服務都不額外建立。</summary>
public static class FailureSummaryRegistration
{
    /// <summary>只讀部署設定；source 必須由上層組合根註冊，不提供 no-op 後備。</summary>
    public static IServiceCollection AddMessageFailureSummary(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Messaging:FailureSummary");
        if (!section.GetValue("Enabled", false)) return services;
        var options = new FailureSummaryOptions(section.GetValue("IntervalMinutes", 5), section.GetValue("Threshold", 10));
        if (services.Any(x => x.ServiceType == typeof(FailureSummaryOptions)))
            throw new InvalidOperationException("Failure summary is already registered.");
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<FailureSummaryWorker>();
        return services;
    }
}

/// <summary>單排程 owner，沒有長存 scoped 服務；通知沿用共用落檔保序及有界佇列。</summary>
public sealed class FailureSummaryWorker(
    IServiceScopeFactory scopeFactory, ExceptionDiagnostics diagnostics, FailureSummaryOptions options,
    TimeProvider clock) : BackgroundService
{
    /// <summary>只記最後已嘗試封閉窗，容量固定；不持有任何教會或資料列快取。</summary>
    private long lastEnd;
    /// <summary>排程或一次性呼叫不得並行；衝突拒絕而非靜默丟失一次執行。</summary>
    private int running;

    /// <summary>背景排程不得捕獲啟動呼叫者的 AsyncLocal、request 或使用者身分。</summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (ExecutionContext.IsFlowSuppressed()) return base.StartAsync(cancellationToken);
        using (ExecutionContext.SuppressFlow()) return base.StartAsync(cancellationToken);
    }

    /// <summary>提供排程的單窗操作；呼叫端須等待返回，不能與 Host 排程並行。</summary>
    public async Task RunWindowAsync(DateTimeOffset utcNow, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            throw new InvalidOperationException("Failure summary cannot run concurrently.");
        try
        {
            var ticks = utcNow.UtcTicks / options.Interval.Ticks * options.Interval.Ticks;
            if (ticks <= lastEnd) return;
            // 先標示嘗試，未知是否已通知時不在同窗重送；只保證本 owner 的 at-most-once。
            lastEnd = ticks;
            var end = new DateTimeOffset(ticks, TimeSpan.Zero);
            var start = end - options.Interval;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await using var scope = scopeFactory.CreateAsyncScope();
            var source = scope.ServiceProvider.GetRequiredService<IFailureSummarySource>();
            var rows = await source.ReadAsync(start, end, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            if (rows is null || rows.Count > 64) throw new InvalidOperationException("Failure summary source capacity exceeded.");
            var identities = new HashSet<Guid>();
            foreach (var row in rows)
                if (row is null || row.ChurchId == Guid.Empty || row.Count is < 0 or > 1000000000 || !identities.Add(row.ChurchId))
                    throw new InvalidOperationException("Failure summary source identity or count is invalid.");
            foreach (var row in rows)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (row.Count > options.Threshold) diagnostics.ReportFailureSummary(row.ChurchId, row.Count, start, end);
            }
        }
        finally { Volatile.Write(ref running, 0); }
    }

    /// <summary>啟動先處理最近封閉窗，後續 timer 只留單一等待，不追補停機歷史。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Interval, clock);
        do
        {
            try { await RunWindowAsync(clock.GetUtcNow(), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception failure) { diagnostics.Report(failure, "Messaging.FailureSummary.Read", stoppingToken); }
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        } while (!stoppingToken.IsCancellationRequested);
    }

    /// <summary>不以 Host 已取消 token 遺棄 source／scope；source 合作取消及傳輸期限決定排空上限。</summary>
    public override Task StopAsync(CancellationToken cancellationToken) => base.StopAsync(CancellationToken.None);
}
