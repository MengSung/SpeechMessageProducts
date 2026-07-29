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

public sealed record RuntimeHostSlotLeaseRequest(
    RuntimeHostSlotLeaseNamespace LeaseNamespace,
    string HostInstanceId,
    int MaximumRuntimeHosts,
    TimeSpan LeaseTtl,
    long AdmissionEpoch,
    string ConfigurationDigest);

/// <summary>
/// runtime host slot 租約。
/// </summary>
public sealed class RuntimeHostSlotLease : IAsyncDisposable, IDisposable
{
    private readonly IRuntimeHostSlotCoordinator _coordinator;
    private int _disposed;
    private long _fencingToken;
    private long _expiresAtUtcTicks;

    public RuntimeHostSlotLease(
        IRuntimeHostSlotCoordinator coordinator,
        RuntimeHostSlotLeaseNamespace leaseNamespace,
        string hostInstanceId,
        long fencingToken,
        DateTimeOffset expiresAtUtc,
        int slotOrdinal = -1,
        long admissionEpoch = 1,
        string? configurationDigest = null)
    {
        _coordinator = coordinator;
        LeaseNamespace = leaseNamespace;
        HostInstanceId = hostInstanceId;
        _fencingToken = fencingToken;
        _expiresAtUtcTicks = expiresAtUtc.UtcTicks;
        SlotOrdinal = slotOrdinal;
        AdmissionEpoch = admissionEpoch;
        ConfigurationDigest = configurationDigest ?? new string('0', 64);
    }

    public RuntimeHostSlotLeaseNamespace LeaseNamespace { get; }
    public string HostInstanceId { get; }
    public long FencingToken => Interlocked.Read(ref _fencingToken);
    public DateTimeOffset ExpiresAtUtc => new(
        Interlocked.Read(ref _expiresAtUtcTicks),
        TimeSpan.Zero);
    public int SlotOrdinal { get; }
    public long AdmissionEpoch { get; }
    public string ConfigurationDigest { get; }

    internal void Update(long fencingToken, DateTimeOffset expiresAtUtc)
    {
        Interlocked.Exchange(ref _fencingToken, fencingToken);
        Interlocked.Exchange(ref _expiresAtUtcTicks, expiresAtUtc.UtcTicks);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // `await using` is the normal path. This compatibility path still waits
        // deterministically, but the release runs off a caller-owned UI/legacy
        // synchronization context so it cannot deadlock that context.
        Task.Run(async () =>
            await _coordinator.ReleaseAsync(this, CancellationToken.None).ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();
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
    /// True only when the durable coordinator atomically validates the global
    /// admission epoch and immutable configuration digest.
    /// </summary>
    bool SupportsAdmissionEpoch => false;

    Task<RuntimeHostSlotLease?> TryAcquireAsync(
        RuntimeHostSlotLeaseRequest request,
        CancellationToken cancellationToken)
        => TryAcquireAsync(
            request.LeaseNamespace,
            request.HostInstanceId,
            request.MaximumRuntimeHosts,
            request.LeaseTtl,
            cancellationToken);

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
