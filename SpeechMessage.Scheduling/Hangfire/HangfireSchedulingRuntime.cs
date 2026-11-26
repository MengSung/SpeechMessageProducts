/*
檔案整體說明
目的與定位：F4排程持久化與四worker的唯一Host資源owner，提供測試可查證的SQL儲存及本Host filter集合。
主要功能與流程：延遲建立產品專用SqlServerStorage，以本Host的filter及activator啟動固定四worker。所有公開操作與dispatch先取有界生命週期lease，停止後拒絕新工作，再等待server及既有操作真正結束。
使用邊界與維護：不讀寫GlobalConfiguration、JobStorage.Current或全域filter；不保存request或使用者狀態。SQL操作各自using；SqlServerStorage本身沒有Dispose介面，停止排空後只清本產品ApplicationName的確切連線池。Host唯一owner重入停止共用同一Task，不以WaitAsync遺棄傳輸或處理器。
*/
using Hangfire;
using Hangfire.Common;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Scheduling.Hangfire;

/// <summary>單一部署產品的SQL與server owner；不跨Host共享可變狀態或工作集合。</summary>
internal sealed class HangfireSchedulingRuntime : IHostedService, IAsyncDisposable
{
    private readonly object gate = new();
    /// <summary>32條專用池只讓16個外層操作同時進SQL，預留鎖內client與Hangfire內部查詢容量，避免持鎖者池飢餓。</summary>
    private readonly SemaphoreSlim capacity = new(16, 16);
    private readonly Lazy<SqlServerStorage> storage;
    private readonly IServiceScopeFactory scopes;
    private readonly JobFilterCollection filters = new();
    private BackgroundJobServer? server;
    private bool started;
    private bool stopping;
    private bool disposed;
    private int operations;
    private TaskCompletionSource? drained;
    private Task? stopTask;

    /// <summary>只保存部署快照與借入的無使用者狀態服務；SQL及worker直到明確解析／啟動才建立。</summary>
    public HangfireSchedulingRuntime(SchedulingOptions options, IServiceScopeFactory scopes,
        IChurchClock clock, ExceptionDiagnostics diagnostics)
    {
        Options = options; this.scopes = scopes; Clock = clock; Diagnostics = diagnostics;
        filters.Add(new SchedulingStateFilter(clock, diagnostics));
        storage = new Lazy<SqlServerStorage>(() => new SqlServerStorage(options.ConnectionString,
            new SqlServerStorageOptions
            {
                SchemaName = options.SchemaName, PrepareSchemaIfNecessary = true,
                SqlClientFactory = SqlClientFactory.Instance, CommandTimeout = TimeSpan.FromSeconds(5),
                CommandBatchMaxTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
                QueuePollInterval = TimeSpan.FromMilliseconds(50), SlidingInvisibilityTimeout = TimeSpan.FromSeconds(30),
                UseRecommendedIsolationLevel = true, DisableGlobalLocks = true,
                TryAutoDetectSchemaDependentOptions = false
            }));
    }

    /// <summary>本Host固定產品及組織；不是工作參數的授權後備。</summary>
    internal SchedulingOptions Options { get; }
    /// <summary>所有應用時間由借入的共用時鐘提供，不讀伺服器local。</summary>
    internal IChurchClock Clock { get; }
    /// <summary>借入既有告警owner；排程器不自行建立通知client或writer。</summary>
    internal ExceptionDiagnostics Diagnostics { get; }
    /// <summary>各操作借用同一產品schema；實際連線由每次SQL using擁有。</summary>
    internal JobStorage Storage
    {
        get { lock (gate) ObjectDisposedException.ThrowIf(disposed, this); return storage.Value; }
    }
    /// <summary>只含本Host核定的三次重試及延期filter，不列舉全域filter。</summary>
    internal IJobFilterProvider FilterProvider => filters;
    /// <summary>建立無全域設定的短命client，底層連線仍由每次呼叫確定關閉。</summary>
    internal BackgroundJobClient Client => new(Storage, filters);

