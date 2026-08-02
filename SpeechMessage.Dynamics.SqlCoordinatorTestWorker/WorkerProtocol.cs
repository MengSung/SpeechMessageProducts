using System.Text;
using System.Threading.Channels;

namespace SpeechMessage.Dynamics.SqlCoordinatorTestWorker;

/// <summary>
/// 測試專用子行程可接受的固定命令種類。
/// 命令名稱不是設定通道；每個種類都只讓 runtime 執行既定且不帶機密資料的測試動作。
/// </summary>
internal enum WorkerCommandKind
{
    AcquireHost,
    AcquireWork,
    BeginDrain,
    ReleaseWork,
    AwaitDrain,
    OutageProbe,
    Stop
}

/// <summary>
/// 測試專用子行程可輸出的固定事件種類。
/// stdout 只允許這些列舉值，避免例外內容、連線字串、端點、認證或可變組態越過行程界線。
/// </summary>
internal enum WorkerEventKind
{
    Ready,
    HostReady,
    HostDenied,
    WorkHeld,
    WorkDenied,
    DrainBegin,
    WorkReleased,
    LeaseLost,
    Drained,
    OutageClean,
    Stopped,
    Fail
}

/// <summary>
/// stdout <see cref="WorkerEventKind.Fail"/> 允許的封閉失敗分類。
/// 分類用來讓父端判斷測試階段，不攜帶任何原始例外或敏感診斷資料。
/// </summary>
internal enum WorkerFailureCategory
{
    Arguments,
    Protocol,
    Admission,
    Outage,
    Lifecycle
}

/// <summary>
/// 經過固定格式驗證的子行程啟動資料。
/// 每個欄位僅能建立本次測試的隔離識別，不能表示連線、認證、權杖、端點或可變 runtime 設定。
/// </summary>
internal sealed record WorkerStartupArguments(
    string RunId,
    Guid OrganizationId,
    string WorkerLabel,
    string Nonce);

/// <summary>
/// 已通過 nonce 繫結與嚴格欄位數驗證的父端命令。
/// 命令不保留父端原始列內容，避免未受信任輸入跨越 parser 之後仍被 runtime 長期保存。
/// </summary>
internal readonly record struct WorkerCommand(WorkerCommandKind Kind);

/// <summary>
/// 由 worker runtime 交給 stdout 唯一 writer 的固定事件。
/// 數值只可承載正的 fencing token；失敗事件只可承載 <see cref="WorkerFailureCategory"/>。
/// </summary>
internal readonly record struct WorkerEvent(
    WorkerEventKind Kind,
    long? PositiveValue = null,
    WorkerFailureCategory? FailureCategory = null);

/// <summary>
/// 有界 stdin 記錄讀取結果。
/// 讀取器只重用一個 128-byte 緩衝區，任何超長、非 ASCII 或 CR/LF 注入立即讓呼叫端 fail closed，
/// 不會把不受限輸入累積到記憶體或傳給 admission runtime。
/// </summary>
internal enum WorkerRecordReadStatus
{
    Record,
    EndOfStream,
    Invalid
}

/// <summary>
/// 固定協定的 parser、formatter 與 stdout writer。
    /// 此型別是 stdout 的唯一格式權威：Program 與未來 lease-loss callback 只能將已列舉的事件寫入有界 channel，
    /// 而單一 writer task 會在 finally 中完成並 flush framework-owned stdout，確保子行程不會留下背景輸出工作或共享 mutable stdout state。
/// </summary>
internal static class WorkerProtocol
{
    internal const int MaximumRecordBytes = 128;
    private const string ProtocolVersion = "P1";
    private const int NonceLength = 32;

    /// <summary>
    /// 以固定順序驗證啟動參數。
    /// 固定順序可拒絕重複開關、未知選項與任意環境設定，讓 worker 的 SQL 與 admission 參數完全由程式碼擁有。
    /// </summary>
    internal static bool TryParseStartupArguments(
        string[] arguments,
        out WorkerStartupArguments? startup)
    {
        startup = null;
        if (arguments is null || arguments.Length != 8 ||
            !string.Equals(arguments[0], "--run-id", StringComparison.Ordinal) ||
            !string.Equals(arguments[2], "--organization-id", StringComparison.Ordinal) ||
            !string.Equals(arguments[4], "--worker-label", StringComparison.Ordinal) ||
            !string.Equals(arguments[6], "--nonce", StringComparison.Ordinal))
        {
            return false;
        }

        var runId = arguments[1];
        var organizationIdText = arguments[3];
        var workerLabel = arguments[5];
        var nonce = arguments[7];
        if (!IsAscii(runId) || !IsAscii(organizationIdText) || !IsAscii(workerLabel) || !IsAscii(nonce) ||
            !IsHex32(runId) ||
            !Guid.TryParseExact(organizationIdText, "D", out var organizationId) ||
            !IsWorkerLabel(workerLabel) ||
            !IsHex32(nonce))
        {
            return false;
        }

        startup = new WorkerStartupArguments(runId, organizationId, workerLabel, nonce);
        return true;
    }

