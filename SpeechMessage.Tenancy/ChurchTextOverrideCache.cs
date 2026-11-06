using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using SpeechMessage.Localization;

namespace SpeechMessage.Tenancy;

/// <summary>
/// Host 擁有的完整教會覆寫快取。只接受已授權 TextContext，命中不能代替每次伺服器授權；
/// 完整組織／產品／教會隔離，快照只含文字，不含使用者、request、token 或 D365 連線。
/// </summary>
/// <remarks>
/// 先 await LoadAsync 再以 GetSnapshot 供同步純記憶體 catalog 消費，冷未載入明確拒絕。
/// 十分鐘到期重讀但失敗保留舊完整快照；閒置三十分移除。改字輪詢由後續 Hosting 擁有，本類沒有輪詢服務。
/// Owner 必須 await DisposeAsync：停止入場、取消每個 flight、排空來源，再釋放 MemoryCache。
/// 來源須有 transport timeout 並遵守取消；同步 SDK 已入場的 I/O 仍須排空，不能提早歸還連線。
/// </remarks>
public sealed class ChurchTextOverrideCache : IChurchTextOverrideSnapshotProvider, IAsyncDisposable
{
    /// <summary>只保護短記憶體狀態轉換；來源 I/O、通知與取消 callback 一律不在鎖內執行。</summary>
    private readonly object gate = new();
    /// <summary>本 owner 獨佔的有界快取；每 scope size=1，沒有共用其他產品 MemoryCache 的容量。</summary>
    private readonly MemoryCache cache;
    /// <summary>最多六十四個正在執行的 flight；每個 Lazy 只啟動一次，完成後移除，不保留失敗 Task。</summary>
    private readonly ConcurrentDictionary<ScopeKey, Flight> flights = new();
    /// <summary>Host 擁有且無 request 狀態的來源；本 owner 不 Dispose 外部來源。</summary>
    private readonly IChurchTextOverrideSource source;
    /// <summary>固定系統資源與驗證政策，可共用但不保存呼叫者候選。</summary>
    private readonly ChurchTextOverrideValidator validator;
    /// <summary>Host 注入共用 ExceptionDiagnostics 的薄轉接；必須傳真實 Exception，禁止捕捉 request 或另建 LINE client。</summary>
    private readonly Action<Exception, string> reportFailure;
    /// <summary>Host 借入時鐘，僅控制期限，不保存租戶資料；不由本類處置。</summary>
    private readonly TimeProvider time;
    /// <summary>每 scope 一個位置，最多二千；允許測試或小型部署選更低上限，不可提高正式門檻。</summary>
    private readonly int maximumScopes;
    /// <summary>來源同時入場上限，沒有額外無界等待佇列。</summary>
    private readonly int maximumConcurrentLoads;
    /// <summary>每 flight owner deadline，最多三分鐘，逾時仍要排空已入場 I/O。</summary>
    private readonly TimeSpan loadTimeout;
    /// <summary>停止旗標由 gate 保護，阻止 Dispose 後新增或晚發布。</summary>
    private bool stopping;
    /// <summary>唯一清理 Task，所有重複 DisposeAsync 共用，不重複處置 owner 資源。</summary>
    private Task? disposal;

