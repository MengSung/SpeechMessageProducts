/*
檔案整體說明
目的與定位：F11正式dispatcher的離線併行關聯與故障排空測試，不啟動Hangfire、SQL或任何LINE傳輸。
主要功能與流程：一百個相同JobKey不同occurrence同時停在各自handler，驗每個logger scope及async Dispose仍帶自身job編號，最高在途100且全部釋放後0、外層logger無殘留scope。另注入validator拒絕、handler故障、caller取消及scope清理故障，確認不遺留計數且原始故障身分保留。
使用邊界與維護：真AddProductScheduling只建立Lazy runtime描述子、不Start；反射呼叫產品同一RunScopedAsync核心，不能替代F4真SQL ledger測試。每案唯一DI、Meter、diagnostics與Logger provider，finally開gate並等待全部Task排空，最後才Dispose。採框架ILogger scope流動，不建立自有AsyncLocal、timer或static可變狀態；capture最多256筆純correlation與計數，不含payload、租戶資料或秘密。
*/
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpeechMessage.Hosting.Observability;
using SpeechMessage.Scheduling.Hangfire;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Scheduling.Tests.Observability;

/// <summary>一百次真正scoped執行的隔離與故障收斂，不把純counter API當正式工作接點。</summary>
public sealed class JobScopeIsolationTests
{
    /// <summary>相同工作鍵各occurrence並行時不可互串；handler與Dispose各一百筆、停機外scope為null、計數零。</summary>
    [Fact]
    public async Task ConcurrentOccurrences_LogScopesStayIsolatedAndDrain()
    {
        using var meters = new HostingMeters();
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        using var capture = new CaptureLogs();
        var state = new State(meters, capture, "concurrent", 100);
        var services = BuildServices(state, diagnostics);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var dispatcher = provider.GetRequiredService<ScheduledJobDispatcher>();
        var tasks = Enumerable.Range(1, 100).Select(i => Run(dispatcher, state.Organization, i.ToString(System.Globalization.CultureInfo.InvariantCulture), default)).ToArray();
        try
        {
            await state.AllEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(100, meters.GetSnapshot().SchedulerJobsRunning);
        }
        finally
        {
            state.Release.TrySetResult();
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(100, state.Disposals);
        Assert.Equal(0, meters.GetSnapshot().SchedulerJobsRunning);
        Assert.Equal(200, capture.Records.Count);
        for (var i = 1; i <= 100; i++)
        {
            var correlation = "job-GoodShepherd2:observed:scope-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(2, capture.Records.Count(record => record == correlation));
        }
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("after");
        logger.Log(LogLevel.Information, new EventId(1), 0, null, static (_, _) => "synthetic");
        Assert.Null(capture.Records.Last());
    }

    /// <summary>拒絕／故障／取消及清理故障都必須釋放；主要handler錯不被第二個Dispose錯取代。</summary>
    [Theory]
    [InlineData("reject")]
    [InlineData("handler")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("both")]
    public async Task FailureOrRejection_CleansScopeAndCounters(string scenario)
    {
        using var meters = new HostingMeters();
        await using var diagnostics = new ExceptionDiagnostics(Path.GetTempPath(), outputOptions: new(false, false));
        using var capture = new CaptureLogs();
        using var cancellation = new CancellationTokenSource();
        var state = new State(meters, capture, scenario, 1) { Cancellation = cancellation };
        var services = BuildServices(state, diagnostics);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var error = await Record.ExceptionAsync(() => Run(provider.GetRequiredService<ScheduledJobDispatcher>(), state.Organization, "42", cancellation.Token));
        if (scenario == "reject") { Assert.Null(error); Assert.Equal(0, state.Disposals); }
        else
        {
            Assert.NotNull(error);
            Assert.Equal(1, state.Disposals);
            if (scenario is "handler" or "both") Assert.Same(state.Failure, error);
            if (scenario == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
        }
        Assert.Equal(0, meters.GetSnapshot().SchedulerJobsRunning);
        Assert.DoesNotContain(capture.Records, record => record is not null && record != "job-GoodShepherd2:observed:scope-42");
    }

    /// <summary>只登錄同一真dispatcher使用的依賴，不啟worker或SQL；容器唯一管理scoped handler清理。</summary>
    private static ServiceCollection BuildServices(State state, ExceptionDiagnostics diagnostics)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state.Meters);
        services.AddSingleton(diagnostics);
        services.AddLogging(logging => logging.AddProvider(state.Logs));
        services.AddScoped<IScheduledJobScopeValidator>(_ => new Validator(state));
        services.AddScoped<IScheduledJobHandler>(provider => new Handler(state, provider.GetRequiredService<ILogger<Handler>>()));
        services.AddProductScheduling(new SchedulingOptions("GoodShepherd2", state.Organization,
            "Server=unopened.example;Database=synthetic;Integrated Security=true;Encrypt=true"));
        return services;
    }

