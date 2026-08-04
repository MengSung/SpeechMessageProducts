using System.Collections.Concurrent;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.ControlPlane.Capacity;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// 管理單一 <c>(ProfileAlias, GenerationId)</c> 的 Data8 Connector Client Pool。
/// Pool 只保存此世代健康且不含請求狀態的 Client；它不保存使用者、Session、Credential、Token、OrganizationId
/// 或端點。Organization 的總容量只由注入的 <see cref="IOrganizationAdmissionManager"/> 管理，
/// <see cref="_localSlots"/> 僅限制本 Pool 的 Client 數量，不能形成第二套或可繞過的組織預算。
/// </summary>
public sealed class Data8ConnectorPool : IConnectorPool
{
    private readonly object _gate = new();
    private readonly ResolvedProfile _profile;
    private readonly IOrganizationAdmissionManager _admissionManager;
    private readonly IData8ConnectorClientFactory _factory;
    private readonly ConcurrentQueue<IConnectorClient> _idleClients = new();
    private readonly SemaphoreSlim _localSlots;
    private readonly TaskCompletionSource<bool> _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeLeases;
    private bool _draining;
    private Task? _drainTask;
    private Task? _disposeTask;

    /// <summary>
    /// 建立一個不可變 Profile Generation 專屬的 Pool。
    /// Admission Manager 與 Factory 都由外部組合根擁有；本類別只擁有本身建立或已移轉進來的 Client、
    /// local slot 與 drain signal。建構時不預熱，避免在尚未取得 Admission Permit 前建立連線或保留 Credential。
    /// </summary>
    public Data8ConnectorPool(
        ResolvedProfile profile,
        IOrganizationAdmissionManager admissionManager,
        IData8ConnectorClientFactory factory,
        int minSize,
        int maxSize)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _admissionManager = admissionManager ?? throw new ArgumentNullException(nameof(admissionManager));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        if (profile.ConnectorKind != ConnectorKind.Data8)
        {
            throw new ArgumentException("Data8ConnectorPool only accepts Data8 profiles.", nameof(profile));
        }

