using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.ControlPlane.Capacity;

namespace SpeechMessage.Dynamics.SqlCoordinatorTestWorker;

/// <summary>
/// 測試 worker 的受限 runtime owner。
/// 只在第一個 ACQUIRE_HOST 才建立 LocalDB coordinator；所有 lease、permit 與 drain task
/// 都由此物件在 STOP 或程序結束前確定性收回，且不保存任何外部設定或秘密資料。
/// </summary>
internal sealed class WorkerRuntime : IAsyncDisposable
{
    private const string LocalDbConnectionString =
        "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=SpeechMessageDynamicsControlPlane;Integrated Security=True;Connect Timeout=5;Max Pool Size=8";

    // 此 instance 名稱是固定且刻意不存在的本機 LocalDB target；它只供 OUTAGE_PROBE 走受限失敗路徑，
    // 不會取得 CRM、遠端 SQL 或 caller 提供的連線資訊，也不會被寫入 stdout/stderr。
    private const string OutageProbeLocalDbConnectionString =
        "Data Source=(localdb)\\SpeechMessageCoordinatorOutageProbe;Initial Catalog=SpeechMessageDynamicsControlPlane;Integrated Security=True;Connect Timeout=1;Max Pool Size=1";

    private readonly WorkerStartupArguments _startup;
    private readonly Func<WorkerEvent, CancellationToken, ValueTask> _leaseLossEventWriter;
    private readonly object _shutdownGate = new();
    private readonly object _leaseLossGate = new();

    private SqlRuntimeHostSlotCoordinator? _coordinator;
    private OrganizationAdmissionManager? _manager;
    private IAdmissionPermit? _heldPermit;
    private Task? _drainTask;
    private Task? _shutdownTask;
    private CancellationTokenSource? _leaseLossObservationStop;
    private Task? _leaseLossObservationTask;
    private int _hostReady;
    private int _leaseLost;
    private int _outageFailClosed;
    private int _disposed;

    /// <summary>
    /// 建立 protocol-bound owner，但不建立 SQL 連線或 admission runtime。
    /// </summary>
    internal WorkerRuntime(
        WorkerStartupArguments startup,
        Func<WorkerEvent, CancellationToken, ValueTask> leaseLossEventWriter)
    {
        _startup = startup ?? throw new ArgumentNullException(nameof(startup));
        _leaseLossEventWriter = leaseLossEventWriter ?? throw new ArgumentNullException(nameof(leaseLossEventWriter));
    }

    /// <summary>
    /// 執行一個已由 Program 驗證的固定命令。此類別不處理 STOP，避免在 cleanup 完成前誤送出 STOPPED。
    /// </summary>
    internal Task<WorkerEvent> ExecuteAsync(WorkerCommand command, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        return command.Kind switch
        {
            WorkerCommandKind.AcquireHost => AcquireHostAsync(cancellationToken),
            WorkerCommandKind.AcquireWork => AcquireWorkAsync(cancellationToken),
            WorkerCommandKind.BeginDrain => BeginDrainAsync(),
            WorkerCommandKind.ReleaseWork => ReleaseWorkAsync(),
            WorkerCommandKind.AwaitDrain => AwaitDrainAsync(),
            WorkerCommandKind.OutageProbe => ProbeCoordinatorOutageAsync(cancellationToken),
            _ => Task.FromException<WorkerEvent>(new WorkerRuntimeCommandException(
                WorkerFailureCategory.Lifecycle))
        };
    }

