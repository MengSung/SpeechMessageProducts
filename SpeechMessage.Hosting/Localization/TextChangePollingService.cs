/*
 * 檔案整體說明
 *
 * 目的與定位：P2-10 的改字輪詢契約及 Hosting 背景服務，只使固定部署組織／產品內
 * 已改字教會的 P1 覆寫快照失效，不取得使用者資料，也不自行建立 D365 client。
 * 主要功能與流程：來源回傳正式教會 ID 與 UTC 版本；完整驗證候選後才逐一 Invalidate。
 * 成功查詢以開始時間前進 watermark，下一輪回看兩分鐘；失敗不得前進或清除快照。
 * 使用邊界與維護：Host 借入來源、cache、時鐘及共用告警委派，必須先停止本服務並
 * 排空 I/O 再 Dispose cache 與連線 manager。單一迴圈每三十秒執行，沒有額外工作佇列、
 * 教會快取或長生命週期憑證；每批最多五千筆，參考只在本次操作保留。
 * caller 取消直接傳播，其他故障保留真實 Exception 交共用入口；離線測試不是實機證據。
 */
using Microsoft.Extensions.Hosting;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Hosting.Localization;

/// <summary>固定已授權部署範圍內的正式教會改字版本，不含名稱、個資、SDK 或憑證。</summary>
public sealed record TextChangedChurch(Guid ChurchId, DateTime ChangedOnUtc);

/// <summary>Host 提供的完整有界改字來源；連線與組織授權由實作的部署 owner 負責。</summary>
public interface ITextChangeSource
{
    /// <summary>查出嚴格大於指定 UTC 時刻的所有變更，失敗不得發布部分頁，最多五千筆。</summary>
    Task<IReadOnlyList<TextChangedChurch>> ReadAsync(DateTime sinceUtc, CancellationToken cancellationToken);
}

/// <summary>固定部署範圍的三十秒輪詢器，借用既有快取且停止時排空當前來源。</summary>
public sealed class TextChangePollingService : BackgroundService
{
    /// <summary>建立固定組織／產品服務；來源必須由組合根核對實際 endpoint 與身分。</summary>
    public TextChangePollingService(
        ITextChangeSource source, ChurchTextOverrideCache cache, string organization, string product,
        TimeProvider? timeProvider = null, Action<Exception, string, CancellationToken>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(cache);
        _ = new TextContext(Guid.NewGuid(), "zh-TW", "zh-TW", organization, product);
        this.source = source;
        this.cache = cache;
        this.organization = organization;
        this.product = product;
        time = timeProvider ?? TimeProvider.System;
        report = reportFailure ?? ((exception, operation, token) => ExceptionReporting.Report(exception, operation, token));
        lastQueryUtc = time.GetUtcNow().UtcDateTime;
    }

    private readonly ITextChangeSource source;
    private readonly ChurchTextOverrideCache cache;
    private readonly string organization;
    private readonly string product;
    private readonly TimeProvider time;
    private readonly Action<Exception, string, CancellationToken> report;
    private DateTime lastQueryUtc;
    private int polling;

    /// <summary>執行一次有界輪詢；與背景迴圈競爭時拒絕第二個 owner，不排入無界等待。</summary>
    public async Task<bool> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref polling, 1, 0) != 0)
            throw new InvalidOperationException("A text-change poll is already active.");
        try
        {
            var started = time.GetUtcNow().UtcDateTime;
            var rows = await source.ReadAsync(lastQueryUtc.AddMinutes(-2), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (rows is null || rows.Count > 5000)
                throw new InvalidOperationException("Text-change result exceeds its bounded contract.");
            var identities = new HashSet<Guid>();
            var candidate = new List<(TextContext Context, DateTime Stamp)>(rows.Count);
            foreach (var row in rows)
            {
                if (row is null || row.ChurchId == Guid.Empty || row.ChangedOnUtc.Kind != DateTimeKind.Utc ||
                    !identities.Add(row.ChurchId))
                    throw new InvalidOperationException("Text-change row identity or UTC stamp is invalid.");
                candidate.Add((new TextContext(row.ChurchId, "zh-TW", "zh-TW", organization, product), row.ChangedOnUtc));
            }
            cancellationToken.ThrowIfCancellationRequested();
            // 完整候選驗證後才發布失效；不移除舊快照，也不把資料庫集合保留到下一輪。
            foreach (var item in candidate) cache.Invalidate(item.Context, item.Stamp);
            // 使用開始而非完成時間；慢查詢與故障期間的變更仍會進入後續重疊視窗。
            if (started > lastQueryUtc) lastQueryUtc = started;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            try { report(exception, "TextChangePollingService.Poll", cancellationToken); }
            catch { Console.Error.WriteLine("[TextChangePollingService] NotificationFailed"); }
            return false;
        }
        finally { Volatile.Write(ref polling, 0); }
    }

    /// <summary>持續輪詢直到 Host 停止；來源必須有 transport deadline 且遵守取消。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host 停止會取消並等待此唯一 task；不把正常排空當成失敗或另外建立背景工作。
        }
    }
}
