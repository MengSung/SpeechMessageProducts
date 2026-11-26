/*
檔案整體說明
目的與定位：F4 LocalDB 測試的 DI 組合與合成處理器，直接使用正式排程 runtime，不依賴產品網站或真實租戶查詢。
主要功能與流程：每個 host 注入自己的可控 UTC 時鐘、scope validator、scoped handler 與有界觀察佇列。處理器記錄完整純值輸入與每個 scope 的建立／釋放，測試以 callback 注入失敗、暫停及預算耗盡。正式 runtime 負責 SQL 與四 worker。
使用邊界與維護：所有探針只保留本案最多數百筆合成資料，不保存 HttpContext、憑證或 CRM client；沒有 static 身分狀態。Stop 有四十秒上限且先 await drain，然後容器非同步 Dispose；假時鐘週期測試可另啟無排程器的四 worker，先釋放這些 worker 才釋放 runtime 與資料庫。
*/
using System.Collections.Concurrent;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Localization;
using SpeechMessage.Scheduling.Hangfire;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Scheduling.Tests;

/// <summary>同一測試內的可控時間來源，以原子 ticks 支援 SQL 排程與 worker 同時讀取，不擁有 timer。</summary>
internal sealed class SchedulingClock : IClock
{
    private long ticks;

    /// <summary>只保存一個 UTC 瞬間；普通測試從系統 UTC 起跑，週期測試明確指定未來合成時間。</summary>
    internal SchedulingClock(DateTimeOffset? initial = null) => Set(initial ?? TimeProvider.System.GetUtcNow());

    /// <summary>每次讀值都回 UTC，不讀主機 local 時區或保存租戶。</summary>
    public DateTimeOffset UtcNow => new(Interlocked.Read(ref ticks), TimeSpan.Zero);

    /// <summary>測試 owner 明確推進或設定時間；不啟動延遲工作，也不影響其他測試的時鐘。</summary>
    internal void Set(DateTimeOffset value) => Interlocked.Exchange(ref ticks, value.UtcTicks);
}

/// <summary>本案純值觀察與故障注入 owner；所有佇列受測試固定工作數限制，生命週期不超出案例。</summary>
internal sealed class SchedulingProbe
{
    /// <summary>每次 handler 的已授權範圍與 payload 候選；只含合成測試資料。</summary>
    internal ConcurrentQueue<(JobScope Scope, string Payload)> Calls { get; } = new();
    /// <summary>每次重新授權的完整範圍，用以證明停用或 scope 改變會在執行時查核。</summary>
    internal ConcurrentQueue<JobScope> Validations { get; } = new();
    /// <summary>在啟動 worker 前設定的故障或阻塞動作；callback 必須 await 自己建立的所有工作。</summary>
    internal Func<JobScope, string, IChannelRateBudget, CancellationToken, Task>? Work { get; init; }
    /// <summary>模擬權威租戶狀態，於執行瞬間讀取；不依賴排程時快照。</summary>
    internal Func<JobScope, bool> Allowed { get; set; } = _ => true;
    /// <summary>僅故障測試指定，在釋放計數歸還後模擬 handler Dispose 拋錯；本替身沒有待釋放的外部資源。</summary>
    internal Exception? DisposeFailure { get; init; }
    /// <summary>scoped handler 已建立數，最後必須等於已釋放數。</summary>
    internal int Created;
    /// <summary>handler Dispose 的次數，每個 instance 僅計一次。</summary>
    internal int Disposed;
    /// <summary>正在執行的 handler 數，Stop 與 Dispose 後必須回零。</summary>
    internal int Active;
}

/// <summary>產品端執行前 guard 替身，只保存本案探針；正式引擎仍必須每次從 scope 呼叫它。</summary>
internal sealed class SchedulingValidator(SchedulingProbe probe) : IScheduledJobScopeValidator
{
    /// <summary>注入停用或查詢故障並記錄輸入；正常取消先拋出，不能冒充已通過授權。</summary>
    public Task<bool> CanExecuteAsync(JobScope scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        probe.Validations.Enqueue(scope);
        return Task.FromResult(probe.Allowed(scope));
    }
}

