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
/// Owns exactly one worker process generation, one local named pipe, and its bounded
/// background stream drains. The instance never retains caller identity, request data,
/// credential material, CRM SDK objects, or mutable state shared with another profile.
/// </summary>
public sealed class OfficialWorkerProfileExecutor : IAsyncDisposable
{
    private const int MaximumIdentifierLength = 128;
    private static readonly TimeSpan MaximumConfiguredTimeout = TimeSpan.FromMinutes(10);

    private readonly OfficialWorkerProfileOptions _options;
    private readonly string _processNonce;
    private readonly WorkerEnvelopeCodec _codec;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _disposeSync = new();

    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private Task? _stdoutDiscardTask;
    private Task? _stderrDiscardTask;
    private Task? _disposeTask;
    private int _accepting;
    private int _activeOperations;
    private int _forceTerminationRequested;
    private int _gateDisposed;

    private OfficialWorkerProfileExecutor(
        OfficialWorkerProfileOptions options,
        string processNonce,
        Process process,
        NamedPipeServerStream pipe,
        Task stdoutDiscardTask,
        Task stderrDiscardTask)
    {
        _options = options;
        _processNonce = processNonce;
        _process = process;
        _pipe = pipe;
        _stdoutDiscardTask = stdoutDiscardTask;
        _stderrDiscardTask = stderrDiscardTask;
        _codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);
        Volatile.Write(ref _accepting, 1);
    }

    /// <summary>
    /// Verifies the executable hash before creating IPC or a process, starts the worker
    /// with non-secret bootstrap values only, and publishes the executor only after the
    /// nonce-bound READY envelope matches every immutable selector.
    /// </summary>
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
        Process? process = null;
        Task? stdoutDiscardTask = null;
        Task? stderrDiscardTask = null;

        try
        {
            process = StartProcess(options, pipeName, processNonce);
            stdoutDiscardTask = DiscardOutputAsync(process.StandardOutput);
            stderrDiscardTask = DiscardOutputAsync(process.StandardError);

            using var startupCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupCancellation.CancelAfter(options.StartupTimeout);
            await pipe.WaitForConnectionAsync(startupCancellation.Token).ConfigureAwait(false);

            var payload = await WorkerFrameCodec.ReadAsync(
                pipe,
                WorkerProtocolLimits.Default.MaximumFrameBytes,
                startupCancellation.Token).ConfigureAwait(false);
            var ready = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default)
                .DeserializeReady(payload);
            ValidateReady(options, processNonce, ready);
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "The official Dynamics worker exited before readiness was published.");
            }

            return new OfficialWorkerProfileExecutor(
                options,
                processNonce,
                process,
                pipe,
                stdoutDiscardTask,
                stderrDiscardTask);
        }
        catch
        {
            pipe.Dispose();
            if (process is not null)
            {
                TryKill(process);
                await WaitForExitIgnoringFailureAsync(process, options.DrainTimeout)
                    .ConfigureAwait(false);
                await AwaitBackgroundTasksIgnoringFailureAsync(
                    stdoutDiscardTask,
                    stderrDiscardTask,
                    options.DrainTimeout).ConfigureAwait(false);
                process.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Executes the currently allowlisted identity operation. Caller cancellation remains
    /// cancellation; an internal timeout returns a fixed failure. Any interrupted frame or
    /// protocol failure retires the complete process generation before returning.
    /// </summary>
    public async Task<OperationExecutionResult> ExecuteAsync(
        OperationExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationDefinition = ValidateRequestMetadata(request);
        var workerParameters = ConvertParameters(request.Parameters);
        ValidateWorkerParameters(request.CapabilityOperationId, workerParameters);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        OperationExecutionResult? result = null;
        Exception? failure = null;
        var retireGeneration = false;
        Interlocked.Increment(ref _activeOperations);
        try
        {
            if (Volatile.Read(ref _accepting) == 0)
            {
                throw new ObjectDisposedException(nameof(OfficialWorkerProfileExecutor));
            }

            var pipe = Volatile.Read(ref _pipe) ??
                throw new ObjectDisposedException(nameof(OfficialWorkerProfileExecutor));
            var process = Volatile.Read(ref _process) ??
                throw new ObjectDisposedException(nameof(OfficialWorkerProfileExecutor));
            if (process.HasExited)
            {
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
                try
                {
                    await WorkerFrameCodec.WriteAsync(
                        pipe,
                        _codec.SerializeRequest(workerRequest),
                        WorkerProtocolLimits.Default.MaximumFrameBytes,
                        operationCancellation.Token).ConfigureAwait(false);
                    var payload = await WorkerFrameCodec.ReadAsync(
                        pipe,
                        WorkerProtocolLimits.Default.MaximumFrameBytes,
                        operationCancellation.Token).ConfigureAwait(false);
                    var response = _codec.DeserializeResponse(payload);
                    result = ProjectResponse(request, workerRequest.RequestId, response);
                }
                catch (OperationCanceledException exception)
                {
                    retireGeneration = true;
                    Interlocked.Exchange(ref _forceTerminationRequested, 1);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        failure = exception;
                    }
                    else
                    {
                        result = WorkerFailure("worker.operation.timeout");
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or WorkerProtocolException or InvalidOperationException)
                {
                    retireGeneration = true;
                    Interlocked.Exchange(ref _forceTerminationRequested, 1);
                    result = WorkerFailure("worker.operation.protocol-failure");
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeOperations);
            _operationGate.Release();
        }

        if (retireGeneration)
        {
            await DisposeAsync().ConfigureAwait(false);
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

    public OfficialWorkerLifecycleSnapshot GetLifecycleSnapshot()
    {
        var process = Volatile.Read(ref _process);
        var isReady = Volatile.Read(ref _accepting) != 0 &&
            process is not null &&
            !HasExitedIgnoringFailure(process) &&
            Volatile.Read(ref _pipe) is not null;
        return new OfficialWorkerLifecycleSnapshot(
            isReady,
            process is null ? 0 : 1,
            Volatile.Read(ref _pipe) is null ? 0 : 1,
            (Volatile.Read(ref _stdoutDiscardTask) is null ? 0 : 1) +
            (Volatile.Read(ref _stderrDiscardTask) is null ? 0 : 1),
            Volatile.Read(ref _activeOperations));
    }

    public ValueTask DisposeAsync()
    {
        Task task;
        lock (_disposeSync)
        {
            task = _disposeTask ??= DisposeCoreAsync();
        }

        return new ValueTask(task);
    }

    private async Task DisposeCoreAsync()
    {
        Volatile.Write(ref _accepting, 0);
        var gateAcquired = false;
        try
        {
            using var drainCancellation = new CancellationTokenSource(_options.DrainTimeout);
            try
            {
                await _operationGate.WaitAsync(drainCancellation.Token).ConfigureAwait(false);
                gateAcquired = true;
            }
            catch (OperationCanceledException)
            {
                // A stuck pipe operation invalidates the generation; forced process exit is
                // the bounded cleanup boundary for SDK/WCF state outside this process.
            }

            var pipe = Volatile.Read(ref _pipe);
            var process = Volatile.Read(ref _process);
            var forceTermination = Volatile.Read(ref _forceTerminationRequested) != 0;
            if (!forceTermination && gateAcquired && pipe is not null && process is not null &&
                !HasExitedIgnoringFailure(process))
            {
                try
                {
                    var deadline = DateTimeOffset.UtcNow.Add(_options.DrainTimeout)
                        .UtcDateTime.Ticks;
                    await WorkerFrameCodec.WriteAsync(
                        pipe,
                        _codec.SerializeDrain(new WorkerDrainV1(
                            WorkerProtocolVersion.Current,
                            _processNonce,
                            deadline)),
                        WorkerProtocolLimits.Default.MaximumFrameBytes,
                        CancellationToken.None).WaitAsync(_options.DrainTimeout)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Failure to write a complete drain frame requires forced termination.
                }
            }

            if (process is not null && forceTermination)
            {
                TryKill(process);
            }
            else if (process is not null &&
                     !await WaitForExitIgnoringFailureAsync(process, _options.DrainTimeout)
                         .ConfigureAwait(false))
            {
                TryKill(process);
            }

            if (process is not null)
            {
                await WaitForExitIgnoringFailureAsync(process, _options.DrainTimeout)
                    .ConfigureAwait(false);
            }

            Interlocked.Exchange(ref _pipe, null)?.Dispose();
            if (!gateAcquired)
            {
                await WaitForOperationsToDrainAsync(_options.DrainTimeout).ConfigureAwait(false);
            }

            var stdoutTask = Interlocked.Exchange(ref _stdoutDiscardTask, null);
            var stderrTask = Interlocked.Exchange(ref _stderrDiscardTask, null);
            await AwaitBackgroundTasksIgnoringFailureAsync(
                stdoutTask,
                stderrTask,
                _options.DrainTimeout).ConfigureAwait(false);
            Interlocked.Exchange(ref _process, null)?.Dispose();
        }
        finally
        {
            if (gateAcquired)
            {
                _operationGate.Release();
            }

            if (Interlocked.Exchange(ref _gateDisposed, 1) == 0)
            {
                _operationGate.Dispose();
            }
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
            !OfficialWorkerOperations.IsSupportedIdentityParameterCount(
                request.CapabilityOperationId,
                request.Parameters.Count))
        {
            throw new InvalidOperationException(
                "The official Dynamics worker operation is not permitted.");
        }

        return operationDefinition;
    }

    /// <summary>
    /// 驗證完成 bounded scalar 轉換後的精確參數名稱、型別與值。這是 request frame 建立前的
    /// 第二道封閉 gate；方法不保存 snapshot，失敗不會啟動另一個 Worker 或改走其他 Profile。
    /// </summary>
    private static void ValidateWorkerParameters(
        string capabilityOperationId,
        IReadOnlyDictionary<string, WorkerValue> workerParameters)
    {
        if (!OfficialWorkerOperations.IsSupportedIdentityOperation(
                capabilityOperationId,
                workerParameters))
        {
            throw new InvalidOperationException(
                "The official Dynamics worker operation is not permitted.");
        }
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
            !IsBoundedTimeout(options.DrainTimeout))
        {
            throw new ArgumentException("The official Dynamics worker profile is invalid.");
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

    private static async Task DiscardOutputAsync(StreamReader reader)
    {
        var buffer = ArrayPool<char>.Shared.Rent(1024);
        try
        {
            while (await reader.ReadAsync(buffer.AsMemory(0, 1024)).ConfigureAwait(false) != 0)
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
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<char>.Shared.Return(buffer);
            reader.Dispose();
        }
    }

    private static bool HasExitedIgnoringFailure(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static async Task<bool> WaitForExitIgnoringFailureAsync(
        Process process,
        TimeSpan timeout)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return HasExitedIgnoringFailure(process);
        }
    }

    private static async Task AwaitBackgroundTasksIgnoringFailureAsync(
        Task? stdoutTask,
        Task? stderrTask,
        TimeSpan timeout)
    {
        var tasks = new[] { stdoutTask, stderrTask }
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();
        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(timeout).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task WaitForOperationsToDrainAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref _activeOperations) != 0 && DateTime.UtcNow < deadline)
        {
            await Task.Yield();
        }
    }
}
