// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Capacity/IRuntimeHostSlotCoordinator.cs
// 目的：runtime host 佔位租約協調器介面。
//
// 保母教學：
// - 這不是 per-user session pool。
// - 這是「這個 Gateway/Embedded 進程能不能佔用一個 host slot」。
// - 正式多 host 必須用 durable coordinator；記憶體版只給單機/測試。
// ============================================================================

namespace SpeechMessage.Dynamics.ControlPlane.Capacity;

/// <summary>
/// 跨程序 runtime-host slot 的取得請求。
/// <see cref="CanonicalOrganizationKey"/> 是由已驗證設定建立的實體 Dynamics Organization 身分，
/// 耐久協調器必須以它驗證 LeaseNamespaceId 的唯一歸屬，絕不可從環境名稱、主機名稱、使用者、Session、Token 或憑證臨時推導。
/// 此請求只承載有界、非機密的控制平面資料；租約建立與失敗都不得保留 HTTP Context、認證標頭或任何使用者狀態。
/// </summary>
public sealed record RuntimeHostSlotLeaseRequest(
    CanonicalOrganizationCapacityKey CanonicalOrganizationKey,
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

        // 正常路徑應使用 await using。同步相容路徑仍會確定等待 ReleaseAsync 完成，
        // 但把非同步釋放放到 ThreadPool 執行，避免沿用呼叫端 UI/legacy SynchronizationContext 而形成死鎖。
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
    /// 只有 durable coordinator 能原子驗證全域 AdmissionEpoch 與不可變設定摘要時才為 true；
    /// 僅能持久化租約但無法 fencing 舊設定的實作，不具備安全的跨主機容量保證。
    /// </summary>
    bool SupportsAdmissionEpoch => false;

    Task<RuntimeHostSlotLease?> TryAcquireAsync(
        RuntimeHostSlotLeaseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 耐久實作若沒有覆寫此入口，便無法把 canonical organization key 寫入/驗證於控制平面資料庫。
        // 因此不可悄悄降級成舊式 namespace-only 路徑；寧可在任何連線、交易或背景資源建立前 fail-closed。
        if (IsDurable)
        {
            throw new InvalidOperationException(
                "Durable host-slot coordinators must validate the canonical organization identity.");
        }

        return TryAcquireAsync(
            request.LeaseNamespace,
            request.HostInstanceId,
            request.MaximumRuntimeHosts,
            request.LeaseTtl,
            cancellationToken);
    }

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
