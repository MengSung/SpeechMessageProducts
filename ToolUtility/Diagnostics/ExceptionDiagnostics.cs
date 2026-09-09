using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.Diagnostics;

/// <summary>
/// Debug／Release 共用的錯誤紀錄 owner。先同步完成有限大小 JSONL 落檔及 flush，
/// 再將純文字摘要放入最多 64 筆的 LINE 佇列；不保存例外、請求、身分或驗證資料。
/// 每個部署目錄最多保留目前檔與五份備份，單筆不超過 4 KiB；正常路徑不執行任何 I/O。
/// </summary>
public sealed class ExceptionDiagnostics : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maximumFileBytes;
    private readonly Mutex _fileMutex;
    private readonly ConditionalWeakTable<Exception, object> _reported = new();
    private readonly Channel<string> _notifications = Channel.CreateBounded<string>(
        new BoundedChannelOptions(64) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _stop = new();
    private readonly AsyncLocal<bool> _sending = new();
    private Task _consumer;
    private Task _disposeTask;
    private bool _closed;

    /// <summary>
    /// 建立獨立 owner。directory 必須由部署組合根提供，不能來自 request。
    /// 命名 mutex 讓相同目錄的多程序輪替不會互相覆寫；只在錯誤落檔期間持有。
    /// </summary>
    public ExceptionDiagnostics(string directory, long maximumFileBytes = 5 * 1024 * 1024)
    {
        if (maximumFileBytes < 4096) throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        _directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        _maximumFileBytes = maximumFileBytes;
        var normalized = OperatingSystem.IsWindows() ? _directory.ToUpperInvariant() : _directory;
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        _fileMutex = new Mutex(false, (OperatingSystem.IsWindows() ? "Global\\" : "") + "ExceptionLog-" + identity);
    }

    /// <summary>
    /// 啟動唯一通知 consumer。sender 必須遵守 cancellation，且由呼叫端在本 owner drain 後釋放。
    /// 不繼承呼叫者 ExecutionContext，避免 request 的 AsyncLocal 身分流入長命背景工作。
    /// </summary>
    public void StartNotifications(Func<string, CancellationToken, Task> sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_consumer != null) throw new InvalidOperationException("Notification consumer already started.");
            if (ExecutionContext.IsFlowSuppressed()) _consumer = Task.Run(() => ConsumeAsync(sender));
            else
            {
                using (ExecutionContext.SuppressFlow()) _consumer = Task.Run(() => ConsumeAsync(sender));
            }
        }
    }

    /// <summary>
    /// 在功能確定失敗的邊界呼叫。相同例外實例以 weak key 去重，不延長例外生命；
    /// 只有呼叫者 token 已取消的 OperationCanceledException 視為正常取消，內部逾時仍須記錄。
    /// 不讀 Message、Data、原始 StackTrace 或任何使用者值。成功回傳表示落檔完成，
    /// 不代表 LINE 已送達；磁碟失敗時不發送 LINE，也不把原例外換成記錄例外。
    /// </summary>
    public bool Report(Exception exception, string operation, CancellationToken cancellationToken = default,
        bool notify = true)
    {
        lock (_gate)
        {
            if (_closed || (exception != null && _reported.TryGetValue(exception, out _))) return false;
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                // middleware 已確認正常取消時仍留 weak marker，避免外層 framework logger 再次上報。
                _reported.Add(exception, new object());
                return false;
            }
            try
            {
                var incidentId = Guid.NewGuid().ToString("N");
                var record = JsonSerializer.Serialize(new
                {
                    IncidentId = incidentId,
                    Utc = DateTimeOffset.UtcNow,
                    Operation = Symbol(operation),
                    ExceptionType = Symbol(exception?.GetType().FullName ?? "ReportedError"),
                    Location = Symbol(exception?.TargetSite?.DeclaringType?.FullName) + "." +
                               Symbol(exception?.TargetSite?.Name),
                    HResult = exception?.HResult ?? 0,
                    Stack = StackSymbols(exception)
                }, JsonOptions);
                if (!Write(record)) return false;
                if (exception != null) _reported.Add(exception, new object());
                if (notify && !_sending.Value && !_notifications.Writer.TryWrite(record))
                    WriteStatus("LineQueueFull", incidentId);
                return true;
            }
            catch
            {
                Emergency("ExceptionLogWriteFailed");
                return false;
            }
        }
    }

    /// <summary>
    /// 字段僅接受開發者擁有的型別／方法符號並限制 80 字；這不是任意文字的去識別化器。
    /// 呼叫端禁止傳入 route 實值、request identifier、姓名或 log formatter 的動態內容。
    /// </summary>
    private static string Symbol(string value)
    {
        if (string.IsNullOrEmpty(value)) return "unknown";
        var output = new StringBuilder(Math.Min(value.Length, 80));
        foreach (var c in value)
        {
            if (output.Length == 80) break;
            output.Append(char.IsLetterOrDigit(c) || c is '.' or '_' or '+' or '-' ? c : '_');
        }
        return output.ToString();
    }

    /// <summary>
    /// 最多五層程式符號與 PDB 行號，不輸出原始路徑／參數／訊息，方便定位 Release 故障。
    /// 無 PDB 時行號為 0；不保存 StackTrace 或 MethodBase 物件。
    /// </summary>
    private static string StackSymbols(Exception exception)
    {
        if (exception == null) return "";
        var trace = new StackTrace(exception, true);
        var symbols = new StringBuilder();
        for (var i = 0; i < Math.Min(trace.FrameCount, 5); i++)
        {
            var frame = trace.GetFrame(i);
            var method = frame?.GetMethod();
            symbols.Append(Symbol(method?.DeclaringType?.FullName + "." + method?.Name));
            symbols.Append(':').Append(frame?.GetFileLineNumber() ?? 0).Append(';');
        }
        return symbols.ToString();
    }

    /// <summary>
    /// 跨程序鎖最多等一秒；UTF-8 無 BOM、CRLF、flush(true) 成功後才允許 LINE 入列。
    /// stream 與 mutex 取得權在 finally／using 確定性釋放。磁碟滿或拒絕存取回 stderr，
    /// 不遞迴寫入同一 logger；不得將診斷檔放在 wwwroot 或公開檔案服務目錄。
    /// </summary>
    private bool Write(string record)
    {
        var acquired = false;
        try
        {
            try { acquired = _fileMutex.WaitOne(TimeSpan.FromSeconds(1)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { Emergency("ExceptionLogLockTimeout"); return false; }
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "Exception.log");
            var bytes = Encoding.UTF8.GetBytes(record + "\r\n");
            if (bytes.Length > 4096) throw new InvalidOperationException("Diagnostic record size exceeded.");
            if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > _maximumFileBytes)
            {
                try
                {
                    for (var i = 5; i >= 1; i--)
                    {
                        var target = Path.Combine(_directory, $"Exception.{i}.log");
                        var source = i == 1 ? path : Path.Combine(_directory, $"Exception.{i - 1}.log");
                        if (File.Exists(source)) File.Move(source, target, true);
                    }
                }
                catch
                {
                    // 外部讀取器未開放 delete share 時無法輪替；先保留原始證據，
                    // 只允許在兩倍正常上限內降級附加。每次新事件仍會重試輪替，
                    // 讀取器釋放後即恢復五份備份策略；達硬上限則拒絕寫入，
                    // 避免以無界檔案成長換取告警，且不會在未 flush 時入列 LINE。
                    Emergency("ExceptionLogRotationFailed");
                    return AppendWithinDegradedCap(path, bytes);
                }
            }
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch { Emergency("ExceptionLogWriteFailed"); return false; }
        finally { if (acquired) _fileMutex.ReleaseMutex(); }
    }

    /// <summary>
    /// 輪替遭外部讀取器拒絕時的有限降級路徑。允許檔案暫時成長至正常上限兩倍，
    /// 使用允許其他讀取器且禁止並行寫入的短生命串流，並以 flush(true) 確保證據已落盤。
    /// 下一筆事件會再次嘗試輪替；硬上限內無法開啟或寫入時回傳 false，呼叫端不得通知 LINE。
    /// </summary>
    private bool AppendWithinDegradedCap(string path, byte[] bytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var currentLength = stream.Length;
            var hardCap = _maximumFileBytes > long.MaxValue / 2 ? long.MaxValue : _maximumFileBytes * 2;
            if (currentLength > hardCap - bytes.Length)
            {
                Emergency("ExceptionLogDegradedCapReached");
                return false;
            }

            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch
        {
            Emergency("ExceptionLogWriteFailed");
            return false;
        }
    }

    /// <summary>通知基礎設施狀態只落本地，關聯既有事件 ID；不包含 provider 回應或再次入列。</summary>
    private void WriteStatus(string status, string incident)
    {
        Write(JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow, Status = status, Incident = incident }));
    }

    /// <summary>最後保底不讀原例外，避免磁碟錯誤把機密路徑或例外文字送到 stdout。</summary>
    private static void Emergency(string code)
    {
        try { Console.Error.WriteLine("[ExceptionDiagnostics] " + code); } catch { }
    }

    /// <summary>
    /// 單一 reader 逐筆傳送；每筆最多五秒。sender 故障只記狀態；關機取消後剩餘摘要
    /// 由 Dispose 清空。未加入自動重試，以免不確定已送達時造成管理者收到重複訊息。
    /// </summary>
    private async Task ConsumeAsync(Func<string, CancellationToken, Task> sender)
    {
        try
        {
            await foreach (var message in _notifications.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    // sender 路徑若透過任何 ILogger 再報錯，只允許落檔，避免背景工作互相生出告警。
                    _sending.Value = true;
                    await sender(message, timeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    using var incident = JsonDocument.Parse(message);
                    lock (_gate) WriteStatus("LineDeliveryFailed", incident.RootElement.GetProperty("IncidentId").GetString());
                }
                finally { _sending.Value = false; }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    /// <summary>
    /// 停止接收並等待最多五秒 drain，再取消可取消的 sender 並等待實際完成，
    /// 最後清空摘要、weak keys、CTS 與 mutex。多次 Dispose 共用同一完成 task，
    /// 防止任何 consumer 尚未停止時提前釋放 I/O 資源。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask != null) return new ValueTask(_disposeTask);
            _closed = true;
            _notifications.Writer.TryComplete();
            _disposeTask = FinishAsync();
            return new ValueTask(_disposeTask);
        }
    }

    /// <summary>只由 Dispose 啟動一次；有限關機計時器本身也以 using 釋放。</summary>
    private async Task FinishAsync()
    {
        if (_consumer != null)
        {
            using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _consumer.WaitAsync(drain.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { _stop.Cancel(); await _consumer.ConfigureAwait(false); }
        }
        lock (_gate)
        {
            var remaining = 0;
            while (_notifications.Reader.TryRead(out _)) remaining++;
            if (remaining > 0) WriteStatus("LinePendingAtShutdown", remaining.ToString());
            _reported.Clear();
            _consumer = null;
            _stop.Dispose();
            _fileMutex.Dispose();
        }
    }
}