    /// <summary>
    /// 在持有 permit 時啟動 manager drain，但刻意不等待它，讓 durable slot 在既有工作結束前保持有效。
    /// </summary>
    private async Task<WorkerEvent> BeginDrainAsync()
    {
        var manager = RequireManager();
        if (_heldPermit is null || _drainTask is not null)
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);
        }

        await StopLeaseLossObservationAsync().ConfigureAwait(false);
        _drainTask = manager.DisposeAsync().AsTask();
        return new WorkerEvent(WorkerEventKind.DrainBegin);
    }

    /// <summary>
    /// 取得 runtime-host slot；容量拒絕是正常的 HOST_DENIED，其餘初始化／SQL 錯誤交由 Program 受控失敗。
    /// </summary>
    private async Task<WorkerEvent> AcquireHostAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _outageFailClosed) != 0)
        {
            return new WorkerEvent(WorkerEventKind.HostDenied);
        }

        if (_drainTask is not null)
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);
        }

        var manager = await GetOrCreateManagerAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await manager.EnsureHostSlotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            Volatile.Write(ref _hostReady, 0);
            return new WorkerEvent(WorkerEventKind.HostDenied);
        }

        var snapshot = manager.GetSnapshot();
        if (!snapshot.HostSlotReady || snapshot.HostFencingToken <= 0)
        {
            Volatile.Write(ref _hostReady, 0);
            return new WorkerEvent(WorkerEventKind.HostDenied);
        }

        Volatile.Write(ref _leaseLost, 0);
        Volatile.Write(ref _hostReady, 1);
        return new WorkerEvent(WorkerEventKind.HostReady, snapshot.HostFencingToken);
    }

    /// <summary>
    /// 建立單一固定、非秘密且有界的 dispatch envelope，並只保留成功 permit 的 owner。
    /// </summary>
    private async Task<WorkerEvent> AcquireWorkAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _outageFailClosed) != 0)
        {
            return new WorkerEvent(WorkerEventKind.WorkDenied);
        }

        if (Volatile.Read(ref _hostReady) == 0)
        {
            if (Volatile.Read(ref _leaseLost) != 0)
            {
                return new WorkerEvent(WorkerEventKind.WorkDenied);
            }

            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);
        }

        if (_heldPermit is not null || _drainTask is not null)
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);
        }

        var result = await RequireManager()
            .AcquireAsync(CreateEnvelope(), cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded || result.Permit is null)
        {
            return new WorkerEvent(WorkerEventKind.WorkDenied);
        }

        if (result.Permit.HostFencingToken <= 0)
        {
            await result.Permit.DisposeAsync().ConfigureAwait(false);
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Admission);
        }

        _heldPermit = result.Permit;
        StartLeaseLossObservation(result.Permit.LeaseLostToken);
        return new WorkerEvent(WorkerEventKind.WorkHeld, result.Permit.HostFencingToken);
    }

    /// <summary>
    /// 以交換欄位的方式接管 permit，確保每個成功 permit 至多釋放一次。
    /// </summary>
    private async Task<WorkerEvent> ReleaseWorkAsync()
    {
        var permit = Interlocked.Exchange(ref _heldPermit, null);
        if (permit is null)
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);
        }

        await permit.DisposeAsync().ConfigureAwait(false);
        return new WorkerEvent(WorkerEventKind.WorkReleased);
    }

    /// <summary>
    /// 等待先前保留的 drain task，並在宣告完成前確認 SQL operation sentinel 已回到零。
    /// </summary>
    private async Task<WorkerEvent> AwaitDrainAsync()
    {
        var drainTask = _drainTask;
        var coordinator = _coordinator;
        if (drainTask is null || coordinator is null || _heldPermit is not null)
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);
        }

        await drainTask.ConfigureAwait(false);
        if (coordinator.ActiveDatabaseOperations != 0)
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);
        }

        Volatile.Write(ref _hostReady, 0);
        return new WorkerEvent(WorkerEventKind.Drained);
    }

    /// <summary>
    /// 對固定且故意不存在的同機 LocalDB instance 執行一次有界 coordinator probe，驗證 SQL 連線失敗不會留下
    /// ActiveDatabaseOperations、連線、Task 或可繼續 admission 的 fallback。probe coordinator 是此方法的唯一短生命週期 owner；
    /// <see cref="SqlRuntimeHostSlotCoordinator.VerifySchemaAsync" /> 內部擁有每次 SQL connection/command 的釋放，
    /// 因此本方法只在預期 SQL failure 且計數回到零後，先把本 worker 設為 fail-closed，再 drain 既有真實 LocalDB
    /// manager 並釋放 durable slot。這個順序防止 outage 與後續 stdin command、lease callback 或 cleanup 競爭時讓舊 host
    /// 繼續取得 work；失敗不會輸出 exception、connection string、host identity、credential 或 token。
    /// </summary>
    private async Task<WorkerEvent> ProbeCoordinatorOutageAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _outageFailClosed) != 0 ||
            _heldPermit is not null ||
            _drainTask is not null)
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);
        }

        var outageCoordinator = new SqlRuntimeHostSlotCoordinator(
            new SqlRuntimeHostSlotCoordinatorOptions
            {
                ConnectionString = OutageProbeLocalDbConnectionString,
                CommandTimeoutSeconds = 1,
                QuarantineSeconds = 1
            },
            NullLogger<SqlRuntimeHostSlotCoordinator>.Instance);
        var observedCoordinatorOutage = false;
        try
        {
            await outageCoordinator.VerifySchemaAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException)
        {
            observedCoordinatorOutage = true;
        }

        if (!observedCoordinatorOutage || outageCoordinator.ActiveDatabaseOperations != 0)
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Outage);
        }

        // 在開始任何 await drain 前先切斷新 admission，讓同一 worker 不會把 outage 當作可延長的 grace period。
        Volatile.Write(ref _outageFailClosed, 1);
        Volatile.Write(ref _hostReady, 0);

        var manager = _manager;
        if (manager is not null)
        {
            try
            {
                _drainTask = manager.DisposeAsync().AsTask();
                await _drainTask.ConfigureAwait(false);
            }
            catch
            {
                throw new WorkerRuntimeCommandException(WorkerFailureCategory.Outage);
            }
        }

        if (_coordinator is { ActiveDatabaseOperations: not 0 })
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Outage);
        }

        return new WorkerEvent(WorkerEventKind.OutageClean);
    }

    /// <summary>
    /// 延遲建立真正的 SQL coordinator 與 admission manager；worker label 不參與 durable plan／namespace identity。
    /// </summary>
    private async Task<OrganizationAdmissionManager> GetOrCreateManagerAsync(CancellationToken cancellationToken)
    {
        if (_manager is not null)
        {
            return _manager;
        }

        var coordinator = new SqlRuntimeHostSlotCoordinator(
            new SqlRuntimeHostSlotCoordinatorOptions
            {
                ConnectionString = LocalDbConnectionString,
                CommandTimeoutSeconds = 5,
                QuarantineSeconds = 2
            },
            NullLogger<SqlRuntimeHostSlotCoordinator>.Instance);
        await coordinator.VerifySchemaAsync(cancellationToken).ConfigureAwait(false);

        var organizationBaseUri = "https://cross-process-" + _startup.RunId + ".invalid/org/";
        var admissionOptions = new OrganizationAdmissionOptions
        {
            ExpectedOrganizationId = _startup.OrganizationId,
            AggregateMaxInFlight = 2,
            MaximumRuntimeHosts = 2,
            LocalQueueCapacity = 0,
            MaxDispatchEnvelopeBytes = 512,
            QueueAdmissionTimeoutSeconds = 5,
            MaxInFlightAndQueuedPerWorkload = 1,
            AdmissionNamespaceId = "cross-process-" + _startup.RunId + "-admission",
            LeaseNamespaceId = "cross-process-" + _startup.RunId,
            AdmissionEpoch = 1,
            RuntimeHostSlotLeaseTtlSeconds = 30,
            RuntimeHostSlotRenewalIntervalSeconds = 10,
            RuntimeHostSlotExpiryFenceSeconds = 1,
            MaximumOutboundWorkLifetimeSeconds = 2,
            ShutdownDrainTimeoutSeconds = 5,
            RequireDurableHostCoordinator = true
        };

        // 測試 worker 只驗證跨行程 SQL admission 與 host-slot fencing；一個 worker、單一 in-flight
        // 保持和正式 worker 的安全基線一致，且 canonical organization 只由非機密 base URI 與 GUID 組成。
        if (!OrganizationAdmissionPlan.TryCreate(
                organizationBaseUri,
                workerCount: 1,
                maxInFlightPerWorker: 1,
                admissionOptions,
                out var plan,
                out _) ||
            plan is null)
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Admission);
        }

        var manager = new OrganizationAdmissionManager(
            plan,
            coordinator,
            NullLogger<OrganizationAdmissionManager>.Instance);
        _coordinator = coordinator;
        _manager = manager;
        return manager;
    }

    /// <summary>
    /// worker label 僅作為有界 workload marker；不會進入 durable namespace、canonical organization 或 plan digest。
    /// </summary>
    private DispatchEnvelope CreateEnvelope()
        => new()
        {
            ProfileAlias = "cross-process-localdb",
            CapabilityOperationId = "cross-process-capacity",
            WorkloadSubjectId = "worker-" + _startup.WorkerLabel,
            TemplateId = "CrossProcessCapacity",
            TemplateHash = new string('a', 64),
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(2),
            EstimatedEnvelopeBytes = 512
        };

    /// <summary>
    /// 由第一個真實 admission permit 的既有 LeaseLostToken 建立唯一觀測 Task；此 worker 不輪詢 SQL、
    /// 不輸出 host、token 或任何 lease identity，僅在 manager 已取消該 token 時透過 Program 擁有的 bounded channel
    /// 送出固定 LEASE_LOST 事件。Task 與 CTS 的唯一 owner 是本 runtime，BeginDrain 與 Dispose 都會先取消並等待它。
    /// </summary>
    private void StartLeaseLossObservation(CancellationToken leaseLostToken)
    {
        lock (_leaseLossGate)
        {
            if (_leaseLossObservationTask is not null)
            {
                return;
            }

            var stop = new CancellationTokenSource();
            _leaseLossObservationStop = stop;
            _leaseLossObservationTask = ObserveLeaseLossAsync(leaseLostToken, stop.Token);
        }
    }

    /// <summary>
    /// 等待 manager 已擁有的 lease-loss cancellation；沒有週期性 timer 或自行續租，故唯一權威仍是 durable coordinator。
    /// 一旦收到取消，先原子清除並釋放任何目前持有的 permit，再關閉本地後續 admission，最後只排入一次固定事件。
    /// 若 shutdown/drain 先開始，停止 token 會使本 Task 安靜結束，避免正常釋放被誤報成 fencing loss。
    /// </summary>
    private async Task ObserveLeaseLossAsync(
        CancellationToken leaseLostToken,
        CancellationToken observationStopToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, leaseLostToken)
                .WaitAsync(observationStopToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (observationStopToken.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException) when (leaseLostToken.IsCancellationRequested)
        {
            if (Interlocked.Exchange(ref _leaseLost, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref _hostReady, 0);
            var permit = Interlocked.Exchange(ref _heldPermit, null);
            if (permit is not null)
            {
                await permit.DisposeAsync().ConfigureAwait(false);
            }

            await _leaseLossEventWriter(
                    new WorkerEvent(WorkerEventKind.LeaseLost),
                    observationStopToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 在 manager、permit 或 Program channel 開始 disposal 前停止 lease-loss 觀測；先擷取唯一 CTS/Task 再取消並 await，
    /// 確保 callback 不會與 permit 釋放或 stdout channel completion 競爭，也不會留下背景 Task 或 cancellation registration。
    /// </summary>
    private async Task StopLeaseLossObservationAsync()
    {
        CancellationTokenSource? stop;
        Task? observationTask;
        lock (_leaseLossGate)
        {
            stop = _leaseLossObservationStop;
            observationTask = _leaseLossObservationTask;
            _leaseLossObservationStop = null;
            _leaseLossObservationTask = null;
        }

        if (stop is null)
        {
            return;
        }

        try
        {
            stop.Cancel();
            if (observationTask is not null)
            {
                await observationTask.ConfigureAwait(false);
            }
        }
        finally
        {
            stop.Dispose();
        }
    }

    /// <summary>
    /// STOP 與 finally 共用同一個 shutdown task；若 drain 已啟動，先釋放 worker 自有 permit 再等待既有 task。
    /// </summary>
    public ValueTask DisposeAsync() => new(DisposeOnceAsync());

    private Task DisposeOnceAsync()
    {
        lock (_shutdownGate)
        {
            return _shutdownTask ??= DisposeCoreAsync();
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        await StopLeaseLossObservationAsync().ConfigureAwait(false);

        var permit = Interlocked.Exchange(ref _heldPermit, null);
        if (permit is not null)
        {
            await permit.DisposeAsync().ConfigureAwait(false);
        }

        var manager = _manager;
        var drainTask = _drainTask;
        if (drainTask is not null)
        {
            await drainTask.ConfigureAwait(false);
        }
        else if (manager is not null)
        {
            await manager.DisposeAsync().ConfigureAwait(false);
        }

        if (_coordinator is { ActiveDatabaseOperations: not 0 })
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);
        }

        Volatile.Write(ref _hostReady, 0);
        _manager = null;
        _coordinator = null;
    }

    private OrganizationAdmissionManager RequireManager()
        => _manager ?? throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new WorkerRuntimeCommandException(WorkerFailureCategory.Lifecycle);
        }
    }
}

/// <summary>
/// 將 runtime 的內部失敗收斂為固定 protocol 類別，避免例外文字穿越 child process 邊界。
/// </summary>
internal sealed class WorkerRuntimeCommandException : Exception
{
    internal WorkerRuntimeCommandException(WorkerFailureCategory failureCategory)
        : base("The bounded worker runtime command failed.")
    {
        FailureCategory = failureCategory;
    }

    internal WorkerFailureCategory FailureCategory { get; }
}
