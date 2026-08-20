using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.Diagnostics;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 定義 Dataverse JSONL 執行軌跡的組態。預設關閉；開啟後，Trace 的生命週期與 DI singleton 相同，
/// 僅在程序關閉時由 <see cref="DataverseTrace.Dispose"/> flush 與釋放檔案資源。此設定不保存使用者、
/// 租約或 CRM 資料，且檔案保留數與大小皆有上限，避免診斷功能形成記憶體或磁碟洩漏。
/// </summary>
public sealed class DataverseTraceOptions
{
    /// <summary>取得或設定是否啟用執行軌跡；關閉時所有觀測熱路徑只進行一次布林判斷。</summary>
    public bool Enabled { get; set; }

    /// <summary>取得或設定 JSONL 基礎路徑；相對路徑會在背景寫入執行緒解析，避免阻塞 request。</summary>
    public string Path { get; set; } = "logs/dataverse-trace.jsonl";

    /// <summary>取得或設定單一檔案可寫入的最大位元組數，預設為 64MB。</summary>
    public long MaxFileBytes { get; set; } = 64L * 1024L * 1024L;

    /// <summary>取得或設定最多保留的最新 trace 檔案數，預設為五個，總量上限因而為 320MB。</summary>
    public int MaxRetainedFiles { get; set; } = 5;

    /// <summary>取得或設定非阻塞佇列容量；滿載時會捨棄最舊事件而非拖慢 request。</summary>
    public int QueueCapacity { get; set; } = 8192;

    /// <summary>取得或設定背景寫入與 flush 間隔；此等待永遠不在 request 執行緒上進行。</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// 從程序級統一診斷設定建立 Dataverse 專用選項。容量與佇列採安全預設，避免部署
    /// 設定遺漏時產生無界資源使用；本方法不會讀取或記錄任何連線認證。
    /// </summary>
    public static DataverseTraceOptions FromDiagnosticOptions(DiagnosticTraceOptions diagnosticOptions)
    {
        if (diagnosticOptions == null)
            throw new ArgumentNullException(nameof(diagnosticOptions));

        return new DataverseTraceOptions
        {
            Enabled = diagnosticOptions.Enabled,
            Path = diagnosticOptions.DataverseTracePath
        };
    }

    /// <summary>驗證所有資源邊界均為正值，避免啟用診斷後出現無效輪替或無界佇列。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Path))
            throw new ArgumentException("Dataverse Trace 路徑不得為空白。", nameof(Path));
        if (MaxFileBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxFileBytes));
        if (MaxRetainedFiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRetainedFiles));
        if (QueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity));
        if (FlushInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(FlushInterval));
    }
}

/// <summary>
/// 寫入 Dataverse 架構稽核所需的結構化 JSONL 軌跡。此 singleton 唯一擁有背景佇列、
/// <see cref="StreamWriter"/>、隨機 HMAC salt 與檔案輪替；request 執行緒只在啟用時建立小型事件並入列，
/// 從不執行同步磁碟 I/O。每個 request 的 traceId、使用者假名與 leaseId 均儲存在 <see cref="AsyncLocal{T}"/>
/// 範圍，結束時立即還原，因此不會跨 request、使用者、profile 或 tenant 留存識別資料。
/// </summary>
public sealed class DataverseTrace : IDisposable
{
    private enum EventKind
    {
        RequestBegin,
        RequestEnd,
        GatewayExecuteEnter,
        GatewayExecuteExit,
        PoolAcquireWait,
        PoolAcquireHit,
        PoolAcquireMiss,
        PoolAcquireTimeout,
        PoolAcquireFail,
        PoolCreateBegin,
        PoolCreateEnd,
        PoolHealth,
        PoolReturn,
        PoolDispose,
        PoolCleanup,
        CrmOperation,
        TraceDropped
    }

    private sealed class TraceEntry
    {
        internal EventKind Kind;
        internal string TraceId;
        internal string User;
        internal string LeaseId;
        internal string ClientId;
        internal string PoolKey;
        internal string Text;
        internal string State;
        internal string Reason;
        internal long First;
        internal long Second;
        internal long Third;
        internal bool Result;
    }

    private sealed class RequestContext
    {
        internal RequestContext(string traceId, string user, string leaseId = "")
        {
            TraceId = traceId;
            User = user;
            LeaseId = leaseId;
        }

