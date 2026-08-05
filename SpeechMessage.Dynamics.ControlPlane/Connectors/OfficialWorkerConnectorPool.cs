using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.ControlPlane.Connectors;

/// <summary>
/// 將一個 Official Worker Profile generation 轉接為 <see cref="IConnectorPool"/>。
/// Pool 不自行建立 Worker、Process、Pipe、Credential 或 admission permit；這些資源仍由既有
/// <see cref="IProfileExecutionLeaseProvider"/> 產生的合併 lease 唯一擁有。Pool 只在同步 prepare
/// 後轉移一份 normalized、bounded dispatch owner，並以 <c>(ProfileAlias, GenerationId)</c> 隔離
/// 所有 connector lease，避免 request、session 或 profile state 跨 generation 保存。
/// </summary>
public sealed class OfficialWorkerConnectorPool : IConnectorPool
{
    private readonly object _gate = new();
    private readonly ResolvedProfile _profile;
    private readonly IProfileExecutionLeaseProvider _leaseProvider;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeLeaseCount;
    private bool _draining;
    private bool _disposed;

    /// <summary>
    /// 建立對應單一 Official 8.2 或 9.1 Profile generation 的 Pool。建構期只驗證 immutable scalar，
    /// 不解析 credential、不啟動 Worker、不建立 pipe、timer、permit 或 background work；任何 CE/Connector
    /// 不相容都在這些資源開始前拒絕。
    /// </summary>
    /// <param name="profile">已由 deployment-owned resolver 驗證的 Official Worker Profile snapshot。</param>
    /// <param name="leaseProvider">既有 Manager 的唯一 admission/runtime lease provider。</param>
    /// <exception cref="ArgumentException">profile 不是相容的 Official Worker Profile。</exception>
    /// <exception cref="ArgumentNullException">profile 或 leaseProvider 為 null。</exception>
    public OfficialWorkerConnectorPool(
        ResolvedProfile profile,
        IProfileExecutionLeaseProvider leaseProvider)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _leaseProvider = leaseProvider ?? throw new ArgumentNullException(nameof(leaseProvider));
        if (!IsCompatibleOfficialWorker(profile.ConnectorKind, profile.CeVersion))
        {
            throw new ArgumentException(
                "Official worker connector kind and CE version must be compatible.",
                nameof(profile));
        }
    }

    /// <summary>取得此 Pool 唯一可服務的 deployment Profile Alias。</summary>
    public string ProfileAlias => _profile.ProfileAlias;

    /// <summary>取得此 Pool 唯一可服務的 immutable Profile generation。</summary>
    public long GenerationId => _profile.GenerationId;

    /// <summary>
    /// 取得 Pool 是否已開始 drain。drain 一經開始不可回復；新 acquire 會在任何 prepare、permit 或
    /// Worker runtime 取得前失敗，既有 lease 則在自己的 deadline／cancellation 範圍內完成並釋放。
    /// </summary>
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
    /// 取得目前由此 adapter Pool 擁有、尚未歸還 underlying profile lease 的數量。值僅供同 assembly
    /// lifecycle test 與 drain 驗證；它不是 user/session/keyed cache，也不暴露 Worker 或 Permit reference。
    /// </summary>
    internal int ActiveLeaseCount
    {
        get
        {
            lock (_gate)
            {
                return _activeLeaseCount;
            }
        }
    }

    /// <summary>
    /// 同步執行 registry/parameter/deadline prepare，然後向既有 Manager 取得唯一的 admission/runtime
    /// composite lease。prepare 在第一個 await 前清除 caller collection 依賴；async 路徑只保留 bounded
    /// PreparedOperationDispatch，因此 queue wait 不會延長 HttpContext、Session、JsonDocument、token 或
    /// request dictionary 的生命週期。
    /// </summary>
    /// <param name="operation">只含 allowlisted operation、bounded parameters、deadline 與 workload scalar 的 operation。</param>
    /// <param name="cancellationToken">單一 caller scope 的取消訊號；不會被 Pool 保存。</param>
    /// <returns>唯一擁有 prepared dispatch 與 profile lease 的 Connector Lease。</returns>
    public Task<IConnectorLease> AcquireAsync(
        ConnectorOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfNotAccepting();
        if (operation.DeadlineUtc <= DateTimeOffset.UtcNow)
        {
            return Task.FromException<IConnectorLease>(new OperationCanceledException(
                "The connector operation deadline expired before acquisition."));
        }

        if (!_leaseProvider.TryGetAdmissionPlan(_profile.ProfileAlias, out var admissionPlan) ||
            admissionPlan is null)
        {
            return Task.FromException<IConnectorLease>(new InvalidOperationException(
                "The official worker profile is not ready for admission."));
        }

        if (!Package01OperationRegistry.TryGet(operation.OperationId, out var definition) || definition is null)
        {
            return Task.FromException<IConnectorLease>(new InvalidOperationException(
                "The connector operation is not registered."));
        }

        var request = new OperationExecutionRequest
        {
            ProfileAlias = _profile.ProfileAlias,
            CapabilityOperationId = definition.CapabilityOperationId,
            WorkloadSubjectId = operation.WorkloadSubjectId,
            Parameters = operation.Parameters
        };
        if (!OperationDispatchPreparer.Shared.TryPrepare(
                request,
                definition,
                admissionPlan,
                out var prepared,
                out var preparationError,
                operation.DeadlineUtc) ||
            prepared is null)
        {
            var failure = preparationError ?? OperationExecutionResult.Failure(
                DynamicsErrorCodes.InvalidParameter,
                "The connector operation could not be prepared.");
            if (string.Equals(failure.ErrorCode, DynamicsErrorCodes.AdmissionTimeout, StringComparison.Ordinal))
            {
                return Task.FromException<IConnectorLease>(new OperationCanceledException(
                    "The connector operation deadline expired before acquisition."));
            }

            return Task.FromException<IConnectorLease>(new InvalidOperationException(
                "The connector operation did not match its registered contract."));
        }

        return AcquirePreparedAsync(prepared, cancellationToken);
    }

    /// <summary>
    /// 封閉新 acquire 並等待既有 Connector Lease 歸還。Worker process、pipe、runtime lease 與 admission
    /// permit 的實際 drain 順序仍完全由 Manager 擁有；本 Pool 只等待自己已轉移的 adapter lease，避免
    /// 對同一底層 resource 建立第二套 termination 或 cleanup owner。
    /// </summary>
    public Task DrainAsync(CancellationToken cancellationToken = default)
        => BeginDrain().WaitAsync(cancellationToken);

    /// <summary>
    /// 由 generation registry 在發佈替代 generation 時同步切換至 draining 狀態。
    /// 這個 seam 只改變 admission 狀態並回傳既有的 drain 訊號；真正的 worker、pipe、runtime
    /// lease 與 Organization permit 仍由原 Pool 在最後一個 connector lease 釋放後負責清理。
    /// 因此 registry 不必以未受控的背景工作持有資源，也不會在替換期間短暫接受新請求。
    /// </summary>
    internal Task BeginDrain()
    {
        lock (_gate)
        {
            _draining = true;
            if (_activeLeaseCount == 0)
            {
                _drained.TrySetResult();
            }

            return _drained.Task;
        }
    }

    /// <summary>
    /// 同步相容路徑完整等待非同步 drain，不建立 fire-and-forget cleanup。底層 Manager 的 Worker/permit
    /// resources 並非本 Pool 所有，故不會由此路徑重複 Dispose。
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// 停止新 acquire 並等待 adapter lease 排空。完成後清除 Pool 自己的 accepting state；所有 underlying
    /// Runtime→Admission cleanup 仍由每一個已完整 await 的 <see cref="IProfileExecutionLease"/> 執行。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await DrainAsync().ConfigureAwait(false);
    }

    private async Task<IConnectorLease> AcquirePreparedAsync(
        PreparedOperationDispatch prepared,
        CancellationToken cancellationToken)
    {
        IProfileExecutionLease? profileLease = null;
        try
        {
            var acquired = await _leaseProvider
                .AcquireAsync(prepared.Envelope, cancellationToken)
                .ConfigureAwait(false);
            if (!acquired.Succeeded || acquired.Lease is null)
            {
                throw new InvalidOperationException("The official worker profile lease was rejected.");
            }

            profileLease = acquired.Lease;
            if (!MatchesProfileGeneration(profileLease.RuntimeKey))
            {
                throw new InvalidOperationException(
                    "The active official worker runtime does not match the resolved profile generation.");
            }

            lock (_gate)
            {
                if (_draining || _disposed)
                {
                    throw new ObjectDisposedException(
                        nameof(OfficialWorkerConnectorPool),
                        "The official worker connector pool is draining.");
                }

                checked
                {
                    _activeLeaseCount++;
                }
            }

            var result = new OfficialWorkerConnectorLease(this, profileLease, prepared);
            profileLease = null;
            prepared = null!;
            return result;
        }
        catch
        {
            prepared?.Dispose();
            if (profileLease is not null)
            {
                await profileLease.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private bool MatchesProfileGeneration(ProfileRuntimeKey? runtimeKey)
    {
        if (runtimeKey is not { } key ||
            !string.Equals(key.ProfileAlias, _profile.ProfileAlias, StringComparison.OrdinalIgnoreCase) ||
            key.Generation != _profile.GenerationId ||
            key.CanonicalOrganizationKey.ExpectedOrganizationId != _profile.OrganizationId)
        {
            return false;
        }

        var expectedCeVersion = _profile.CeVersion == CeVersion.Ce82 ? "8.2" : "9.1";
        return string.Equals(key.CeVersion, expectedCeVersion, StringComparison.Ordinal);
    }

    private void ThrowIfNotAccepting()
    {
        lock (_gate)
        {
            if (_draining || _disposed)
            {
                throw new ObjectDisposedException(
                    nameof(OfficialWorkerConnectorPool),
                    "The official worker connector pool is draining.");
            }
        }
    }

    private void ReleaseLease()
    {
        lock (_gate)
        {
            _activeLeaseCount--;
            if (_draining && _activeLeaseCount == 0)
            {
                _drained.TrySetResult();
            }
        }
    }

    private static bool IsCompatibleOfficialWorker(ConnectorKind connectorKind, CeVersion ceVersion)
        => (connectorKind == ConnectorKind.OfficialCrm82Worker && ceVersion == CeVersion.Ce82) ||
           (connectorKind == ConnectorKind.OfficialCrm91Worker && ceVersion == CeVersion.Ce91);

    /// <summary>
    /// 轉移單一 prepared dispatch 與既有 ProfileExecutionLease ownership 的 adapter lease。它絕不公開
    /// Worker、Process、Pipe、Credential、Permit 或 raw SDK client；執行與釋放都只能經此 Lease，確保
    /// Runtime lease 在底層 Provider 定義的順序中先完成、Admission permit 最後歸還。
    /// </summary>
    private sealed class OfficialWorkerConnectorLease : IConnectorLease
    {
        private readonly OfficialWorkerConnectorPool _owner;
        private IProfileExecutionLease? _profileLease;
        private PreparedOperationDispatch? _prepared;
        private int _executed;
        private int _faulted;
        private int _disposed;

        /// <summary>建立並接管已完成 admission/runtime acquisition 的唯一 lease owner。</summary>
        public OfficialWorkerConnectorLease(
            OfficialWorkerConnectorPool owner,
            IProfileExecutionLease profileLease,
            PreparedOperationDispatch prepared)
        {
            _owner = owner;
            _profileLease = profileLease;
            _prepared = prepared;
        }

        /// <summary>取得來源 Pool 的 Profile Alias。</summary>
        public string ProfileAlias => _owner.ProfileAlias;

        /// <summary>取得來源 Pool 的 immutable generation。</summary>
        public long GenerationId => _owner.GenerationId;

        /// <summary>
        /// 只允許執行 acquisition 時已 prepare 的同一 operation，並把 caller、host-lease-loss、retirement
        /// 與原始 deadline 合併為一次性 CTS。任何取消、timeout、runtime failure 或重複 execute 都會標記
        /// faulted；Dispose 仍會完整 await underlying composite lease，確保不遺留 permit 或 runtime reference。
        /// </summary>
        public async Task<ConnectorOperationResult> ExecuteAsync(
            ConnectorOperation operation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(OfficialWorkerConnectorLease));
            }

            var prepared = _prepared ?? throw new ObjectDisposedException(nameof(OfficialWorkerConnectorLease));
            if (Interlocked.Exchange(ref _executed, 1) != 0 ||
                !string.Equals(
                    operation.OperationId,
                    prepared.Envelope.CapabilityOperationId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    operation.WorkloadSubjectId,
                    prepared.Envelope.WorkloadSubjectId,
                    StringComparison.Ordinal) ||
                operation.DeadlineUtc > prepared.Envelope.DeadlineUtc)
            {
                MarkFaulted();
                throw new InvalidOperationException(
                    "A connector lease can execute only its acquired operation once.");
            }

            var profileLease = _profileLease ?? throw new ObjectDisposedException(nameof(OfficialWorkerConnectorLease));
            var remaining = prepared.Envelope.DeadlineUtc - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                MarkFaulted();
                throw new OperationCanceledException("The connector operation deadline expired before execution.");
            }

            using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                profileLease.LeaseLostToken,
                profileLease.RetirementToken);
            executionCancellation.CancelAfter(remaining);
            try
            {
                var executionResult = await profileLease.Executor.ExecuteAsync(
                        new OperationExecutionRequest
                        {
                            ProfileAlias = prepared.Envelope.ProfileAlias,
                            CapabilityOperationId = prepared.Envelope.CapabilityOperationId,
                            WorkloadSubjectId = prepared.Envelope.WorkloadSubjectId,
                            IdempotencyKey = prepared.Envelope.IdempotencyKey,
                            Parameters = prepared.Parameters
                        },
                        executionCancellation.Token)
                    .ConfigureAwait(false);
                return new ConnectorOperationResult(executionResult.Succeeded, executionResult.ErrorCode)
                {
                    Data = executionResult.Data
                };
            }
            catch
            {
                MarkFaulted();
                throw;
            }
        }

        /// <summary>
        /// 標記本 lease 所屬的 operation 失敗。Official Worker Runtime 會依自己的 fail-closed lifecycle
        /// 決定 recycle/termination；此 adapter 不保存例外物件，避免 endpoint、credential 或 stack graph
        /// 被 generation pool 或後續 request 長期保留。
        /// </summary>
        public void MarkFaulted(Exception? cause = null)
        {
            _ = cause;
            Interlocked.Exchange(ref _faulted, 1);
        }

        /// <summary>同步 Dispose 等待相同的 deterministic async cleanup，不建立 background work。</summary>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// 先清除 prepared request owner，再完整 await既有 Runtime→Admission composite lease，最後才把 adapter
        /// lease 從 Pool 計數移除。即使底層 cleanup 失敗，finally 仍歸還 Pool active count，使 drain 不會永久
        /// 等待已不再擁有的 wrapper；底層 Manager 仍保留其可重試的 resource owner 與失敗資訊。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var prepared = Interlocked.Exchange(ref _prepared, null);
            prepared?.Dispose();
            var profileLease = Interlocked.Exchange(ref _profileLease, null);
            try
            {
                if (profileLease is not null)
                {
                    await profileLease.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _owner.ReleaseLease();
            }
        }
    }
}