    /// <summary>每次操作保留owner直到實際完成；停止發布後不接受新的SQL或handler操作。</summary>
    internal async ValueTask<IDisposable> EnterOperationAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(stopping || disposed, this);
            operations++;
        }
        var acquired = false;
        try
        {
            acquired = await capacity.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (!acquired) throw new TimeoutException("Scheduling SQL concurrency is saturated.");
            lock (gate) ObjectDisposedException.ThrowIf(stopping || disposed, this);
            return new Operation(this);
        }
        catch
        {
            EndOperation(acquired);
            throw;
        }
    }

    /// <summary>取得固定scope與新async DI scope的dispatcher，不借用任何request服務。</summary>
    internal ScheduledJobDispatcher CreateDispatcher() => new(this, scopes);

    /// <summary>完成schema初始化後啟四worker；不把呼叫者ExecutionContext流入常駐背景工作。</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(stopping || disposed, this);
            if (started) throw new InvalidOperationException("Scheduling host is already started.");
            _ = Storage;
            if (Options.StartServer)
            {
                var serverOptions = new BackgroundJobServerOptions
                {
                    WorkerCount = 4, ShutdownTimeout = TimeSpan.FromSeconds(30),
                    SchedulePollingInterval = TimeSpan.FromMilliseconds(100),
                    CancellationCheckInterval = TimeSpan.FromMilliseconds(100),
                    FilterProvider = filters, Activator = new DispatcherActivator(CreateDispatcher())
                };
                if (ExecutionContext.IsFlowSuppressed()) server = new BackgroundJobServer(serverOptions, Storage);
                else
                {
                    using var flow = ExecutionContext.SuppressFlow();
                    server = new BackgroundJobServer(serverOptions, Storage);
                }
            }
            started = true;
        }
        return Task.CompletedTask;
    }

    /// <summary>不以caller取消放棄清理；先停止server並等待所有真實操作，再清確切專用SQL池。</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            stopping = true;
            return stopTask ??= StopCoreAsync();
        }
    }

    /// <summary>容器清理與正常停止共用同一工作，不重複處置其他Host的資源。</summary>
    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    /// <summary>等待server、工作lease和SQL各階段；前一階段故障也不得略過後續清理。</summary>
    private async Task StopCoreAsync()
    {
        Exception? failure = null;
        try
        {
            if (server is not null)
            {
                server.SendStop();
                await server.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            try { server?.Dispose(); }
            catch (Exception exception) { failure ??= exception; }
            server = null;
            Task wait;
            lock (gate)
            {
                wait = operations == 0 ? Task.CompletedTask :
                    (drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
            await wait.ConfigureAwait(false);
            try
            {
                if (storage.IsValueCreated)
                {
                    using var connection = new SqlConnection(Options.ConnectionString);
                    SqlConnection.ClearPool(connection);
                }
            }
            catch (Exception exception) { failure ??= exception; }
            finally
            {
                capacity.Dispose();
                lock (gate) disposed = true;
            }
        }
        if (failure is not null)
        {
            Diagnostics.Report(failure, "Scheduling.Host.Stop");
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>單次執行的計數lease，不保存工作內容；重複Dispose不重複歸還。</summary>
    private sealed class Operation(HangfireSchedulingRuntime owner) : IDisposable
    {
        private HangfireSchedulingRuntime? current = owner;
        /// <summary>最後操作歸還時喚醒停止owner；沒有在鎖中執行continuation。</summary>
        public void Dispose()
        {
            var value = Interlocked.Exchange(ref current, null);
            if (value is null) return;
            value.EndOperation(releasePermit: true);
        }
    }

    /// <summary>成功、等待逾時、取消與停止競爭共用歸還路徑；先還permit再發布drain，清理時不留下等待者。</summary>
    private void EndOperation(bool releasePermit)
    {
        if (releasePermit) capacity.Release();
        lock (gate)
        {
            operations--;
            if (operations == 0) drained?.TrySetResult();
        }
    }

    /// <summary>只允許唯一正式dispatcher型別，不做任意反射啟用或保存scoped handler。</summary>
    private sealed class DispatcherActivator(ScheduledJobDispatcher dispatcher) : JobActivator
    {
        /// <summary>dispatcher只含Host借入依賴；其內部每次另建async scope並確定排空。</summary>
        public override object ActivateJob(Type jobType) => jobType == typeof(ScheduledJobDispatcher)
            ? dispatcher : throw new InvalidOperationException("An unregistered scheduling job type was requested.");
    }
}