    /// <summary>
    /// 解析一筆已由有界 ASCII reader 驗證的父端命令。
    /// 命令必須精確符合 <c>P1 &lt;nonce&gt; &lt;command&gt;</c>，沒有可選欄位、額外空白或可延伸的資料欄位。
    /// </summary>
    internal static bool TryParseCommand(
        ReadOnlySpan<byte> record,
        string expectedNonce,
        out WorkerCommand command)
    {
        command = default;
        if (!IsHex32(expectedNonce) || record.Length is 0 or > MaximumRecordBytes)
        {
            return false;
        }

        var text = Encoding.ASCII.GetString(record);
        var prefix = ProtocolVersion + " " + expectedNonce + " ";
        if (!text.StartsWith(prefix, StringComparison.Ordinal) || text.Length == prefix.Length)
        {
            return false;
        }

        var commandText = text[prefix.Length..];
        WorkerCommandKind? kind = commandText switch
        {
            "ACQUIRE_HOST" => WorkerCommandKind.AcquireHost,
            "ACQUIRE_WORK" => WorkerCommandKind.AcquireWork,
            "BEGIN_DRAIN" => WorkerCommandKind.BeginDrain,
            "RELEASE_WORK" => WorkerCommandKind.ReleaseWork,
            "AWAIT_DRAIN" => WorkerCommandKind.AwaitDrain,
            "OUTAGE_PROBE" => WorkerCommandKind.OutageProbe,
            "STOP" => WorkerCommandKind.Stop,
            _ => null
        };

        if (kind is null)
        {
            return false;
        }

        command = new WorkerCommand(kind.Value);
        return true;
    }

    /// <summary>
    /// 將固定事件加入 bounded channel。
    /// 呼叫端必須 await 此作業，讓 channel 背壓成為唯一的輸出節流機制，而非建立未觀察的 stdout 背景工作。
    /// </summary>
    internal static ValueTask QueueEventAsync(
        ChannelWriter<WorkerEvent> writer,
        WorkerEvent workerEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return writer.WriteAsync(workerEvent, cancellationToken);
    }

    /// <summary>
    /// 由唯一 stdout owner 依序輸出有界事件。
    /// 每筆事件先轉成 ASCII 固定 record，writer 在 channel 完成後才 flush framework-owned stdout；當 pipe 關閉或 shutdown 取消時，
    /// 不會回傳或列印例外資料，也不會保留尚未觀察的 writer task。Console.Out 由 .NET runtime 持有，worker 不得擅自 Dispose，
    /// 避免關閉 runtime 的標準輸出包裝器而遺失 parent 已重導的 pipe handle。
    /// </summary>
    internal static async Task<int> WriteEventsAsync(
        ChannelReader<WorkerEvent> reader,
        TextWriter output,
        string nonce,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(output);

        var emittedEventCount = 0;

        try
        {
            await foreach (var workerEvent in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryFormatEvent(workerEvent, nonce, out var record))
                {
                    return -1;
                }

                await output.WriteLineAsync(record.AsMemory(), cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                emittedEventCount++;
            }

            return emittedEventCount;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutdown owner 已取消 writer；不再嘗試寫出任何可能阻塞的診斷資料。
            return -2;
        }
        catch (IOException)
        {
            // 父端已關閉 pipe 時，worker 只能結束其唯一 stdout owner，不能改用 stderr 或另一條輸出路徑。
            return -3;
        }
        catch (ObjectDisposedException)
        {
            // shutdown 與 pipe 關閉競爭時，stream owner 已完成釋放；此處不得再保留或重建輸出資源。
            return -4;
        }
    }

