/*
檔案整體說明
目的與定位：F11單一Host擁有的四項UpDownCounter owner，觀察Webhook總保留槽、SDK租約、執行工作及推播批次的在途生命週期，不承擔業務排程或授權。
主要功能與流程：每次Track取得只持owner與固定種類的短命計數租約；第一次Dispose精確扣回一次，重入沒有副作用。GetSnapshot在同一鎖內發布四個一致純計數，同步listener故障交共用診斷而不讓觀測中斷取得／歸還；本地快照仍為真實票據權威。Meter名稱固定SpeechMessage.Hosting，instrument不含產品／租戶／事件／使用者tag，沒有高基數字典。
使用邊界與維護：DI是唯一Meter owner、診斷為借用且不得早於Meter停止；無DI相容建構以既有程序轉接入口告警，不自行建新writer。各操作需先排空並歸還租約才Dispose；若仍在途則明確拒絕關閉，不能以Meter停止假造歸零。最多四計數器與當次有界工作數的租約，沒有timer、task、subscription或request參照；取消與故障沿呼叫端finally歸還。離線測試驗證四類各一百租約與重入Dispose，真正資源是否停止仍由各原始owner證明。
*/
using System.Diagnostics.Metrics;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Hosting.Observability;

/// <summary>固定四項主機資源的唯一Meter owner；不保存任何使用者或業務操作內容。</summary>
public sealed class HostingMeters : IDisposable
{
    private readonly object gate = new();
    private readonly ExceptionDiagnostics? diagnostics;
    private readonly Meter meter = new("SpeechMessage.Hosting");
    private readonly UpDownCounter<long>[] counters;
    private readonly long[] values = new long[4];
    private bool disposed;

    /// <summary>無DI的合成測試或工具相容入口；通知仍只沿既有程序入口，不自建owner。</summary>
    public HostingMeters() : this(null) { }

    /// <summary>建立固定instrument，不建立全域static狀態；建構失敗立即關Meter，成功後由同一Host確定性Dispose。</summary>
    /// <param name="diagnostics">只借同一Host的共用診斷owner；省略時沿既有程序相容入口，不另建通知器。</param>
    public HostingMeters(ExceptionDiagnostics? diagnostics)
    {
        this.diagnostics = diagnostics;
        try
        {
            counters = [meter.CreateUpDownCounter<long>("webhook.queue.depth"),
                meter.CreateUpDownCounter<long>("dataverse.leases.active"),
                meter.CreateUpDownCounter<long>("scheduler.jobs.running"),
                meter.CreateUpDownCounter<long>("push.batches.inflight")];
        }
        catch
        {
            // 監聽器在instrument發布期間也可能拋錯；建構尚未移交DI時由本建構子確定釋放Meter。
            meter.Dispose();
            throw;
        }
    }

    /// <summary>涵蓋queue取得總容量到提交／處理／丟棄排空的完整保留期，不僅是buffer長度。</summary>
    public IDisposable TrackWebhook() => Track(0);
    /// <summary>成功借入SDK lease後計入，底層歸還／淘汰及清理完成後才歸還此租約。</summary>
    public IDisposable TrackDataverseLease() => Track(1);
    /// <summary>工作開始執行到本次DI scope清理完成，故障或取消同樣必須歸還。</summary>
    public IDisposable TrackSchedulerJob() => Track(2);
    /// <summary>單批發送開始到傳輸與回應釋放結束；不能代替gateway的送達結果或冪等記錄。</summary>
    public IDisposable TrackPushBatch() => Track(3);

    /// <summary>發布與當次讀取一致的四純值，不暴露底層陣列或讓caller修改監控狀態。</summary>
    public HostingMeterSnapshot GetSnapshot()
    {
        lock (gate) return new(values[0], values[1], values[2], values[3]);
    }

    /// <summary>在owner關閉後拒絕新租約；建立與遞增在相同鎖內，不留下未歸屬的正計數。</summary>
    private CounterLease Track(int index)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var lease = new CounterLease(this, index);
            values[index]++;
            Publish(index, 1);
            return lease;
        }
    }

    /// <summary>每租約僅執行一次，先完成實際資源清理才可呼叫；負計數是必須停止放行的內部錯誤。</summary>
    private void Release(int index)
    {
        lock (gate)
        {
            if (values[index] <= 0) throw new InvalidOperationException("Hosting resource counter ownership conflicts.");
            values[index]--;
            Publish(index, -1);
        }
    }

    /// <summary>監測listener的同步回呼不是產品資源owner；其真故障安全告警，但不能中斷取得票據或finally歸還。</summary>
    private void Publish(int index, long delta)
    {
        try { counters[index].Add(delta); }
        catch (Exception exception)
        {
            if (diagnostics is not null) diagnostics.Report(exception, "Hosting.Meters.Publish");
            else ExceptionReporting.Report(exception, "Hosting.Meters.Publish");
        }
    }

    /// <summary>只有真實在途皆零才能停止Meter，不清空計數或放棄仍執行的工作；重入正常關閉無副作用。</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            if (values.Any(value => value != 0)) throw new InvalidOperationException("Hosting resources have not drained.");
            disposed = true;
            meter.Dispose();
        }
    }

    /// <summary>不保存操作資料的短命唯一歸還票據；只以原子交換拒絕重複釋放。</summary>
    private sealed class CounterLease(HostingMeters owner, int index) : IDisposable
    {
        private HostingMeters? current = owner;
        /// <summary>先斷開owner參照，第一次才扣回，避免關閉後由已完成Task保留整個Host。</summary>
        public void Dispose() => Interlocked.Exchange(ref current, null)?.Release(index);
    }
}

/// <summary>一致不可變監控快照；無部署、身分或業務資料，Host停止後四項必須回到零。</summary>
/// <param name="WebhookQueueDepth">提交中、buffer及執行中的保留槽數。</param>
/// <param name="DataverseLeasesActive">尚未完成歸還／淘汰的SDK租約。</param>
/// <param name="SchedulerJobsRunning">本次scope尚未釋放的執行工作。</param>
/// <param name="PushBatchesInflight">傳輸尚未完成及釋放的發送批次。</param>
public sealed record HostingMeterSnapshot(long WebhookQueueDepth, long DataverseLeasesActive,
    long SchedulerJobsRunning, long PushBatchesInflight);
