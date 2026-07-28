// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Capacity/IRuntimeHostSlotCoordinator.cs
// 目的：runtime host 佔位租約協調器介面。
//
// 保母教學：
// - 這不是 per-user session pool。
// - 這是「這個 Gateway/Embedded 進程能不能佔用一個 host slot」。
// - 正式多 host 必須用 durable coordinator；記憶體版只給單機/測試。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Capacity;

/// <summary>
/// runtime host slot 租約。
/// </summary>
public sealed class RuntimeHostSlotLease : IAsyncDisposable, IDisposable
{
    private readonly IRuntimeHostSlotCoordinator _coordinator;
    private int _disposed;

    public RuntimeHostSlotLease(
        IRuntimeHostSlotCoordinator coordinator,
        RuntimeHostSlotLeaseNamespace leaseNamespace,
        string hostInstanceId,
        long fencingToken,
        DateTimeOffset expiresAtUtc)
    {
        _coordinator = coordinator;
        LeaseNamespace = leaseNamespace;
        HostInstanceId = hostInstanceId;
        FencingToken = fencingToken;
        ExpiresAtUtc = expiresAtUtc;
    }

    public RuntimeHostSlotLeaseNamespace LeaseNamespace { get; }
    public string HostInstanceId { get; }
    public long FencingToken { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    internal void Update(long fencingToken, DateTimeOffset expiresAtUtc)
    {
        FencingToken = fencingToken;
        ExpiresAtUtc = expiresAtUtc;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _ = _coordinator.ReleaseAsync(this, CancellationToken.None).AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _coordinator.ReleaseAsync(this, CancellationToken.None).ConfigureAwait(false);
    }
}

/// <summary>
/// host slot 協調器。
/// </summary>
public interface IRuntimeHostSlotCoordinator
{
    /// <summary>
    /// 是否為 durable 實作。正式多 host 應要求 true。
    /// </summary>
    bool IsDurable { get; }

    /// <summary>
    /// 嘗試取得 host slot。
    /// </summary>
    Task<RuntimeHostSlotLease?> TryAcquireAsync(
        RuntimeHostSlotLeaseNamespace leaseNamespace,
        string hostInstanceId,
        int maximumRuntimeHosts,
        TimeSpan leaseTtl,
        CancellationToken cancellationToken);

    /// <summary>
    /// 續租。fencing token 必須單調遞增。
    /// </summary>
    Task<bool> TryRenewAsync(
        RuntimeHostSlotLease lease,
        TimeSpan leaseTtl,
        CancellationToken cancellationToken);

    /// <summary>
    /// 釋放 host slot。
    /// </summary>
    ValueTask ReleaseAsync(RuntimeHostSlotLease lease, CancellationToken cancellationToken);
}
