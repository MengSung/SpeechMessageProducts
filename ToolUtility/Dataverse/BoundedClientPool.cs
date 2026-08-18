using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;

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
        private int _disposed;

        internal ClientLease(BoundedClientPool owner, SubPool subPool, PooledClient client)
        {
            _owner = owner;
            _subPool = subPool;
            _client = client;
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
            _owner.Return(_subPool, _client);
        }
    }

    private readonly ConcurrentDictionary<DataverseConnectionKey, SubPool> _subPools = new();
    private readonly Func<DataverseConnectionKey, CancellationToken, IOrganizationService> _clientFactory;
    private readonly Func<IOrganizationService, bool> _healthCheck;
    private readonly DataversePoolOptions _options;
    private readonly Action<IOrganizationService> _beforeCleanupDispose;
    private readonly Timer _cleanupTimer;
    private long _faulted;
    private long _acquireTimeouts;
    private long _created;
    private long _discarded;
    private long _totalAcquires;
    private long _totalReleases;
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
        var subPool = _subPools.GetOrAdd(key, value => new SubPool(value, _options.MaxN));
        Interlocked.Increment(ref _waiting);
        var entered = false;
        try
        {
            if (!subPool.Slots.Wait(_options.AcquireTimeout, cancellationToken))
            {
                Interlocked.Increment(ref _acquireTimeouts);
                throw new TimeoutException($"無法在 {_options.AcquireTimeout.TotalMilliseconds:0} 毫秒內取得 Dataverse client。");
            }
            entered = true;
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }

        try
        {
            EnsureMinimum(subPool, key, cancellationToken);
            var now = DateTime.UtcNow;
            while (subPool.Idle.TryDequeue(out var candidate))
            {
                if (!candidate.TryLease(now))
                    continue;

                if (NeedsHealthCheck(candidate, now))
                {
                    var healthy = false;
                    try { healthy = _healthCheck(candidate.Service); } catch { healthy = false; }
                    if (!healthy)
                    {
                        Interlocked.Increment(ref _faulted);
                        RemoveAndDispose(subPool, candidate);
                        continue;
                    }
                    candidate.MarkValidated(now);
                }

                Interlocked.Increment(ref _totalAcquires);
                return new ClientLease(this, subPool, candidate);
            }

            // MinSize 可能因健康檢查淘汰而低於目前需求，此處補建一個新 client。
            var created = CreateClient(subPool, key, cancellationToken);
            created.TryLease(now);
            Interlocked.Increment(ref _totalAcquires);
            return new ClientLease(this, subPool, created);
        }
        catch
        {
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
            var expired = new List<PooledClient>();
            lock (subPool.Sync)
            {
                var idleCount = subPool.All.Count(client => client.State == PooledClientState.Idle);
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
                if (client.DisposeUnderlying())
                {
                    lock (subPool.Sync)
                    {
                        if (subPool.All.Remove(client))
                            Interlocked.Increment(ref _discarded);
                    }
                }
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
                if (client.DisposeUnderlying())
                    Interlocked.Increment(ref _discarded);
            }
            subPool.Slots.Dispose();
        }
        _subPools.Clear();
    }

    private void EnsureMinimum(SubPool subPool, DataverseConnectionKey key, CancellationToken cancellationToken)
    {
        lock (subPool.Sync)
        {
            var missing = _options.MinSize - subPool.All.Count(client => client.State != PooledClientState.Disposed);
            for (var index = 0; index < missing; index++)
            {
                var client = CreateClientCore(key, cancellationToken);
                subPool.All.Add(client);
                subPool.Idle.Enqueue(client);
            }
        }
    }

    private PooledClient CreateClient(SubPool subPool, DataverseConnectionKey key, CancellationToken cancellationToken)
    {
        var client = CreateClientCore(key, cancellationToken);
        lock (subPool.Sync)
            subPool.All.Add(client);
        return client;
    }

    private PooledClient CreateClientCore(DataverseConnectionKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = _clientFactory(key, cancellationToken);
        if (service == null)
            throw new InvalidOperationException("Dataverse client factory 不得回傳 null。");
        Interlocked.Increment(ref _created);
        return new PooledClient(service);
    }

    private bool NeedsHealthCheck(PooledClient client, DateTime now)
        => now - client.LastValidatedUtc >= _options.HealthInterval;

    private void MarkFaulted(PooledClient client)
    {
        if (client.MarkFaulted())
            Interlocked.Increment(ref _faulted);
    }

    private void Return(SubPool subPool, PooledClient client)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                client.MarkFaulted();
                RemoveAndDispose(subPool, client);
                return;
            }

            if (client.State == PooledClientState.Faulted)
            {
                RemoveAndDispose(subPool, client);
                return;
            }

            if (!client.ReturnHealthy(DateTime.UtcNow))
            {
                if (client.State == PooledClientState.Faulted)
                    Interlocked.Increment(ref _faulted);
                RemoveAndDispose(subPool, client);
                return;
            }
            subPool.Idle.Enqueue(client);
            Interlocked.Increment(ref _totalReleases);
        }
        finally
        {
            if (Volatile.Read(ref _disposed) == 0)
                subPool.Slots.Release();
        }
    }

    private void RemoveAndDispose(SubPool subPool, PooledClient client)
    {
        lock (subPool.Sync)
            subPool.All.Remove(client);
        if (client.DisposeUnderlying())
            Interlocked.Increment(ref _discarded);
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