        if (minSize < 0 || maxSize < 1 || minSize > maxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSize), "Pool size must satisfy 0 <= minSize <= maxSize.");
        }

        // P3 不做連線預熱：MinSize 是日後 host 策略的保留值，不能在未獲 Admission 的情況下保留 Client。
        _localSlots = new SemaphoreSlim(maxSize, maxSize);
    }

    /// <summary>取得此 Pool 的部署端 Profile Alias。</summary>
    public string ProfileAlias => _profile.ProfileAlias;

    /// <summary>取得此 Pool 的不可變 Profile Generation 識別碼。</summary>
    public long GenerationId => _profile.GenerationId;

    /// <summary>取得 Pool 是否已開始 Drain；此狀態一旦設定便不可逆轉。</summary>
    public bool IsDraining
    {
        get
        {
            lock (_gate)
            {
                return _draining;
            }
        }
    }

    /// <summary>
    /// 先取得既有 Organization Admission Permit，再取得本世代 local slot 與 Client。
    /// 任何失敗路徑都會依反向取得順序釋放暫存 Client、local slot 與 Permit；取消來源只存在於此方法，
    /// 因此不會把 Timer、CancellationRegistration 或請求資料保留在 Pool 中。
    /// </summary>
    public async Task<IConnectorLease> AcquireAsync(ConnectorOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDraining();

        var remaining = operation.DeadlineUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            throw new OperationCanceledException("The connector operation deadline has expired.");
        }

        using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCancellation.CancelAfter(remaining);
        var effectiveCancellationToken = deadlineCancellation.Token;

        var admission = await _admissionManager
            .AcquireAsync(CreateEnvelope(operation), effectiveCancellationToken)
            .ConfigureAwait(false);
        if (!admission.Succeeded || admission.Permit is null)
        {
            throw new InvalidOperationException(admission.Error?.ErrorMessage ?? "Organization admission was rejected.");
        }

        var permit = admission.Permit;
        var slotAcquired = false;
        IConnectorClient? client = null;
        try
        {
            await _localSlots.WaitAsync(effectiveCancellationToken).ConfigureAwait(false);
            slotAcquired = true;
            ThrowIfDraining();

            if (!_idleClients.TryDequeue(out client))
            {
                client = await _factory.CreateAsync(_profile, effectiveCancellationToken).ConfigureAwait(false);
            }

            if (client is null)
            {
                throw new InvalidOperationException("Data8 client factory returned null.");
            }

            // 即使 Factory 沒有立即觀察取消，也不能把新建 Client 交給已逾時的作業。
            effectiveCancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ThrowIfDraining();
                _activeLeases++;
            }

            var lease = new Data8ConnectorLease(this, client, permit);
            client = null;
            permit = null!;
            slotAcquired = false;
            return lease;
        }
        catch
        {
            if (client is not null)
            {
                await DisposeClientAsync(client).ConfigureAwait(false);
            }

            if (slotAcquired)
            {
                _localSlots.Release();
            }

            await permit.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 原子地將 Pool 切換為 Drain 狀態並等待既有 Lease 釋放。
    /// 呼叫端取消只會取消自身等待；底層 Drain Task 仍由 Pool 持有並會完成 idle Client 清理，
    /// 不會形成 fire-and-forget Task 或因取消而遺留已淘汰世代的連線。
    /// </summary>
    public Task DrainAsync(CancellationToken cancellationToken = default)
    {
        Task drainTask;
        lock (_gate)
        {
            _draining = true;
            drainTask = _drainTask ??= DrainCoreAsync();
        }

        return drainTask.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 接收 Lease 歸還的 Client。健康 Client 僅在同一個未 Drain Pool 重新入隊；
    /// 故障、取消、逾時或 Drain 中的 Client 一律 Dispose。最後必定釋放 local slot 與遞減 Lease 計數，
    /// 即使 Client Dispose 失敗也不會永久耗盡容量或阻塞 Drain。
    /// </summary>
    internal async ValueTask ReturnAsync(IConnectorClient client, bool faulted)
    {
        ArgumentNullException.ThrowIfNull(client);
        Exception? failure = null;
        try
        {
            if (faulted || IsDraining)
            {
                await DisposeClientAsync(client).ConfigureAwait(false);
            }
            else
            {
                _idleClients.Enqueue(client);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            _localSlots.Release();
            lock (_gate)
            {
                _activeLeases--;
                if (_draining && _activeLeases == 0)
                {
                    _drained.TrySetResult(true);
                }
            }
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    /// <summary>
    /// 執行冪等的最終清理。所有呼叫端等待相同的 Dispose Task，確保 semaphore 只會在 Drain 完成後 Dispose，
    /// 並避免平行 Dispose 造成 ObjectDisposedException、未完成的 Client 清理或資源生命週期競爭。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    /// <summary>以同步方式等待相同的非同步清理流程，不建立背景工作或額外資源擁有者。</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task DisposeCoreAsync()
    {
        await DrainAsync().ConfigureAwait(false);
        _localSlots.Dispose();
    }

    private async Task DrainCoreAsync()
    {
        lock (_gate)
        {
            if (_activeLeases == 0)
            {
                _drained.TrySetResult(true);
            }
        }

        await _drained.Task.ConfigureAwait(false);
        List<Exception>? failures = null;
        while (_idleClients.TryDequeue(out var client))
        {
            try
            {
                await DisposeClientAsync(client).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException(failures);
        }
    }

    private DispatchEnvelope CreateEnvelope(ConnectorOperation operation)
        => new()
        {
            ProfileAlias = _profile.ProfileAlias,
            CapabilityOperationId = operation.OperationId,
            WorkloadSubjectId = operation.WorkloadSubjectId,
            TemplateId = "connector-p3",
            TemplateHash = "connector-p3",
            DeadlineUtc = operation.DeadlineUtc,
            EstimatedEnvelopeBytes = Math.Max(256, operation.EstimatedBytes)
        };

    private void ThrowIfDraining()
    {
        lock (_gate)
        {
            if (_draining || _disposeTask is not null)
            {
                throw new ObjectDisposedException(nameof(Data8ConnectorPool), "The profile generation is draining.");
            }
        }
    }

    private static async ValueTask DisposeClientAsync(IConnectorClient client)
        => await client.DisposeAsync().ConfigureAwait(false);
}

/// <summary>
/// 封裝單次借用的 Client 與 Admission Permit，並保證兩者只由此 Lease 釋放一次。
/// Lease 不保存請求內容、取消來源、Credential 或例外物件；它僅保存本次 Client 與 Permit，
/// 直到呼叫端以 <c>await using</c> 釋放為止，避免跨請求與跨 Profile 的資源或 Session 洩漏。
/// </summary>
internal sealed class Data8ConnectorLease : IConnectorLease
{
    private readonly Data8ConnectorPool _pool;
    private IConnectorClient? _client;
    private IAdmissionPermit? _permit;
    private int _faulted;
    private int _disposed;

    /// <summary>建立 Lease 並接手已借出的 Client 與 Admission Permit 之唯一釋放責任。</summary>
    internal Data8ConnectorLease(Data8ConnectorPool pool, IConnectorClient client, IAdmissionPermit permit)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _permit = permit ?? throw new ArgumentNullException(nameof(permit));
    }

    /// <summary>取得來源 Pool 的 Profile Alias。</summary>
    public string ProfileAlias => _pool.ProfileAlias;

    /// <summary>取得來源 Pool 的 Generation 識別碼。</summary>
    public long GenerationId => _pool.GenerationId;

    /// <summary>
    /// 透過 Lease 擁有的 Client 執行作業，避免公開 Client 後讓取消或逾時繞過故障淘汰規則。
    /// 每次呼叫只建立並持有方法範圍內的 linked CTS；不會把 CancellationToken、Timer 或作業參數
    /// 存入 Pool、Lease 或 idle Client。任何取消、截止時間到期或傳輸例外都會使 Client 在 Dispose 時被淘汰。
    /// </summary>
    public async Task<ConnectorOperationResult> ExecuteAsync(
        ConnectorOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var client = Volatile.Read(ref _client)
            ?? throw new ObjectDisposedException(nameof(Data8ConnectorLease));
        var remaining = operation.DeadlineUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            MarkFaulted();
            throw new OperationCanceledException("The connector operation deadline has expired.");
        }

        using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCancellation.CancelAfter(remaining);
        try
        {
            return await client.ExecuteAsync(operation, deadlineCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 已取消的傳輸狀態不可證明可重用；寧可確定 Dispose 也不可把它交給下一個請求。
            MarkFaulted();
            throw;
        }
        catch
        {
            // 任何未分類傳輸例外同樣視為連線健康狀態未知，避免 Session 或 Channel 狀態跨 Lease 洩漏。
            MarkFaulted();
            throw;
        }
    }

    /// <summary>
    /// 將目前 Lease 標記為不可重用。例外原因不會被保存，避免例外鏈意外保留 Credential、端點或 Session。
    /// </summary>
    public void MarkFaulted(Exception? cause = null)
    {
        _ = cause;
        Interlocked.Exchange(ref _faulted, 1);
    }

    /// <summary>
    /// 以 exactly-once 順序先歸還或淘汰 Client，再於 finally 釋放 Admission Permit。
    /// 即使 Client Dispose 或 Pool 歸還失敗，Permit 仍必定釋放；多個清理失敗會保留為 AggregateException，
    /// 防止容量洩漏被次要清理例外遮蔽。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var client = Interlocked.Exchange(ref _client, null);
        var permit = Interlocked.Exchange(ref _permit, null);
        List<Exception>? failures = null;
        try
        {
            if (client is not null)
            {
                await _pool.ReturnAsync(client, Volatile.Read(ref _faulted) != 0).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
        finally
        {
            if (permit is not null)
            {
                try
                {
                    await permit.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
        }

        if (failures is { Count: 1 })
        {
            throw failures[0];
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures);
        }
    }

    /// <summary>以同步方式完成與 <see cref="DisposeAsync"/> 相同的唯一釋放流程。</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