        internal string TraceId { get; }
        internal string User { get; }
        internal string LeaseId { get; }
    }

    private sealed class RequestScope : IDisposable
    {
        private readonly DataverseTrace _owner;
        private readonly RequestContext _previous;
        private readonly RequestContext _current;
        private readonly DataverseTrace _previousTrace;
        private readonly long _startedTimestamp;
        private int _disposed;

        internal RequestScope(
            DataverseTrace owner,
            RequestContext previous,
            RequestContext current,
            DataverseTrace previousTrace)
        {
            _owner = owner;
            _previous = previous;
            _current = current;
            _previousTrace = previousTrace;
            _startedTimestamp = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            var duration = Stopwatch.GetElapsedTime(_startedTimestamp).TotalMilliseconds;
            _owner.Enqueue(new TraceEntry
            {
                Kind = EventKind.RequestEnd,
                TraceId = _current.TraceId,
                User = _current.User,
                First = Math.Max(0, (long)duration)
            });
            _owner._requestContext.Value = _previous;
            s_current.Value = _previousTrace;
        }
    }

    private sealed class LeaseScope : IDisposable
    {
        private readonly DataverseTrace _owner;
        private readonly RequestContext _previous;
        private int _disposed;

        internal LeaseScope(DataverseTrace owner, RequestContext previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner._requestContext.Value = _previous;
        }
    }

    private sealed class NoopScope : IDisposable
    {
        internal static readonly NoopScope Instance = new();

        public void Dispose() { }
    }

    // Trace 的目前執行個體只存在於 request 的 ExecutionContext。它不使用程序全域 singleton，
    // 所以同一個 process 內的另一個產品 Host 不可能接手或寫入本 Host 的使用者軌跡。
    private static readonly AsyncLocal<DataverseTrace> s_current = new();

    private readonly DataverseTraceOptions _options;
    private readonly AsyncLocal<RequestContext> _requestContext = new();
    private readonly ConcurrentQueue<TraceEntry> _queue = new();
    private readonly object _queueSync = new();
    private readonly CancellationTokenSource _writerWakeup = new();
    private readonly byte[] _salt;
    private readonly Task _writerTask;
    private StreamWriter _writer;
    private string _currentFilePath;
    private long _currentFileBytes;
    private long _dropped;
    private long _reportedDropped;
    private int _queued;
    private int _disposed;
    private int _writerFaulted;
    private long _lastTimestampTicks;
    private long _fileSequence;

    /// <summary>
    /// 建立 Trace singleton。啟用時產生只存在記憶體的隨機 salt 並啟動唯一背景寫入工作；
    /// 關閉時不建立檔案、不啟動背景工作，呼叫端可維持一次布林讀取加分支的成本。
    /// </summary>
    public DataverseTrace(DataverseTraceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        Enabled = _options.Enabled;
        if (Enabled)
        {
            _salt = RandomNumberGenerator.GetBytes(32);
            _writerTask = Task.Run(WriterLoopAsync);
        }

    }

    /// <summary>取得目前程序註冊的 Trace；未註冊時為 null，讓呼叫端不配置物件即可略過觀測。</summary>
    public static DataverseTrace Current => s_current.Value;

    /// <summary>取得此程序是否已啟用 JSONL 觀測；關閉時所有事件方法立即返回。</summary>
    public bool Enabled { get; }

    /// <summary>
    /// 開始目前執行單位的追蹤範圍。Host 層只提供關聯識別碼與原始身分來源；本方法依序選用
    /// identityName、sessionId、anon，並在 ToolUtility 內立即轉為每程序 salt 的 HMAC 假名。
    /// 原始名稱、帳號、email、會友識別碼與 CRM 值均不會進入佇列或 JSONL。
    /// </summary>
    /// <param name="traceId">由 Host 建立的單次工作關聯識別碼；只用於事件間關聯，不參與授權。</param>
    /// <param name="identityName">由 Host 提供的已驗證身分名稱；空值時才退回 Session Id。</param>
    /// <param name="sessionId">Host 的短期 Session 識別碼；只在沒有身分名稱時作為假名來源。</param>
    /// <returns>結束時還原 AsyncLocal 並寫出 request.end 的範圍；停用或三個輸入皆為 null 時為無操作範圍。</returns>
    public IDisposable BeginRequest(string traceId, string identityName, string sessionId)
    {
        // 停用時必須只有一次布林判斷與分支，不檢查輸入或配置任何 request 物件。
        if (!Enabled)
            return NoopScope.Instance;
        // 舊 API 收到 null HttpContext 時不建立事件；三個 Host 輸入皆為 null 時維持相同行為。
        if (traceId == null && identityName == null && sessionId == null)
            return NoopScope.Instance;

        var user = CreateUserPseudonym(identityName, sessionId);
        var current = new RequestContext(traceId, user);
        var previous = _requestContext.Value;
        var previousTrace = s_current.Value;
        _requestContext.Value = current;
        s_current.Value = this;
        Enqueue(new TraceEntry { Kind = EventKind.RequestBegin, TraceId = current.TraceId, User = current.User });
        return new RequestScope(this, previous, current, previousTrace);
    }

