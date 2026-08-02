using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Data.SqlClient;

namespace SpeechMessage.Dynamics.Tests.Support;

/// <summary>
/// 父行程交給測試專用 worker 的固定啟動識別資料。
/// 此值物件只允許每次測試產生的 run id、組織識別碼、固定 worker 標籤與 nonce，
/// 因此不會成為傳遞連線字串、認證、權杖或其他可跨測試洩漏狀態的通道。
/// </summary>
internal sealed record WorkerStartRequest(
    string RunId,
    Guid OrganizationId,
    string WorkerLabel,
    string Nonce)
{
    /// <summary>
    /// 建立經過固定格式檢查的啟動要求。
    /// 驗證在建立子行程前完成，讓父行程能 fail closed，且避免未驗證文字進入命令列或協定邊界。
    /// </summary>
    internal static WorkerStartRequest Create(
        string runId,
        Guid organizationId,
        string workerLabel,
        string nonce)
    {
        if (!WorkerProtocol.IsLowercaseHex32(runId) ||
            !WorkerProtocol.IsLowercaseHex32(nonce) ||
            organizationId == Guid.Empty ||
            !WorkerProtocol.IsWorkerLabel(workerLabel))
        {
            throw new ArgumentException("The test-only worker start request is invalid.");
        }

        return new WorkerStartRequest(runId, organizationId, workerLabel, nonce);
    }

    /// <summary>
    /// 取得只由父端新產生 run id 組成的 durable SQL namespace。
    /// 這個前綴是父端 fencing 與清理 SQL 的唯一可變範圍；任何不完全相符的 namespace 都必須拒絕，
    /// 避免測試工具意外修改既有部署或其他測試的 lease。
    /// </summary>
    internal string LeaseNamespaceId => "cross-process-" + RunId;
}

/// <summary>
/// 父端可傳送給 worker 的固定協定命令。
/// 命令列舉不允許呼叫端傳入任意文字、連線字串或診斷參數，讓 stdin 保持狹窄且可完整驗證。
/// </summary>
internal enum WorkerCommand
{
    /// <summary>要求 worker 取得 durable host slot。</summary>
    AcquireHost,

    /// <summary>要求 worker 在既有 host slot 下保留一個 admission permit。</summary>
    AcquireWork,

    /// <summary>要求 worker 開始 drain，但在既有 permit 尚未釋放前保留 lease。</summary>
    BeginDrain,

    /// <summary>要求 worker 釋放目前唯一保留的 admission permit。</summary>
    ReleaseWork,

    /// <summary>要求 worker 等待既有 drain 完成。</summary>
    AwaitDrain,

    /// <summary>要求 worker 執行固定的 coordinator outage 清理探測。</summary>
    OutageProbe,

    /// <summary>要求 worker 以受控路徑停止。</summary>
    Stop
}

/// <summary>
/// 測試專用跨行程協定可接受的固定事件種類。
/// 列舉不攜帶例外、stderr 或任意文字，避免子行程資料越過測試的信任邊界。
/// </summary>
internal enum WorkerEventKind
{
    /// <summary>worker 已完成啟動參數檢查並準備接收固定命令。</summary>
    Ready,

    /// <summary>worker 已取得 durable host slot，並附帶正數 fencing token。</summary>
    HostReady,

    /// <summary>worker 無法取得 durable host slot。</summary>
    HostDenied,

    /// <summary>worker 已保留 admission permit，並附帶正數固定值。</summary>
    WorkHeld,

    /// <summary>worker 無法保留 admission permit。</summary>
    WorkDenied,

    /// <summary>worker 已開始 drain。</summary>
    DrainBegin,

    /// <summary>worker 已釋放目前保留的 admission permit。</summary>
    WorkReleased,

    /// <summary>worker 偵測到 durable lease 的 fencing 已遺失。</summary>
    LeaseLost,

    /// <summary>worker 已完成 drain 與資源釋放。</summary>
    Drained,

    /// <summary>worker 已完成固定 coordinator outage 探測與清理。</summary>
    OutageClean,

    /// <summary>worker 已接收 STOP 並完成受控停止。</summary>
    Stopped,

    /// <summary>worker 只以固定失敗分類回報失敗。</summary>
    Fail
}

/// <summary>
/// worker 可回報的固定失敗分類。
/// 分類刻意不包含例外型別、SQL 訊息、主機名稱、連線字串或堆疊內容，使失敗可判斷但不跨行程洩漏診斷資料。
/// </summary>
internal enum WorkerFailureCategory
{
    /// <summary>啟動引數不符合固定契約。</summary>
    Arguments,

    /// <summary>stdin 或 stdout 固定協定不符合契約。</summary>
    Protocol,

    /// <summary>admission 或 durable host slot 作業失敗。</summary>
    Admission,

    /// <summary>固定 outage 探測未完成。</summary>
    Outage,

    /// <summary>worker 的生命週期或清理順序不成立。</summary>
    Lifecycle
}

/// <summary>
/// 已解析的 worker 協定事件。
/// <see cref="PositiveValue" /> 僅能是 HOST_READY 或 WORK_HELD 的正數欄位；
/// 其他事件不攜帶自由文字，<see cref="FailureCategory" /> 也只能在 FAIL 事件中存在。
/// </summary>
internal sealed record WorkerEvent(
    WorkerEventKind Kind,
    string Nonce,
    long? PositiveValue,
    WorkerFailureCategory? FailureCategory)
{
    /// <summary>
    /// 取得 HOST_READY 事件所公布的 fencing token。
    /// 其他事件回傳 <see langword="null" />，避免呼叫端誤把 WORK_HELD 的固定正數值當成 lease 身分。
    /// </summary>
    internal long? FencingToken => Kind == WorkerEventKind.HostReady ? PositiveValue : null;
}

/// <summary>
/// 父端與 worker 共用的固定 ASCII 協定語法。
/// 此型別只格式化已宣告命令並解析已宣告事件；所有格式錯誤都會轉成不含原始子行程資料的受控失敗。
/// </summary>
internal static class WorkerProtocol
{
    /// <summary>固定協定版本字面值。</summary>
    internal const string Version = "P1";

    /// <summary>每一個協定資料列可接受的最大 ASCII 位元組數，不包含 CRLF 結尾。</summary>
    internal const int MaximumLineBytes = 128;

