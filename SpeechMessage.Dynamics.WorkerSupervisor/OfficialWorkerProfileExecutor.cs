using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.WorkerSupervisor;

/// <summary>
/// 擁有一個且僅一個官方 Worker process generation、local named pipe、operation gate 與
/// stdout/stderr reader task。此 owner 在 READY 前即建立，直到 OS 明確確認 process exit、reader
/// terminal、pipe／gate 釋放及所有 Execute entrant 離開後才可歸零；cleanup 未完成時 instance
/// 保持非 accepting 並保留精確資源 reference 供後續 serialized Dispose 重試。它不保存 caller identity、
/// request payload、Credential、Token、CRM SDK object、Session 或跨 profile mutable state。
/// </summary>
public sealed class OfficialWorkerProfileExecutor : IAsyncDisposable
{
    private const int MaximumIdentifierLength = 128;
    private const string CleanupFailureMessage =
        "The official Dynamics worker cleanup did not complete.";
    private static readonly TimeSpan MaximumConfiguredTimeout = TimeSpan.FromMinutes(10);

    private readonly OfficialWorkerProfileOptions _options;
    private readonly string _processNonce;
    private readonly WorkerEnvelopeCodec _codec;
    private readonly OfficialWorkerRecyclePolicy _recyclePolicy;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _disposeSync = new();

    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private StreamReader? _stdoutReader;
    private StreamReader? _stderrReader;
    private CancellationTokenSource? _outputReadCancellation = new();
    private Task? _stdoutDiscardTask;
    private Task? _stderrDiscardTask;
    private Task? _disposeTask;
    private int _accepting;
    private int _activeOperations;
    private int _operationEntrants;
    private int _forceTerminationRequested;

    // 每次 Process exit wait 都必須是當前 Dispose attempt 內的有限 owner；counter 涵蓋其 timeout timer
    // 與 cancellation registration 的完整生命週期。並行 Dispose caller 共用同一 attempt，因此此值最多為一，
    // 且 attempt 返回前必須歸零，避免 retry 遺留不可見的 WaitForExitAsync task／event registration。
    private int _processExitWaitOwners;

    // SemaphoreSlim 本身是 readonly reference，Dispose 後仍非 null；另以單一 scalar 記錄唯一 cleanup
    // owner，只有所有 Execute entrant 離開且 gate 成功 Dispose 後才歸零，避免 snapshot 假報已釋放。
    private int _operationGateOwned = 1;