    /// <summary>呼叫真五參數核心，不使用已刪除的舊簽章或在測試複製業務流程。</summary>
    private static Task Run(ScheduledJobDispatcher dispatcher, Guid organization, string occurrence, CancellationToken ct)
    {
        var method = typeof(ScheduledJobDispatcher).GetMethod("RunScopedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsAssignableFrom<Task>(method.Invoke(dispatcher, new object[]
            { new JobKey("GoodShepherd2", "observed", "scope"), new JobScope("GoodShepherd2", organization, null), "{}", occurrence, ct }));
    }

    /// <summary>當案純故障控制與有界同步gate；只活至容器與全部工作排空，不含使用者資料。</summary>
    private sealed class State(HostingMeters meters, CaptureLogs logs, string scenario, int count)
    {
        internal HostingMeters Meters { get; } = meters;
        internal CaptureLogs Logs { get; } = logs;
        internal string Scenario { get; } = scenario;
        internal Guid Organization { get; } = Guid.NewGuid();
        internal IOException Failure { get; } = new("synthetic-handler-canary");
        internal TaskCompletionSource AllEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenSource? Cancellation { get; init; }
        internal int Disposals;
        private int entered;
        /// <summary>最後一個handler到達才通知測試讀峰值；不建立或遺棄背景工作。</summary>
        internal void Enter() { if (Interlocked.Increment(ref entered) == count) AllEntered.TrySetResult(); }
    }

    /// <summary>只驗本案固定設定；拒絕發生在handler解析之前，仍須計在途直到scope釋放。</summary>
    private sealed class Validator(State state) : IScheduledJobScopeValidator
    {
        public Task<bool> CanExecuteAsync(JobScope scope, CancellationToken ct)
        { Assert.True(state.Meters.GetSnapshot().SchedulerJobsRunning > 0); return Task.FromResult(state.Scenario != "reject"); }
    }

    /// <summary>單scope合成工作，使用真正框架logger並於Dispose觀察相同scope；無外部資源。</summary>
    private sealed class Handler(State state, ILogger<Handler> logger) : IScheduledJobHandler, IAsyncDisposable
    {
        public string Name => "observed";
        public async Task ExecuteAsync(JobScope scope, string payloadJson, CancellationToken ct)
        {
            Log();
            state.Enter();
            if (state.Scenario == "concurrent") await state.Release.Task.WaitAsync(ct);
            if (state.Scenario is "handler" or "both") throw state.Failure;
            if (state.Scenario == "cancel") { state.Cancellation!.Cancel(); ct.ThrowIfCancellationRequested(); }
        }
        public ValueTask DisposeAsync()
        {
            Assert.True(state.Meters.GetSnapshot().SchedulerJobsRunning > 0);
            Log();
            Interlocked.Increment(ref state.Disposals);
            return state.Scenario is "dispose" or "both" ? ValueTask.FromException(new IOException("synthetic-cleanup")) : ValueTask.CompletedTask;
        }
        /// <summary>不格式化任何業務資料，只使真provider讀當次scope。</summary>
        private void Log() => logger.Log(LogLevel.Information, new EventId(1), 0, null, static (_, _) => "synthetic");
    }

    /// <summary>固定最多256筆測試觀察，借框架external scope provider；Dispose不持有task或任何傳輸。</summary>
    private sealed class CaptureLogs : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
        internal ConcurrentQueue<string?> Records { get; } = new();
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);
        public void Dispose() { }
        /// <summary>只讀固定CorrelationId鍵，不讀message、Exception、payload或其他scope值。</summary>
        private void Record()
        {
            if (Records.Count >= 256) throw new InvalidOperationException("Synthetic capture capacity exceeded.");
            string? correlation = null;
            scopes.ForEachScope((scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> entries)
                    foreach (var pair in entries) if (pair.Key == "CorrelationId") correlation = pair.Value as string;
            }, 0);
            Records.Enqueue(correlation);
        }
        /// <summary>不自建scope，由LoggerFactory呼叫ISupportExternalScope；只觀察固定合成log。</summary>
        private sealed class CaptureLogger(CaptureLogs owner) : ILogger
        {
            public bool IsEnabled(LogLevel logLevel) => true;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => owner.Record();
        }
    }
}