    /// <summary>
    /// 將識別來源轉成不可逆的短假名。salt 每個 Trace 實例隨機產生、僅留在記憶體且在 Dispose 時清零；
    /// 相同程序中的同一來源可關聯，跨程序則不能反推或穩定對照。
    /// </summary>
    public string CreateUserPseudonym(string identityName, string sessionId)
    {
        if (!Enabled)
            return "u_disabled";

        var source = !string.IsNullOrWhiteSpace(identityName)
            ? identityName
            : !string.IsNullOrWhiteSpace(sessionId)
                ? sessionId
                : "anon";
        var bytes = Encoding.UTF8.GetBytes(source);
        var hash = HMACSHA256.HashData(_salt, bytes);
        return "u_" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>記錄進入 Gateway 的 reentrant 深度；僅在目前有 HTTP request 關聯時輸出。</summary>
    public void GatewayExecuteEnter(int depth)
    {
        if (!Enabled)
            return;
        if (!TryGetRequest(out var context))
            return;
        Enqueue(new TraceEntry { Kind = EventKind.GatewayExecuteEnter, TraceId = context.TraceId, User = context.User, First = depth });
    }

    /// <summary>記錄離開 Gateway 的 reentrant 深度；不改變 lease、例外或歸還順序。</summary>
    public void GatewayExecuteExit(int depth)
    {
        if (!Enabled)
            return;
        if (!TryGetRequest(out var context))
            return;
        Enqueue(new TraceEntry { Kind = EventKind.GatewayExecuteExit, TraceId = context.TraceId, User = context.User, First = depth });
    }

    /// <summary>記錄 pool semaphore 的等待毫秒數；不會在沒有 request 關聯時捏造 traceId。</summary>
    public void PoolAcquireWait(long waitedMs)
    {
        if (!Enabled)
            return;
        if (!TryGetRequest(out var context))
            return;
        Enqueue(new TraceEntry { Kind = EventKind.PoolAcquireWait, TraceId = context.TraceId, First = Math.Max(0, waitedMs) });
    }

    /// <summary>記錄新建或重用 client 的 lease；poolKey 只含結構化隔離欄位，永不含密碼。</summary>
    public void PoolAcquire(string leaseId, string clientId, string poolKey, bool hit)
    {
        if (!Enabled)
            return;
        if (!TryGetRequest(out var context))
            return;
        Enqueue(new TraceEntry
        {
            Kind = hit ? EventKind.PoolAcquireHit : EventKind.PoolAcquireMiss,
            TraceId = context.TraceId,
            User = context.User,
            LeaseId = leaseId,
            ClientId = clientId,
            PoolKey = poolKey
        });
    }

    /// <summary>記錄 request 關聯的取得逾時，供外部分析器比對等待與容量壓力。</summary>
    public void PoolAcquireTimeout()
    {
        if (!Enabled)
            return;
        if (!TryGetRequest(out var context))
            return;
        Enqueue(new TraceEntry { Kind = EventKind.PoolAcquireTimeout, TraceId = context.TraceId });
    }

    /// <summary>
    /// 記錄 Acquire 在取得 semaphore 之後失敗而未產生 lease 的情形。
    /// </summary>
    /// <remarks>
    /// 沒有這個事件時，建線失敗的 request 只會留下一筆 <c>pool.acquire.wait</c> 而沒有任何 hit／miss，
    /// 在稽核檔中形同消失；分析器因此無法解釋最慢的那些 request。有了本事件，
    /// <c>wait == hit + miss + fail</c> 便成為可驗證的等式。
    /// </remarks>
    /// <param name="phase">失敗發生的階段：ensureMin、health、lease 或 create。</param>
    /// <param name="waitedMs">在 semaphore 上等待的毫秒數。</param>
    /// <param name="totalMs">自 Acquire 進入起算的總毫秒數，可直接對照 request 耗時。</param>
    /// <param name="errorKind">例外型別名稱；刻意不記錄訊息內容，避免 URL、帳號或 CRM 資料進入稽核檔。</param>
    public void PoolAcquireFail(string phase, long waitedMs, long totalMs, string errorKind)
    {
        if (!Enabled)
            return;
        TryGetRequest(out var context);
        Enqueue(new TraceEntry
        {
            Kind = EventKind.PoolAcquireFail,
            TraceId = context?.TraceId ?? string.Empty,
            Text = phase ?? string.Empty,
            State = errorKind ?? string.Empty,
            First = Math.Max(0, waitedMs),
            Second = Math.Max(0, totalMs)
        });
    }

    /// <summary>
    /// 記錄一條實體連線開始建立。與 <see cref="PoolCreateEnd"/> 成對，用來量測建線耗時
    /// —— 這段時間目前在子池鎖內執行，因此它同時是其他 request 被阻擋的時間下限。
    /// </summary>
    /// <param name="poolKey">已格式化的隔離鍵；不含密碼或呼叫端資料。</param>
    /// <param name="reason">建立原因：ensureMin（補足 MinSize）或 overflow（idle 用盡時補建）。</param>
    public void PoolCreateBegin(string poolKey, string reason)
    {
        if (!Enabled)
            return;
        TryGetRequest(out var context);
        Enqueue(new TraceEntry
        {
            Kind = EventKind.PoolCreateBegin,
            TraceId = context?.TraceId ?? string.Empty,
            PoolKey = poolKey,
            Reason = reason
        });
    }

    /// <summary>
    /// 記錄一條實體連線建立結束，含耗時與成敗。失敗時 clientId 為空，因為 client 從未成立。
    /// </summary>
    /// <param name="clientId">成功時為新 client 的識別碼；失敗時為空字串。</param>
    /// <param name="reason">與 <see cref="PoolCreateBegin"/> 相同的建立原因。</param>
    /// <param name="elapsedMs">建立耗時毫秒。</param>
    /// <param name="ok">是否成功建立。</param>
    /// <param name="errorKind">失敗時的例外型別名稱；成功時為空字串。不記錄訊息內容。</param>
    public void PoolCreateEnd(string clientId, string reason, long elapsedMs, bool ok, string errorKind)
    {
        if (!Enabled)
            return;
        TryGetRequest(out var context);
        Enqueue(new TraceEntry
        {
            Kind = EventKind.PoolCreateEnd,
            TraceId = context?.TraceId ?? string.Empty,
            ClientId = clientId ?? string.Empty,
            Reason = reason,
            State = errorKind ?? string.Empty,
            First = Math.Max(0, elapsedMs),
            Result = ok
        });
    }

    /// <summary>記錄 WhoAmI 類健康檢查結果；此事件只包含 client ID 與布林結果，不含 CRM 回應內容。</summary>
    public void PoolHealth(string clientId, bool result)
    {
        if (!Enabled)
            return;
        Enqueue(new TraceEntry { Kind = EventKind.PoolHealth, ClientId = clientId, Result = result });
    }

    /// <summary>
    /// 記錄 lease 歸還的隔離結果。callerIdAtReturn 必須由呼叫端在 Run F 清除前讀取；
    /// 此值僅是 GUID 字串或空值，不會記錄使用者、CRM entity 或任何 profile 資料。
    /// </summary>
    public void PoolReturn(string leaseId, string clientId, string state, string callerIdAtReturn, long heldMs)
    {
        if (!Enabled)
            return;
        if (!TryGetRequest(out var context))
            return;
        Enqueue(new TraceEntry
        {
            Kind = EventKind.PoolReturn,
            TraceId = context.TraceId,
            User = context.User,
            LeaseId = leaseId,
            ClientId = clientId,
            State = state,
            Text = callerIdAtReturn ?? string.Empty,
            First = Math.Max(0, heldMs)
        });
    }

    /// <summary>
    /// 記錄 pool 嘗試釋放 client 時的即時狀態與原因。狀態由呼叫點緊鄰 Dispose 時讀取，
    /// 因此能辨識 cleanup 與 Acquire 的交錯；此事件本身不改變 Run F 的延後淘汰行為。
    /// </summary>
    public void PoolDispose(string clientId, string stateAtDispose, string reason)
    {
        if (!Enabled)
            return;
        Enqueue(new TraceEntry
        {
            Kind = EventKind.PoolDispose,
            ClientId = clientId,
            State = stateAtDispose,
            Reason = reason
        });
    }

    /// <summary>記錄 cleanup 前後的 idle 數量與 MinSize，下游可據此驗證 Run F 的保底不變量。</summary>
    public void PoolCleanup(long idleBefore, long idleAfter, long minSize)
    {
        if (!Enabled)
            return;
        Enqueue(new TraceEntry { Kind = EventKind.PoolCleanup, First = idleBefore, Second = idleAfter, Third = minSize });
    }

    /// <summary>
    /// 記錄由 Gateway 代理執行的 CRM 操作名稱與目前 lease。沒有 request 或 lease 關聯時不輸出，
    /// 避免捏造 HttpContext traceId；正常代理路徑的 leaseId 必定非空，可供稽核繞過 Gateway 的情況。
    /// </summary>
    public void CrmOperation(string operation)
    {
        if (!Enabled)
            return;
        if (!TryGetRequest(out var context))
            return;
        Enqueue(new TraceEntry
        {
            Kind = EventKind.CrmOperation,
            TraceId = context.TraceId,
            // crm.op 的 schema 固定含 leaseId；在尚未由 pool 接線的早期呼叫點以空字串
            // 表示「沒有可證明的 lease」，而非輸出 JSON null 讓分析器誤判為損毀紀錄。
            LeaseId = context.LeaseId ?? string.Empty,
            Text = operation
        });
    }

    /// <summary>
    /// 將目前 request 的 lease 關聯推入 AsyncLocal，讓 GatewayOrganizationService 與 Ambient proxy 的
    /// crm.op 都能寫出同一 leaseId；Scope 結束後立即還原，絕不跨巢狀操作或 request 留存。
    /// </summary>
    public IDisposable PushLease(string leaseId)
    {
        if (!Enabled)
            return NoopScope.Instance;
        if (!TryGetRequest(out var context))
            return NoopScope.Instance;
        // AsyncLocal 的值必須不可變：同一 request 若平行啟動多個子工作，ExecutionContext 會複製
        // 參考而非深複製物件。以新 context 取代目前值可讓每個 flow 各自還原 lease，避免把 A 的
        // leaseId 暫時寫入 B 的 flow；RequestScope 仍是唯一負責還原 request 身分的外層 owner。
        var previous = context;
        _requestContext.Value = new RequestContext(previous.TraceId, previous.User, leaseId ?? string.Empty);
        return new LeaseScope(this, previous);
    }

    /// <summary>
    /// 停止接受新事件、喚醒背景工作、flush 已入列的 JSONL 並釋放 writer。此方法只應由 DI singleton
    /// 關閉流程呼叫；其同步等待發生於程序關閉，不會在 request 執行緒進行磁碟 I/O。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (Enabled)
        {
            try
            {
                _writerWakeup.Cancel();
                _writerTask.GetAwaiter().GetResult();
            }
            catch
            {
                // 診斷檔 I/O 不得改變應用程式關閉、pool shutdown 或其他資源的釋放結果；
                // WriterLoop 已 fail-closed 停止收集，這裡只確保其餘 Trace 擁有資源仍會被釋放。
            }
            finally
            {
                if (_salt != null)
                    CryptographicOperations.ZeroMemory(_salt);
            }
        }

        _writerWakeup.Dispose();
        if (ReferenceEquals(s_current.Value, this))
            s_current.Value = null;
    }

