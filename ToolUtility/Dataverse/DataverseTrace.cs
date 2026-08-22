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
    /// 資源快照（proc.snapshot / pool.snapshot）的最小輸出間隔。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 這個值在兩個目標之間取捨：間隔太長會讓記憶體或連線的成長趨勢採樣不足而看不出斜率；
    /// 太短則會讓快照淹沒稽核檔，並且每次都要付出讀取程序計量值的成本
    /// （<c>Process.PrivateMemorySize64</c> 與 <c>HandleCount</c> 皆為系統呼叫）。
    /// </para>
    /// <para>
    /// 預設 30 秒：一次十分鐘的除錯重現可得到約 20 個取樣點，足以分辨「持續上升」與「上下震盪」，
    /// 而整段期間的額外事件量不到 50 筆，遠低於單一 request 的 crm.op 數量。
    /// </para>
    /// </remarks>
    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromSeconds(30);

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
        if (SnapshotInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SnapshotInterval));
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
        BackgroundBegin,
        BackgroundEnd,
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
        PoolSnapshot,
        ProcessSnapshot,
        PoolLockWait,
        GatewayConcurrent,
        GatewayScopeEnd,
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
        internal long Fourth;
        internal long Fifth;
        internal long Sixth;
        internal long Seventh;
        internal long Eighth;
        internal bool Result;

        /// <summary>
        /// 只有 proc.snapshot 使用的資源量測承載。其餘事件維持 null，因此熱路徑事件仍是
        /// 單一物件、零額外配置；快照每數十秒才產生一次，額外配置可忽略。
        /// </summary>
        internal ProcessSnapshotData Process;

        /// <summary>只有 pool.snapshot 使用的連線池計數承載；其餘事件維持 null。</summary>
        internal PoolSnapshotData Pool;
    }

    /// <summary>
    /// 一次程序層級資源量測。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 存在理由：先前的分析報告只能寫出「本報告無法證明不存在記憶體洩漏」，因為整份 Trace 沒有
    /// 任何一筆資源基準線。有了時間序列的這些值，「單調成長」才第一次成為可被檢定的命題，
    /// 而不是靠感覺判斷。
    /// </para>
    /// <para>
    /// 這些全部是程序自身的計量值，不含任何使用者、request 或 CRM 資料，因此不構成隱私風險。
    /// </para>
    /// </remarks>
    public sealed class ProcessSnapshotData
    {
        /// <summary>Managed 堆目前配置量（MB）。不強制 GC，因此不影響被觀測程序的行為。</summary>
        public long ManagedMb { get; init; }

        /// <summary>GC 回報的堆大小（MB），含尚未回收的空間，可與 <see cref="ManagedMb"/> 對照出碎片化。</summary>
        public long HeapMb { get; init; }

        /// <summary>作業系統認定的私有記憶體（MB）。Managed 正常但此值持續上升，指向非受控資源洩漏。</summary>
        public long PrivateMb { get; init; }

        /// <summary>Gen0 累計回收次數。與 Gen2 的比例可看出配置壓力來自短命還是長命物件。</summary>
        public int Gen0 { get; init; }

        /// <summary>Gen1 累計回收次數。</summary>
        public int Gen1 { get; init; }

        /// <summary>Gen2 累計回收次數。持續上升代表有物件不斷晉升到長命世代，是洩漏的典型徵兆。</summary>
        public int Gen2 { get; init; }

        /// <summary>作業系統控制代碼數。連線、檔案與 socket 未釋放時此值單調上升，是非受控洩漏最直接的指標。</summary>
        public int Handles { get; init; }

        /// <summary>程序執行緒總數。與下列 ThreadPool 數值一起看，可辨識執行緒洩漏或飢餓。</summary>
        public int Threads { get; init; }

        /// <summary>ThreadPool 目前執行緒數。搭配 <see cref="PendingWorkItems"/> 可辨識執行緒池飢餓。</summary>
        public int PoolThreads { get; init; }

        /// <summary>ThreadPool 待處理工作項目數。持續 &gt; 0 代表工作進來的速度超過處理速度。</summary>
        public long PendingWorkItems { get; init; }
    }

    /// <summary>
    /// 一次連線池計數快照。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 存在理由：連線洩漏的判準是「建立數與銷毀數的差值隨時間單調成長」，而這需要時間序列，
    /// 單筆事件無法證明。<see cref="Alive"/> 是核心指標：穩定運行下它應在 MinSize 附近震盪，
    /// 若持續上升則代表 client 只進不出。
    /// </para>
    /// <para>
    /// <see cref="SubPools"/> 是另一顆地雷的哨兵：子池字典目前只在 pool 關閉時清空，沒有空子池
    /// 回收路徑。今天只有一個 poolKey 所以無害，但啟用 per-user impersonation 之後，每個使用者
    /// 都會產生一個永不回收的子池，而每個子池各自持有一個 SemaphoreSlim。
    /// </para>
    /// </remarks>
    public sealed class PoolSnapshotData
    {
        /// <summary>已格式化的隔離鍵；不含密碼或呼叫端資料。</summary>
        public string PoolKey { get; init; }

        /// <summary>目前可立即出借的 client 數。</summary>
        public int Idle { get; init; }

        /// <summary>目前已出借、正被某個 request 持有的 client 數。</summary>
        public int Leased { get; init; }

        /// <summary>尚未被銷毀的 client 總數（Idle + Leased + 其他非 Disposed 狀態）。洩漏偵測的主指標。</summary>
        public int Alive { get; init; }

        /// <summary>已保留名額但尚未完成建立的 client 數。長期不歸零代表有建線卡住。</summary>
        public int Pending { get; init; }

        /// <summary>程序啟動至今累計建立的 client 數。</summary>
        public long Created { get; init; }

        /// <summary>程序啟動至今累計銷毀的 client 數。與 <see cref="Created"/> 的差值應約等於 <see cref="Alive"/>。</summary>
        public long Discarded { get; init; }

        /// <summary>累計租借次數。</summary>
        public long TotalAcquires { get; init; }

        /// <summary>累計歸還次數。與 <see cref="TotalAcquires"/> 的差值等於目前未歸還的租約數。</summary>
        public long TotalReleases { get; init; }

        /// <summary>目前在 semaphore 上等待的呼叫端數。持續 &gt; 0 代表容量不足或持有時間過長。</summary>
        public int Waiting { get; init; }

        /// <summary>累計取得逾時次數。</summary>
        public long AcquireTimeouts { get; init; }

        /// <summary>累計故障淘汰次數。</summary>
        public long Faulted { get; init; }

        /// <summary>目前子池數量。啟用 per-key impersonation 後若單調成長，即為子池洩漏。</summary>
        public int SubPools { get; init; }
    }

    /// <summary>
    /// 單一 request 的跨 lease 聚合統計。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 為什麼不能放在 <see cref="RequestContext"/>：該型別刻意設計為不可變，且 <see cref="PushLease"/>
    /// 每次都會建立新實例，因此放在其中的計數會隨 lease 進出而遺失。本物件以參考方式在所有
    /// 衍生 context 之間共享，是唯一能涵蓋整個 request 的累積點。
    /// </para>
    /// <para>
    /// 所有欄位皆以 Interlocked 更新：同一 request 可能透過 Task.Run 在多執行緒上發出 CRM 操作，
    /// ExecutionContext 會把本物件的參考複製給每一條分支。
    /// </para>
    /// </remarks>
    private sealed class RequestStats
    {
        internal long CrmCount;
        internal long CrmMs;
        internal long LeaseAcquires;
        internal long LeaseReturns;
        internal long MaxDepth;
        internal long ConcurrentGateway;

        /// <summary>
        /// 每個 entity 被查詢的次數。這是判定 N+1 的關鍵：30 次呼叫散落在 30 張表是複雜流程，
        /// 集中在同一張表則是應該合併的迴圈查詢。只存 logical name 與次數，不含任何資料列。
        /// </summary>
        internal readonly ConcurrentDictionary<string, int> EntityCounts = new(StringComparer.Ordinal);

        /// <summary>以 CAS 迴圈記錄觀測到的最大深度，避免讀改寫競態低估巢狀層數。</summary>
        internal void ObserveDepth(long depth)
        {
            long observed;
            while (depth > (observed = Interlocked.Read(ref MaxDepth)))
            {
                if (Interlocked.CompareExchange(ref MaxDepth, depth, observed) == observed)
                    return;
            }
        }

        /// <summary>取出被查詢次數最多的 entity 與其次數；沒有任何查詢時回傳空字串與 0。</summary>
        internal (string Entity, int Count) TopEntity()
        {
            var topEntity = string.Empty;
            var topCount = 0;
            foreach (var pair in EntityCounts)
            {
                if (pair.Value > topCount)
                {
                    topCount = pair.Value;
                    topEntity = pair.Key;
                }
            }
            return (topEntity, topCount);
        }
    }

    private sealed class RequestContext
    {
        internal RequestContext(string traceId, string user, string leaseId = "", RequestStats stats = null)
        {
            TraceId = traceId;
            User = user;
            LeaseId = leaseId;
            Stats = stats;
        }

        internal string TraceId { get; }
        internal string User { get; }
        internal string LeaseId { get; }

        /// <summary>
        /// 整個 request 共享的聚合統計。<see cref="PushLease"/> 建立新 context 時必須原樣傳遞這個
        /// 參考，否則 lease 範圍內發生的 CRM 操作會累積到一個隨即被丟棄的物件上，request.end
        /// 的聚合值將永遠是零。
        /// </summary>
        internal RequestStats Stats { get; }
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
            var stats = _current.Stats;
            var top = stats?.TopEntity() ?? (string.Empty, 0);

            // 在 request 結束的唯一時點把聚合值寫出，讓「這個 request 的時間花在哪裡」成為單筆
            // 可讀事件，而不需要下游分析器自行重組數千筆 crm.op。
            _owner.Enqueue(new TraceEntry
            {
                Kind = EventKind.RequestEnd,
                TraceId = _current.TraceId,
                User = _current.User,
                First = Math.Max(0, (long)duration),
                Second = stats != null ? Interlocked.Read(ref stats.CrmCount) : 0,
                Third = stats != null ? Interlocked.Read(ref stats.CrmMs) : 0,
                Fourth = stats != null ? Interlocked.Read(ref stats.LeaseAcquires) : 0,
                // 未歸還租約數：request 結束時應恆為 0，非零即代表 lease 洩漏到 request 邊界之外。
                Fifth = stats != null
                    ? Interlocked.Read(ref stats.LeaseAcquires) - Interlocked.Read(ref stats.LeaseReturns)
                    : 0,
                Sixth = stats != null ? Interlocked.Read(ref stats.MaxDepth) : 0,
                Seventh = stats != null ? Interlocked.Read(ref stats.ConcurrentGateway) : 0,
                Eighth = top.Item2,
                Text = top.Item1,
                State = stats != null ? stats.EntityCounts.Count.ToString(CultureInfo.InvariantCulture) : "0"
            });
            _owner._requestContext.Value = _previous;
            s_current.Value = _previousTrace;
        }
    }

    /// <summary>
    /// 擁有一個背景工作的獨立統計範圍。背景工作結束時由此 scope 唯一負責寫出 <c>bg.end</c>，
    /// 並還原建立前的 AsyncLocal context；即使呼叫端重複 Dispose，也不會重複計數或重複寫檔。
    /// </summary>
    private sealed class BackgroundScope : IDisposable
    {
        private readonly DataverseTrace _owner;
        private readonly RequestContext _previous;
        private readonly RequestContext _current;
        private readonly string _parentTraceId;
        private readonly string _operationName;
        private readonly long _startedTimestamp;
        private int _disposed;

        internal BackgroundScope(
            DataverseTrace owner,
            RequestContext previous,
            RequestContext current,
            string parentTraceId,
            string operationName)
        {
            _owner = owner;
            _previous = previous;
            _current = current;
            _parentTraceId = parentTraceId;
            _operationName = operationName;
            _startedTimestamp = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            var duration = Stopwatch.GetElapsedTime(_startedTimestamp).TotalMilliseconds;
            var stats = _current.Stats;
            var top = stats?.TopEntity() ?? (string.Empty, 0);
            _owner.Enqueue(new TraceEntry
            {
                Kind = EventKind.BackgroundEnd,
                TraceId = _current.TraceId,
                User = _current.User,
                Reason = _parentTraceId,
                Text = _operationName,
                First = Math.Max(0, (long)duration),
                Second = stats != null ? Interlocked.Read(ref stats.CrmCount) : 0,
                Third = stats != null ? Interlocked.Read(ref stats.CrmMs) : 0,
                Fourth = stats != null ? Interlocked.Read(ref stats.LeaseAcquires) : 0,
                Fifth = stats != null
                    ? Interlocked.Read(ref stats.LeaseAcquires) - Interlocked.Read(ref stats.LeaseReturns)
                    : 0,
                Sixth = stats != null ? Interlocked.Read(ref stats.MaxDepth) : 0,
                Seventh = stats != null ? Interlocked.Read(ref stats.ConcurrentGateway) : 0,
                Eighth = top.Item2,
                ClientId = top.Item1,
                State = stats != null ? stats.EntityCounts.Count.ToString(CultureInfo.InvariantCulture) : "0"
            });
            _owner._requestContext.Value = _previous;
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
    private static long _backgroundSequence;

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
    private long _lastSnapshotTimestamp;
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
        var current = new RequestContext(traceId, user, leaseId: string.Empty, stats: new RequestStats());
        var previous = _requestContext.Value;
        var previousTrace = s_current.Value;
        _requestContext.Value = current;
        s_current.Value = this;
        Enqueue(new TraceEntry { Kind = EventKind.RequestBegin, TraceId = current.TraceId, User = current.User });
        return new RequestScope(this, previous, current, previousTrace);
    }

    /// <summary>
    /// 為由目前 request 分支出的背景工作建立獨立觀測範圍，讓背景 CRM 統計不污染父 request 的
    /// <c>request.end</c>。本方法只接受由產品程式碼提供的固定作業名稱；<paramref name="operationName"/>
    /// 不得包含使用者、租戶、認證或 CRM 資料，因為它會寫入診斷檔並在背景工作整個生命週期內保留。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 背景 scope 建立全新的 <see cref="RequestStats"/> 與子 traceId，最長只存活到該背景工作
    /// <see cref="IDisposable.Dispose"/>；Dispose 會先寫出 <c>bg.end</c>，再還原前一個 context，
    /// 因此是資料與 AsyncLocal 狀態的唯一確定性釋放路徑。
    /// </para>
    /// <para>
    /// <see cref="AsyncLocal{T}"/> 的 copy-on-write 讓背景 flow 只替換自己的 context 參考，父 request
    /// 仍保有原本的統計物件，不會因背景的 CRM 次數或耗時而改變；巢狀與平行背景 flow 也各自擁有
    /// 獨立統計。停用 Trace 或沒有可繼承的 request context 時回傳共用無操作 scope，不配置任何資料。
    /// </para>
    /// </remarks>
    /// <param name="operationName">不含使用者資料的固定背景作業名稱，例如 <c>SaveIntegrate.Upload</c>。</param>
    /// <returns>背景 scope；結束時寫出 <c>bg.end</c> 並復原先前 context。</returns>
    public IDisposable BeginBackgroundOperation(string operationName)
    {
        if (!Enabled)
            return NoopScope.Instance;
        if (!TryGetRequest(out var previous))
            return NoopScope.Instance;

        var parentTraceId = previous.TraceId ?? string.Empty;
        var operation = operationName ?? string.Empty;
        var traceId = parentTraceId + "#bg" + Interlocked.Increment(ref _backgroundSequence).ToString(CultureInfo.InvariantCulture);
        var current = new RequestContext(traceId, previous.User, leaseId: string.Empty, stats: new RequestStats());
        _requestContext.Value = current;
        Enqueue(new TraceEntry
        {
            Kind = EventKind.BackgroundBegin,
            TraceId = current.TraceId,
            User = current.User,
            Reason = parentTraceId,
            Text = operation
        });
        return new BackgroundScope(this, previous, current, parentTraceId, operation);
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
        context.Stats?.ObserveDepth(depth);
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
        // 與 PoolReturn 的計數配對，使 request.end 能直接回報「結束時仍未歸還的租約數」。
        // 這是 lease 洩漏最早、最便宜的偵測點：不必等到連線池耗盡才發現。
        if (context.Stats != null)
            Interlocked.Increment(ref context.Stats.LeaseAcquires);
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

    /// <summary>
    /// 記錄同一個 scoped Gateway 同時被多個執行緒進入。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>這是目前架構中最嚴重的未防護 Session Leakage 風險。</b><c>DataverseGateway</c> 是 Scoped
    /// （一個 request 一個實例），但其 <c>_depth</c> 與 <c>_lease</c> 是無同步保護的一般欄位；
    /// 而產品程式碼中有十餘處 <c>Task.Run</c> / <c>Task.WhenAll</c> 會讓多條執行緒共用同一個實例。
    /// </para>
    /// <para>
    /// 兩種競態後果：其一，兩條執行緒同時看到 <c>_depth == 0</c>，各自租一條 lease 而 <c>_lease</c>
    /// 被覆寫，其中一條永遠不會歸還，連線池因此永久少一格；其二，先完成的執行緒把 <c>_depth</c>
    /// 遞減為零並歸還連線，而另一條執行緒仍在使用它 —— 這正是整套架構要防止的跨 request 共用連線。
    /// </para>
    /// <para>
    /// <b>本事件只負責觀測，不修正競態。</b>單元測試涵蓋的是單執行緒巢狀呼叫，實測 trace 中
    /// depth 也恆為 1，代表平行路徑尚未被觸發過 —— 但一旦觸發，在本事件加入之前不會留下任何痕跡。
    /// 先讓它可見，修正（將狀態改為 AsyncLocal）是獨立的變更。
    /// </para>
    /// </remarks>
    /// <param name="activeCalls">觀測到的同時進行呼叫數；恆大於 1，等於 1 的情況不會產生事件。</param>
    public void GatewayConcurrent(int activeCalls)
    {
        if (!Enabled)
            return;
        if (!TryGetRequest(out var context))
            return;
        if (context.Stats != null)
            Interlocked.Increment(ref context.Stats.ConcurrentGateway);
        Enqueue(new TraceEntry
        {
            Kind = EventKind.GatewayConcurrent,
            TraceId = context.TraceId,
            User = context.User,
            First = activeCalls
        });
    }

    /// <summary>
    /// 記錄 scoped Gateway 釋放時的狀態，特別是是否仍持有未歸還的 lease。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gateway 的 <c>Dispose</c> 是 request scope 的最後一道防線：正常流程中 lease 已於最外層
    /// <c>Execute</c> 的 finally 歸還，此時 <paramref name="leaseStillHeld"/> 應為 false。
    /// 若為 true，代表有一條 lease 是靠 DI 容器回收 scope 才被救回來的，而不是靠正常的執行路徑。
    /// </para>
    /// <para>
    /// 為什麼值得單獨記錄：這種情況本身不會造成錯誤（DI 仍會釋放），因此在既有 trace 中完全隱形；
    /// 但它代表控制流有一條沒被預期的路徑，而在沒有 DI scope 保護的呼叫點（例如背景工作）
    /// 同樣的控制流就會變成真正的連線洩漏。
    /// </para>
    /// </remarks>
    /// <param name="depthAtDispose">釋放當下的 reentrant 深度；正常應為 0。</param>
    /// <param name="leaseStillHeld">釋放當下是否仍持有 lease；正常應為 false。</param>
    public void GatewayScopeEnd(int depthAtDispose, bool leaseStillHeld)
    {
        if (!Enabled)
            return;
        if (!TryGetRequest(out var context))
            return;
        Enqueue(new TraceEntry
        {
            Kind = EventKind.GatewayScopeEnd,
            TraceId = context.TraceId,
            User = context.User,
            First = depthAtDispose,
            Result = leaseStillHeld
        });
    }

    /// <summary>
    /// 記錄取得子池同步鎖時的等待時間，只在超過門檻時輸出。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 建線移出鎖之後，鎖內只剩清單操作，等待時間應在微秒等級，因此本事件平時完全不會出現。
    /// 它的價值在於回歸偵測：一旦有人再度把網路 I/O 放回鎖內，或鎖競爭因其他原因惡化，
    /// 這個事件會立刻出現，而不必等到使用者回報延遲。
    /// </para>
    /// <para>
    /// 只記錄超過門檻的樣本是刻意的取捨：無條件記錄會讓每次租借都多一筆事件，把稽核檔淹沒，
    /// 反而讓真正的訊號更難被看見。
    /// </para>
    /// </remarks>
    /// <param name="waitedMs">等待鎖的毫秒數。</param>
    /// <param name="site">取鎖位置的固定識別字串，用於分辨是哪一段造成競爭。</param>
    public void PoolLockWait(long waitedMs, string site)
    {
        if (!Enabled)
            return;
        TryGetRequest(out var context);
        Enqueue(new TraceEntry
        {
            Kind = EventKind.PoolLockWait,
            TraceId = context?.TraceId ?? string.Empty,
            Text = site ?? string.Empty,
            First = Math.Max(0, waitedMs)
        });
    }

    /// <summary>
    /// 寫出一次連線池計數快照，供下游檢定連線是否只進不出。
    /// </summary>
    /// <param name="snapshot">已在呼叫端於鎖內一致取樣的計數集合。</param>
    public void PoolSnapshot(PoolSnapshotData snapshot)
    {
        if (!Enabled || snapshot == null)
            return;
        Enqueue(new TraceEntry { Kind = EventKind.PoolSnapshot, Pool = snapshot });
    }

    /// <summary>
    /// 記錄一次 WhoAmI 類健康檢查的結果與呼叫端以單調時鐘量得的執行耗時。
    /// </summary>
    /// <param name="clientId">由 pool 配發的診斷用 client 識別字串；不是使用者或 CRM 身分。</param>
    /// <param name="result">健康檢查是否成功；既有的成功／失敗語意不變。</param>
    /// <param name="elapsedMs">
    /// 僅包住健康檢查委派呼叫的毫秒數；包含該呼叫拋出例外前已消耗的時間，但不包含
    /// pool 的租借、淘汰、建線或 trace 寫檔成本。
    /// </param>
    /// <remarks>
    /// <para>
    /// 此事件只新增 <c>ms</c> 欄位，不改變既有 <c>clientId</c> 與 <c>result</c> 的 JSONL
    /// 名稱或語意，讓既有分析器仍可讀取舊欄位。耗時由呼叫端的 <c>Stopwatch</c> 單調時鐘
    /// 提供，避免以背景 writer 寫檔時間推論遠端 CRM 操作時間。
    /// </para>
    /// <para>
    /// trace 停用時會在配置任何佇列項目前立即返回；事件刻意不保存 WhoAmI 回應、使用者、
    /// tenant、認證或其他 CRM 資料，因此診斷佇列不會延長跨 request 身分或敏感資料的生命週期。
    /// </para>
    /// </remarks>
    public void PoolHealth(string clientId, bool result, long elapsedMs)
    {
        if (!Enabled)
            return;
        Enqueue(new TraceEntry
        {
            Kind = EventKind.PoolHealth,
            ClientId = clientId,
            Result = result,
            First = Math.Max(0, elapsedMs)
        });
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
        if (context.Stats != null)
            Interlocked.Increment(ref context.Stats.LeaseReturns);
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

    /// <summary>
    /// 記錄一次確實淘汰了 client 的 cleanup。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>只在有淘汰時輸出。</b>先前版本無條件輸出，導致在開發組態（IdleTimeout 4 秒 → 每 2 秒觸發）
    /// 下實測 866 筆事件中有 865 筆是 idleBefore == idleAfter 的空轉，佔整份稽核檔的 22.5%。
    /// 診斷資料的價值在訊噪比：把空轉紀錄拿掉，真正的淘汰行為才看得見。
    /// </para>
    /// <para>
    /// 另一個附帶效果是修正下游的誤判。舊的分析規則以 <c>idleAfter &lt; minSize</c> 判定「清理過度」，
    /// 但池中沒有閒置連線時（idle = 0）該條件恆為真，實測產生 158 筆假陽性。
    /// 只在真正淘汰時輸出，並附上 <paramref name="evicted"/>，判讀才有依據。
    /// </para>
    /// </remarks>
    /// <param name="idleBefore">淘汰前的閒置數。</param>
    /// <param name="idleAfter">淘汰後的閒置數。</param>
    /// <param name="minSize">設定的保底數量。</param>
    /// <param name="evicted">本次實際淘汰的 client 數；為 0 時不會產生事件。</param>
    public void PoolCleanup(long idleBefore, long idleAfter, long minSize, long evicted)
    {
        if (!Enabled || evicted <= 0)
            return;
        Enqueue(new TraceEntry
        {
            Kind = EventKind.PoolCleanup,
            First = idleBefore,
            Second = idleAfter,
            Third = minSize,
            Fourth = evicted
        });
    }

    /// <summary>
    /// 記錄由 Gateway 代理執行的 CRM 操作名稱與目前 lease。沒有 request 或 lease 關聯時不輸出，
    /// 避免捏造 HttpContext traceId；正常代理路徑的 leaseId 必定非空，可供稽核繞過 Gateway 的情況。
    /// </summary>
    public void CrmOperation(string operation)
        => CrmOperation(operation, entity: string.Empty, elapsedMs: 0, ok: true, count: -1);

    /// <summary>
    /// 記錄一次 CRM 操作，含 entity、耗時與回傳筆數。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>可記錄什麼</b>：只有 schema 層級的識別與量測值 —— entity logical name、SDK 訊息名稱、
    /// 毫秒數、筆數、成敗。這些與 op 名稱同級，描述的是「呼叫了什麼形狀的操作」。
    /// </para>
    /// <para>
    /// <b>絕不可記錄什麼</b>：資料列 GUID、欄位值、ColumnSet 內容、QueryExpression 條件、FetchXML
    /// 本文與任何 CRM 回應內容。FetchExpression 一律只以固定字串標示，不得解析其 XML。
    /// </para>
    /// <para>
    /// 為什麼需要 entity 與 ms：沒有它們就無法分辨「一個慢查詢」與「同一張表被查了 20 次」，
    /// 而這兩者的處置方式完全不同。這是唯一能指出該合併哪些查詢的資訊。
    /// </para>
    /// </remarks>
    /// <param name="operation">操作種類，例如 RetrieveMultiple；Execute 則為 SDK 訊息名稱。</param>
    /// <param name="entity">entity logical name；無法判定時為空字串。不含任何資料列識別。</param>
    /// <param name="elapsedMs">該次 CRM 往返耗時毫秒。</param>
    /// <param name="ok">操作是否成功完成。</param>
    /// <param name="count">查詢回傳筆數；不適用的操作為 -1。只記數量，不記內容。</param>
    public void CrmOperation(string operation, string entity, long elapsedMs, bool ok, int count)
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
            Text = operation,
            State = entity ?? string.Empty,
            First = Math.Max(0, elapsedMs),
            Second = count,
            Result = ok
        });

        // 同步累積到 request 層級聚合。放在 Enqueue 之後是刻意的：即使佇列已滿而丟棄了個別
        // crm.op，request.end 的總數與總毫秒仍然正確，聚合值不會因取樣遺失而失真。
        var stats = context.Stats;
        if (stats == null)
            return;
        Interlocked.Increment(ref stats.CrmCount);
        Interlocked.Add(ref stats.CrmMs, Math.Max(0, elapsedMs));
        if (!string.IsNullOrEmpty(entity))
            stats.EntityCounts.AddOrUpdate(entity, 1, static (_, existing) => existing + 1);
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
        _requestContext.Value = new RequestContext(previous.TraceId, previous.User, leaseId ?? string.Empty, previous.Stats);
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

    /// <summary>
    /// 若距離上次取樣已超過設定間隔，寫出一筆程序資源快照。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 所有量測皆為唯讀且不干擾程序：<see cref="GC.GetTotalMemory(bool)"/> 傳入 false 以免強制回收
    /// 而扭曲被觀測的行為，<see cref="GC.CollectionCount(int)"/> 與 <see cref="GC.GetGCMemoryInfo()"/>
    /// 只讀取既有計數。
    /// </para>
    /// <para>
    /// 取樣本身絕不能讓應用程式失敗：在受限環境中 <see cref="Process"/> 的部分屬性可能擲出例外，
    /// 因此整段以 try/catch 包住，失敗時靜默略過本次取樣。診斷功能不得成為新的故障來源。
    /// </para>
    /// </remarks>
    private void EmitProcessSnapshotIfDue()
    {
        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastSnapshotTimestamp);
        if (last != 0 && Stopwatch.GetElapsedTime(last, now) < _options.SnapshotInterval)
            return;
        if (Interlocked.CompareExchange(ref _lastSnapshotTimestamp, now, last) != last)
            return;

        try
        {
            const long BytesPerMb = 1024L * 1024L;
            var gcInfo = GC.GetGCMemoryInfo();
            using var process = Process.GetCurrentProcess();

            Enqueue(new TraceEntry
            {
                Kind = EventKind.ProcessSnapshot,
                Process = new ProcessSnapshotData
                {
                    ManagedMb = GC.GetTotalMemory(forceFullCollection: false) / BytesPerMb,
                    HeapMb = gcInfo.HeapSizeBytes / BytesPerMb,
                    PrivateMb = process.PrivateMemorySize64 / BytesPerMb,
                    Gen0 = GC.CollectionCount(0),
                    Gen1 = GC.CollectionCount(1),
                    Gen2 = GC.CollectionCount(2),
                    Handles = process.HandleCount,
                    Threads = process.Threads.Count,
                    PoolThreads = ThreadPool.ThreadCount,
                    PendingWorkItems = ThreadPool.PendingWorkItemCount
                }
            });
        }
        catch
        {
            // 受限執行環境可能拒絕存取程序計量值。略過本次取樣即可，
            // 絕不可讓資源觀測本身中斷 writer 迴圈而導致所有 trace 停止輸出。
        }
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
                    // 快照在背景 writer 上取樣，不佔用任何 request 執行緒。這點很重要：
                    // 讀取程序記憶體與控制代碼數是系統呼叫，放在熱路徑會讓觀測行為本身
                    // 影響被觀測的效能數字。
                    EmitProcessSnapshotIfDue();
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
            EventKind.BackgroundBegin => "bg.begin",
            EventKind.BackgroundEnd => "bg.end",
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
            EventKind.PoolSnapshot => "pool.snapshot",
            EventKind.ProcessSnapshot => "proc.snapshot",
            EventKind.PoolLockWait => "pool.lock.wait",
            EventKind.GatewayConcurrent => "gateway.concurrent",
            EventKind.GatewayScopeEnd => "gateway.scope.end",
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
                // 以下聚合值讓「這個 request 的時間花在哪裡」成為單筆可讀事件：
                // durationMs - crmMs 即為應用程式自身耗時，不需下游重組數千筆 crm.op。
                json.WriteNumber("crmCount", entry.Second);
                json.WriteNumber("crmMs", entry.Third);
                json.WriteNumber("leaseCount", entry.Fourth);
                // 非零即代表 request 結束時仍有租約未歸還 —— lease 洩漏的直接證據。
                json.WriteNumber("leaseOutstanding", entry.Fifth);
                json.WriteNumber("maxDepth", entry.Sixth);
                // 非零即代表同一個 scoped Gateway 曾被多執行緒同時進入。
                json.WriteNumber("concurrentGateway", entry.Seventh);
                json.WriteString("topEntity", entry.Text);
                json.WriteNumber("topEntityCount", entry.Eighth);
                json.WriteString("distinctEntities", entry.State);
                break;
            case EventKind.BackgroundBegin:
                WriteTraceAndUser(json, entry);
                json.WriteString("parentTraceId", entry.Reason ?? string.Empty);
                json.WriteString("op", entry.Text ?? string.Empty);
                break;
            case EventKind.BackgroundEnd:
                WriteTraceAndUser(json, entry);
                json.WriteString("parentTraceId", entry.Reason ?? string.Empty);
                json.WriteString("op", entry.Text ?? string.Empty);
                json.WriteNumber("durationMs", entry.First);
                json.WriteNumber("crmCount", entry.Second);
                json.WriteNumber("crmMs", entry.Third);
                json.WriteNumber("leaseCount", entry.Fourth);
                json.WriteNumber("leaseOutstanding", entry.Fifth);
                json.WriteNumber("maxDepth", entry.Sixth);
                json.WriteNumber("concurrentGateway", entry.Seventh);
                json.WriteString("topEntity", entry.ClientId ?? string.Empty);
                json.WriteNumber("topEntityCount", entry.Eighth);
                json.WriteString("distinctEntities", entry.State ?? "0");
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
                json.WriteNumber("ms", entry.First);
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
                json.WriteNumber("evicted", entry.Fourth);
                break;
            case EventKind.PoolLockWait:
                json.WriteString("traceId", entry.TraceId);
                json.WriteString("site", entry.Text);
                json.WriteNumber("waitedMs", entry.First);
                break;
            case EventKind.GatewayConcurrent:
                WriteTraceAndUser(json, entry);
                json.WriteNumber("activeCalls", entry.First);
                break;
            case EventKind.GatewayScopeEnd:
                WriteTraceAndUser(json, entry);
                json.WriteNumber("depthAtDispose", entry.First);
                json.WriteBoolean("leaseStillHeld", entry.Result);
                break;
            case EventKind.PoolSnapshot:
                json.WriteString("poolKey", entry.Pool.PoolKey ?? string.Empty);
                json.WriteNumber("idle", entry.Pool.Idle);
                json.WriteNumber("leased", entry.Pool.Leased);
                json.WriteNumber("alive", entry.Pool.Alive);
                json.WriteNumber("pending", entry.Pool.Pending);
                json.WriteNumber("created", entry.Pool.Created);
                json.WriteNumber("discarded", entry.Pool.Discarded);
                json.WriteNumber("totalAcquires", entry.Pool.TotalAcquires);
                json.WriteNumber("totalReleases", entry.Pool.TotalReleases);
                json.WriteNumber("waiting", entry.Pool.Waiting);
                json.WriteNumber("acquireTimeouts", entry.Pool.AcquireTimeouts);
                json.WriteNumber("faulted", entry.Pool.Faulted);
                json.WriteNumber("subPools", entry.Pool.SubPools);
                break;
            case EventKind.ProcessSnapshot:
                json.WriteNumber("managedMb", entry.Process.ManagedMb);
                json.WriteNumber("heapMb", entry.Process.HeapMb);
                json.WriteNumber("privateMb", entry.Process.PrivateMb);
                json.WriteNumber("gen0", entry.Process.Gen0);
                json.WriteNumber("gen1", entry.Process.Gen1);
                json.WriteNumber("gen2", entry.Process.Gen2);
                json.WriteNumber("handles", entry.Process.Handles);
                json.WriteNumber("threads", entry.Process.Threads);
                json.WriteNumber("poolThreads", entry.Process.PoolThreads);
                json.WriteNumber("pendingWorkItems", entry.Process.PendingWorkItems);
                break;
            case EventKind.CrmOperation:
                json.WriteString("traceId", entry.TraceId);
                json.WriteString("op", entry.Text);
                json.WriteString("leaseId", entry.LeaseId);
                json.WriteString("entity", entry.State);
                json.WriteNumber("ms", entry.First);
                json.WriteNumber("count", entry.Second);
                json.WriteBoolean("ok", entry.Result);
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