    /// <summary>建立專屬 owner；不接管 source、validator、notifier 或時鐘的外部生命週期。</summary>
    /// <param name="source">Host 提供完整範圍來源，須設定有界 transport timeout。</param>
    /// <param name="validator">固定系統鍵、schema、參數及長度政策。</param>
    /// <param name="reportFailure">連接共用告警入口的必要轉接，不得提供空實作作為正式 Host 配置。</param>
    /// <param name="timeProvider">Host 借用時鐘；缺省為 TimeProvider.System。</param>
    /// <param name="maximumScopes">1～2000，只允許降低保留容量。</param>
    /// <param name="maximumConcurrentLoads">1～64，只允許降低來源並行入場容量。</param>
    /// <param name="loadTimeout">0～3 分鐘之間的正 deadline，缺省三十秒。</param>
    public ChurchTextOverrideCache(IChurchTextOverrideSource source, ChurchTextOverrideValidator validator,
        Action<Exception, string> reportFailure, TimeProvider? timeProvider = null, int maximumScopes = 2000,
        int maximumConcurrentLoads = 64, TimeSpan? loadTimeout = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        if (maximumScopes is < 1 or > 2000) throw new ArgumentOutOfRangeException(nameof(maximumScopes));
        if (maximumConcurrentLoads is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maximumConcurrentLoads));
        this.loadTimeout = loadTimeout ?? TimeSpan.FromSeconds(30);
        if (this.loadTimeout <= TimeSpan.Zero || this.loadTimeout > TimeSpan.FromMinutes(3))
            throw new ArgumentOutOfRangeException(nameof(loadTimeout));
        this.maximumScopes = maximumScopes;
        this.maximumConcurrentLoads = maximumConcurrentLoads;
        time = timeProvider ?? TimeProvider.System;
        cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = maximumScopes, Clock = new CacheClock(time) });
    }

    /// <summary>只暴露容量計數，不提供 scope、文字、身分或連線診斷；Dispose 完成及停用排空後須回基線。</summary>
    public int CachedScopeCount { get { lock (gate) return stopping ? 0 : cache.Count; } }
    /// <summary>只計正在擁有來源的 flight，不計 caller 等待者；flight 完成／故障後必須歸零。</summary>
    public int ActiveLoadCount { get { lock (gate) return flights.Count; } }

    /// <summary>
    /// 非同步準備完整快照；同 scope 用 ConcurrentDictionary／Lazy Task 單一載入，caller's WaitAsync 取消不傳入共享來源。
    /// stamp 只前進；較舊讀取者必須收斂至新世代，不可回寫舊資料。不同 culture 共用同 scope 的原始覆寫表。
    /// </summary>
    /// <param name="context">每次由 Host 先授權的完整 scope，不可接受未驗證路由當權限。</param>
    /// <param name="textChangedOnUtc">伺服器讀到的 new_textchangedon UTC 值；不得以 local／unspecified 代替。</param>
    /// <param name="cancellationToken">只取消這位 caller 的等待，WaitAsync 自行釋放 registration。</param>
    /// <returns>新完整快照，或 reload 失敗時同 scope 的舊完整快照；冷失敗拋原例外。</returns>
    public async Task<IChurchTextOverrideSnapshot> LoadAsync(TextContext context, DateTime textChangedOnUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequireUtc(textChangedOnUtc);
        cancellationToken.ThrowIfCancellationRequested();
        var key = ScopeKey.From(context);
        Entry entry;
        lock (gate)
        {
            ThrowIfStopping();
            entry = FindEntry(key) ?? AddEntry(key, textChangedOnUtc);
            Advance(entry, textChangedOnUtc);
        }
        while (true)
        {
            Flight flight;
            lock (gate)
            {
                ThrowIfStopping();
                if (!ReferenceEquals(entry, FindEntry(key)))
                    throw new OperationCanceledException("Override scope was removed during loading.");
                if (entry.Snapshot is { } current && entry.LoadedStamp == entry.ExpectedStamp
                    && time.GetUtcNow() - entry.LoadedAt < TimeSpan.FromMinutes(10)) return current;
                if (!flights.TryGetValue(key, out flight!))
                {
                    if (flights.Count >= maximumConcurrentLoads)
                        throw new InvalidOperationException("Override source admission capacity is exhausted.");
                    flight = new Flight(key, entry, context, loadTimeout, time, this);
                    if (!flights.TryAdd(key, flight)) throw new InvalidOperationException("Override flight ownership conflict.");
                }
            }
            var result = await flight.GetTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                ThrowIfStopping();
                if (!ReferenceEquals(entry, FindEntry(key)))
                    throw new OperationCanceledException("Override scope was removed during loading.");
            }
            if (result.Error is { } failure) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            if (result.Snapshot is { } snapshot) return snapshot;
            // 來源讀取中發生 invalidate：只可重試同 entry 的最新世代。停用／淘汰後不能暗中重新啟用。
        }
    }

    /// <summary>只讀已載入完整快照，不做 I/O 或啟動背景工作；尚未準備／閒置淘汰／停用時明確拒絕。</summary>
    /// <param name="context">Host 每次重新驗證的完整範圍。</param>
    /// <returns>不可變快照；語系解析交給純 TextCatalog，不能由文字語系改變教會。</returns>
    public IChurchTextOverrideSnapshot GetSnapshot(TextContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (gate)
        {
            ThrowIfStopping();
            return FindEntry(ScopeKey.From(context))?.Snapshot
                ?? throw new InvalidOperationException("Override scope has not been loaded.");
        }
    }

    /// <summary>只前進指定完整 scope 的改字版本，不發 I/O；舊正確快照留到下一次完整候選成功。</summary>
    /// <param name="context">已授權且仍啟用的 scope。</param>
    /// <param name="textChangedOnUtc">伺服器 metadata UTC stamp。</param>
    /// <returns>只有現有 entry 的版本實際前進才為真；較舊／相同 stamp 不回退。</returns>
    public bool Invalidate(TextContext context, DateTime textChangedOnUtc)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequireUtc(textChangedOnUtc);
        lock (gate)
        {
            ThrowIfStopping();
            return FindEntry(ScopeKey.From(context)) is { } entry && Advance(entry, textChangedOnUtc);
        }
    }

    /// <summary>Host 確認教會停用後移除完整 scope 並停止其 flight；不保留無界 tombstone，重新啟用須先重新授權再 Load。</summary>
    /// <param name="context">由伺服器確認應移除的完整 scope。</param>
    public void Disable(TextContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Flight? flight;
        lock (gate)
        {
            ThrowIfStopping();
            var key = ScopeKey.From(context);
            cache.Remove(key);
            flights.TryGetValue(key, out flight);
            if (flight is not null) flight.Stopped = true;
        }
        if (flight?.Cancel() is { } failure) ReportFailure(failure);
    }

    /// <summary>唯一 Host 清理入口：先停止入場，再取消／排空所有 owner 工作，最後 Dispose MemoryCache；可重入且不處置借入服務。</summary>
    /// <returns>只有全部來源歸還且資源已清理才完成。</returns>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null) return new ValueTask(disposal);
            stopping = true;
            var pending = flights.Values.ToArray();
            foreach (var flight in pending) flight.Stopped = true;
            disposal = DisposeCoreAsync(pending);
            return new ValueTask(disposal);
        }
    }

    /// <summary>在 Lazy 的唯一執行中讀來源／驗候選；所有真正故障先交同一共用 notifier，結果容器避免無人等待時的 unobserved fault。</summary>
    private async Task<LoadResult> ReadAsync(Flight flight)
    {
        // Lazy.Value 可同步啟動；強制離開 caller 堆疊且不擷取其 SynchronizationContext，
        // 避免 UI／主機排程器延長 scope 生命週期；仍不在全域短鎖內執行同步 SDK。
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            var rows = await source.ReadAsync(flight.Context, flight.Cancellation.Token).ConfigureAwait(false);
            flight.Cancellation.Token.ThrowIfCancellationRequested();
            var snapshot = validator.Validate(flight.Context, rows);
            lock (gate)
            {
                if (!IsCurrent(flight)) return default;
                flight.Entry.Snapshot = snapshot;
                flight.Entry.LoadedAt = time.GetUtcNow();
                flight.Entry.LoadedStamp = flight.Stamp;
                return new LoadResult(snapshot, null);
            }
        }
        catch (OperationCanceledException) when (flight.Stopped)
        {
            // Disable／Dispose 是 owner 明確停止，不是內部 deadline。停止後也不發布既有候選。
            return default;
        }
        catch (Exception failure)
        {
            // 即使 caller 早已取消等待，真正來源故障仍由 owner 觀測；不以內部 timeout token 當正常 caller 取消。
            ReportFailure(failure);
            lock (gate)
            {
                if (!IsCurrent(flight)) return default;
                return new LoadResult(flight.Entry.Snapshot, flight.Entry.Snapshot is null ? failure : null);
            }
        }
        finally
        {
            await flight.DisposeAsync().ConfigureAwait(false);
            lock (gate)
                if (flights.TryGetValue(flight.Key, out var current) && ReferenceEquals(current, flight))
                    flights.TryRemove(flight.Key, out _);
        }
    }

    /// <summary>清理工作不在 gate 內執行取消 callback 或 await；ReadAsync 已觀測每個來源失敗，這裡只負責完整 drain。</summary>
    private async Task DisposeCoreAsync(Flight[] pending)
    {
        // 清理同樣由 owner 排程，不把 flight／snapshot 留在 caller 同步內容的工作槽。
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        foreach (var flight in pending)
            if (flight.Cancel() is { } failure) ReportFailure(failure);
        await Task.WhenAll(pending.Select(flight => flight.GetTask())).ConfigureAwait(false);
        lock (gate) { cache.Clear(); cache.Dispose(); }
    }

    /// <summary>gate 內核對相同 entry 與世代，淘汰／停用／後進版本皆不能讓舊 flight 回寫。</summary>
    private bool IsCurrent(Flight flight) => !stopping && !flight.Stopped
        && ReferenceEquals(FindEntry(flight.Key), flight.Entry) && flight.Entry.Generation == flight.Generation;

    /// <summary>每次取 entry 同時由 MemoryCache 檢查三十分閒置期限；只更新 LRU 時間，不延長十分快速重讀期限。</summary>
    private Entry? FindEntry(ScopeKey key)
    {
        if (!cache.TryGetValue(key, out Entry? entry) || entry is null) return null;
        entry.LastAccess = time.GetUtcNow();
        return entry;
    }

    /// <summary>只在冷 miss 建立控制殼；容量滿時最多掃二千項選最舊非 flight，命中路徑無全表掃描且不淘汰正在入場的 scope。</summary>
    private Entry AddEntry(ScopeKey key, DateTime stamp)
    {
        if (cache.Count >= maximumScopes)
        {
            ScopeKey? victim = null;
            var oldest = DateTimeOffset.MaxValue;
            foreach (var raw in cache.Keys)
            {
                if (raw is not ScopeKey candidate || flights.ContainsKey(candidate)) continue;
                if (!cache.TryGetValue(candidate, out Entry? existing) || existing is null) continue;
                if (existing.LastAccess < oldest) { oldest = existing.LastAccess; victim = candidate; }
            }
            if (victim is { } selected) cache.Remove(selected);
            else if (cache.Count >= maximumScopes) throw new InvalidOperationException("Override cache admission capacity is exhausted.");
        }
        var entry = new Entry(stamp, time.GetUtcNow());
        cache.Set(key, entry, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromMinutes(30) });
        if (!cache.TryGetValue(key, out Entry? actual) || !ReferenceEquals(actual, entry))
            throw new InvalidOperationException("Override cache rejected the bounded entry.");
        return entry;
    }

    /// <summary>gate 內只前進版本及 generation，不改成功時間或快照；失敗重試仍讀相同預期版本。</summary>
    private static bool Advance(Entry entry, DateTime stamp)
    {
        if (stamp <= entry.ExpectedStamp) return false;
        entry.ExpectedStamp = stamp;
        entry.Generation = checked(entry.Generation + 1);
        return true;
    }

    /// <summary>版本一定採明確 UTC，不能把機器時區當作教會資料版本。</summary>
    private static void RequireUtc(DateTime stamp)
    {
        if (stamp.Kind != DateTimeKind.Utc) throw new ArgumentException("Override version must be explicitly UTC.", nameof(stamp));
    }
    /// <summary>gate 內拒絕清理中或已清理 owner，不容許重新載入復活。</summary>
    private void ThrowIfStopping() { ObjectDisposedException.ThrowIf(stopping, this); }

    /// <summary>通知失敗不得覆蓋功能原例外或阻斷清理；只送固定 stderr，不遞迴通知，也不記原本文。</summary>
    /// <param name="failure">真正功能／取消 callback 的原 Exception。</param>
    private void ReportFailure(Exception failure)
    {
        try { reportFailure(failure, "ChurchTextOverrideCache.Load"); }
        catch (Exception)
        {
            // stderr 也可能由已關閉主機 writer 拒絕；原故障仍留在 LoadResult，不讓監控管道阻止 drain。
            try { Console.Error.WriteLine("Override failure notification failed."); }
            catch (Exception) { }
        }
    }

    /// <summary>完整授權邊界的不可變鍵；語言及格式不是教會資料權限，不納入覆寫原文表的識別。</summary>
    /// <param name="Organization">已驗證組織。</param>
    /// <param name="Product">已驗證產品。</param>
    /// <param name="Church">正式教會 ID。</param>
    private readonly record struct ScopeKey(string Organization, string Product, Guid Church)
    {
        /// <summary>只複製純值，沒有 context、request 或 credential 的反向參照。</summary>
        internal static ScopeKey From(TextContext context) => new(context.Organization, context.Product, context.ChurchId);
    }

    /// <summary>gate 保護的控制殼；唯一可對外讀取的是完整不可變 Snapshot，其他欄位不可發布。</summary>
    private sealed class Entry(DateTime stamp, DateTimeOffset now)
    {
        /// <summary>伺服器已知最新版本；不因失敗重試回退。</summary>
        internal DateTime ExpectedStamp = stamp;
        /// <summary>只有成功完整發布才更新的版本。</summary>
        internal DateTime LoadedStamp;
        /// <summary>失效 race 的單調世代。</summary>
        internal long Generation;
        /// <summary>成功載入時間，不被一般命中重設。</summary>
        internal DateTimeOffset LoadedAt;
        /// <summary>LRU 冷 miss 淘汰用途，不含使用者。</summary>
        internal DateTimeOffset LastAccess = now;
        /// <summary>原子發布的純值快照；null 代表冷未完成，不表示成功空結果。</summary>
        internal ChurchTextOverrideSnapshot? Snapshot;
    }

    /// <summary>一次有界來源 owner；Lazy closure 只活至工作移除，CTS timer 在 ReadAsync finally Dispose。</summary>
    private sealed class Flight : IAsyncDisposable
    {
        /// <summary>完整不可變 scope，與 dictionary 的入場鍵相同。</summary>
        internal readonly ScopeKey Key;
        /// <summary>本次入場控制殼，只有相同殼與 generation 才能發布。</summary>
        internal readonly Entry Entry;
        /// <summary>已授權純值 scope，不含 HttpContext 或使用者引用。</summary>
        internal readonly TextContext Context;
        /// <summary>入場時的預期 UTC 版本。</summary>
        internal readonly DateTime Stamp;
        /// <summary>入場時的唯一世代，不能被後來 invalidate 改寫。</summary>
        internal readonly long Generation;
        /// <summary>本 flight 唯一擁有的 deadline／取消資源，排空後確定 Dispose。</summary>
        internal readonly CancellationTokenSource Cancellation;
        /// <summary>同 scope 所有等待者共用的唯一工作，來源故障已觀測為純結果。</summary>
        private readonly Lazy<Task<LoadResult>> work;
        /// <summary>由本 flight 擁有的單次 deadline；用安全 callback 觀測 Cancel 的 AggregateException，清理時 await 排空 callback。</summary>
        private readonly ITimer deadline;
        /// <summary>僅指向 Host 快取 owner，不捕捉 request，timer DisposeAsync 後不再使用。</summary>
        private readonly ChurchTextOverrideCache owner;
        /// <summary>由 gate 設定且跨取消 catch 讀取的 owner 停止理由；不是 deadline，volatile 保證可見性。</summary>
        internal volatile bool Stopped;
        /// <summary>記下入場世代及 deadline；只在通過 bounded admission 後建立，沒有旁支無界工作。</summary>
        internal Flight(ScopeKey key, Entry entry, TextContext context, TimeSpan timeout, TimeProvider time, ChurchTextOverrideCache owner)
        {
            Key = key; Entry = entry; Context = context; Stamp = entry.ExpectedStamp; Generation = entry.Generation;
            this.owner = owner;
            Cancellation = new CancellationTokenSource();
            work = new Lazy<Task<LoadResult>>(() => owner.ReadAsync(this), LazyThreadSafetyMode.ExecutionAndPublication);
            try
            {
                // System timer 本身會捕捉 ExecutionContext；絕不可讓第一個 request 的 AsyncLocal 活到 owner deadline。
                if (ExecutionContext.IsFlowSuppressed()) deadline = CreateDeadline(time, timeout);
                else
                {
                    using var flow = ExecutionContext.SuppressFlow();
                    deadline = CreateDeadline(time, timeout);
                }
            }
            catch { Cancellation.Dispose(); throw; }
        }
        /// <summary>同 scope 的 Lazy 啟動也須抑制 caller ExecutionContext；只在同步取得 Task 期間抑制，不跨 await。</summary>
        internal Task<LoadResult> GetTask()
        {
            if (ExecutionContext.IsFlowSuppressed()) return work.Value;
            using var flow = ExecutionContext.SuppressFlow();
            return work.Value;
        }
        /// <summary>建立單次 deadline；所有真正 callback 故障送共用入口，不讓 ThreadPool 未處理例外終止程序。</summary>
        private ITimer CreateDeadline(TimeProvider time, TimeSpan timeout) => time.CreateTimer(static state =>
        {
            var flight = (Flight)state!;
            if (flight.Cancel() is { } failure) flight.owner.ReportFailure(failure);
        }, this, timeout, Timeout.InfiniteTimeSpan);
        /// <summary>取消 callback 在 gate 外觸發；工作已完成並 Dispose CTS 的競態是正常收尾，不重複告警。</summary>
        internal AggregateException? Cancel()
        {
            try { Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (AggregateException failure) { return failure; }
            return null;
        }
        /// <summary>只由 ReadAsync finally 呼叫，先排空 timer callback 才釋放 CTS；不處置借入來源，Host 仍須最後 drain 本工作。</summary>
        public async ValueTask DisposeAsync()
        {
            await deadline.DisposeAsync().ConfigureAwait(false);
            Cancellation.Dispose();
        }
    }

    /// <summary>不會形成無人觀測 fault 的純結果；兩者 null 表示世代已失效，caller 必須重新核 entry 或停止。</summary>
    /// <param name="Snapshot">已完成或已保留的正確快照。</param>
    /// <param name="Error">冷失敗的原始例外，只交這次 caller，不加入 cache。</param>
    private readonly record struct LoadResult(IChurchTextOverrideSnapshot? Snapshot, Exception? Error);

    /// <summary>MemoryCache 與 reload 共用同一 TimeProvider；只借時鐘，不建立額外 timer 或保存 request。</summary>
    private sealed class CacheClock(TimeProvider time) : ISystemClock
    {
        /// <summary>取明確 UTC，供 MemoryCache 判斷滑動到期。</summary>
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
