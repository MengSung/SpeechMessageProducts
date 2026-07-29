// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Capacity/InMemoryRuntimeHostSlotCoordinator.cs
// 目的：單機/測試用 host-slot coordinator。
//
// 保母教學：
// - IsDurable=false，代表它不能提供跨行程、跨主機或重啟後仍成立的容量保證。
// - 只能保證「同一個進程內」不會超過 MaximumRuntimeHosts。
// - 多 Gateway 正式部署不可把它當最終方案；要換成 durable store。
// - 這裡不保存任何使用者、LINE、token、CRM session。
// ============================================================================

using System.Collections.Concurrent;

namespace SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// 記憶體版 host-slot coordinator。
/// </summary>
public sealed class InMemoryRuntimeHostSlotCoordinator : IRuntimeHostSlotCoordinator
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, SlotRecord> _slots = new(StringComparer.Ordinal);
    private long _fencing;

    public bool IsDurable => false;

    public Task<RuntimeHostSlotLease?> TryAcquireAsync(
        RuntimeHostSlotLeaseNamespace leaseNamespace,
        string hostInstanceId,
        int maximumRuntimeHosts,
        TimeSpan leaseTtl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(hostInstanceId);
        if (maximumRuntimeHosts < 1)
        {
            return Task.FromResult<RuntimeHostSlotLease?>(null);
        }

        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            PurgeExpired(now);

            var key = BuildKey(leaseNamespace, hostInstanceId);
            var token = Interlocked.Increment(ref _fencing);
            var expires = now.Add(leaseTtl);

            // 既有 host 重入：更新租約。
            if (_slots.TryGetValue(key, out var existing) && existing.ExpiresAtUtc > now)
            {
                existing.FencingToken = token;
                existing.ExpiresAtUtc = expires;
                return Task.FromResult<RuntimeHostSlotLease?>(
                    new RuntimeHostSlotLease(this, leaseNamespace, hostInstanceId, token, expires));
            }

            var active = _slots.Count(pair =>
                pair.Value.LeaseNamespace.LeaseNamespaceId == leaseNamespace.LeaseNamespaceId &&
                pair.Value.ExpiresAtUtc > now);

            if (active >= maximumRuntimeHosts)
            {
                return Task.FromResult<RuntimeHostSlotLease?>(null);
            }

            _slots[key] = new SlotRecord
            {
                LeaseNamespace = leaseNamespace,
                HostInstanceId = hostInstanceId,
                FencingToken = token,
                ExpiresAtUtc = expires
            };

            return Task.FromResult<RuntimeHostSlotLease?>(
                new RuntimeHostSlotLease(this, leaseNamespace, hostInstanceId, token, expires));
        }
    }

    public Task<bool> TryRenewAsync(
        RuntimeHostSlotLease lease,
        TimeSpan leaseTtl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(lease);

        lock (_sync)
        {
            PurgeExpired(DateTimeOffset.UtcNow);

            var key = BuildKey(lease.LeaseNamespace, lease.HostInstanceId);
            if (!_slots.TryGetValue(key, out var record))
            {
                return Task.FromResult(false);
            }

            // fencing 必須匹配，避免 stale lease 續租。
            if (record.FencingToken != lease.FencingToken)
            {
                return Task.FromResult(false);
            }

            var token = Interlocked.Increment(ref _fencing);
            var expires = DateTimeOffset.UtcNow.Add(leaseTtl);
            record.FencingToken = token;
            record.ExpiresAtUtc = expires;
            lease.Update(token, expires);
            return Task.FromResult(true);
        }
    }

    public ValueTask ReleaseAsync(RuntimeHostSlotLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync)
        {
            PurgeExpired(DateTimeOffset.UtcNow);

            var key = BuildKey(lease.LeaseNamespace, lease.HostInstanceId);
            if (_slots.TryGetValue(key, out var record) && record.FencingToken == lease.FencingToken)
            {
                _slots.TryRemove(key, out _);
            }
        }

        return ValueTask.CompletedTask;
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        foreach (var pair in _slots)
        {
            if (pair.Value.ExpiresAtUtc <= now)
            {
                _slots.TryRemove(pair.Key, out _);
            }
        }
    }

    private static string BuildKey(RuntimeHostSlotLeaseNamespace leaseNamespace, string hostInstanceId)
        => leaseNamespace.LeaseNamespaceId + "|" + hostInstanceId;

    private sealed class SlotRecord
    {
        public required RuntimeHostSlotLeaseNamespace LeaseNamespace { get; init; }
        public required string HostInstanceId { get; init; }
        public long FencingToken { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
}