    /// <summary>
    /// 將列舉命令格式化為唯一的父端協定資料列。
    /// 輸出不含換行符號，讓唯一 stdin owner 可以明確決定輸入邊界與 flush 時機。
    /// </summary>
    internal static string FormatCommand(WorkerCommand command, string nonce)
    {
        if (!IsLowercaseHex32(nonce) || !Enum.IsDefined(command))
        {
            throw new ArgumentException("The worker command is invalid.");
        }

        return $"{Version} {nonce} {command switch
        {
            WorkerCommand.AcquireHost => "ACQUIRE_HOST",
            WorkerCommand.AcquireWork => "ACQUIRE_WORK",
            WorkerCommand.BeginDrain => "BEGIN_DRAIN",
            WorkerCommand.ReleaseWork => "RELEASE_WORK",
            WorkerCommand.AwaitDrain => "AWAIT_DRAIN",
            WorkerCommand.OutageProbe => "OUTAGE_PROBE",
            WorkerCommand.Stop => "STOP",
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        }}";
    }

    /// <summary>
    /// 解析一列 worker stdout 事件並驗證它與目前 worker 的 nonce 完全相符。
    /// 此方法絕不在錯誤訊息中回顯未受信任資料列，避免 raw stdout 進入 xUnit、CI 日誌或上層例外。
    /// </summary>
    internal static WorkerEvent ParseWorkerEvent(string line, string expectedNonce)
    {
        if (!IsLowercaseHex32(expectedNonce) ||
            string.IsNullOrEmpty(line) ||
            line.Length > MaximumLineBytes ||
            line.IndexOfAny(['\r', '\n']) >= 0 ||
            !IsAscii(line))
        {
            throw MalformedEvent();
        }

        var fields = line.Split(' ', StringSplitOptions.None);
        if (fields.Length < 3 || fields.Any(static field => field.Length == 0) ||
            !string.Equals(fields[0], Version, StringComparison.Ordinal) ||
            !string.Equals(fields[1], expectedNonce, StringComparison.Ordinal) ||
            !IsLowercaseHex32(fields[1]))
        {
            throw MalformedEvent();
        }

        return fields[2] switch
        {
            "READY" when fields.Length == 3 => new WorkerEvent(
                WorkerEventKind.Ready,
                expectedNonce,
                null,
                null),
            "HOST_READY" when fields.Length == 4 && TryParsePositiveLong(fields[3], out var hostToken) =>
                new WorkerEvent(WorkerEventKind.HostReady, expectedNonce, hostToken, null),
            "HOST_DENIED" when fields.Length == 3 => new WorkerEvent(
                WorkerEventKind.HostDenied,
                expectedNonce,
                null,
                null),
            "WORK_HELD" when fields.Length == 4 && TryParsePositiveLong(fields[3], out var workValue) =>
                new WorkerEvent(WorkerEventKind.WorkHeld, expectedNonce, workValue, null),
            "WORK_DENIED" when fields.Length == 3 => new WorkerEvent(
                WorkerEventKind.WorkDenied,
                expectedNonce,
                null,
                null),
            "DRAIN_BEGIN" when fields.Length == 3 => new WorkerEvent(
                WorkerEventKind.DrainBegin,
                expectedNonce,
                null,
                null),
            "WORK_RELEASED" when fields.Length == 3 => new WorkerEvent(
                WorkerEventKind.WorkReleased,
                expectedNonce,
                null,
                null),
            "LEASE_LOST" when fields.Length == 3 => new WorkerEvent(
                WorkerEventKind.LeaseLost,
                expectedNonce,
                null,
                null),
            "DRAINED" when fields.Length == 3 => new WorkerEvent(
                WorkerEventKind.Drained,
                expectedNonce,
                null,
                null),
            "OUTAGE_CLEAN" when fields.Length == 3 => new WorkerEvent(
                WorkerEventKind.OutageClean,
                expectedNonce,
                null,
                null),
            "STOPPED" when fields.Length == 3 => new WorkerEvent(
                WorkerEventKind.Stopped,
                expectedNonce,
                null,
                null),
            "FAIL" when fields.Length == 4 && TryParseFailureCategory(fields[3], out var failureCategory) =>
                new WorkerEvent(WorkerEventKind.Fail, expectedNonce, null, failureCategory),
            _ => throw MalformedEvent()
        };
    }

    /// <summary>
    /// 判斷文字是否為 32 個小寫十六進位字元。
    /// run id 與 nonce 使用這個狹窄格式，讓命令列、namespace 與協定都不需要處理空白、路徑或控制字元。
    /// </summary>
    internal static bool IsLowercaseHex32(string? value)
    {
        if (value is null || value.Length != 32)
        {
            return false;
        }

        foreach (var character in value)
        {
            if ((character < '0' || character > '9') &&
                (character < 'a' || character > 'f'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 判斷 worker 標籤是否為短小、固定 ASCII 識別字。
    /// 標籤只用於區分同一個測試的 worker，不承載使用者、工作負載、主機或認證資料。
    /// </summary>
    internal static bool IsWorkerLabel(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 16)
        {
            return false;
        }

        foreach (var character in value)
        {
            if ((character < 'a' || character > 'z') &&
                (character < '0' || character > '9') &&
                character != '-')
            {
                return false;
            }
        }

        return value[0] is >= 'a' and <= 'z';
    }

    /// <summary>
    /// 建立不回顯 worker 原始輸出的固定協定失敗。
    /// 所有 parser 分支都呼叫此方法，讓錯誤分類一致且避免後續變更意外把 child stdout 寫入例外訊息。
    /// </summary>
    private static InvalidOperationException MalformedEvent()
        => new("Worker protocol event is malformed.");

    /// <summary>
    /// 驗證協定資料列完全由 ASCII 位元組組成。
    /// stdout reader 已在位元組層限制大小；此處再次限制字元集合，防止 Unicode 空白或同形字改變欄位語義。
    /// </summary>
    private static bool IsAscii(string value)
    {
        foreach (var character in value)
        {
            if (character > 0x7f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 將協定正數欄位以 invariant 格式解析。
    /// 不接受正負號、空白、前置格式或零值，讓 fencing token 與 work 序號不會因文化設定而變形。
    /// </summary>
    private static bool TryParsePositiveLong(string value, out long result)
        => long.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out result) &&
            result > 0;

    /// <summary>
    /// 將 FAIL 的最後欄位限制為預先宣告的分類。
    /// 不接受例外名稱或任何自由診斷字串，避免 worker 將 SQL 或環境資訊從 stdout 外洩。
    /// </summary>
    private static bool TryParseFailureCategory(string value, out WorkerFailureCategory category)
    {
        category = value switch
        {
            "arguments" => WorkerFailureCategory.Arguments,
            "protocol" => WorkerFailureCategory.Protocol,
            "admission" => WorkerFailureCategory.Admission,
            "outage" => WorkerFailureCategory.Outage,
            "lifecycle" => WorkerFailureCategory.Lifecycle,
            _ => default
        };

        return value is "arguments" or "protocol" or "admission" or "outage" or "lifecycle";
    }
}

/// <summary>
/// 管理一個測試專用 SQL coordinator worker 行程的父端外殼。
/// 此型別是 Process、stdin/stdout/stderr、取消來源、事件 channel 與讀取工作唯一 owner；
/// 它以有界 ASCII 協定與確定的 stop/kill/dispose 順序避免 child 行程、stream 或緩衝資料跨測試保留。
/// </summary>
internal sealed class CrossProcessSqlCoordinatorWorker : IAsyncDisposable
{
    private const string WorkerExecutableFileName = "SpeechMessage.Dynamics.SqlCoordinatorTestWorker.exe";
    private const int StandardOutputByteLimit = 32 * 1024;
    private const int StandardErrorByteLimit = 8 * 1024;
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ForcedExitTimeout = TimeSpan.FromSeconds(3);

    private readonly WorkerStartRequest _startRequest;
    private readonly string _executablePath;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Channel<WorkerEvent> _events = Channel.CreateBounded<WorkerEvent>(
        new BoundedChannelOptions(capacity: 16)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });

    private Process? _process;
    private StreamWriter? _standardInput;
    private StreamReader? _standardOutput;
    private StreamReader? _standardError;
    private Task? _standardOutputDrainTask;
    private Task? _standardErrorDrainTask;
    private int _started;
    private int _disposeStarted;
    private int _timeoutShutdownStarted;

    /// <summary>
    /// 建立尚未啟動的 worker parent owner。
    /// 可執行檔路徑只根據目前測試組態的已建置輸出位置解析；建構階段不會啟動、編譯或探測任何 SQL 資源。
    /// </summary>
    private CrossProcessSqlCoordinatorWorker(WorkerStartRequest startRequest)
    {
        _startRequest = startRequest;
        _executablePath = ResolveWorkerExecutablePath();
    }

    /// <summary>
    /// 建立並啟動一個具固定引數與清理 owner 的 worker。
    /// 呼叫端取得 owner 後必須以 <c>await using</c> 釋放它；若啟動任何階段失敗，此方法會先回收已取得的資源，
    /// 再回傳不含 child stderr、路徑或原始例外內容的受控失敗。
    /// </summary>
    internal static async Task<CrossProcessSqlCoordinatorWorker> StartAsync(
        WorkerStartRequest startRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startRequest);
        cancellationToken.ThrowIfCancellationRequested();

        var worker = new CrossProcessSqlCoordinatorWorker(startRequest);
        try
        {
            await worker.StartAsync(cancellationToken).ConfigureAwait(false);
            return worker;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await worker.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch
        {
            await worker.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("The test-only worker process could not be started.");
        }
    }

    /// <summary>
    /// 啟動直接 worker 可執行檔並在寫入任何 stdin 前建立 stdout 與 stderr 的有界 drain owner。
    /// 明確清理環境可阻止父行程的 live SQL 選擇、Dynamics/CRM 設定或 credential-shaped 變數跨越行程，
    /// 而固定引數透過 <see cref="ProcessStartInfo.ArgumentList" /> 傳遞，避免 shell 重新解析。
    /// </summary>
    internal Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposing();
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException("The test-only worker process was already started.");
        }

        var process = new Process
        {
            StartInfo = CreateProcessStartInfo()
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("The test-only worker process could not be started.");
            }

            _process = process;
            _standardInput = process.StandardInput;
            _standardInput.AutoFlush = true;
            _standardOutput = process.StandardOutput;
            _standardError = process.StandardError;

            // stdout 與 stderr 必須在第一個 stdin command 之前各自開始 drain；
            // 否則 child 的有限 pipe buffer 可能造成父子行程互等，並讓測試誤判為 coordinator deadlock。
            _standardOutputDrainTask = DrainStandardOutputAsync(
                _standardOutput.BaseStream,
                _lifetimeCancellation.Token);
            _standardErrorDrainTask = DrainStandardErrorAsync(
                _standardError.BaseStream,
                _lifetimeCancellation.Token);

            return Task.CompletedTask;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 傳送一個固定命令並等待同一個 worker 的預期固定事件。
    /// 命令 gate 是 stdin writer 與對應 stdout 回應的唯一序列化 owner；它避免兩個測試操作交錯後，
    /// 將某一命令的事件錯配給另一命令，且任何 timeout 都會走受控 stop、關閉 stdin、等待與 tree-kill 路徑。
    /// </summary>
    internal async Task<WorkerEvent> SendAndWaitAsync(
        WorkerCommand command,
        WorkerEventKind expected,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposing();
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendCommandCoreAsync(command, cancellationToken).ConfigureAwait(false);
            return await ReadEventCoreAsync(expected, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopAfterTimeoutAsync().ConfigureAwait(false);
            throw new TimeoutException("Timed out while exchanging a fixed worker protocol event.");
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            await StopAfterTimeoutAsync().ConfigureAwait(false);
            throw;
        }
        catch
        {
            await StopAfterTimeoutAsync().ConfigureAwait(false);
            throw new InvalidOperationException("The fixed worker protocol exchange failed closed.");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    /// <summary>
    /// 等待 worker 主動產生的固定事件，例如 fencing 失敗通知。
    /// 此方法共用命令 gate，確保自發事件不會與某一同步命令的預期回應同時消費同一個 channel；
    /// timeout 同樣會終止 child，避免在測試結束後保留無 owner 的 reader 或 worker 行程。
    /// </summary>
    internal async Task<WorkerEvent> ReadEventAsync(
        WorkerEventKind expected,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposing();
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadEventCoreAsync(expected, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopAfterTimeoutAsync().ConfigureAwait(false);
            throw new TimeoutException("Timed out while waiting for a fixed worker protocol event.");
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            await StopAfterTimeoutAsync().ConfigureAwait(false);
            throw;
        }
        catch
        {
            await StopAfterTimeoutAsync().ConfigureAwait(false);
            throw new InvalidOperationException("The fixed worker protocol stream failed closed.");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    /// <summary>
    /// 要求 worker 發出 STOPPED 後以自己的 lifecycle 完成停止。
    /// 此路徑優先保留 worker 的確定 disposal 語義；若它未在呼叫端的有界期限內回應，
    /// <see cref="SendAndWaitAsync" /> 會升級為關閉 stdin、等待短暫期限與終止整個 process tree。
    /// </summary>
    internal Task RequestGracefulStopAsync(CancellationToken cancellationToken)
        => SendAndWaitAsync(WorkerCommand.Stop, WorkerEventKind.Stopped, cancellationToken);

    /// <summary>
    /// 以不傳送 STOP 的方式終止 worker，模擬 host process 異常消失。
    /// 這個方法只用於 crash recovery 事實；它會等待有界的結束通知，但不會替 child 補送 graceful 指令，
    /// 讓 SQL TTL 與 quarantine 的後續斷言保持真實的 crash 邊界。
    /// </summary>
    internal async Task TerminateForCrashAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposing();
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            KillProcessTree();
            if (!await WaitForProcessExitWithinAsync(ForcedExitTimeout, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The test-only worker process did not terminate after the crash simulation.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ForceStopAfterTimeoutAsync().ConfigureAwait(false);
            throw new TimeoutException("Timed out while terminating the test-only worker process.");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    /// <summary>
    /// 以有界 graceful-stop 優先、關閉 stdin、等待與 tree-kill fallback 的順序釋放所有 owned 資源。
    /// 即使測試的 assertion 已失敗，此方法仍會取消 drain reader、關閉三個標準 stream、等待其工作結束並 dispose Process、
    /// channel 與 semaphore，避免 worker、pipe handle 或等待中的 callback 遺留到下一個 live 測試。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        try
        {
            if (Volatile.Read(ref _started) != 0 && !HasProcessExited())
            {
                using var gracefulStop = new CancellationTokenSource(GracefulStopTimeout);
                if (await TryEnterCommandGateAsync(gracefulStop.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await TrySendStopAndObserveAsync(gracefulStop.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _commandGate.Release();
                    }
                }
            }

            CloseStandardInput();
            await EnsureProcessExitedAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifetimeCancellation.Cancel();
            CloseStandardInput();
            CloseStandardOutputAndError();
            await AwaitDrainTasksAsync().ConfigureAwait(false);
            _events.Writer.TryComplete();
            _process?.Dispose();
            _lifetimeCancellation.Dispose();
            _commandGate.Dispose();
        }
    }

    /// <summary>
    /// 以父端唯一 stdin writer 寫入固定 command line 並立即 flush。
    /// <see cref="ProcessStartInfo.ArgumentList" /> 已隔離啟動引數；這裡同樣只寫入由 <see cref="WorkerProtocol" />
    /// 建立的 ASCII 行，且不接受任意 caller supplied stdin 文字。
    /// </summary>
    private async Task SendCommandCoreAsync(WorkerCommand command, CancellationToken cancellationToken)
    {
        var input = _standardInput ?? throw new InvalidOperationException(
            "The test-only worker input stream is unavailable.");
        var line = WorkerProtocol.FormatCommand(command, _startRequest.Nonce);

        await input.WriteAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await input.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
        await input.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 從唯一 stdout event channel 讀取一個預期事件。
    /// channel 只由受限 reader 寫入；若 child 已結束、stdout 違反協定、stderr 超過限制或生命週期已取消，
    /// 此方法只回報固定失敗並保留 timeout cleanup 的唯一 owner，不會包裝或回顯 child 資料。
    /// </summary>
    private async Task<WorkerEvent> ReadEventCoreAsync(
        WorkerEventKind expected,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(expected))
        {
            throw new ArgumentOutOfRangeException(nameof(expected));
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        try
        {
            var workerEvent = await _events.Reader.ReadAsync(linkedCancellation.Token).ConfigureAwait(false);
            if (workerEvent.Kind != expected)
            {
                await StopAfterTimeoutAsync().ConfigureAwait(false);
                // 僅回報 parser 已驗證的列舉名稱與固定 failure category，不回顯 child stdout、stderr、nonce、
                // fencing token、連線資訊或例外文字；這讓 live test 可定位協定狀態機錯誤，同時維持跨行程資料邊界。
                var actualEvent = workerEvent.Kind == WorkerEventKind.Fail && workerEvent.FailureCategory is { } failureCategory
                    ? "FAIL " + failureCategory.ToString().ToLowerInvariant()
                    : workerEvent.Kind.ToString();
                throw new InvalidOperationException(
                    $"The worker emitted fixed event {actualEvent} instead of {expected}.");
            }

            return workerEvent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopAfterTimeoutAsync().ConfigureAwait(false);
            throw new TimeoutException("Timed out while waiting for a fixed worker protocol event.");
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException("The fixed worker protocol stream closed before the expected event.");
        }
        catch (ChannelClosedException)
        {
            throw new InvalidOperationException("The fixed worker protocol stream closed before the expected event.");
        }
    }

    /// <summary>
    /// 由唯一 stdout owner 逐列讀取受限 ASCII 資料並寫入有界 event channel。
    /// 協定違規、超過 32 KiB 或 reader 失敗時會立即 fail closed、取消剩餘 I/O 並終止 child tree；
    /// 這能避免任意 stdout、長行或持續輸出佔用未受限記憶體，也不會把原始內容傳給測試。
    /// </summary>
    private async Task DrainStandardOutputAsync(Stream standardOutput, CancellationToken cancellationToken)
    {
        try
        {
            var reader = new BoundedAsciiLineReader(standardOutput, StandardOutputByteLimit);
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var workerEvent = WorkerProtocol.ParseWorkerEvent(line, _startRequest.Nonce);
                await _events.Writer.WriteAsync(workerEvent, cancellationToken).ConfigureAwait(false);
            }

            _events.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _events.Writer.TryComplete();
        }
        catch
        {
            _events.Writer.TryComplete(new InvalidOperationException("The fixed worker protocol stream failed closed."));
            FailClosedForOutputViolation();
        }
    }

    /// <summary>
    /// 由唯一 stderr owner 以固定 byte buffer 丟棄 child 診斷輸出。
    /// stderr 永遠不會寫進 assertion 或例外；若其總量超過 8 KiB，代表 child 已超出測試協定的資源預算，
    /// 所以會立即走 fail-closed tree-kill，而不是以 <c>ReadToEndAsync</c> 等待無界輸出。
    /// </summary>
    private async Task DrainStandardErrorAsync(Stream standardError, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        var observedBytes = 0;
        try
        {
            while (true)
            {
                var maximumRead = Math.Min(
                    buffer.Length,
                    checked(StandardErrorByteLimit - observedBytes + 1));
                var read = await standardError
                    .ReadAsync(buffer.AsMemory(0, maximumRead), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                observedBytes = checked(observedBytes + read);
                if (observedBytes > StandardErrorByteLimit)
                {
                    FailClosedForOutputViolation();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Dispose owner 已取消 reader；stderr 內容仍保持丟棄且不會跨越測試邊界。
        }
        catch
        {
            FailClosedForOutputViolation();
        }
    }

    /// <summary>
    /// 在任何協定或輸出預算違規時取消 reader 並盡快殺掉 child tree。
    /// 這個方法不等待自己可能正在執行的 drain task，避免自我等待；外層 <see cref="DisposeAsync" />
    /// 仍是唯一負責關閉 stream、等待 task 與 dispose Process 的生命週期 owner。
    /// </summary>
    private void FailClosedForOutputViolation()
    {
        _lifetimeCancellation.Cancel();
        KillProcessTree();
    }

    /// <summary>
    /// 在 command/event timeout 後執行固定的受控 shutdown 順序。
    /// 先嘗試送出 STOP、接著關閉 stdin、等待短暫期限，最後才終止整個 process tree；
    /// Interlocked guard 確保並行 timeout 不會重複寫入已關閉 stdin 或競爭同一個 Process。
    /// </summary>
    private async Task StopAfterTimeoutAsync()
    {
        if (Interlocked.Exchange(ref _timeoutShutdownStarted, 1) != 0)
        {
            return;
        }

        try
        {
            using var stopTimeout = new CancellationTokenSource(GracefulStopTimeout);
            await TrySendStopWithoutWaitingAsync(stopTimeout.Token).ConfigureAwait(false);
            CloseStandardInput();
            await EnsureProcessExitedAsync().ConfigureAwait(false);
        }
        catch
        {
            CloseStandardInput();
            KillProcessTree();
        }
    }

    /// <summary>
    /// 在 crash termination 或 timeout cleanup 需要時直接收斂 child tree。
    /// 這條路徑不依賴 stdout、stderr 或 child cooperative shutdown，因此可處理卡住的 stdin reader；
    /// 所有 Win32/Process race 都被轉成後續固定狀態檢查，避免向測試輸出原始系統例外。
    /// </summary>
    private async Task ForceStopAfterTimeoutAsync()
    {
        CloseStandardInput();
        KillProcessTree();
        await EnsureProcessExitedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 在 dispose 路徑嘗試送 STOP 並等待 STOPPED，但不將 cleanup 中的 child failure 回傳為 raw exception。
    /// 若 child 不合作，呼叫端會接著關閉 stdin 並進入有界 kill fallback；這保證 cleanup 不會因單一協定事件永久卡住。
    /// </summary>
    private async Task TrySendStopAndObserveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendCommandCoreAsync(WorkerCommand.Stop, cancellationToken).ConfigureAwait(false);
            await ReadEventCoreAsync(WorkerEventKind.Stopped, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Dispose 仍必須繼續關閉 stdin 與 kill fallback，不能讓固定 STOPPED 缺失遺留 worker。
        }
    }

    /// <summary>
    /// 在 timeout cleanup 中嘗試寫入 STOP，但不等待 STOPPED 或占用 command gate。
    /// 呼叫端可能正持有 gate 並等待事件，因此這個私有路徑不能遞迴呼叫公開方法；任何寫入失敗都只代表後續
    /// 必須關閉 stdin 並使用 tree-kill，而不會將 stream/例外內容擴散出去。
    /// </summary>
    private async Task TrySendStopWithoutWaitingAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_standardInput is not null && !HasProcessExited())
            {
                await SendCommandCoreAsync(WorkerCommand.Stop, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // 逾時後的 stop 是 best effort；CloseStandardInput 與 KillProcessTree 才是確定 cleanup 邊界。
        }
    }

    /// <summary>
    /// 建立直接 worker executable 的 ProcessStartInfo 與最小化 child 環境。
    /// 使用 <see cref="ProcessStartInfo.ArgumentList" /> 避免 command-line shell 解析，指定 worker 自己的輸出目錄做工作目錄，
    /// 並將所有非必要或 credential-shaped 父環境變數排除，使 LocalDB 同使用者需求與 Dynamics 資訊隔離同時成立。
    /// </summary>
    private ProcessStartInfo CreateProcessStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            WorkingDirectory = Path.GetDirectoryName(_executablePath)
                ?? throw new InvalidOperationException("The test-only worker executable directory is unavailable."),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("--run-id");
        startInfo.ArgumentList.Add(_startRequest.RunId);
        startInfo.ArgumentList.Add("--organization-id");
        startInfo.ArgumentList.Add(_startRequest.OrganizationId.ToString("D", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--worker-label");
        startInfo.ArgumentList.Add(_startRequest.WorkerLabel);
        startInfo.ArgumentList.Add("--nonce");
        startInfo.ArgumentList.Add(_startRequest.Nonce);
        ConfigureScrubbedChildEnvironment(startInfo);
        return startInfo;
    }

    /// <summary>
    /// 清除預設繼承環境後只複製 LocalDB 與 .NET runtime 所需的少數 OS 變數。
    /// 除了白名單外不會有任何父端變數進入 child，並且會再次移除 live SQL selector 與所有 Dynamics/CRM 或 credential-shaped 名稱，
    /// 讓未來白名單調整也不會意外破壞測試的跨行程秘密隔離。
    /// </summary>
    private static void ConfigureScrubbedChildEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment.Clear();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string name ||
                entry.Value is not string value ||
                !IsMinimumWorkerEnvironmentVariable(name) ||
                IsSensitiveWorkerEnvironmentVariable(name) ||
                string.IsNullOrEmpty(value))
            {
                continue;
            }

            startInfo.Environment[name] = value;
        }

        startInfo.Environment.Remove("SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION");
        foreach (var name in startInfo.Environment.Keys.ToArray())
        {
            if (IsSensitiveWorkerEnvironmentVariable(name))
            {
                startInfo.Environment.Remove(name);
            }
        }
    }

    /// <summary>
    /// 判斷可保留給同一 Windows 使用者 LocalDB 與已建置 .NET apphost 的最小 OS 環境變數。
    /// 此方法刻意不用前綴式允許規則；新增變數必須經過明確審查，避免父行程的測試、CRM 或認證設定被預設複製。
    /// </summary>
    private static bool IsMinimumWorkerEnvironmentVariable(string name)
        => name.Equals("SystemRoot", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("WINDIR", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ComSpec", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PATH", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PATHEXT", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("TMP", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("USERPROFILE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("HOMEDRIVE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("HOMEPATH", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("APPDATA", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LOCALAPPDATA", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("USERNAME", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("USERDOMAIN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("USERDOMAIN_ROAMINGPROFILE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("COMPUTERNAME", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("NUMBER_OF_PROCESSORS", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PROCESSOR_ARCHITECTURE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PROCESSOR_IDENTIFIER", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PROCESSOR_LEVEL", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PROCESSOR_REVISION", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("OS", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("DOTNET_ROOT_X64", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("DOTNET_ROOT_X86", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 判斷名稱是否可能承載 Dynamics/CRM 或 credential-like 狀態。
    /// 名稱檢查獨立於白名單保護，可防止後續為 OS 相容性新增變數時不小心傳遞 connection、secret、token 或 CRM 設定。
    /// </summary>
    private static bool IsSensitiveWorkerEnvironmentVariable(string name)
        => name.Equals("SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DYNAMICS", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("CRM", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PASSWD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AUTH", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("CONNECTION", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("CONNSTR", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SQL", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("KEY", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解析目前測試輸出組態對應的 worker apphost，不進行 PATH 搜尋或 fallback build。
    /// 若檔案不存在，第一個 live handshake 會以固定訊息失敗；這使 RED 證據可區分「未建置 worker」與協定、SQL 或 child I/O 問題。
    /// </summary>
    private static string ResolveWorkerExecutablePath()
    {
        var targetFrameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var configurationDirectory = targetFrameworkDirectory.Parent
            ?? throw new InvalidOperationException("The test output configuration directory is unavailable.");
        var configuration = configurationDirectory.Name;
        if (string.IsNullOrWhiteSpace(configuration) ||
            configuration.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InvalidOperationException("The test output configuration is invalid.");
        }

        var executablePath = Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessage.Dynamics.SqlCoordinatorTestWorker",
            "bin",
            configuration,
            "net10.0",
            WorkerExecutableFileName);
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException(
                "The test-only worker executable SpeechMessage.Dynamics.SqlCoordinatorTestWorker.exe is not built or resolvable.");
        }

        return executablePath;
    }

    /// <summary>
    /// 在有界期限內等待 Process 結束。
    /// 這裡不使用無期限的 <c>WaitForExit</c>；呼叫端依結果決定是否升級為 tree-kill，因此等待本身不會使測試清理永久阻塞。
    /// </summary>
    private async Task<bool> WaitForProcessExitWithinAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var process = _process;
        if (process is null || HasProcessExited())
        {
            return true;
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HasProcessExited();
        }
    }

    /// <summary>
    /// 確保 child 已在 graceful timeout 後結束，否則終止整個 process tree 並再等一次有界期限。
    /// stdout/stderr drain 在 process exit 後才能可靠完成，因此這個方法是 stream dispose 前的生命週期屏障；
    /// 若作業系統仍無法回收 child，方法只回報固定失敗而不將系統例外或 child 資料外洩。
    /// </summary>
    private async Task EnsureProcessExitedAsync()
    {
        if (await WaitForProcessExitWithinAsync(GracefulStopTimeout).ConfigureAwait(false))
        {
            return;
        }

        KillProcessTree();
        if (!await WaitForProcessExitWithinAsync(ForcedExitTimeout).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The test-only worker process could not be terminated.");
        }
    }

    /// <summary>
    /// 在不依賴 child cooperative behavior 的情況下終止整個行程樹。
    /// Process 可能剛好已離開或被其他 cleanup 路徑 dispose；這些 race 一律由後續有界 exit 檢查處理，
    /// 所以此方法不回傳平台例外或要求呼叫端根據瞬間狀態判斷清理是否成功。
    /// </summary>
    private void KillProcessTree()
    {
        try
        {
            if (_process is { } process && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // exit/dispose race 交由 EnsureProcessExitedAsync 的固定結果判斷，避免回顯系統例外。
        }
    }

    /// <summary>
    /// 關閉父端唯一 stdin writer，通知 child 不會再收到命令。
    /// 此動作在 timeout 與 dispose 路徑都可重複呼叫；第一次取得 writer 的 owner 負責釋放 pipe handle，
    /// 後續呼叫只看到 null，避免多個 cleanup 路徑重複 dispose 同一個 stream。
    /// </summary>
    private void CloseStandardInput()
    {
        var input = Interlocked.Exchange(ref _standardInput, null);
        try
        {
            input?.Dispose();
        }
        catch
        {
            // close race 不可阻止後續 kill/dispose；不輸出 child 或系統例外。
        }
    }

    /// <summary>
    /// 關閉 stdout 與 stderr reader，讓已取消或已終止 child 的 drain task 離開底層 pipe read。
    /// 必須在取消 lifetime token 後執行，避免 reader 繼續等待而持有 pipe handle；reader task 的最終 await 仍由 Dispose owner 負責。
    /// </summary>
    private void CloseStandardOutputAndError()
    {
        try
        {
            _standardOutput?.Dispose();
        }
        catch
        {
            // output reader 可能已因 process exit 關閉，無需把 close race 變成測試診斷輸出。
        }

        try
        {
            _standardError?.Dispose();
        }
        catch
        {
            // stderr 永遠丟棄，close race 同樣不能回顯任何 child 資料。
        }
    }

    /// <summary>
    /// 在關閉 stream 後等待兩個 drain task 結束，但保持有界期限。
    /// 每個 task 的 reader 都持有 lifetime cancellation 與已關閉的 pipe，正常情況必定結束；若平台層仍延遲，
    /// 此方法不會無限等待或保存 task 例外，且 Process/stream owner 仍在 finally 中釋放。
    /// </summary>
    private async Task AwaitDrainTasksAsync()
    {
        await AwaitTaskWithinAsync(_standardOutputDrainTask, ForcedExitTimeout).ConfigureAwait(false);
        await AwaitTaskWithinAsync(_standardErrorDrainTask, ForcedExitTimeout).ConfigureAwait(false);
    }

    /// <summary>
    /// 等待單一 cleanup task 的固定期限並吞掉已預期的 protocol/pipe 失敗。
    /// child stdout 與 stderr 不可作為 cleanup exception 的載體；因此 task fault 只影響既有 fail-closed 狀態，
    /// 不會把任意資料送回測試或讓 Dispose 因 unbounded task join 卡住。
    /// </summary>
    private static async Task AwaitTaskWithinAsync(Task? task, TimeSpan timeout)
    {
        if (task is null)
        {
            return;
        }

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCancellation.Token);
        if (await Task.WhenAny(task, timeoutTask).ConfigureAwait(false) == task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // reader fault 已在 channel/fail-closed path 表示，Dispose 不重送原始資料。
            }
        }
    }

    /// <summary>
    /// 在 dispose 路徑以取消權杖取得 command gate。
    /// 無法取得代表另一個 bounded command/read 操作正在收尾；Dispose 會跳過 graceful write，改由關閉 stdin 與 tree-kill
    /// 建立單一終止邊界，而不是無限等待 lock 或遺留第二個 writer。
    /// </summary>
    private async Task<bool> TryEnterCommandGateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// 判斷目前 worker 是否已離開，並將 Process 狀態讀取 race 視為已不可互動。
    /// 這個 fail-closed 判斷避免 cleanup 在已 dispose 或已退出的 Process 上繼續寫 stdin；後續 exit wait 仍會確認資源收斂。
    /// </summary>
    private bool HasProcessExited()
    {
        try
        {
            return _process is null || _process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 防止已進入 Dispose 的 owner 再接受新的 public command/read 操作。
    /// 一旦清理開始，既有呼叫只可由 cancellation/stream close 收斂；新的操作若繼續取得 I/O 會破壞唯一 owner 與 deterministic cleanup 保證。
    /// </summary>
    private void ThrowIfDisposing()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            throw new ObjectDisposedException(nameof(CrossProcessSqlCoordinatorWorker));
        }
    }

    /// <summary>
    /// 由測試輸出目錄向上尋找方案根目錄。
    /// 解析只依賴已簽入的方案檔與固定專案相對路徑，不會搜尋 PATH、使用工作目錄猜測或取用父行程環境中的機密設定。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "SpeechMessageProducts.sln")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate SpeechMessageProducts.sln from the test output directory.");
    }

    /// <summary>
    /// 以固定大小 buffer 從 stdout 讀取 ASCII 協定資料列。
    /// 此 reader 只屬於 stdout drain task，總輸出與單列都先在 byte 層限制，再交給 parser；
    /// 因此惡意或損毀 child 無法透過無換行列或大量輸出讓父端建立無界字串。
    /// </summary>
    private sealed class BoundedAsciiLineReader
    {
        private readonly Stream _stream;
        private readonly int _maximumBytes;
        private readonly byte[] _readBuffer = new byte[256];
        private int _bufferOffset;
        private int _bufferLength;
        private int _observedBytes;

        /// <summary>
        /// 建立由 stdout drain task 唯一擁有的 byte reader。
        /// stream 不在這個 helper 中 dispose；外層 worker owner 必須先取消 reader、關閉 reader/stream，再等待 drain task，
        /// 以維持所有 pipe handle 的單一 deterministic cleanup 路徑。
        /// </summary>
        internal BoundedAsciiLineReader(Stream stream, int maximumBytes)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _maximumBytes = maximumBytes > 0
                ? maximumBytes
                : throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        /// <summary>
        /// 讀取一列 CRLF 或 LF 結尾的 ASCII 協定資料。
        /// 每列最多配置 128 bytes；單獨 CR、非 ASCII、超長列或 EOF 中斷都會以固定錯誤 fail closed，
        /// 而不會保留 partial child output 或讓 caller 以例外訊息取得它。
        /// </summary>
        internal async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = new byte[WorkerProtocol.MaximumLineBytes];
            var lineLength = 0;
            while (true)
            {
                var next = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
                if (next < 0)
                {
                    if (lineLength == 0)
                    {
                        return null;
                    }

                    throw new InvalidOperationException("The fixed worker protocol stream is incomplete.");
                }

                if (next == '\n')
                {
                    return Encoding.ASCII.GetString(line, 0, lineLength);
                }

                if (next == '\r')
                {
                    if (await ReadByteAsync(cancellationToken).ConfigureAwait(false) != '\n')
                    {
                        throw new InvalidOperationException("The fixed worker protocol stream is malformed.");
                    }

                    return Encoding.ASCII.GetString(line, 0, lineLength);
                }

                if (next > 0x7f || lineLength >= line.Length)
                {
                    throw new InvalidOperationException("The fixed worker protocol stream is malformed.");
                }

                line[lineLength++] = (byte)next;
            }
        }

        /// <summary>
        /// 從底層 pipe 取得一個位元組並計入全域 stdout 預算。
        /// buffer 每次最多讀取剩餘預算加一個偵測位元組；因此 reader 可以可靠偵測超額，卻不會先把大量 child 輸出讀入記憶體。
        /// </summary>
        private async Task<int> ReadByteAsync(CancellationToken cancellationToken)
        {
            if (_bufferOffset == _bufferLength)
            {
                var maximumRead = Math.Min(
                    _readBuffer.Length,
                    checked(_maximumBytes - _observedBytes + 1));
                var read = await _stream
                    .ReadAsync(_readBuffer.AsMemory(0, maximumRead), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return -1;
                }

                _observedBytes = checked(_observedBytes + read);
                if (_observedBytes > _maximumBytes)
                {
                    throw new InvalidOperationException("The fixed worker protocol stream exceeded its byte limit.");
                }

                _bufferOffset = 0;
                _bufferLength = read;
            }

            return _readBuffer[_bufferOffset++];
        }
    }
}

/// <summary>
/// 執行跨行程 fencing 事實所需的唯一 parent-owned SQL mutation。
/// 此工具只接受由 <see cref="WorkerStartRequest" /> 產生的 namespace 與 worker 已公布的正數舊 fencing token，
/// 並以參數化 UPDATE fail closed；它不提供任意 SQL、查詢、列舉或一般資料庫管理能力。
/// </summary>
internal static class CrossProcessSqlCoordinatorFencer
{
    private const string FenceExactlyOneLeaseSql = """
        SET NOCOUNT ON;
        UPDATE dbo.RuntimeHostSlotLease
        SET FencingToken = FencingToken + 1,
            LastTouchedAtUtc = SYSUTCDATETIME()
        WHERE LeaseNamespaceId = @leaseNamespaceId
          AND FencingToken = @oldFencingToken
          AND HostInstanceId IS NOT NULL;
        SELECT @@ROWCOUNT;
        """;

    /// <summary>
    /// 以 namespace 與精確舊 token 更新恰好一個 live durable lease。
    /// 呼叫端必須在 parent 行程保有已明確選擇的 LocalDB connection；這個方法絕不將它傳給 child，
    /// 若 namespace 不屬於本測試或受影響列數不是一，會 fail closed 並停止後續 fencing 斷言。
    /// </summary>
    internal static async Task<int> FenceExactlyOneLeaseAsync(
        string connectionString,
        string leaseNamespaceId,
        long oldFencingToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString) ||
            !IsGeneratedLeaseNamespace(leaseNamespaceId) ||
            oldFencingToken <= 0)
        {
            throw new ArgumentException("The scoped fencing request is invalid.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(FenceExactlyOneLeaseSql, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 5
        };
        command.Parameters.Add("@leaseNamespaceId", SqlDbType.NVarChar, 128).Value = leaseNamespaceId;
        command.Parameters.Add("@oldFencingToken", SqlDbType.BigInt).Value = oldFencingToken;

        // SET NOCOUNT ON 會讓 ExecuteNonQueryAsync 無法可靠取得 DML 筆數；必須由同一受限 batch
        // 立即回傳 @@ROWCOUNT，才能在不讀取其他 namespace/slot 的前提下 fail-closed 驗證唯一 mutation。
        var scalarResult = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (scalarResult is not int affectedRows || affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The scoped fencing mutation did not affect exactly one generated lease.");
        }

        return affectedRows;
    }

    /// <summary>
    /// 確認 namespace 完全符合 <c>cross-process-</c> 加上 32 位小寫 run id 的生成規則。
    /// SQL mutation 之前先在記憶體拒絕所有其他 namespace，讓參數化本身之外還有明確的測試所有權界線，
    /// 並防止未來呼叫端把既有部署或其他 live 測試的名稱交給 fencer。
    /// </summary>
    private static bool IsGeneratedLeaseNamespace(string leaseNamespaceId)
        => leaseNamespaceId.StartsWith("cross-process-", StringComparison.Ordinal) &&
            WorkerProtocol.IsLowercaseHex32(leaseNamespaceId["cross-process-".Length..]);
}

/// <summary>
/// 父行程在每個 cross-process worker 已經透過 <c>await using</c> 結束後，於 finally 路徑刪除該測試唯一擁有的 durable namespace。
/// 呼叫端必須提供與情境 timeout 無關、獨立且有界的 cleanup Token；即使 assertion、協定失敗或 timeout 已取消原 Token，
/// 也會清除已知的測試列而不遺留跨測試控制平面狀態。清理工具只接受固定生成規則與 LocalDB integrated-auth selector，
/// 並依外鍵順序執行參數化刪除，因此不會擴大成刪除其他測試、部署或使用者資料；任何 SQL 問題仍以固定錯誤 fail closed。
/// </summary>
internal static class CrossProcessSqlCoordinatorNamespaceCleanup
{
    private const string DeleteGeneratedNamespaceSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DELETE dbo.RuntimeHostSlotLease WHERE LeaseNamespaceId = @leaseNamespaceId;
        DELETE dbo.RuntimeHostAdmissionEpoch WHERE LeaseNamespaceId = @leaseNamespaceId;
        DELETE dbo.RuntimeHostOrganizationBinding WHERE LeaseNamespaceId = @leaseNamespaceId;
        COMMIT TRANSACTION;
        """;

    /// <summary>
    /// 以 FK 安全順序刪除一個已完全結束或因失敗離開的跨行程情境所建立 namespace。
    /// 呼叫端是 cleanup 的唯一 owner，必須先等待所有 worker 的 <c>await using</c> Dispose 完成，再傳入新建且有界的 Token；
    /// 這可避免已取消的情境 Token 跳過釋放，同時不會在 child 仍可能寫入時刪列。此方法只接受經驗證的唯一 namespace，
    /// 不猜測任何其他 scope，並在 LocalDB/SQL 問題時以固定訊息 fail closed，使遺留風險對測試可見。
    /// </summary>
    internal static async Task DeleteGeneratedNamespaceAsync(
        string connectionString,
        string leaseNamespaceId,
        CancellationToken cancellationToken)
    {
        if (!IsExpectedLocalDbConnection(connectionString) ||
            !IsGeneratedLeaseNamespace(leaseNamespaceId))
        {
            throw new ArgumentException("The generated namespace cleanup request is invalid.");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(DeleteGeneratedNamespaceSql, connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 5
            };
            command.Parameters.Add("@leaseNamespaceId", SqlDbType.NVarChar, 128).Value = leaseNamespaceId;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("The generated namespace cleanup failed closed.");
        }
    }

    /// <summary>
    /// 檢查 parent-only cleanup selector 是否仍是既定的同使用者 LocalDB control-plane。
    /// 即使呼叫端誤傳其他連線字串，清理也不會開啟外部 SQL 連線或嘗試刪除其資料；
    /// 此防線與 namespace 前綴驗證共同限制了測試唯一被授權的破壞性範圍。
    /// </summary>
    private static bool IsExpectedLocalDbConnection(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return builder.DataSource.Equals(
                    @"(localdb)\MSSQLLocalDB",
                    StringComparison.OrdinalIgnoreCase) &&
                builder.InitialCatalog.Equals(
                    "SpeechMessageDynamicsControlPlane",
                    StringComparison.Ordinal) &&
                builder.IntegratedSecurity &&
                string.IsNullOrWhiteSpace(builder.UserID);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// 確認刪除目標完全符合 parent 產生的 <c>cross-process-</c> namespace。
    /// 不接受任意 prefix、大小寫變體或其他測試名稱，讓 DELETE 的參數化範圍仍有可讀、可驗證的所有權契約。
    /// </summary>
    private static bool IsGeneratedLeaseNamespace(string leaseNamespaceId)
        => leaseNamespaceId.StartsWith("cross-process-", StringComparison.Ordinal) &&
            WorkerProtocol.IsLowercaseHex32(leaseNamespaceId["cross-process-".Length..]);
}