/// <summary>scoped 合成處理器；以建立／釋放計數與 active finally 證明故障及停止不保留工作 scope。</summary>
internal sealed class SchedulingHandler : IScheduledJobHandler, IDisposable
{
    private readonly SchedulingProbe probe;
    private readonly IChannelRateBudget budget;
    private int disposed;

    /// <summary>借用本次 scoped 預算，不把它保存到其他 scope 或 static；每個 handler instance 都須被容器釋放。</summary>
    public SchedulingHandler(SchedulingProbe probe, IChannelRateBudget budget)
    {
        this.probe = probe;
        this.budget = budget;
        Interlocked.Increment(ref probe.Created);
    }

    /// <summary>與所有合成 JobKey.Name 一致；不做反射或動態型別解析。</summary>
    public string Name => "capture";

    /// <summary>先記錄本次已重驗輸入，再執行可控故障；active 無論成功、取消或例外皆在 finally 歸還。</summary>
    public async Task ExecuteAsync(JobScope scope, string payloadJson, CancellationToken ct)
    {
        Interlocked.Increment(ref probe.Active);
        try
        {
            probe.Calls.Enqueue((scope, payloadJson));
            if (probe.Work is not null) await probe.Work(scope, payloadJson, budget, ct);
        }
        finally { Interlocked.Decrement(ref probe.Active); }
    }

    /// <summary>只更新本案資源證據，不釋放借入預算；重複 Dispose 不重複計數。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Interlocked.Increment(ref probe.Disposed);
        if (probe.DisposeFailure is not null) throw probe.DisposeFailure;
    }
}