    private bool TryGetRequest(out RequestContext context)
    {
        context = _requestContext.Value;
        return context != null && !string.IsNullOrWhiteSpace(context.TraceId);
    }

    private void Enqueue(TraceEntry entry)
    {
        if (!Enabled || Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _writerFaulted) != 0)
            return;

        // 此臨界區只保護 ConcurrentQueue 的入列／淘汰與計數，不含 JSON、檔案或 flush I/O；
        // 因而 request 不會等待磁碟。以同一把鎖更新三者可維持容量硬上限，避免 writer 與
        // producer 交錯時出現負計數，造成無界保留。
        lock (_queueSync)
        {
            if (Volatile.Read(ref _writerFaulted) != 0)
                return;

            _queue.Enqueue(entry);
            _queued++;
            while (_queued > _options.QueueCapacity && _queue.TryDequeue(out _))
            {
                _queued--;
                _dropped++;
            }
        }
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            while (true)
            {
                try
                {
                    await Task.Delay(_options.FlushInterval, _writerWakeup.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_writerWakeup.IsCancellationRequested)
                {
                    // Dispose 會喚醒等待中的背景工作，接著 drain 所有已入列事件。
                }

                try
                {
                    DrainQueue();
                }
                catch
                {
                    // 寫入、輪替或舊檔刪除失敗時停止收集並丟棄待寫事件；診斷功能不得讓
                    // request、pool 歸還或 Host 關閉因可觀測性 I/O 而失敗或無界累積。
                    Interlocked.Exchange(ref _writerFaulted, 1);
                    DiscardQueuedEntries();
                    return;
                }
                if (Volatile.Read(ref _disposed) != 0 && _queue.IsEmpty)
                    break;
            }
        }
        finally
        {
            try { _writer?.Flush(); } catch { }
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }

