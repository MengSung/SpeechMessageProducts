using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using PowerPlatform.Dataverse.Client;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Keyed、bounded、可觀測的 client pool。
/// 每個 client 由本 pool 建立並在閒置逾時、故障或 pool 關閉時最終 Dispose；租約 Dispose
/// 只回報狀態並歸還 semaphore，故不會讓短命 request 釋放長命 pooled client。子池以完整
/// <see cref="DataverseConnectionKey"/> 分割，並在每次歸還前清除可辨識的呼叫者身分，防止跨
/// request、使用者、profile、tenant 或環境重用可變連線狀態。
/// </summary>
public sealed class BoundedClientPool : IBoundedClientPool
{
    private sealed class SubPool
    {
        internal readonly DataverseConnectionKey Key;
        internal readonly SemaphoreSlim Slots;
        internal readonly ConcurrentQueue<PooledClient> Idle = new();
        internal readonly List<PooledClient> All = new();
        internal readonly object Sync = new();

        /// <summary>
        /// 已保留名額但尚未完成建立的 client 數量，由 <see cref="Sync"/> 保護。
        /// 建線移到鎖外之後，這個計數是避免多執行緒同時為同一個缺口重複建線的唯一依據。
        /// </summary>
        internal int Pending;

        internal SubPool(DataverseConnectionKey key, int maxSize)
        {
            Key = key;
            Slots = new SemaphoreSlim(maxSize, maxSize);
        }
    }

    private sealed class ClientLease : IClientLease
    {
        private readonly BoundedClientPool _owner;
        private readonly SubPool _subPool;
        private readonly PooledClient _client;
        private readonly DataverseTrace _trace;
        private readonly string _leaseId;
        private readonly long _leaseStartedTimestamp;
        private readonly IDisposable _leaseScope;
        private int _disposed;

        internal ClientLease(
            BoundedClientPool owner,
            SubPool subPool,
            PooledClient client,
            DataverseTrace trace,
            string leaseId,
            long leaseStartedTimestamp,
            IDisposable leaseScope)
        {
            _owner = owner;
            _subPool = subPool;
            _client = client;
            _trace = trace;
            _leaseId = leaseId;
            _leaseStartedTimestamp = leaseStartedTimestamp;
            _leaseScope = leaseScope;
        }

        public IOrganizationService Service
        {
            get
            {
                if (Volatile.Read(ref _disposed) != 0)
                    throw new ObjectDisposedException(nameof(IClientLease));
                return _client.Service;
            }
        }