    private static bool TryFormatEvent(WorkerEvent workerEvent, string nonce, out string record)
    {
        record = string.Empty;
        if (!IsHex32(nonce))
        {
            return false;
        }

        var eventText = workerEvent.Kind switch
        {
            WorkerEventKind.Ready => "READY",
            WorkerEventKind.HostDenied => "HOST_DENIED",
            WorkerEventKind.WorkDenied => "WORK_DENIED",
            WorkerEventKind.DrainBegin => "DRAIN_BEGIN",
            WorkerEventKind.WorkReleased => "WORK_RELEASED",
            WorkerEventKind.LeaseLost => "LEASE_LOST",
            WorkerEventKind.Drained => "DRAINED",
            WorkerEventKind.OutageClean => "OUTAGE_CLEAN",
            WorkerEventKind.Stopped => "STOPPED",
            WorkerEventKind.HostReady => "HOST_READY",
            WorkerEventKind.WorkHeld => "WORK_HELD",
            WorkerEventKind.Fail => "FAIL",
            _ => null
        };
        if (eventText is null)
        {
            return false;
        }

        if (workerEvent.Kind is WorkerEventKind.HostReady or WorkerEventKind.WorkHeld)
        {
            if (workerEvent.PositiveValue is not > 0 || workerEvent.FailureCategory is not null)
            {
                return false;
            }

            record = string.Concat(
                ProtocolVersion,
                " ",
                nonce,
                " ",
                eventText,
                " ",
                workerEvent.PositiveValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (workerEvent.Kind == WorkerEventKind.Fail)
        {
            if (workerEvent.PositiveValue is not null || workerEvent.FailureCategory is null)
            {
                return false;
            }

            var failureText = workerEvent.FailureCategory.Value switch
            {
                WorkerFailureCategory.Arguments => "arguments",
                WorkerFailureCategory.Protocol => "protocol",
                WorkerFailureCategory.Admission => "admission",
                WorkerFailureCategory.Outage => "outage",
                WorkerFailureCategory.Lifecycle => "lifecycle",
                _ => null
            };
            if (failureText is null)
            {
                return false;
            }

            record = string.Concat(ProtocolVersion, " ", nonce, " ", eventText, " ", failureText);
        }
        else
        {
            if (workerEvent.PositiveValue is not null || workerEvent.FailureCategory is not null)
            {
                return false;
            }

            record = string.Concat(ProtocolVersion, " ", nonce, " ", eventText);
        }

        return record.Length <= MaximumRecordBytes && IsAscii(record);
    }

    private static bool IsHex32(string value)
    {
        if (value.Length != NonceLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWorkerLabel(string value)
    {
        if (value.Length is < 1 or > 32)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!((character >= 'a' && character <= 'z') ||
                  (character >= '0' && character <= '9') ||
                  character == '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAscii(string value)
    {
        if (value.Length is 0 or > MaximumRecordBytes)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < (char)0x20 or > (char)0x7e)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// stdin 的單一有界 reader。
/// 此 reader 擁有其 input stream 與兩個固定小型 buffer，逐 byte 讀取以在配置任何完整字串前拒絕超長資料；
/// DisposeAsync 會清除 buffer 並釋放 stream，讓每個 worker 行程的 stdin 生命週期有唯一且可驗證的 owner。
/// </summary>
internal sealed class BoundedAsciiRecordReader : IAsyncDisposable
{
    private Stream? _input;
    private byte[]? _recordBuffer = new byte[WorkerProtocol.MaximumRecordBytes];
    private byte[]? _singleByteBuffer = new byte[1];
    private int _disposed;

    internal ReadOnlyMemory<byte> CurrentRecord
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _recordBuffer!;
        }
    }

    /// <summary>
    /// 建立唯一擁有 stdin stream 的 reader。
    /// stream 不會與其他 reader 共用，避免多個消費者重排命令、保留輸入或破壞 command/response 對應。
    /// </summary>
    internal BoundedAsciiRecordReader(Stream input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <summary>
    /// 讀取一筆最多 128 ASCII byte 的 LF 或 CRLF record。
    /// 正常行尾以外的 CR/LF、控制字元、非 ASCII、超長資料與未完成行尾都回報 Invalid，呼叫端必須立即 fail closed 並結束 runtime。
    /// </summary>
    internal async ValueTask<(WorkerRecordReadStatus Status, int Length)> ReadAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var recordBuffer = _recordBuffer!;
        var singleByteBuffer = _singleByteBuffer!;
        var length = 0;
        var carriageReturnPending = false;

        while (true)
        {
            var read = await _input!.ReadAsync(singleByteBuffer.AsMemory(0, 1), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return length == 0 && !carriageReturnPending
                    ? (WorkerRecordReadStatus.EndOfStream, 0)
                    : (WorkerRecordReadStatus.Invalid, 0);
            }

            var value = singleByteBuffer[0];
            if (carriageReturnPending)
            {
                return value == (byte)'\n'
                    ? (WorkerRecordReadStatus.Record, length)
                    : (WorkerRecordReadStatus.Invalid, 0);
            }

            if (value == (byte)'\n')
            {
                return (WorkerRecordReadStatus.Record, length);
            }

            if (value == (byte)'\r')
            {
                carriageReturnPending = true;
                continue;
            }

            if (value is < 0x20 or > 0x7e || length >= WorkerProtocol.MaximumRecordBytes)
            {
                return (WorkerRecordReadStatus.Invalid, 0);
            }

            recordBuffer[length++] = value;
        }
    }

    /// <summary>
    /// 釋放 input 與固定 buffer。
    /// 這個 worker 不會在 stdin 中接收機密資料，但仍清除已解析過的資料，以維持跨測試行程不保留可變輸入的生命週期界線。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var input = Interlocked.Exchange(ref _input, null);
        var recordBuffer = Interlocked.Exchange(ref _recordBuffer, null);
        var singleByteBuffer = Interlocked.Exchange(ref _singleByteBuffer, null);
        if (recordBuffer is not null)
        {
            Array.Clear(recordBuffer);
        }

        if (singleByteBuffer is not null)
        {
            Array.Clear(singleByteBuffer);
        }

        if (input is not null)
        {
            await input.DisposeAsync().ConfigureAwait(false);
        }
    }
}