/// <summary>每案 Host owner；保證先停 worker、再停 runtime、最後釋放容器，資料庫由外層 await using 最後清理。</summary>
internal sealed class SchedulingTestHost : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private BackgroundJobServer? manualWorkers;
    private bool stopped;

    /// <summary>已完成建構的容器與 runtime 由本 owner 接管，不向外暴露可變服務集合。</summary>
    private SchedulingTestHost(ServiceProvider provider, HangfireSchedulingRuntime runtime, string product, Guid organization, SchedulingClock clock, string diagnosticsDirectory)
    {
        this.provider = provider;
        Runtime = runtime;
        Product = product;
        Organization = organization;
        Clock = clock;
        DiagnosticsDirectory = diagnosticsDirectory;
    }

    /// <summary>正式資源 owner，提供本 host SQL storage 與 filter 的可觀察 seam。</summary>
    internal HangfireSchedulingRuntime Runtime { get; }
    /// <summary>已驗證的排程 API，測試從容器取得而不自行建立引擎。</summary>
    internal IJobScheduler Scheduler => provider.GetRequiredService<IJobScheduler>();
    /// <summary>固定部署產品，跨產品測試另建獨立 host。</summary>
    internal string Product { get; }
    /// <summary>本案合成組織 ID，不讀組態或真實環境。</summary>
    internal Guid Organization { get; }
    /// <summary>本案可控時鐘，由 host 借用但不建立背景更新器。</summary>
    internal SchedulingClock Clock { get; }
    /// <summary>本 host 唯一的合成診斷目錄；一般雙關不建立檔案，故障告警測試可明確只啟本地落檔。</summary>
    internal string DiagnosticsDirectory { get; }

    /// <summary>直接組合正式服務；註冊或啟動失敗仍 Dispose 已建容器，讓 RED 保留原本未實作原因。</summary>
    internal static async Task<SchedulingTestHost> CreateAsync(SchedulingDatabase database, SchedulingProbe probe,
        bool startServer = true, SchedulingClock? clock = null, string product = "GoodShepherd2", Guid? organization = null,
        bool writeExceptionLog = false)
    {
        var source = clock ?? new SchedulingClock();
        var organizationId = organization ?? Guid.NewGuid();
        var diagnosticsDirectory = Path.Combine(SchedulingDatabase.Evidence, "diagnostics-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ => new ExceptionDiagnostics(diagnosticsDirectory, outputOptions: new ExceptionOutputOptions(writeExceptionLog, false)));
        services.AddSingleton<IChurchClock>(new ChurchClock(source));
        services.AddScoped<IScheduledJobScopeValidator>(_ => new SchedulingValidator(probe));
        services.AddScoped<IScheduledJobHandler>(sp => new SchedulingHandler(probe, sp.GetRequiredService<IChannelRateBudget>()));
        services.AddProductScheduling(new SchedulingOptions(product, organizationId, database.ConnectionString) { StartServer = startServer });
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        try
        {
            var runtime = provider.GetServices<IHostedService>().OfType<HangfireSchedulingRuntime>().Single();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await runtime.StartAsync(deadline.Token);
            return new SchedulingTestHost(provider, runtime, product, organizationId, source, diagnosticsDirectory);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    /// <summary>為指定合成租戶建立純值範圍，不以此取代執行前 validator。</summary>
    internal JobScope Scope(Guid? tenant = null) => new(Product, Organization, tenant ?? Guid.NewGuid());

    /// <summary>以 handler 固定名稱建立伺服器端業務鍵；不同 case 的合法鍵不合併。</summary>
    internal JobKey Key(string business = "event") => new(Product, "capture", business);

    /// <summary>只為一次直接預算操作建立 scope；失敗或取消仍非同步釋放，不能把延期標記帶進其他 job。</summary>
    internal async Task<bool> AcquireAsync(string channel, string operation, CancellationToken ct = default)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IChannelRateBudget>().TryAcquireAsync(Product, channel, operation, ct);
    }

    /// <summary>週期假時鐘測試只啟實際四 worker，不啟官方的實時排程器；occurrence 由測試明確驅動官方 scheduler。</summary>
    internal void StartManualWorkers()
    {
        Assert.Null(manualWorkers);
        manualWorkers = new BackgroundJobServer(new BackgroundJobServerOptions
        {
            WorkerCount = 4, ShutdownTimeout = TimeSpan.FromSeconds(30), IsLightweightServer = true,
            FilterProvider = Runtime.FilterProvider, Activator = new ScopedTestActivator(provider.GetRequiredService<IServiceScopeFactory>()),
            CancellationCheckInterval = TimeSpan.FromMilliseconds(50)
        }, Runtime.Storage);
    }

    /// <summary>案例可先停止再檢查持久狀態；正常停止須等待 handler 完成或明確失敗，不能留下 Processing。</summary>
    internal async Task StopAsync()
    {
        if (stopped) return;
        if (manualWorkers is not null)
        {
            manualWorkers.SendStop();
            await manualWorkers.WaitForShutdownAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(40));
            manualWorkers.Dispose();
            manualWorkers = null;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await Runtime.StopAsync(deadline.Token).WaitAsync(TimeSpan.FromSeconds(40));
        stopped = true;
    }

    /// <summary>即使停止失敗仍釋放容器；不搶先刪庫掩蓋 worker、scope 或 SQL session 殘留。</summary>
    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); }
        finally { await provider.DisposeAsync(); }
    }

    /// <summary>手動 worker 專用 DI activator，所有執行各取新 scope；不修改 Hangfire 程序全域設定。</summary>
    private sealed class ScopedTestActivator(IServiceScopeFactory factory) : JobActivator
    {
        /// <summary>每次只為一個 job 建立 scope，Hangfire 在 perform 結束後確定 Dispose。</summary>
        public override JobActivatorScope BeginScope(JobActivatorContext context) => new Scope(factory.CreateScope());

        /// <summary>單次工作 scope 的唯一 owner，Resolve 結果不得跨工作保留。</summary>
        private sealed class Scope(IServiceScope scope) : JobActivatorScope
        {
            /// <summary>只從這次 scope 解析正式 dispatcher 及 scoped 依賴。</summary>
            public override object Resolve(Type type) => ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, type);
            /// <summary>由 Hangfire finally 呼叫，先於 test host 與 SQL storage 釋放。</summary>
            public override void DisposeScope() => scope.Dispose();
        }
    }
}