        public void MarkFaulted()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(IClientLease));
            _owner.MarkFaulted(_client);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            {
                _owner.Return(_subPool, _client, _trace, _leaseId, _leaseStartedTimestamp);
            }
            finally
            {
                // pool.return 必須在 lease 關聯還存在時輸出；之後立即還原 AsyncLocal，
                // 避免同一 request 後續作業誤把已歸還 client 的 leaseId 寫入 crm.op。
                _leaseScope?.Dispose();
            }
        }
    }

    private readonly ConcurrentDictionary<DataverseConnectionKey, SubPool> _subPools = new();
    private readonly Func<DataverseConnectionKey, CancellationToken, IOrganizationService> _clientFactory;
    private readonly Func<IOrganizationService, bool> _healthCheck;
    private readonly DataversePoolOptions _options;
    private readonly Action<IOrganizationService> _beforeCleanupDispose;
    private readonly Timer _cleanupTimer;
    private DataverseTrace _trace;
    private long _faulted;
    private long _acquireTimeouts;
    private long _created;
    private long _discarded;
    private long _totalAcquires;
    private long _totalReleases;
    private long _nextLeaseId;
    private int _waiting;
    private int _disposed;

    /// <summary>
    /// 建立 keyed pool。factory 是唯一的 client 建立點；healthCheck 只執行 WhoAmI 類健康驗證。
    /// Timer 只選取閒置 client，真正的網路或 Dispose I/O 一律在子池鎖外執行，避免阻塞租借；
    /// <paramref name="beforeCleanupDispose"/> 僅供受控協調／測試在銷毀前建立交錯點，例外會被
    /// 忽略，且不改變 pool 對 client 的唯一所有權與淘汰決策。
    /// </summary>
    public BoundedClientPool(
        Func<DataverseConnectionKey, CancellationToken, IOrganizationService> clientFactory,
        Func<IOrganizationService, bool> healthCheck,
        DataversePoolOptions options,
        Action<IOrganizationService> beforeCleanupDispose = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _healthCheck = healthCheck ?? throw new ArgumentNullException(nameof(healthCheck));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _beforeCleanupDispose = beforeCleanupDispose;
        _options.Validate();

        var interval = _options.IdleTimeout <= TimeSpan.FromSeconds(1)
            ? TimeSpan.FromMilliseconds(250)
            : TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(250).Ticks, _options.IdleTimeout.Ticks / 2));
        _cleanupTimer = new Timer(CleanupTimerCallback, null, interval, interval);
    }

    /// <summary>
    /// 依完整隔離鍵取得唯一 lease。此方法在 semaphore 容量內等待，並只把已成功轉為
    /// Leased、通過必要健康檢查的 client 交給呼叫端；超時、取消或健康失敗不會讓同一條
    /// client 同時交給其他 request，故障 client 會被淘汰而非重用。
    /// </summary>
    public IClientLease Acquire(DataverseConnectionKey key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var trace = GetTrace();
        var traceEnabled = trace?.Enabled == true;
        var subPool = _subPools.GetOrAdd(key, value => new SubPool(value, _options.MaxN));
        Interlocked.Increment(ref _waiting);
        var entered = false;
        var waitedMs = 0L;
        var waitStartedTimestamp = traceEnabled ? Stopwatch.GetTimestamp() : 0;
        try
        {
            if (!subPool.Slots.Wait(_options.AcquireTimeout, cancellationToken))
            {
                Interlocked.Increment(ref _acquireTimeouts);
                if (traceEnabled)
                {
                    trace.PoolAcquireWait(GetElapsedMilliseconds(waitStartedTimestamp));
                    trace.PoolAcquireTimeout();
                }
                throw new TimeoutException($"無法在 {_options.AcquireTimeout.TotalMilliseconds:0} 毫秒內取得 Dataverse client。");
            }
            entered = true;
            if (traceEnabled)
            {
                waitedMs = GetElapsedMilliseconds(waitStartedTimestamp);
                trace.PoolAcquireWait(waitedMs);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }

        // 失敗事件必須能指出是哪一段出問題；建線與健康檢查的處置方式完全不同。
        var phase = "ensureMin";
        try
        {
            EnsureMinimum(subPool, key, cancellationToken);
            phase = "lease";
            var now = DateTime.UtcNow;
            while (subPool.Idle.TryDequeue(out var candidate))
            {
                if (!candidate.TryLease(now))
                    continue;

                if (NeedsHealthCheck(candidate, now))
                {
                    phase = "health";
                    var healthy = false;
                    try { healthy = _healthCheck(candidate.Service); } catch { healthy = false; }
                    if (!healthy)
                    {
                        if (traceEnabled)
                            trace.PoolHealth(candidate.ClientId, result: false);
                        Interlocked.Increment(ref _faulted);
                        RemoveAndDispose(subPool, candidate, "faulted");
                        continue;
                    }
                    if (traceEnabled)
                        trace.PoolHealth(candidate.ClientId, result: true);
                    candidate.MarkValidated(now);
                }

                Interlocked.Increment(ref _totalAcquires);
                return CreateLease(subPool, candidate, key, hit: true, trace, traceEnabled);
            }

            // MinSize 可能因健康檢查淘汰而低於目前需求，此處補建一個新 client。
            phase = "create";
            var created = CreateClient(subPool, key, cancellationToken);
            created.TryLease(now);
            Interlocked.Increment(ref _totalAcquires);
            return CreateLease(subPool, created, key, hit: false, trace, traceEnabled);
        }
        catch (Exception ex)
        {
            // 沒有這一筆事件，建線失敗的 request 在稽核檔中只剩一筆 pool.acquire.wait，
            // 既無 hit 也無 miss，最慢的那些 request 因此無法被解釋。
            if (traceEnabled)
                trace.PoolAcquireFail(phase, waitedMs, GetElapsedMilliseconds(waitStartedTimestamp), DescribeErrorKind(ex));
            if (entered)
                subPool.Slots.Release();
            throw;
        }
    }

    /// <summary>
    /// 取得各子池彙總的 Idle、Leased、Faulted、等待與淘汰計數。只讀取受鎖保護的狀態，
    /// 不取得 lease、不做網路 I/O，也不保留任何 request 或使用者資料。
    /// </summary>
    public DataversePoolMetrics GetMetrics()
    {
        var idle = 0;
        var leased = 0;
        foreach (var subPool in _subPools.Values)
        {
            lock (subPool.Sync)
            {
                foreach (var client in subPool.All)
                {
                    if (client.State == PooledClientState.Idle) idle++;
                    else if (client.State == PooledClientState.Leased) leased++;
                }
            }
        }

        return new DataversePoolMetrics
        {
            Idle = idle,
            Leased = leased,
            Faulted = Interlocked.Read(ref _faulted),
            Waiting = Volatile.Read(ref _waiting),
            AcquireTimeouts = Interlocked.Read(ref _acquireTimeouts),
            Created = Interlocked.Read(ref _created),
            Discarded = Interlocked.Read(ref _discarded),
            TotalAcquires = Interlocked.Read(ref _totalAcquires),
            TotalReleases = Interlocked.Read(ref _totalReleases),
            SubPoolCount = _subPools.Count
        };
    }

    /// <summary>
    /// 立即執行一次閒置淘汰，供 timer、shutdown 前 drain 與可重現測試使用。每個子池在選取時
    /// 逐一遞減 idle 計數，絕不淘汰到 <see cref="DataversePoolOptions.MinSize"/> 以下；選取後若
    /// Acquire 已把 client 轉為 Leased，<see cref="PooledClient.DisposeUnderlying"/> 會拒絕立即銷毀
    /// 並標記它在歸還時淘汰，因此 cleanup 不會中斷正在使用中的 request，也不會讓已選中的
    /// 過期 client 再回到 Idle。
    /// </summary>
    public void CleanupIdleClients()
    {
        var now = DateTime.UtcNow;
        foreach (var subPool in _subPools.Values)
        {
            var trace = GetTrace();
            var traceEnabled = trace?.Enabled == true;
            var expired = new List<PooledClient>();
            var idleBefore = 0;
            lock (subPool.Sync)
            {
                var idleCount = subPool.All.Count(client => client.State == PooledClientState.Idle);
                if (traceEnabled)
                    idleBefore = idleCount;
                foreach (var client in subPool.All)
                {
                    if (idleCount <= _options.MinSize)
                        break;
                    if (!client.IsIdleExpired(now, _options.IdleTimeout))
                        continue;
                    expired.Add(client);
                    idleCount--;
                }
            }

            foreach (var client in expired)
            {
                try { _beforeCleanupDispose?.Invoke(client.Service); } catch { }
                RemoveAndDispose(subPool, client, "idle");
            }

            if (traceEnabled)
            {
                int idleAfter;
                lock (subPool.Sync)
                    idleAfter = subPool.All.Count(client => client.State == PooledClientState.Idle);
                trace.PoolCleanup(idleBefore, idleAfter, _options.MinSize);
            }
        }
    }

    /// <summary>
    /// 停止 cleanup timer 並釋放由本 pool 建立的 idle／faulted client。已租借的 client 由
    /// <see cref="PooledClient.DisposeUnderlying"/> 拒絕提前銷毀，避免短命 pool shutdown 流程
    /// 破壞仍持有 lease 的呼叫端；所有可安全銷毀的資源均由此唯一所有者決定性處理。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _cleanupTimer.Dispose();

        foreach (var subPool in _subPools.Values)
        {
            List<PooledClient> clients;
            lock (subPool.Sync)
            {
                clients = subPool.All.ToList();
                subPool.All.Clear();
                while (subPool.Idle.TryDequeue(out _)) { }
            }
            foreach (var client in clients)
            {
                TraceDisposeAttempt(client, "shutdown");
                if (client.DisposeUnderlying())
                    Interlocked.Increment(ref _discarded);
            }
            subPool.Slots.Dispose();
        }
        _subPools.Clear();
    }

    /// <summary>
    /// 補足子池的 MinSize。採「鎖內保留名額 → 鎖外建立 → 鎖內提交」三段式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>為什麼不能在鎖內建線</b>：建立 client 是完整的網路驗證握手，而 <see cref="SubPool.Sync"/>
    /// 同時被 Acquire、cleanup 與 metrics 共用。一旦在鎖內做這件事，CRM 變慢或失敗時，N 個併發
    /// request 會被串列化成 N 倍延遲；實測曾出現三個同時到達的 request 以約 21 秒等差依序失敗
    /// （21.9 / 41.6 / 61.3 秒），且 cleanup timer callback 在解鎖瞬間一次釋放 30 筆。
    /// </para>
    /// <para>
    /// <b><see cref="SubPool.Pending"/> 的作用</b>：把建線移到鎖外之後，若只看 All 的數量，多個執行緒
    /// 會各自算出同一個缺口並重複建線。先在鎖內保留名額，其他執行緒便會看到缺口已被認領。
    /// </para>
    /// <para>
    /// <b>行為取捨</b>：其他執行緒不再被阻擋，但在保留期間它們會走 overflow 路徑自行建線，因此瞬間
    /// 連線數可能高於 MinSize。此數量仍受 semaphore 的 MaxN 上限約束，並由閒置回收降回 MinSize；
    /// 以少量額外連線換取不再被單一慢速握手鎖死整個子池。
    /// </para>
    /// <para>
    /// 提交階段會重新檢查 pool 是否已關閉：若在鎖外建立期間 <see cref="Dispose"/> 已執行，新建的
    /// client 不得掛進沒有擁有者的子池，必須就地釋放，否則會成為無人回收的連線。
    /// </para>
    /// </remarks>
    private void EnsureMinimum(SubPool subPool, DataverseConnectionKey key, CancellationToken cancellationToken)
    {
        int reserved;
        lock (subPool.Sync)
        {
            var alive = subPool.All.Count(client => client.State != PooledClientState.Disposed);
            reserved = _options.MinSize - alive - subPool.Pending;
            if (reserved <= 0)
                return;
            subPool.Pending += reserved;
        }

        var created = new List<PooledClient>(reserved);
        List<PooledClient> orphaned = null;
        try
        {
            for (var index = 0; index < reserved; index++)
                created.Add(CreateClientCore(key, cancellationToken, "ensureMin"));
        }
        finally
        {
            lock (subPool.Sync)
            {
                subPool.Pending -= reserved;

                if (Volatile.Read(ref _disposed) != 0)
                {
                    // pool 已於鎖外建立期間關閉；這些 client 沒有擁有者，必須就地釋放。
                    orphaned = created;
                }
                else
                {
                    // 即使中途失敗，已成功建立的 client 仍要入池，不浪費已付出的握手成本。
                    foreach (var client in created)
                    {
                        subPool.All.Add(client);
                        subPool.Idle.Enqueue(client);
                    }
                }
            }

            if (orphaned != null)
            {
                foreach (var client in orphaned)
                {
                    TraceDisposeAttempt(client, "shutdown");
                    if (client.DisposeUnderlying())
                        Interlocked.Increment(ref _discarded);
                }
            }
        }
    }

    private PooledClient CreateClient(SubPool subPool, DataverseConnectionKey key, CancellationToken cancellationToken)
    {
        var client = CreateClientCore(key, cancellationToken, "overflow");
        lock (subPool.Sync)
            subPool.All.Add(client);
        return client;
    }

    /// <summary>
    /// 唯一的 client 建立點，並在此量測建線耗時。
    /// </summary>
    /// <remarks>
    /// 本方法目前由 <see cref="EnsureMinimum"/> 在子池鎖內呼叫，因此 <c>pool.create.end</c> 的 ms
    /// 同時也是其他 request 被這把鎖阻擋的時間下限；把它記下來，才有辦法量化建線序列化的實際成本。
    /// </remarks>
    private PooledClient CreateClientCore(DataverseConnectionKey key, CancellationToken cancellationToken, string reason)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var trace = GetTrace();
        var traceEnabled = trace?.Enabled == true;
        var startedTimestamp = traceEnabled ? Stopwatch.GetTimestamp() : 0;
        if (traceEnabled)
            trace.PoolCreateBegin(FormatPoolKey(key), reason);

        IOrganizationService service;
        try
        {
            service = _clientFactory(key, cancellationToken);
            if (service == null)
                throw new InvalidOperationException("Dataverse client factory 不得回傳 null。");
        }
        catch (Exception ex)
        {
            if (traceEnabled)
                trace.PoolCreateEnd(string.Empty, reason, GetElapsedMilliseconds(startedTimestamp), ok: false, DescribeErrorKind(ex));
            throw;
        }

        Interlocked.Increment(ref _created);
        var client = new PooledClient(service);
        if (traceEnabled)
            trace.PoolCreateEnd(client.ClientId, reason, GetElapsedMilliseconds(startedTimestamp), ok: true, string.Empty);
        return client;
    }

    /// <summary>
    /// 取出最內層例外的型別名稱作為診斷用的錯誤種類。
    /// </summary>
    /// <remarks>
    /// 只輸出型別名稱，絕不輸出 <see cref="Exception.Message"/>：連線失敗的訊息通常內嵌組織 URL、
    /// 服務帳號或 CRM 回應內容，寫入稽核檔會讓診斷功能本身變成資料外洩管道。取最內層是因為外層
    /// 常被包成一般 <see cref="Exception"/>，真正的原因（例如 WebException）只在鏈的末端。
    /// </remarks>
    private static string DescribeErrorKind(Exception exception)
    {
        var current = exception;
        while (current?.InnerException != null)
            current = current.InnerException;
        return current?.GetType().Name ?? string.Empty;
    }

    private bool NeedsHealthCheck(PooledClient client, DateTime now)
        => now - client.LastValidatedUtc >= _options.HealthInterval;

    private void MarkFaulted(PooledClient client)
    {
        if (client.MarkFaulted())
            Interlocked.Increment(ref _faulted);
    }

    private ClientLease CreateLease(
        SubPool subPool,
        PooledClient client,
        DataverseConnectionKey key,
        bool hit,
        DataverseTrace trace,
        bool traceEnabled)
    {
        if (!traceEnabled)
            return new ClientLease(this, subPool, client, null, null, 0, null);

        // 只有開啟觀測才建立 leaseId、格式化 poolKey 與建立 AsyncLocal scope；這些值不參與
        // pool 決策，亦不會改變 Run F 的狀態機、semaphore 或底層連線生命週期。
        var leaseId = "l-" + Interlocked.Increment(ref _nextLeaseId);
        trace.PoolAcquire(leaseId, client.ClientId, FormatPoolKey(key), hit);
        return new ClientLease(
            this,
            subPool,
            client,
            trace,
            leaseId,
            Stopwatch.GetTimestamp(),
            trace.PushLease(leaseId));
    }

    private void Return(
        SubPool subPool,
        PooledClient client,
        DataverseTrace trace,
        string leaseId,
        long leaseStartedTimestamp)
    {
        var traceEnabled = trace?.Enabled == true && !string.IsNullOrEmpty(leaseId);
        var callerIdAtReturn = traceEnabled ? ReadCallerIdAtReturn(client) : string.Empty;
        var returnState = "faulted";
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                client.MarkFaulted();
                RemoveAndDispose(subPool, client, "shutdown");
                return;
            }

            if (client.State == PooledClientState.Faulted)
            {
                RemoveAndDispose(subPool, client, "faulted");
                return;
            }

            if (!client.ReturnHealthy(DateTime.UtcNow))
            {
                if (client.State == PooledClientState.Faulted)
                    Interlocked.Increment(ref _faulted);
                RemoveAndDispose(subPool, client, "faulted");
                return;
            }
            subPool.Idle.Enqueue(client);
            Interlocked.Increment(ref _totalReleases);
            returnState = "healthy";
        }
        finally
        {
            if (traceEnabled)
                trace.PoolReturn(
                    leaseId,
                    client.ClientId,
                    returnState,
                    callerIdAtReturn,
                    GetElapsedMilliseconds(leaseStartedTimestamp));
            if (Volatile.Read(ref _disposed) == 0)
                subPool.Slots.Release();
        }
    }

    private void RemoveAndDispose(SubPool subPool, PooledClient client, string reason)
    {
        lock (subPool.Sync)
            subPool.All.Remove(client);
        TraceDisposeAttempt(client, reason);
        if (client.DisposeUnderlying())
            Interlocked.Increment(ref _discarded);
    }

    private static long GetElapsedMilliseconds(long startedTimestamp)
    {
        if (startedTimestamp == 0)
            return 0;
        return Math.Max(0, (long)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds);
    }

    private static string FormatPoolKey(DataverseConnectionKey key)
    {
        var host = Uri.TryCreate(key.OrganizationUrl, UriKind.Absolute, out var organizationUri)
            ? organizationUri.Host
            : key.OrganizationUrl;
        return $"{key.Product}|{key.Environment}|{host}|{key.EffectiveIdentity}";
    }

    private static string ReadCallerIdAtReturn(PooledClient client)
    {
        try
        {
            // Run F 的清除發生在 ReturnHealthy 內；此讀值必須緊鄰該呼叫之前，才能讓稽核
            // 判定歸還時是否確實帶有 impersonation state。僅記錄 GUID，沒有 entity 或使用者欄位。
            return client.Service is OnPremiseClient onPremiseClient && onPremiseClient.CallerId != Guid.Empty
                ? onPremiseClient.CallerId.ToString("D")
                : string.Empty;
        }
        catch
        {
            // Trace 讀取失敗不得影響歸還、CallerId 清除或 fault eviction 的既有決策。
            return string.Empty;
        }
    }

    private void TraceDisposeAttempt(PooledClient client, string reason)
    {
        var trace = GetTrace();
        if (trace?.Enabled == true)
        {
            // 狀態緊鄰 DisposeUnderlying 取得，特別保留 Leased + idle 的 Run F 延後淘汰證據。
            trace.PoolDispose(client.ClientId, client.State.ToString(), reason);
        }
    }

    private DataverseTrace GetTrace()
    {
        var captured = Volatile.Read(ref _trace);
        if (captured != null)
            return captured;

        var current = DataverseTrace.Current;
        if (current == null)
            return null;

        // Pool 一經 request trace 綁定便只使用該實例；背景 cleanup 也因此不會因另一個產品
        // Host 在同一 process 開始 request 而把 A 的 pool 事件寫入 B 的診斷檔。
        Interlocked.CompareExchange(ref _trace, current, null);
        return Volatile.Read(ref _trace);
    }

    private void CleanupTimerCallback(object state)
    {
        try { CleanupIdleClients(); } catch { /* timer 不得終止應用程式。 */ }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(BoundedClientPool));
    }
}