    private OfficialWorkerProfileExecutor(
        OfficialWorkerProfileOptions options,
        string processNonce,
        NamedPipeServerStream pipe)
    {
        _options = options;
        _processNonce = processNonce;
        _pipe = pipe;
        _codec = new WorkerEnvelopeCodec(Package01FeeWorkerContract.ProtocolLimits);
        _recyclePolicy = new OfficialWorkerRecyclePolicy(
            options.RecyclePolicyOptions,
            Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Gets the first sanitized, sticky reason that requires this generation to retire.
    /// </summary>
    public OfficialWorkerRecycleReason RecycleReason => _recyclePolicy.RecycleReason;

    /// <summary>
    /// Evaluates the next-admission recycle policy from executor-owned process counters only.
    /// An exited, unreadable, or otherwise unknown process state fails closed.
    /// </summary>
    /// <returns>The sticky recycle reason, or <see cref="OfficialWorkerRecycleReason.None"/>.</returns>
    public OfficialWorkerRecycleReason EvaluateRecycleForNextAdmission()
    {
        var process = Volatile.Read(ref _process);
        var isRunning = process is not null && IsProcessRunningConfirmed(process);
        return _recyclePolicy.EvaluateForNextAdmission(
            Stopwatch.GetTimestamp(),
            isReady: Volatile.Read(ref _accepting) != 0 && isRunning,
            isHealthy: isRunning,
            protocolViolation: false,
            isRunning && process is not null
                ? ObserveProcessResources(process)
                : OfficialWorkerResourceObservation.Unreadable);
    }

    /// <summary>
    /// 先驗證 executable SHA-256；此階段若取消，尚未建立 IPC 或 Process，可直接傳回 cancellation。驗證
    /// 完成後立即建立未發布 executor owner 與 local pipe，並只以非機密 bootstrap 欄位啟動 Worker。
    /// 只有 nonce-bound READY 同時符合 protocol、worker kind、package lock、CE version 與 profile
    /// generation，且 process 仍被確認存活時才開放 admission。owner 建立後的 timeout、cancellation、
    /// process-start 或 READY protocol failure 都先強制退休；若 bounded cleanup 尚未完成，
    /// <see cref="OfficialWorkerStartupException"/> 會承接相同 owner，保留原始失敗為第一原因供 Factory 重試。
    /// </summary>
    /// <param name="options">已凍結且不含秘密值的 Worker profile、執行檔 identity 與有限 timeout。</param>
    /// <param name="cancellationToken">只控制 startup 等待；取消後仍會完整 await 一次 bounded cleanup。</param>
    /// <returns>已完成 READY 驗證且開始接受一個在途作業的唯一 generation owner。</returns>
    public static async Task<OfficialWorkerProfileExecutor> StartAsync(
        OfficialWorkerProfileOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        await VerifyExecutableHashAsync(options, cancellationToken).ConfigureAwait(false);

        var processNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();
        var pipeName = "speechmessage-dynamics-" +
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var pipe = CreatePipe(pipeName);
        var owner = new OfficialWorkerProfileExecutor(options, processNonce, pipe);

        try
        {
            var process = StartProcess(options, pipeName, processNonce);
            owner._process = process;
            var stdoutReader = process.StandardOutput;
            var stderrReader = process.StandardError;
            owner._stdoutReader = stdoutReader;
            owner._stderrReader = stderrReader;
            var outputReadCancellation = owner._outputReadCancellation ??
                throw new InvalidOperationException();
            owner._stdoutDiscardTask = DiscardOutputAsync(
                stdoutReader,
                outputReadCancellation.Token);
            owner._stderrDiscardTask = DiscardOutputAsync(
                stderrReader,
                outputReadCancellation.Token);

            using var startupCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupCancellation.CancelAfter(options.StartupTimeout);
            await pipe.WaitForConnectionAsync(startupCancellation.Token).ConfigureAwait(false);

            var payload = await WorkerFrameCodec.ReadAsync(
                pipe,
                Package01FeeWorkerContract.ProtocolLimits.MaximumFrameBytes,
                startupCancellation.Token).ConfigureAwait(false);
            var ready = new WorkerEnvelopeCodec(Package01FeeWorkerContract.ProtocolLimits)
                .DeserializeReady(payload);
            ValidateReady(options, processNonce, ready);
            if (!IsProcessRunningConfirmed(process))
            {
                throw new InvalidOperationException(
                    "The official Dynamics worker exited before readiness was published.");
            }

            Volatile.Write(ref owner._accepting, 1);
            return owner;
        }
        catch (Exception startupFailure)
        {
            // READY 尚未發布即代表此 generation 永遠不可接收要求；先標記強制退休，再由已在
            // pipe 建立時取得 ownership 的 executor 嘗試清理。任何 cleanup failure 都只留在同一
            // owner 內供 host 重試，不能跳過 Process、reader 或 pipe，也不能取代原始 startup 原因。
            Interlocked.Exchange(ref owner._forceTerminationRequested, 1);
            try
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException cleanupFailure) when (
                string.Equals(
                    cleanupFailure.Message,
                    CleanupFailureMessage,
                    StringComparison.Ordinal))
            {
                // 固定 cleanup failure 僅表示 owner 尚未完成；例外 contract 會把相同 owner 交給 host。
            }

            throw new OfficialWorkerStartupException(startupFailure, owner);
        }
    }

    /// <summary>
    /// 驗證並執行目前 allowlist 中的 identity operation。要求只在取得單一 operation gate 後寫入
    /// nonce-bound pipe；caller cancellation 保留原 token 的 <see cref="OperationCanceledException"/>，
    /// internal timeout、protocol failure 與 worker exit 則回傳固定 typed result。任何可能使 frame
    /// 失同步的結果都關閉 admission 並自動退休整個 generation，絕不 replay 或切換 transport/profile。
    /// 若退休 cleanup 暫時未完成，原始 operation outcome 仍優先交還 caller；相同 executor 持續由
    /// runtime 擁有並可在 descendant output handle 關閉後重試清零。
    /// </summary>
    /// <param name="request">已由 ControlPlane 授權且只含 bounded SDK-free parameter 的要求。</param>
    /// <param name="cancellationToken">caller 要求期限；不會成為共享 cache 或 background owner。</param>
    /// <returns>成功的 bounded WhoAmI DTO，或固定 sanitized worker failure。</returns>
    public async Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationDefinition = ValidateRequestMetadata(request);
        var workerParameters = ValidateWorkerParameters(
            request.CapabilityOperationId,
            ConvertParameters(request.Parameters));
        OperationExecutionResult? result = null;
        Exception? failure = null;
        var retireGeneration = false;
        var gateAcquired = false;
        var operationActive = false;
        Interlocked.Increment(ref _operationEntrants);
        try
        {
            if (Volatile.Read(ref _accepting) == 0)
            {
                throw new ObjectDisposedException(nameof(OfficialWorkerProfileExecutor));
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateAcquired = true;
            Interlocked.Increment(ref _activeOperations);
            operationActive = true;
            if (Volatile.Read(ref _accepting) == 0)
            {
                throw new ObjectDisposedException(nameof(OfficialWorkerProfileExecutor));
            }

            var pipe = Volatile.Read(ref _pipe) ??
                throw new ObjectDisposedException(nameof(OfficialWorkerProfileExecutor));
            var process = Volatile.Read(ref _process) ??
                throw new ObjectDisposedException(nameof(OfficialWorkerProfileExecutor));
            if (!IsProcessRunningConfirmed(process))
            {
                _ = EvaluateRecycleForNextAdmission();
                retireGeneration = true;
                result = WorkerFailure("worker.process.exited");
            }
            else
            {
                using var operationCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                operationCancellation.CancelAfter(_options.OperationTimeout);
                var workerRequest = CreateWorkerRequest(
                    request,
                    operationDefinition,
                    workerParameters);
                WorkerResponseV1? completedResponse = null;
                try
                {
                    var requestPayload = _codec.SerializeRequest(workerRequest);
                    if (RecycleReason != OfficialWorkerRecycleReason.None)
                    {
                        retireGeneration = true;
                        result = WorkerFailure("worker.operation.recycle-required");
                    }
                    else
                    {
                        await WorkerFrameCodec.WriteAsync(
                            pipe,
                            requestPayload,
                            Package01FeeWorkerContract.ProtocolLimits.MaximumFrameBytes,
                            operationCancellation.Token).ConfigureAwait(false);
                        var payload = await WorkerFrameCodec.ReadAsync(
                            pipe,
                            Package01FeeWorkerContract.ProtocolLimits.MaximumFrameBytes,
                            operationCancellation.Token).ConfigureAwait(false);
                        completedResponse = _codec.DeserializeResponse(payload);
                        result = ProjectResponse(
                            request,
                            workerRequest.RequestId,
                            completedResponse);
                    }
                }
                catch (OperationCanceledException exception)
                {
                    retireGeneration = true;
                    Interlocked.Exchange(ref _forceTerminationRequested, 1);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _ = _recyclePolicy.RecordSupervisorCancellation();
                        failure = exception;
                    }
                    else
                    {
                        _ = _recyclePolicy.RecordSupervisorTimeout();
                        result = WorkerFailure("worker.operation.timeout");
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or WorkerProtocolException or InvalidOperationException)
                {
                    retireGeneration = true;
                    Interlocked.Exchange(ref _forceTerminationRequested, 1);
                    _ = _recyclePolicy.RecordFatalFrameInterruption();
                    result = WorkerFailure("worker.operation.protocol-failure");
                }
                finally
                {
                    if (completedResponse is not null)
                    {
                        _ = _recyclePolicy.RecordCompletedResponse(
                            Stopwatch.GetTimestamp(),
                            completedResponse.Outcome == WorkerResponseOutcome.Timeout,
                            ObserveProcessResources(process));
                    }
                }
            }
        }
        finally
        {
            if (operationActive)
            {
                Interlocked.Decrement(ref _activeOperations);
            }

            if (gateAcquired)
            {
                _operationGate.Release();
            }

            Interlocked.Decrement(ref _operationEntrants);
        }

        if (retireGeneration)
        {
            try
            {
                await DisposeAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException cleanupFailure) when (
                string.Equals(
                    cleanupFailure.Message,
                    CleanupFailureMessage,
                    StringComparison.Ordinal))
            {
                // Operation outcome 已在上方唯一決定；固定 cleanup failure 只表示相同 executor owner
                // 尚未歸零。Runtime 仍持有非 accepting owner 並可重試，因此不得覆蓋 cancellation、
                // timeout、protocol 或 process-exit 結果，也不得 replay 已送出的要求。
            }
        }

        if (failure is not null)
        {
            throw new OperationCanceledException(
                "The official Dynamics worker operation was cancelled.",
                failure,
                cancellationToken);
        }

        return result ?? WorkerFailure("worker.operation.failed");
    }

    /// <summary>
    /// 取得不暴露 Process、Pipe、Task、Nonce、路徑或任何敏感資料的 bounded lifecycle counters。
    /// Readiness 只有在 admission 開放、process 被明確確認仍存活且 pipe 仍由本 instance 擁有時為 true；
    /// parent 已退出但 reader 未 terminal 時，Process 與 background owner 仍保持非零，避免假的 cleanup 基線。
    /// </summary>
    /// <returns>
    /// 目前 generation 的 readiness，以及 Process、Pipe、operation gate、output reader/task、output-read
    /// cancellation source、process-exit wait、operation entrant 與 active operation 的有限 ownership 計數。
    /// </returns>
    public OfficialWorkerLifecycleSnapshot GetLifecycleSnapshot()
    {
        var process = Volatile.Read(ref _process);
        var pipe = Volatile.Read(ref _pipe);
        var stdoutReader = Volatile.Read(ref _stdoutReader);
        var stderrReader = Volatile.Read(ref _stderrReader);
        var stdoutTask = Volatile.Read(ref _stdoutDiscardTask);
        var stderrTask = Volatile.Read(ref _stderrDiscardTask);
        var processExitWaitCount = Volatile.Read(ref _processExitWaitOwners);
        var outputTaskCount = (stdoutTask is null ? 0 : 1) +
            (stderrTask is null ? 0 : 1);
        var isReady = Volatile.Read(ref _accepting) != 0 &&
            process is not null &&
            IsProcessRunningConfirmed(process) &&
            pipe is not null;
        return new OfficialWorkerLifecycleSnapshot(
            isReady,
            process is null ? 0 : 1,
            pipe is null ? 0 : 1,
            outputTaskCount + processExitWaitCount,
            Volatile.Read(ref _activeOperations))
        {
            OwnedOperationGateCount = Volatile.Read(ref _operationGateOwned),
            OwnedOutputReaderCount = (stdoutReader is null ? 0 : 1) +
                (stderrReader is null ? 0 : 1),
            OwnedOutputTaskCount = outputTaskCount,
            OwnedOutputCancellationSourceCount =
                Volatile.Read(ref _outputReadCancellation) is null ? 0 : 1,
            OwnedProcessExitWaitCount = processExitWaitCount,
            OperationEntrantCount = Volatile.Read(ref _operationEntrants)
        };
    }

    /// <summary>
    /// 關閉此 immutable Worker generation。並行呼叫共享同一次 cleanup；若該次 cleanup 尚未能
    /// 確認 process exit、stdout/stderr drain 或 operation entrant 歸零，固定回報清理失敗並保留
    /// 尚未完成的 owner，讓下一次呼叫可重試，而不是快取 faulted task 或製造假的零基線。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Task task;
        lock (_disposeSync)
        {
            task = _disposeTask ??= DisposeAttemptAsync();
        }

        return new ValueTask(task);
    }

