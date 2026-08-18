using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Keyed、bounded、可觀測的 client pool。
/// 每個 client 由本 pool 建立並最終 Dispose；租約 Dispose 只回報狀態並歸還 semaphore，
/// 故不會讓短命 request 釋放長命 pooled client，也不會跨 key、request 或身分重用 client。
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
    /// </summary>
    public BoundedClientPool(
        Func<DataverseConnectionKey, CancellationToken, IOrganizationService> clientFactory,
        Func<IOrganizationService, bool> healthCheck,
        DataversePoolOptions options)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _healthCheck = healthCheck ?? throw new ArgumentNullException(nameof(healthCheck));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        var interval = _options.IdleTimeout <= TimeSpan.FromSeconds(1)
            ? TimeSpan.FromMilliseconds(250)
            : TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(250).Ticks, _options.IdleTimeout.Ticks / 2));
        _cleanupTimer = new Timer(CleanupTimerCallback, null, interval, interval);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>立即執行一次閒置淘汰，供 shutdown 與 deterministic 測試使用。</summary>
    public void CleanupIdleClients()
    {
        var now = DateTime.UtcNow;
        foreach (var subPool in _subPools.Values)
        {
            List<PooledClient> expired;
            lock (subPool.Sync)
            {
                var idleCount = subPool.All.Count(client => client.State == PooledClientState.Idle);
                expired = subPool.All
                    .Where(client => idleCount > _options.MinSize && client.IsIdleExpired(now, _options.IdleTimeout))
                    .ToList();
                foreach (var client in expired)
                    subPool.All.Remove(client);
            }

            foreach (var client in expired)
            {
                if (client.DisposeUnderlying())
                {
                    Interlocked.Increment(ref _discarded);
                    subPool.Idle.TryDequeue(out _);
                }
            }
        }
    }

    /// <inheritdoc />
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
            if (Volatile.Read(ref _disposed) != 0 || client.State == PooledClientState.Faulted)
            {
                RemoveAndDispose(subPool, client);
                return;
            }

            if (!client.ReturnHealthy(DateTime.UtcNow))
            {
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
