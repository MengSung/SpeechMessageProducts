/*
檔案整體說明
目的與定位：提供 LegacyGolden PluginHarness 單一情境 AppDomain 的背景工作擁有者，
由 ScenarioDomainWorker 在 capture 與假服務仍存活時建立、排空，再讀取完整輸出快照。
主要功能與流程：rewriter 將 Task 回傳值交給 Track、Thread 建構交給 CreateThread，
並將兩種 Timer 型別改為本檔替身。最多登錄 256 個不同工作，回呼亦限制為 256 次；
執行緒及計時器明確捕捉建立時的 ExecutionContext，回呼例外只保存安全型別。
Drain 使用同一個二十秒單調時鐘期限，等待工作、已啟動執行緒與啟用計時器的首輪回呼，
再停用計時器、等待原生回呼佇列退場；失敗一律拋出 InvalidDataException，不能發布黃金。
使用邊界與維護：AsyncLocal 只指向本案例 owner；失敗診斷不複製例外訊息或憑證。
登錄的 Task 本身可能持有結果及例外，只保留到本案例 Dispose，沒有跨案例快取。
計時器、通知 handle 與 ExecutionContext 由本 owner 確定釋放；未啟動 Thread 不可 Join，
成功封存後不得新增工作。無法合作結束的 Task／Thread 由外層三十秒期限及 AppDomain
卸載作最後清理，不能假稱本檔能中止任意程式，也不是敵意原生程式的作業系統沙箱。
驗證須涵蓋巢狀工作、未啟動執行緒、停用／首輪計時器、例外、容量與逾時；本檔以
net48 介面為準，ElapsedEventArgs 僅反射固定框架建構子，不執行來源提供的反射命令。
直接 async void 回呼拒絕登錄；rewriter 必須一併拒絕可到達的 async void 方法，避免其
await 後的例外繞過同步回呼邊界。Task 型非同步仍由登錄與排空正常支援。
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace LegacyGolden.PluginHarness;

/// <summary>
/// 擁有單一案例的背景工作與計時器；呼叫端必須先 Drain，再序列化，最後 Dispose，
/// 並讓 capture／假服務的生命週期包住整段排空與清理。
/// </summary>
public sealed class BackgroundWork : IDisposable
{
    private const int MaximumWork = 256;
    private const int DrainMilliseconds = 20000;
    private static readonly AsyncLocal<BackgroundWork?> Slot = new();
    private readonly List<Task> tasks = new();
    private readonly HashSet<Task> observedTasks = new();
    private readonly List<Thread> threads = new();
    private readonly List<ScenarioTimer> timers = new();
    private readonly BackgroundWork? previous;
    private Stopwatch? deadline;
    private string? failure;
    private int callbackCount;
    private int activeCallbacks;
    private bool sealedOwner;
    private bool disposed;

    /// <summary>共用短鎖只保護案例狀態；不得持鎖等待工作或執行舊來源回呼。</summary>
    internal object Gate { get; } = new();

    /// <summary>建立本執行流程唯一案例 owner；巢狀 owner 會使所有權不明，直接拒絕。</summary>
    internal BackgroundWork()
    {
        previous = Slot.Value;
        if (previous != null) throw new InvalidDataException("Background owner already active.");
        Slot.Value = this;
    }

    /// <summary>取得目前案例，沒有 owner 時禁止偷偷建立全域工作或跨案例後備擁有者。</summary>
    internal static BackgroundWork Current =>
        Slot.Value ?? throw new InvalidDataException("Background owner required.");

    /// <summary>
    /// 登錄已建立的工作並原樣返回；不改變排程、結果或例外。相同實例重複登錄只計一次，
    /// 其 fault／取消由 Drain 決定性觀察，不能讓未等待的失敗冒充成功。
    /// </summary>
    public static Task Track(Task task)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));
        var owner = Current;
        lock (owner.Gate)
        {
            owner.EnsureOpen();
            if (!owner.tasks.Contains(task))
            {
                owner.Reserve();
                owner.tasks.Add(task);
            }
        }
        return task;
    }

    /// <summary>保留泛型工作與原本結果型別；登錄不建立 continuation 或第二個工作。</summary>
    public static Task<T> Track<T>(Task<T> task)
    {
        Track((Task)task);
        return task;
    }

    /// <summary>建立尚未 Start 的原生 Thread；回呼明確流動建立時上下文並攔截例外。</summary>
    public static Thread CreateThread(ThreadStart start) => CreateThread(start, 0);

    /// <summary>保留原生最大堆疊大小語意；背景旗標避免異常工作阻止主機結束。</summary>
    public static Thread CreateThread(ThreadStart start, int maxStackSize)
    {
        if (start == null) throw new ArgumentNullException(nameof(start));
        var owner = Current;
        lock (owner.Gate)
        {
            owner.ValidateCallback(start);
            owner.Reserve();
            var context = owner.CaptureContext();
            try
            {
                var thread = new Thread(() =>
                {
                    try { owner.Invoke(context, () => start()); }
                    finally { context.Dispose(); }
                }, maxStackSize) { IsBackground = true };
                owner.threads.Add(thread);
                owner.threadContexts.Add(thread, context);
                return thread;
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }
    }

    /// <summary>建立可接受 Start(object) 的原生 Thread；輸入只屬於本案例。</summary>
    public static Thread CreateThread(ParameterizedThreadStart start) => CreateThread(start, 0);

    /// <summary>保留參數式 Thread 與堆疊大小；例外只記型別，不從背景執行緒逸出。</summary>
    public static Thread CreateThread(ParameterizedThreadStart start, int maxStackSize)
    {
        if (start == null) throw new ArgumentNullException(nameof(start));
        var owner = Current;
        lock (owner.Gate)
        {
            owner.ValidateCallback(start);
            owner.Reserve();
            var context = owner.CaptureContext();
            try
            {
                var thread = new Thread(state =>
                {
                    try { owner.Invoke(context, () => start(state)); }
                    finally { context.Dispose(); }
                }, maxStackSize) { IsBackground = true };
                owner.threads.Add(thread);
                owner.threadContexts.Add(thread, context);
                return thread;
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }
    }

    // 未 Start 的 Thread 不會進入委派 finally；owner 必須另保留其 context 並負責釋放。
    private readonly Dictionary<Thread, ExecutionContext> threadContexts = new();

    /// <summary>容量以不同 Task、Thread、Timer 的總數計算；已完成者仍占案例配額。</summary>
    internal void Reserve()
    {
        EnsureOpen();
        if (tasks.Count + threads.Count + timers.Count >= MaximumWork)
        {
            Fail("CapacityExceeded");
            throw new InvalidDataException("Background work capacity exceeded.");
        }
    }

    /// <summary>註冊停用中的 timer；必須持 Gate，啟用前完成所有權移交。</summary>
    internal void Register(ScenarioTimer timer)
    {
        Reserve();
        timers.Add(timer);
    }

    /// <summary>拒絕封存後的排程；記錄失敗使舊來源 catch 也不能掩蓋基礎設施錯誤。</summary>
    internal void EnsureOpen()
    {
        if (sealedOwner || disposed)
        {
            Fail("OwnerClosed");
            throw new InvalidDataException("Background owner closed.");
        }
    }

    /// <summary>明確捕捉完整 capture 上下文；來源抑制流動時 fail closed，避免遺失歸屬。</summary>
    internal ExecutionContext CaptureContext()
    {
        var context = ExecutionContext.Capture();
        if (context != null) return context;
        Fail("ExecutionContextSuppressed");
        throw new InvalidDataException("Background execution context suppressed.");
    }

    /// <summary>
    /// async void 的 await 後例外不會經過同步委派邊界，無法由本 wrapper 捕捉，必須拒絕。
    /// rewriter 亦須拒絕可到達的 async void 方法，防止普通 callback 間接呼叫後逸出主機。
    /// Task 回傳的非同步流程仍經 Track 與 Drain 觀察，不受此限制。
    /// </summary>
    internal void ValidateCallback(Delegate callback)
    {
        foreach (var handler in callback.GetInvocationList())
        {
            if (!handler.Method.IsDefined(typeof(AsyncStateMachineAttribute), false)) continue;
            Fail("AsyncVoidCallbackUnsupported");
            throw new InvalidDataException("Async void background callback unsupported.");
        }
    }

    /// <summary>只保存第一個有界型別識別，不保留 Exception、訊息、stack 或來源參數。</summary>
    internal void Fail(string type)
    {
        lock (Gate)
        {
            if (failure == null) failure = type.Length <= 256 ? type : "ExceptionTypeTooLong";
        }
    }

    /// <summary>
    /// 每次回呼使用上下文複本，並確認其 owner 與本案例一致；不持 Gate 執行任意來源。
    /// 所有一般例外在背景邊界轉為安全失敗，CLR 域卸載／ThreadAbort 仍由 CLR 負責。
    /// </summary>
    internal void Invoke(ExecutionContext context, Action callback)
    {
        var entered = false;
        try
        {
            lock (Gate)
            {
                if (sealedOwner || disposed) return;
                if (++callbackCount > MaximumWork)
                {
                    Fail("CallbackCapacityExceeded");
                    return;
                }
                activeCallbacks++;
                entered = true;
            }
            using var copy = context.CreateCopy();
            ExecutionContext.Run(copy, _ =>
            {
                if (!ReferenceEquals(Slot.Value, this))
                    throw new InvalidDataException("Background context owner mismatch.");
                callback();
            }, null);
        }
        catch (Exception exception)
        {
            Fail(exception.GetType().FullName ?? exception.GetType().Name);
        }
        finally
        {
            if (entered)
                lock (Gate) activeCallbacks--;
        }
    }

    /// <summary>
    /// 以同一二十秒期限等待所有可追蹤工作；迴圈重新讀取清單以納入回呼新增的巢狀工作。
    /// 所有工作與計時器首輪完成後一起停用，等待 native Dispose 通知，才允許封存快照。
    /// fault、取消、超量與逾時均以 InvalidDataException 拒絕整批，不能當舊入口例外輸出。
    /// </summary>
    internal void Drain()
    {
        lock (Gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BackgroundWork));
            if (sealedOwner)
            {
                ThrowFailure();
                return;
            }
            if (deadline != null) throw new InvalidDataException("Background drain already active.");
            deadline = Stopwatch.StartNew();
        }
        try
        {
            while (true)
            {
                Task[] taskSnapshot;
                Thread[] threadSnapshot;
                ScenarioTimer[] timerSnapshot;
                lock (Gate)
                {
                    taskSnapshot = tasks.ToArray();
                    threadSnapshot = threads.ToArray();
                    timerSnapshot = timers.ToArray();
                }
                var complete = true;
                foreach (var task in taskSnapshot)
                {
                    if (!task.IsCompleted) { complete = false; continue; }
                    // Task.Exception 可能每次建立 AggregateException；每個完成工作只觀察一次。
                    if (!observedTasks.Add(task)) continue;
                    if (task.IsFaulted)
                    {
                        var errors = task.Exception!;
                        foreach (var error in errors.InnerExceptions)
                            Fail(error.GetType().FullName ?? error.GetType().Name);
                    }
                    else if (task.IsCanceled) Fail("System.Threading.Tasks.TaskCanceledException");
                }
                foreach (var thread in threadSnapshot)
                {
                    // 未啟動者沒有執行中資源；ThreadState 是旗標，背景執行緒亦有 Background 位元。
                    if ((thread.ThreadState & System.Threading.ThreadState.Unstarted) == 0
                        && !thread.Join(0)) complete = false;
                }
                lock (Gate)
                {
                    // 所有工作與首輪回呼均完成後才一起停止；避免另一個回呼還要啟用停用的 timer。
                    var canClose = complete && activeCallbacks == 0
                        && taskSnapshot.Length == tasks.Count && threadSnapshot.Length == threads.Count
                        && timerSnapshot.Length == timers.Count;
                    foreach (var timer in timerSnapshot)
                        if (!timer.ReadyToClose) canClose = false;
                    foreach (var timer in timerSnapshot)
                    {
                        if (canClose) timer.Close();
                        else complete = false;
                        if (!timer.IsDrained) complete = false;
                        else timer.ReleaseIfDrained();
                    }
                }
                lock (Gate)
                {
                    if (complete && activeCallbacks == 0 && taskSnapshot.Length == tasks.Count
                        && threadSnapshot.Length == threads.Count && timerSnapshot.Length == timers.Count)
                    {
                        sealedOwner = true;
                        break;
                    }
                }
                if (deadline.ElapsedMilliseconds >= DrainMilliseconds)
                {
                    Fail("DrainTimeout");
                    throw new InvalidDataException("Background drain timed out.");
                }
                Thread.Sleep(5);
            }
            ThrowFailure();
        }
        finally
        {
            lock (Gate)
            {
                sealedOwner = true;
                foreach (var timer in timers) timer.Close();
            }
        }
    }

    /// <summary>於所有可等待工作退場後回報安全型別；不附帶來源例外或私密訊息。</summary>
    private void ThrowFailure()
    {
        if (failure != null) throw new InvalidDataException("Background work failed:" + failure);
    }

    /// <summary>
    /// 尚未 Drain 時先嘗試排空；無論失敗與否都停止 timer 並還原 ambient owner。
    /// 排空逾時後不再另等二十秒；外層必須拒絕整批並卸載 AppDomain 才能釋放不合作工作。
    /// </summary>
    public void Dispose()
    {
        lock (Gate) { if (disposed) return; }
        try
        {
            if (deadline == null) Drain();
            else ThrowFailure();
        }
        finally
        {
            lock (Gate)
            {
                disposed = true;
                sealedOwner = true;
                foreach (var timer in timers)
                {
                    timer.Close();
                    timer.ReleaseIfDrained();
                }
                foreach (var entry in threadContexts)
                    if ((entry.Key.ThreadState & System.Threading.ThreadState.Unstarted) != 0)
                        entry.Value.Dispose();
                threadContexts.Clear();
                tasks.Clear();
                observedTasks.Clear();
                threads.Clear();
                timers.Clear();
                if (ReferenceEquals(Slot.Value, this)) Slot.Value = previous;
            }
        }
    }
}

/// <summary>
/// System.Threading.Timer 的受管替身；原生 timer、callback、state 與上下文只由案例擁有。
/// Dispose 為非阻塞停止，owner Drain 另等待原生通知，避免 callback 自行 Dispose 時死鎖。
/// </summary>
public sealed class ScenarioTimer : IDisposable
{
    private readonly BackgroundWork owner;
    private readonly System.Threading.Timer timer;
    private readonly ManualResetEvent closed;
    private ExecutionContext? context;
    private TimerCallback? callback;
    private object? state;
    private bool closing;
    private bool released;
    private bool pendingFirst;
    private long generation;

    /// <summary>建立停用計時器，保留原生 callback-only 建構時以自身作為 state 的語意。</summary>
    public ScenarioTimer(TimerCallback callback) : this(callback, null, -1, -1) { state = this; }

    /// <summary>以毫秒啟用；-1 表示停用，0 period 表示單次，不建立額外背景工作。</summary>
    public ScenarioTimer(TimerCallback callback, object? state, int dueTime, int period)
        : this(callback, state, (long)dueTime, period) { }

    /// <summary>保留無號毫秒與 uint.MaxValue 的停用語意。</summary>
    public ScenarioTimer(TimerCallback callback, object? state, uint dueTime, uint period)
        : this(callback, state, Unsigned(dueTime), Unsigned(period)) { }

    /// <summary>以 TimeSpan 毫秒設定，和 net48 Timer 相同截去次毫秒部分。</summary>
    public ScenarioTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        : this(callback, state, Milliseconds(dueTime), Milliseconds(period)) { }

    /// <summary>
    /// 唯一建立路徑：驗證、捕捉上下文、建立停用 timer、登錄，再啟用；
    /// 建構失敗即關閉原生資源，不讓立即觸發的回呼在登錄之前執行。
    /// </summary>
    public ScenarioTimer(TimerCallback callback, object? state, long dueTime, long period)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        Validate(dueTime, nameof(dueTime));
        Validate(period, nameof(period));
        owner = BackgroundWork.Current;
        this.callback = callback;
        this.state = state;
        lock (owner.Gate)
        {
            owner.ValidateCallback(callback);
            owner.Reserve();
            context = owner.CaptureContext();
            try
            {
                closed = new ManualResetEvent(false);
                // SuppressFlow 避免原生 timer 額外保存第二份 capture；唯一複本由本替身管理。
                using (ExecutionContext.SuppressFlow())
                    timer = new System.Threading.Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
                owner.Register(this);
                Change(dueTime, period);
            }
            catch
            {
                owner.Fail("TimerConstructionFailed");
                if (timer != null)
                {
                    // 登錄後才會啟用；保留 context 與通知 handle 到 owner 排空，不能提早釋放。
                    Close();
                }
                else
                {
                    context.Dispose();
                    context = null;
                    closed?.Dispose();
                }
                throw;
            }
        }
    }

    /// <summary>標準化無號停用常數；其餘數值仍須符合原生 timer 上限。</summary>
    private static long Unsigned(uint value) => value == uint.MaxValue ? -1 : value;

    /// <summary>先截去次毫秒部分再驗證，保留 net48 將負零點幾毫秒視為零的語意。</summary>
    private static long Milliseconds(TimeSpan value)
    {
        var milliseconds = (long)value.TotalMilliseconds;
        Validate(milliseconds, nameof(value));
        return milliseconds;
    }

    /// <summary>原生 timer 使用 -1 或 0 至 4294967294 毫秒，不能接受更大的排程。</summary>
    private static void Validate(long value, string name)
    {
        if (value < -1 || value > uint.MaxValue - 1) throw new ArgumentOutOfRangeException(name);
    }

    /// <summary>保留 int Change 簽章；成功後 owner 必須等待該次排程的首輪回呼。</summary>
    public bool Change(int dueTime, int period) => Change((long)dueTime, period);

    /// <summary>保留 uint Change 簽章及停用常數。</summary>
    public bool Change(uint dueTime, uint period) => Change(Unsigned(dueTime), Unsigned(period));

    /// <summary>保留 TimeSpan Change 簽章，範圍與建構相同。</summary>
    public bool Change(TimeSpan dueTime, TimeSpan period) => Change(Milliseconds(dueTime), Milliseconds(period));

    /// <summary>在 owner 短鎖內原子更新排程與首輪世代，避免回呼中的重排被舊世代完成覆蓋。</summary>
    public bool Change(long dueTime, long period)
    {
        Validate(dueTime, nameof(dueTime));
        Validate(period, nameof(period));
        lock (owner.Gate)
        {
            owner.EnsureOpen();
            if (closing) throw new ObjectDisposedException(nameof(ScenarioTimer));
            var changed = timer.Change(dueTime, period);
            generation++;
            pendingFirst = dueTime != -1;
            return changed;
        }
    }

    /// <summary>回呼之外不改變 state；上下文複本由 Invoke 釋放，原件只在原生回呼全退場後釋放。</summary>
    private void Tick(object? unused)
    {
        long currentGeneration;
        lock (owner.Gate)
        {
            if (closing) return;
            currentGeneration = generation;
        }
        try
        {
            owner.Invoke(context!, () => callback!(state));
        }
        finally
        {
            lock (owner.Gate)
                if (currentGeneration == generation) pendingFirst = false;
        }
    }

    /// <summary>停用或目前排程首輪已完成即可停止；呼叫端必須持 owner Gate。</summary>
    internal bool ReadyToClose => closing || !pendingFirst;

    /// <summary>必須收到原生通知才表示 queued 與 running 回呼均已退場。</summary>
    internal bool IsDrained => released || (closing && closed.WaitOne(0));

    /// <summary>只發起一次原生 Dispose 通知；不等待，允許從自己的 callback 呼叫。</summary>
    internal void Close()
    {
        if (closing) return;
        closing = true;
        pendingFirst = false;
        if (!timer.Dispose(closed))
            owner.Fail("TimerDisposeNotificationRejected");
    }

    /// <summary>原生回呼退場後才釋放 handle 與上下文，清除委派／state 避免保留案例資料。</summary>
    internal void ReleaseIfDrained()
    {
        if (released || !IsDrained) return;
        released = true;
        context?.Dispose();
        context = null;
        callback = null;
        state = null;
        closed.Dispose();
    }

    /// <summary>與原生 Timer.Dispose 一樣不阻塞目前回呼；owner 負責後續通知等待與 handle 清理。</summary>
    public void Dispose()
    {
        lock (owner.Gate) Close();
    }
}

/// <summary>
/// System.Timers.Timer 常用事件介面替身；透過 ScenarioTimer 共用有界所有權與建立時上下文。
/// 不提供 UI SynchronizingObject 或元件設計器介面，rewriter 應拒絕這些不支援的用法。
/// </summary>
public sealed class ScenarioEventTimer : IDisposable
{
    private readonly BackgroundWork owner;
    private readonly ScenarioTimer timer;
    private double interval = 100;
    private bool autoReset = true;
    private bool enabled;
    private bool disposed;
    private ElapsedEventHandler? elapsed;

    /// <summary>建立預設一百毫秒但尚未啟用的計時器；即使沒有訂閱也計入案例配額。</summary>
    public ScenarioEventTimer()
    {
        owner = BackgroundWork.Current;
        timer = new ScenarioTimer(_ => RaiseElapsed(), null, -1, -1);
    }

    /// <summary>建立指定毫秒間距的停用計時器；無效間距在配置資源之前拒絕。</summary>
    public ScenarioEventTimer(double interval) : this(ValidateInterval(interval), true) { }

    /// <summary>已驗證的間距才進入預設建立流程，避免失敗建構留下無法交付的事件計時器。</summary>
    private ScenarioEventTimer(double interval, bool validated) : this() { this.interval = interval; }

    /// <summary>事件訂閱只存在本案例；觸發時取委派快照，Dispose 時清除訂閱。</summary>
    public event ElapsedEventHandler Elapsed
    {
        add
        {
            lock (owner.Gate)
            {
                EnsureAlive();
                if (value != null) owner.ValidateCallback(value);
                elapsed += value;
            }
        }
        remove { lock (owner.Gate) elapsed -= value; }
    }

    /// <summary>嚴格正數毫秒，採天花板取整，啟用時變更立即重新排程。</summary>
    public double Interval
    {
        get { lock (owner.Gate) return interval; }
        set
        {
            ValidateInterval(value);
            lock (owner.Gate) { EnsureAlive(); interval = value; Reschedule(); }
        }
    }

    /// <summary>決定是否重複；單次 callback 先停用再呼叫使用者，使事件內可明確重新 Start。</summary>
    public bool AutoReset
    {
        get { lock (owner.Gate) return autoReset; }
        set
        {
            lock (owner.Gate)
            {
                EnsureAlive();
                if (autoReset == value) return;
                autoReset = value;
                Reschedule();
            }
        }
    }

    /// <summary>啟用與停止皆經 owner 短鎖；停止不等待正在執行的 callback，由 Drain 負責等待。</summary>
    public bool Enabled
    {
        get { lock (owner.Gate) return enabled; }
        set
        {
            lock (owner.Gate)
            {
                EnsureAlive();
                if (enabled == value) return;
                enabled = value;
                Reschedule();
            }
        }
    }

    /// <summary>啟用目前設定的計時器，首輪必須在 owner 的排空期限內完成。</summary>
    public void Start() => Enabled = true;

    /// <summary>停止排程；已排入原生佇列的 callback 在事件入口再次檢查 Enabled。</summary>
    public void Stop() => Enabled = false;

    /// <summary>拒絕不合法或無法由原生毫秒整數表示的間距。</summary>
    private static double ValidateInterval(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }

    /// <summary>事件 timer 關閉後不能再修改；owner 封存後亦禁止偷偷重新排程。</summary>
    private void EnsureAlive()
    {
        if (disposed) throw new ObjectDisposedException(nameof(ScenarioEventTimer));
        owner.EnsureOpen();
    }

    /// <summary>在 owner Gate 下把事件介面狀態轉成單一原生 timer 排程，不建立新 timer。</summary>
    private void Reschedule()
    {
        var milliseconds = (int)Math.Ceiling(interval);
        timer.Change(enabled ? milliseconds : -1, enabled && autoReset ? milliseconds : -1);
    }

    /// <summary>所有訂閱於捕捉的上下文執行，例外由外層 Invoke 記錄並使整批失敗。</summary>
    private void RaiseElapsed()
    {
        ElapsedEventHandler? handlers;
        lock (owner.Gate)
        {
            if (disposed || !enabled) return;
            if (!autoReset) enabled = false;
            handlers = elapsed;
        }
        if (handlers != null) handlers(this, CreateElapsedArgs());
    }

    /// <summary>
    /// net48 框架的 ElapsedEventArgs 沒有公開建構子；僅呼叫固定的兩個 FILETIME 整數建構子，
    /// 保留真正 ElapsedEventHandler 型別與 SignalTime，不依來源名稱尋找或執行任何成員。
    /// </summary>
    private static ElapsedEventArgs CreateElapsedArgs()
    {
        var constructor = typeof(ElapsedEventArgs).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)
            ?? throw new InvalidDataException("Framework elapsed arguments constructor unavailable.");
        var fileTime = DateTime.UtcNow.ToFileTimeUtc();
        return (ElapsedEventArgs)constructor.Invoke(new object[] { unchecked((int)fileTime), (int)(fileTime >> 32) });
    }

    /// <summary>移除事件訂閱並停止底層 timer；最終原生 callback 與 handle 的排空由案例 owner 完成。</summary>
    public void Dispose()
    {
        lock (owner.Gate)
        {
            if (disposed) return;
            disposed = true;
            enabled = false;
            elapsed = null;
            timer.Dispose();
        }
    }
}