    /// <summary>
    /// 發布一次可共享的非同步 cleanup owner。先讓出一次排程，確保 task 已寫入
    /// <see cref="_disposeTask"/> 後才執行；失敗時只清除這個 lifecycle task owner，所有尚未完成的
    /// process、reader 與 gate reference 仍留在 instance 內，下一次 Dispose 才能安全接手。
    /// </summary>
    private async Task DisposeAttemptAsync()
    {
        await Task.Yield();
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupFailure)
        {
            lock (_disposeSync)
            {
                _disposeTask = null;
            }

            if (cleanupFailure is InvalidOperationException invalidOperationException &&
                string.Equals(
                    invalidOperationException.Message,
                    CleanupFailureMessage,
                    StringComparison.Ordinal))
            {
                throw;
            }

            // Dispose 是 process/pipe/reader 的唯一 cleanup boundary；非預期例外不可穿透並改寫
            // startup 或 operation 的第一原因，也不可洩漏 OS/path 細節。保留所有 resource field，
            // 清除本次 task owner，並只回報固定訊號讓下一次 Dispose 接手相同 graph。
            throw new InvalidOperationException(CleanupFailureMessage);
        }
    }

    /// <summary>
    /// 依序停止 admission、取得 operation gate、要求 graceful drain、必要時終止 process、關閉 pipe，
    /// 並等待 reader 與所有已進入 ExecuteAsync 的呼叫歸零。任何無法確認的結果都保留 owner 並以固定
    /// sanitized 例外失敗；只有完整成功後才 Dispose semaphore，避免 use-after-dispose、process/pipe
    /// handle 遺失與 memory/resource leakage。
    /// </summary>
    private async Task DisposeCoreAsync()
    {
        Volatile.Write(ref _accepting, 0);
        var cleanupDeadline = CleanupDeadline.Start(_options.DrainTimeout);
        var gateAcquired = false;
        var resourceCleanupComplete = false;
        var cleanupUncertain = false;
        try
        {
            var gateRemaining = cleanupDeadline.GetRemaining();
            if (gateRemaining > TimeSpan.Zero)
            {
                gateAcquired = await _operationGate.WaitAsync(gateRemaining).ConfigureAwait(false);
            }

            var pipe = Volatile.Read(ref _pipe);
            var process = Volatile.Read(ref _process);
            var forceTermination = Volatile.Read(ref _forceTerminationRequested) != 0;
            if (!forceTermination && gateAcquired && pipe is not null && process is not null &&
                IsProcessRunningConfirmed(process))
            {
                try
                {
                    var drainRemaining = cleanupDeadline.GetRemaining();
                    if (drainRemaining <= TimeSpan.Zero)
                    {
                        throw new TimeoutException();
                    }

                    var deadline = DateTimeOffset.UtcNow.Add(drainRemaining)
                        .UtcDateTime.Ticks;
                    using var drainWriteCancellation =
                        new CancellationTokenSource(drainRemaining);
                    await WorkerFrameCodec.WriteAsync(
                        pipe,
                        _codec.SerializeDrain(new WorkerDrainV1(
                            WorkerProtocolVersion.Current,
                            _processNonce,
                            deadline)),
                        Package01FeeWorkerContract.ProtocolLimits.MaximumFrameBytes,
                        drainWriteCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Failure to write a complete drain frame requires forced termination.
                }
            }

            var processExited = process is null;
            if (process is not null)
            {
                if (forceTermination)
                {
                    _ = TryKill(process);
                }
                else
                {
                    processExited = await WaitForExitConfirmedAsync(
                            process,
                            cleanupDeadline.GetRemaining())
                        .ConfigureAwait(false);
                    if (!processExited)
                    {
                        _ = TryKill(process);
                    }
                }

                // parent exit／kill confirmation 只證明 Worker process 狀態，不能證明 inherited output
                // write-end 已全部關閉。不得取消或 Dispose local reader 來製造 terminal task；若 descendant
                // 仍持有 handle，本次 attempt 必須保留 Process／reader owner 並以固定 failure 結束。
                if (!processExited)
                {
                    processExited = await WaitForExitConfirmedAsync(
                            process,
                            cleanupDeadline.GetRemaining())
                        .ConfigureAwait(false);
                }

                if (!processExited)
                {
                    // Kill API 與 exit query 都無法確認結果；保留 Process owner，不能把未知狀態當成退出。
                    cleanupUncertain = true;
                }
            }

            var pipeReleased = pipe is null;
            if (pipe is not null)
            {
                try
                {
                    pipe.Dispose();
                    _ = Interlocked.CompareExchange(ref _pipe, null, pipe);
                    pipeReleased = Volatile.Read(ref _pipe) is null;
                }
                catch
                {
                    cleanupUncertain = true;
                }
            }

            var stdoutTask = Volatile.Read(ref _stdoutDiscardTask);
            var stderrTask = Volatile.Read(ref _stderrDiscardTask);
            var backgroundTasksCompleted = await AwaitBackgroundTasksCompletionAsync(
                    stdoutTask,
                    stderrTask,
                    cleanupDeadline.GetRemaining())
                .ConfigureAwait(false);
            backgroundTasksCompleted =
                (stdoutTask is null || stdoutTask.IsCompleted) &&
                (stderrTask is null || stderrTask.IsCompleted);
            if (processExited && (stdoutTask is null || stdoutTask.IsCompleted))
            {
                var stdoutReader = Volatile.Read(ref _stdoutReader);
                if (TryDisposeTerminalOutputReader(stdoutReader))
                {
                    _ = Interlocked.CompareExchange(ref _stdoutReader, null, stdoutReader);
                    if (Volatile.Read(ref _stdoutReader) is null && stdoutTask is not null)
                    {
                        _ = Interlocked.CompareExchange(
                            ref _stdoutDiscardTask,
                            null,
                            stdoutTask);
                    }
                }
                else
                {
                    cleanupUncertain = true;
                }
            }

            if (processExited && (stderrTask is null || stderrTask.IsCompleted))
            {
                var stderrReader = Volatile.Read(ref _stderrReader);
                if (TryDisposeTerminalOutputReader(stderrReader))
                {
                    _ = Interlocked.CompareExchange(ref _stderrReader, null, stderrReader);
                    if (Volatile.Read(ref _stderrReader) is null && stderrTask is not null)
                    {
                        _ = Interlocked.CompareExchange(
                            ref _stderrDiscardTask,
                            null,
                            stderrTask);
                    }
                }
                else
                {
                    cleanupUncertain = true;
                }
            }

            if (Volatile.Read(ref _stdoutDiscardTask) is null &&
                Volatile.Read(ref _stderrDiscardTask) is null &&
                Volatile.Read(ref _stdoutReader) is null &&
                Volatile.Read(ref _stderrReader) is null)
            {
                var outputReadCancellation = Volatile.Read(ref _outputReadCancellation);
                if (outputReadCancellation is not null)
                {
                    try
                    {
                        outputReadCancellation.Dispose();
                        _ = Interlocked.CompareExchange(
                            ref _outputReadCancellation,
                            null,
                            outputReadCancellation);
                    }
                    catch
                    {
                        cleanupUncertain = true;
                    }
                }
            }

            processExited = process is null || processExited || HasExitedConfirmed(process);
            var processReleased = process is null;
            var outputResourcesReleased =
                Volatile.Read(ref _outputReadCancellation) is null &&
                Volatile.Read(ref _stdoutReader) is null &&
                Volatile.Read(ref _stderrReader) is null &&
                Volatile.Read(ref _stdoutDiscardTask) is null &&
                Volatile.Read(ref _stderrDiscardTask) is null;
            if (process is not null &&
                processExited &&
                backgroundTasksCompleted &&
                outputResourcesReleased)
            {
                try
                {
                    process.Dispose();
                    _ = Interlocked.CompareExchange(ref _process, null, process);
                    processReleased = Volatile.Read(ref _process) is null;
                }
                catch
                {
                    cleanupUncertain = true;
                }
            }

            resourceCleanupComplete =
                processExited &&
                processReleased &&
                pipeReleased &&
                backgroundTasksCompleted &&
                Volatile.Read(ref _outputReadCancellation) is null &&
                Volatile.Read(ref _stdoutReader) is null &&
                Volatile.Read(ref _stderrReader) is null &&
                Volatile.Read(ref _stdoutDiscardTask) is null &&
                Volatile.Read(ref _stderrDiscardTask) is null;
        }
        catch
        {
            cleanupUncertain = true;
        }
        finally
        {
            if (gateAcquired)
            {
                _operationGate.Release();
            }
        }

        var operationsDrained = await WaitForOperationsToDrainAsync(
                cleanupDeadline.GetRemaining())
            .ConfigureAwait(false);
        if (cleanupUncertain || !resourceCleanupComplete || !operationsDrained)
        {
            throw new InvalidOperationException(CleanupFailureMessage);
        }

        try
        {
            _operationGate.Dispose();
            Volatile.Write(ref _operationGateOwned, 0);
        }
        catch
        {
            throw new InvalidOperationException(CleanupFailureMessage);
        }
    }

    /// <summary>
    /// 依已完成 Gateway registry 驗證的 immutable operation definition 建立一次性 IPC request。
    /// 此方法只複製 bounded scalar parameter，不保存 caller dictionary，也不把 Profile、Credential、
    /// Token、Session 或 SDK 物件放入跨行程 envelope；request 完成後唯一保留者是目前的 stack scope。
    /// </summary>
    private WorkerRequestV1 CreateWorkerRequest(
        OperationExecutionRequest request,
        OperationDefinition operationDefinition,
        IReadOnlyDictionary<string, WorkerValue> workerParameters)
    {
        var deadline = DateTimeOffset.UtcNow.Add(_options.OperationTimeout)
            .UtcDateTime.Ticks;
        return new WorkerRequestV1(
            WorkerProtocolVersion.Current,
            _processNonce,
            Guid.NewGuid(),
            _options.ProfileGenerationId,
            operationDefinition.TemplateHash,
            request.CapabilityOperationId,
            deadline,
            workerParameters);
    }

    /// <summary>
    /// 將 ControlPlane 已正規化的 scalar 轉成 SDK-free WorkerValue。轉換結果是本次呼叫專用的
    /// bounded snapshot，避免 caller 在等待 operation gate 時修改原始 dictionary，亦避免跨 Session
    /// 共用 mutable request state。未列入正式 scalar contract 的型別一律 fail closed。
    /// </summary>
    private static IReadOnlyDictionary<string, WorkerValue> ConvertParameters(
        IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var converted = new Dictionary<string, WorkerValue>(parameters.Count, StringComparer.Ordinal);
        foreach (var pair in parameters)
        {
            var value = pair.Value switch
            {
                null => WorkerValue.Null(),
                bool boolean => WorkerValue.FromBoolean(boolean),
                long integer => WorkerValue.FromInt64(integer),
                decimal number => WorkerValue.FromDecimal(number),
                string text => WorkerValue.FromString(text),
                Guid guid => WorkerValue.FromGuid(guid),
                DateTimeOffset dateTime => WorkerValue.FromUtcDateTime(dateTime),
                _ => throw new InvalidOperationException(
                    "The official Dynamics worker parameter type is not permitted.")
            };
            converted.Add(pair.Key, value);
        }

        return converted;
    }

    /// <summary>
    /// 驗證回覆確實屬於本次唯一在途要求，再把有界 Worker 結果投影成 SDK-free 回應。
    /// RequestId 或 nonce 任一不符都代表 pipe 已可能失同步；呼叫端會終止整個 generation，
    /// 不會把其他要求的資料交給目前 caller，也不會嘗試在同一條 pipe 上繼續工作。
    /// </summary>
    /// <param name="request">目前已通過 allowlist 驗證的產品要求。</param>
    /// <param name="expectedRequestId">送入 Worker frame 的一次性要求識別碼。</param>
    /// <param name="response">從目前 generation pipe 解碼出的 Worker 回覆。</param>
    /// <returns>成功的 WhoAmI 投影或固定且不含上游細節的失敗結果。</returns>
    private OperationExecutionResult ProjectResponse(
        OperationExecutionRequest request,
        Guid expectedRequestId,
        WorkerResponseV1 response)
    {
        // 必須在讀取任何結果欄位前先完成要求身分比對，避免錯序或惡意 frame 造成跨要求資料誤配。
        if (response.RequestId != expectedRequestId ||
            !string.Equals(response.ProcessNonce, _processNonce, StringComparison.Ordinal))
        {
            throw new WorkerProtocolException(
                WorkerProtocolFailureCategory.InvalidEnvelope,
                "The official worker response identity is invalid.");
        }

        if (response.Outcome != WorkerResponseOutcome.Success)
        {
            if (response.Outcome == WorkerResponseOutcome.UpstreamFailure &&
                string.Equals(
                    request.CapabilityOperationId,
                    Package01FeeWorkerContract.CapabilityOperationId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    response.ErrorCode,
                    "crm.operation.result-too-large",
                    StringComparison.Ordinal))
            {
                return WorkerFailure("worker.operation.result-too-large");
            }

            return response.Outcome switch
            {
                WorkerResponseOutcome.InvalidRequest => WorkerFailure("worker.operation.invalid-request"),
                WorkerResponseOutcome.NotReady => WorkerFailure("worker.operation.not-ready"),
                WorkerResponseOutcome.Timeout => WorkerFailure("worker.operation.timeout"),
                WorkerResponseOutcome.UpstreamFailure => WorkerFailure("worker.operation.upstream-failure"),
                _ => WorkerFailure("worker.operation.protocol-failure")
            };
        }

        var result = response.Result;
        if (string.Equals(
                request.CapabilityOperationId,
                Package01FeeWorkerContract.CapabilityOperationId,
                StringComparison.Ordinal))
        {
            if (result is null)
            {
                throw new WorkerProtocolException(
                    WorkerProtocolFailureCategory.InvalidEnvelope,
                    "The official worker Package01 result is invalid.");
            }

            return OperationExecutionResult.Success(
                Package01FeeWorkerResponseProjector.Project(
                    request.CapabilityOperationId,
                    _options.WorkerVersion.ToCeVersion(),
                    result));
        }

        if (result?.Kind != WorkerValueKind.Object ||
            result.Members is null ||
            result.Members.Count != 3)
        {
            throw new WorkerProtocolException(
                WorkerProtocolFailureCategory.InvalidEnvelope,
                "The official worker identity result is invalid.");
        }

        var userId = ReadRequiredGuid(result.Members, "userId");
        var businessUnitId = ReadRequiredGuid(result.Members, "businessUnitId");
        var organizationId = ReadRequiredGuid(result.Members, "organizationId");
        return OperationExecutionResult.Success(
            OperationResponseData.ForWhoAmI(
                request.CapabilityOperationId,
                _options.WorkerVersion.ToCeVersion(),
                new WhoAmIResponseData
                {
                    UserId = userId,
                    BusinessUnitId = businessUnitId,
                    OrganizationId = organizationId
                }));
    }

    private static Guid ReadRequiredGuid(
        IReadOnlyDictionary<string, WorkerValue> members,
        string name)
    {
        if (!members.TryGetValue(name, out var value) ||
            value.Kind != WorkerValueKind.Guid ||
            !Guid.TryParseExact(value.Scalar, "N", out var guid) ||
            guid == Guid.Empty)
        {
            throw new WorkerProtocolException(
                WorkerProtocolFailureCategory.InvalidEnvelope,
                "The official worker identity result is invalid.");
        }

        return guid;
    }

    /// <summary>
    /// 在列舉 caller parameter collection 前驗證 profile、workload、registry 與固定參數數量。
    /// 此固定成本 gate 防止繞過 ControlPlane 的呼叫端迫使 Supervisor 建立大型 snapshot；失敗時
    /// 尚未寫入 pipe、建立 request frame 或接觸 endpoint/secret，也不會 fallback 到其他 transport。
    /// </summary>
    private OperationDefinition ValidateRequestMetadata(OperationExecutionRequest request)
    {
        if (!string.Equals(request.ProfileAlias, _options.ProfileAlias, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.WorkloadSubjectId) ||
            request.Parameters is null ||
            !Package01OperationRegistry.TryGet(
                request.CapabilityOperationId,
                out var operationDefinition) ||
            operationDefinition is null ||
            !IsSupportedWorkerParameterCount(
                request.CapabilityOperationId,
                request.Parameters.Count))
        {
            throw new InvalidOperationException(
                "The official Dynamics worker operation is not permitted.");
        }

        return operationDefinition;
    }

    /// <summary>
    /// 在列舉 caller collection 前只以 operation ID 與 Count 做零配置 shape gate。Package01
    /// 日期區間固定接受三個必要欄與一個 optional compatibility 欄；typed value 與精確名稱由下一層
    /// shared contract 驗證，未知 operation 不會取得 pipe 或 SDK 呼叫機會。
    /// </summary>
    private static bool IsSupportedWorkerParameterCount(
        string capabilityOperationId,
        int parameterCount)
    {
        if (OfficialWorkerOperations.IsSupportedIdentityParameterCount(
                capabilityOperationId,
                parameterCount))
        {
            return true;
        }

        return string.Equals(
                capabilityOperationId,
                Package01FeeWorkerContract.CapabilityOperationId,
                StringComparison.Ordinal) &&
            parameterCount is 3 or 4;
    }

    /// <summary>
    /// 驗證完成 bounded scalar 轉換後的精確參數名稱、型別與值。這是 request frame 建立前的
    /// 第二道封閉 gate；方法不保存 snapshot，失敗不會啟動另一個 Worker 或改走其他 Profile。
    /// </summary>
    private static IReadOnlyDictionary<string, WorkerValue> ValidateWorkerParameters(
        string capabilityOperationId,
        IReadOnlyDictionary<string, WorkerValue> workerParameters)
    {
        if (OfficialWorkerOperations.IsSupportedIdentityOperation(
                capabilityOperationId,
                workerParameters))
        {
            return workerParameters;
        }

        if (string.Equals(
                capabilityOperationId,
                Package01FeeWorkerContract.CapabilityOperationId,
                StringComparison.Ordinal))
        {
            try
            {
                return Package01FeeWorkerContract.ValidateAndNormalizeParameters(
                    workerParameters);
            }
            catch (WorkerProtocolException)
            {
                // 不附帶 parameter name/value 或 inner exception，避免 caller-controlled 資料進入
                // Supervisor error surface；共用 contract 仍是唯一 shape owner。
            }
        }

        throw new InvalidOperationException(
            "The official Dynamics worker operation is not permitted.");
    }

    private static OperationExecutionResult WorkerFailure(string errorCode) =>
        OperationExecutionResult.Failure(
            errorCode,
            "The official Dynamics worker operation failed.");

    private static NamedPipeServerStream CreatePipe(string pipeName) =>
        new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            4096,
            4096);

    private static Process StartProcess(
        OfficialWorkerProfileOptions options,
        string pipeName,
        string processNonce)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.WorkerExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(options.WorkerExecutablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false
        };
        foreach (var argument in new[]
                 {
                     "--pipe", pipeName,
                     "--nonce", processNonce,
                     "--protocol", WorkerProtocolVersion.Current.ToString(CultureInfo.InvariantCulture),
                     "--worker-kind", options.WorkerVersion.ToWorkerKind().ToString(),
                     "--package-lock", options.PackageLockId,
                     "--profile-generation", options.ProfileGenerationId
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        OfficialWorkerProcessEnvironment.Configure(startInfo);

        return Process.Start(startInfo) ??
            throw new InvalidOperationException("The official Dynamics worker could not be started.");
    }

    private static async Task VerifyExecutableHashAsync(
        OfficialWorkerProfileOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                options.WorkerExecutablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    hash,
                    Convert.FromHexString(options.WorkerExecutableSha256)))
            {
                throw new InvalidOperationException(
                    "The official Dynamics worker executable identity is invalid.");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FormatException)
        {
            throw new InvalidOperationException(
                "The official Dynamics worker executable identity is invalid.");
        }
    }

    private static void ValidateReady(
        OfficialWorkerProfileOptions options,
        string processNonce,
        WorkerReadyV1 ready)
    {
        if (ready.ProtocolVersion != WorkerProtocolVersion.Current ||
            !string.Equals(ready.ProcessNonce, processNonce, StringComparison.Ordinal) ||
            ready.WorkerKind != options.WorkerVersion.ToWorkerKind() ||
            !string.Equals(ready.PackageLockId, options.PackageLockId, StringComparison.Ordinal) ||
            !string.Equals(
                ready.ProfileGenerationId,
                options.ProfileGenerationId,
                StringComparison.Ordinal) ||
            !string.Equals(
                ready.CeVersion,
                options.WorkerVersion.ToCeVersion(),
                StringComparison.Ordinal))
        {
            throw new WorkerProtocolException(
                WorkerProtocolFailureCategory.InvalidEnvelope,
                "The official Dynamics worker readiness identity is invalid.");
        }
    }

    private static void ValidateOptions(OfficialWorkerProfileOptions options)
    {
        if (!Enum.IsDefined(options.WorkerVersion) ||
            !IsSafeIdentifier(options.ProfileAlias) ||
            !IsSafeIdentifier(options.ProfileGenerationId) ||
            !IsSafeIdentifier(options.PackageLockId) ||
            !Path.IsPathFullyQualified(options.WorkerExecutablePath) ||
            options.WorkerExecutableSha256.Length != 64 ||
            options.WorkerExecutableSha256.Any(character => !Uri.IsHexDigit(character)) ||
            !IsBoundedTimeout(options.StartupTimeout) ||
            !IsBoundedTimeout(options.OperationTimeout) ||
            !IsBoundedTimeout(options.DrainTimeout) ||
            options.RecyclePolicyOptions is null)
        {
            throw new ArgumentException("The official Dynamics worker profile is invalid.");
        }
    }

    /// <summary>
    /// Reads a scalar resource snapshot from the executor-owned process without exposing its handle.
    /// </summary>
    private static OfficialWorkerResourceObservation ObserveProcessResources(Process process)
    {
        try
        {
            process.Refresh();
            if (process.HasExited)
            {
                return OfficialWorkerResourceObservation.Unreadable;
            }

            return new OfficialWorkerResourceObservation(
                isReadable: true,
                privateBytes: process.PrivateMemorySize64,
                workingSetBytes: process.WorkingSet64);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            return OfficialWorkerResourceObservation.Unreadable;
        }
    }

    private static bool IsSafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumIdentifierLength &&
        value.All(character =>
            character is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9' or '-' or '_' or '.');

    private static bool IsBoundedTimeout(TimeSpan timeout) =>
        timeout > TimeSpan.Zero && timeout <= MaximumConfiguredTimeout;

    /// <summary>
    /// 持續讀取一條 redirected stdout/stderr stream，但不累積或記錄 Worker 內容。Executor field 是
    /// StreamReader 的唯一 owner；此 task 只暫借 reader，並在 finally 清除及歸還 rented buffer。cleanup
    /// 不得因 parent exit/kill 就取消或 Dispose reader 來偽造 terminal；descendant 若仍持有 inherited
    /// write-end，本 task、reader 與 Process 必須保持可見 owner，待 handle 真正關閉後再由下一次 retry 釋放。
    /// </summary>
    /// <param name="reader">由本 executor 所擁有 Process 提供的單一 redirected output reader。</param>
    /// <param name="cancellationToken">由同一 executor 擁有；正常 cleanup 不取消它，僅在 reader terminal 後 Dispose source。</param>
    private static async Task DiscardOutputAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<char>.Shared.Rent(1024);
        try
        {
            while (await reader.ReadAsync(
                       buffer.AsMemory(0, 1024),
                       cancellationToken).ConfigureAwait(false) != 0)
            {
                // Deliberately discard without accumulating or logging worker output.
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 僅在 OS 已明確確認 parent process exit，且對應 output reader task 已進入 terminal state 後，
    /// Dispose 單一 redirected reader。任何例外都代表 cleanup 結果未知；呼叫端必須保留 reader、Task
    /// 與 Process owner 供下次 serialized retry，不能先清 field 或用 local close 偽造 descendant handle
    /// 已釋放。此方法不終止 descendant、不取消 pending read，也不建立額外 background owner。
    /// </summary>
    /// <returns>reader 已安全 Dispose，或原本不存在；失敗時回傳 false 並保留精確 owner。</returns>
    private static bool TryDisposeTerminalOutputReader(StreamReader? reader)
    {
        if (reader is null)
        {
            return true;
        }

        try
        {
            reader.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 只有 Process API 明確回報仍在執行時才回傳 true；handle 已失效或狀態不可讀都視為未確認，
    /// readiness 必須 fail closed，不能把未知狀態當成可接受的 Worker generation。
    /// </summary>
    private static bool IsProcessRunningConfirmed(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 嘗試終止完整 process tree。此方法只表示 kill 呼叫是否已送出或 process 已確認退出；
    /// caller 仍必須再以 <see cref="WaitForExitConfirmedAsync"/> 驗證真正退出，不能吞掉不確定結果。
    /// </summary>
    private static bool TryKill(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return HasExitedConfirmed(process);
        }
    }

    /// <summary>
    /// 在有限期限內等待並重新讀取 Process 狀態。每次呼叫以可取消的 <c>WaitForExitAsync</c> 綁定一個
    /// attempt-local timeout source，並用明確 counter 涵蓋 OS event subscription、timer 與 registration；
    /// finally 在返回前歸零並 Dispose timeout source。timeout、disposed handle 或 OS 查詢失敗一律回傳
    /// false，讓 lifecycle owner 保留 Process reference 供下一次 serialized cleanup 重試，不遺留 hidden task。
    /// </summary>
    private async Task<bool> WaitForExitConfirmedAsync(
        Process process,
        TimeSpan timeout)
    {
        if (HasExitedConfirmed(process))
        {
            return true;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        Interlocked.Increment(ref _processExitWaitOwners);
        try
        {
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
            {
            }
            catch
            {
            }

            return HasExitedConfirmed(process);
        }
        finally
        {
            Interlocked.Decrement(ref _processExitWaitOwners);
        }
    }

    /// <summary>
    /// 等待 stdout/stderr reader 進入 terminal state，但不清除 task owner。reader fault 會在有限 await
    /// 中被觀察，且其 finally 已歸還 pooled buffer；StreamReader 仍須等 Process exit 明確確認後才 Dispose。
    /// 唯有尚未完成的 reader task 必須保留並由後續 serialized retry 繼續等待。
    /// </summary>
    private static async Task<bool> AwaitBackgroundTasksCompletionAsync(
        Task? stdoutTask,
        Task? stderrTask,
        TimeSpan timeout)
    {
        if ((stdoutTask is null || stdoutTask.IsCompleted) &&
            (stderrTask is null || stderrTask.IsCompleted))
        {
            return true;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        var deadline = CleanupDeadline.Start(timeout);
        await AwaitBackgroundTaskCompletionAsync(
                stdoutTask,
                deadline.GetRemaining())
            .ConfigureAwait(false);
        await AwaitBackgroundTaskCompletionAsync(
                stderrTask,
                deadline.GetRemaining())
            .ConfigureAwait(false);
        return (stdoutTask is null || stdoutTask.IsCompleted) &&
            (stderrTask is null || stderrTask.IsCompleted);
    }

    /// <summary>
    /// 等待一個已由 executor snapshot 明確計數的 output reader task 進入 terminal state。方法只輪詢
    /// <see cref="Task.IsCompleted"/>，並完整 await 每個有限 <see cref="Task.Delay(TimeSpan)"/>；它不對
    /// incomplete reader task 呼叫 WaitAsync／WhenAll，因此 timeout 返回後不會在 antecedent 上留下 wrapper
    /// continuation。底層 reader task 仍由 executor field 唯一擁有，完成時在目前 stack 觀察其 fault。
    /// </summary>
    private static async Task AwaitBackgroundTaskCompletionAsync(
        Task? task,
        TimeSpan timeout)
    {
        if (task is null)
        {
            return;
        }

        if (!task.IsCompleted)
        {
            if (timeout <= TimeSpan.Zero)
            {
                return;
            }

            var deadline = CleanupDeadline.Start(timeout);
            while (!task.IsCompleted)
            {
                var remaining = deadline.GetRemaining();
                if (remaining <= TimeSpan.Zero)
                {
                    return;
                }

                await Task.Delay(
                        remaining < TimeSpan.FromMilliseconds(10)
                            ? remaining
                            : TimeSpan.FromMilliseconds(10))
                    .ConfigureAwait(false);
            }
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Faulted reader task 仍是 terminal；await 在此觀察例外，field 只在 process exit 確認後清除。
        }
    }

    /// <summary>
    /// 等待所有已進入 ExecuteAsync 的 caller（包含尚未取得 semaphore 的 waiter）離開。使用有限的
    /// 非同步 polling，避免舊有 Task.Yield busy loop 佔用 ThreadPool；accepting 已先關閉，因此歸零後
    /// 不會再有 caller 觸碰即將 Dispose 的 gate。
    /// </summary>
    private async Task<bool> WaitForOperationsToDrainAsync(TimeSpan timeout)
    {
        if (Volatile.Read(ref _operationEntrants) == 0)
        {
            return true;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        var startedAt = Stopwatch.GetTimestamp();
        while (Volatile.Read(ref _operationEntrants) != 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            if (elapsed >= timeout)
            {
                return false;
            }

            var remaining = timeout - elapsed;
            await Task.Delay(
                    remaining < TimeSpan.FromMilliseconds(10)
                        ? remaining
                        : TimeSpan.FromMilliseconds(10))
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// 只在 Process API 明確回報退出時回傳 true；任何查詢例外都保留 owner，避免把未知狀態
    /// 誤判為已退出而遺失 OS process handle 或留下孤兒 Worker。
    /// </summary>
    private static bool HasExitedConfirmed(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 表示單次 cleanup attempt 的唯一 monotonic absolute deadline。所有 stage 只取得目前剩餘時間；
    /// wall clock 只用來編碼送給 Worker 的 UTC drain fence，不參與本行程 timeout 計算，因此系統時間調整
    /// 不會讓 cleanup 延長。此值不建立 Timer、Cancellation registration 或背景 Task。
    /// </summary>
    private readonly struct CleanupDeadline
    {
        private readonly long _startedAt;
        private readonly TimeSpan _timeout;

        private CleanupDeadline(long startedAt, TimeSpan timeout)
        {
            _startedAt = startedAt;
            _timeout = timeout;
        }

        /// <summary>在 cleanup attempt 入口擷取唯一 Stopwatch timestamp 與有限 timeout。</summary>
        internal static CleanupDeadline Start(TimeSpan timeout) =>
            new(Stopwatch.GetTimestamp(), timeout);

        /// <summary>
        /// 依相同起點計算尚可使用的時間；期限已到時固定回傳零，stage 不得重新建立完整 timeout。
        /// </summary>
        internal TimeSpan GetRemaining()
        {
            var elapsed = Stopwatch.GetElapsedTime(_startedAt);
            return elapsed >= _timeout ? TimeSpan.Zero : _timeout - elapsed;
        }
    }
}