    private void DrainQueue()
    {
        while (true)
        {
            TraceEntry entry;
            lock (_queueSync)
            {
                if (!_queue.TryDequeue(out entry))
                    break;
                _queued--;
            }
            WriteEntry(entry);
        }

        var dropped = Interlocked.Read(ref _dropped);
        if (dropped > _reportedDropped)
        {
            _reportedDropped = dropped;
            WriteEntry(new TraceEntry { Kind = EventKind.TraceDropped, First = dropped });
        }
        _writer?.Flush();
    }

    private void DiscardQueuedEntries()
    {
        lock (_queueSync)
        {
            while (_queue.TryDequeue(out _))
                _queued--;
            _queued = 0;
        }
    }

    private void WriteEntry(TraceEntry entry)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("ts", NextTimestamp());
            json.WriteString("ev", GetEventName(entry.Kind));
            WriteEventFields(json, entry);
            json.WriteEndObject();
            json.Flush();
        }

        var byteCount = buffer.WrittenCount + Environment.NewLine.Length;
        if (_writer == null || _currentFileBytes + byteCount > _options.MaxFileBytes)
            RotateWriter();
        _writer.Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
        _writer.WriteLine();
        _currentFileBytes += byteCount;
    }

    private void RotateWriter()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;

        var configuredPath = System.IO.Path.GetFullPath(_options.Path);
        var directory = System.IO.Path.GetDirectoryName(configuredPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var nextPath = _currentFilePath == null
            ? configuredPath
            : System.IO.Path.Combine(
                directory ?? Environment.CurrentDirectory,
                $"{System.IO.Path.GetFileNameWithoutExtension(configuredPath)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Interlocked.Increment(ref _fileSequence)}{System.IO.Path.GetExtension(configuredPath)}");
        PruneOldFiles(configuredPath, nextPath);

        _writer = new StreamWriter(
            new FileStream(
                nextPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            64 * 1024);
        _currentFilePath = nextPath;
        _currentFileBytes = 0;
    }

    private void PruneOldFiles(string configuredPath, string nextPath)
    {
        var directory = System.IO.Path.GetDirectoryName(configuredPath) ?? Environment.CurrentDirectory;
        var prefix = System.IO.Path.GetFileNameWithoutExtension(configuredPath);
        var extension = System.IO.Path.GetExtension(configuredPath);
        var files = Directory.EnumerateFiles(directory, prefix + "*" + extension)
            .Where(path => !string.Equals(path, nextPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(File.GetLastWriteTimeUtc)
            .ToList();
        while (files.Count >= _options.MaxRetainedFiles)
        {
            File.Delete(files[0]);
            files.RemoveAt(0);
        }
    }

    private string NextTimestamp()
    {
        var now = DateTime.UtcNow.Ticks;
        if (now <= _lastTimestampTicks)
            now = _lastTimestampTicks + 1;
        _lastTimestampTicks = now;
        return new DateTime(now, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
    }

    private static string GetEventName(EventKind kind)
    {
        return kind switch
        {
            EventKind.RequestBegin => "request.begin",
            EventKind.RequestEnd => "request.end",
            EventKind.GatewayExecuteEnter => "gateway.execute.enter",
            EventKind.GatewayExecuteExit => "gateway.execute.exit",
            EventKind.PoolAcquireWait => "pool.acquire.wait",
            EventKind.PoolAcquireHit => "pool.acquire.hit",
            EventKind.PoolAcquireMiss => "pool.acquire.miss",
            EventKind.PoolAcquireTimeout => "pool.acquire.timeout",
            EventKind.PoolAcquireFail => "pool.acquire.fail",
            EventKind.PoolCreateBegin => "pool.create.begin",
            EventKind.PoolCreateEnd => "pool.create.end",
            EventKind.PoolHealth => "pool.health",
            EventKind.PoolReturn => "pool.return",
            EventKind.PoolDispose => "pool.dispose",
            EventKind.PoolCleanup => "pool.cleanup",
            EventKind.CrmOperation => "crm.op",
            EventKind.TraceDropped => "trace.dropped",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static void WriteEventFields(Utf8JsonWriter json, TraceEntry entry)
    {
        switch (entry.Kind)
        {
            case EventKind.RequestBegin:
                WriteTraceAndUser(json, entry);
                break;
            case EventKind.RequestEnd:
                WriteTraceAndUser(json, entry);
                json.WriteNumber("durationMs", entry.First);
                break;
            case EventKind.GatewayExecuteEnter:
            case EventKind.GatewayExecuteExit:
                WriteTraceAndUser(json, entry);
                json.WriteNumber("depth", entry.First);
                break;
            case EventKind.PoolAcquireWait:
                json.WriteString("traceId", entry.TraceId);
                json.WriteNumber("waitedMs", entry.First);
                break;
            case EventKind.PoolAcquireHit:
            case EventKind.PoolAcquireMiss:
                WriteTraceAndUser(json, entry);
                json.WriteString("leaseId", entry.LeaseId);
                json.WriteString("clientId", entry.ClientId);
                json.WriteString("poolKey", entry.PoolKey);
                break;
            case EventKind.PoolAcquireTimeout:
                json.WriteString("traceId", entry.TraceId);
                break;
            case EventKind.PoolAcquireFail:
                json.WriteString("traceId", entry.TraceId);
                json.WriteString("phase", entry.Text);
                json.WriteNumber("waitedMs", entry.First);
                json.WriteNumber("totalMs", entry.Second);
                json.WriteString("errKind", entry.State);
                break;
            case EventKind.PoolCreateBegin:
                json.WriteString("traceId", entry.TraceId);
                json.WriteString("poolKey", entry.PoolKey);
                json.WriteString("reason", entry.Reason);
                break;
            case EventKind.PoolCreateEnd:
                json.WriteString("traceId", entry.TraceId);
                json.WriteString("clientId", entry.ClientId);
                json.WriteString("reason", entry.Reason);
                json.WriteNumber("ms", entry.First);
                json.WriteBoolean("ok", entry.Result);
                json.WriteString("errKind", entry.State);
                break;
            case EventKind.PoolHealth:
                json.WriteString("clientId", entry.ClientId);
                json.WriteBoolean("result", entry.Result);
                break;
            case EventKind.PoolReturn:
                WriteTraceAndUser(json, entry);
                json.WriteString("leaseId", entry.LeaseId);
                json.WriteString("clientId", entry.ClientId);
                json.WriteString("state", entry.State);
                json.WriteString("callerIdAtReturn", entry.Text);
                json.WriteNumber("heldMs", entry.First);
                break;
            case EventKind.PoolDispose:
                json.WriteString("clientId", entry.ClientId);
                json.WriteString("stateAtDispose", entry.State);
                json.WriteString("reason", entry.Reason);
                break;
            case EventKind.PoolCleanup:
                json.WriteNumber("idleBefore", entry.First);
                json.WriteNumber("idleAfter", entry.Second);
                json.WriteNumber("minSize", entry.Third);
                break;
            case EventKind.CrmOperation:
                json.WriteString("traceId", entry.TraceId);
                json.WriteString("op", entry.Text);
                json.WriteString("leaseId", entry.LeaseId);
                break;
            case EventKind.TraceDropped:
                json.WriteNumber("count", entry.First);
                break;
        }
    }

    private static void WriteTraceAndUser(Utf8JsonWriter json, TraceEntry entry)
    {
        json.WriteString("traceId", entry.TraceId);
        json.WriteString("user", entry.User);
    }
}
