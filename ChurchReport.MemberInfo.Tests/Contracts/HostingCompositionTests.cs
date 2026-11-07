/*
檔案整體說明

目的與定位：保護 ChurchReport 真正組合根接入 Hosting 的單一診斷 owner 與停止契約。
使用實際 Program 接點、DI 容器及純合成資源；不讀產品設定、User Secrets，不啟動
Kestrel、不連 LINE 或 D365，也不把離線結果當成必要實機驗收。

主要功能與流程：
先檢查 Main 確實呼叫共用註冊，再驗證 adapter／diagnostics 的參考身分、註冊失敗回復、
WebApplication 組態所有權移交，以及解除綁定→非同步 drain→sender→configuration 的
完整清理順序。逐階及多重 Dispose 故障必須繼續清理，沒有主要失敗時保留原故障，
已有主要失敗時不得覆蓋它；stderr 只能輸出固定階段代碼。

資源與隔離：
涉及 ExceptionReporting 與 Console.Error 的案例使用既有獨占 collection。暫存目錄、
adapter、DI container、診斷 owner、組態 provider、TaskCompletionSource 與探針只屬
各案例；finally 解除 barrier、等待工作、關閉 owner、還原 stderr 並刪除自有目錄。
探針只保留合成事件與整數，測試不建立常駐 timer、socket 或跨使用者狀態。
*/
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Hosting;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Contracts;

/// <summary>以真正 Program 接點與故障注入保護共用註冊及停止時的有界、確定性資源清理。</summary>
[Collection("ExceptionReporting")]
public sealed class HostingCompositionTests
{
    /// <summary>Main 必須經 Hosting 組合接點註冊，不可只保留獨立 helper 或重複的手動 singleton。</summary>
    [Fact]
    public void Main_uses_shared_registration_and_failure_safe_cleanup()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "SpeechMessageProducts.ChurchReport/Program.cs"));
        Assert.Contains("registration = RegisterHostingDiagnostics(builder.Services, diagnostics);", source, StringComparison.Ordinal);
        Assert.Contains("services.AddSpeechMessageHosting(diagnostics, adapter);", source, StringComparison.Ordinal);
        Assert.Contains("CleanupHostingResources(registration, diagnostics, sender, configuration, preservePrimaryFailure);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("registration = ExceptionReporting.Attach(diagnostics);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.Services.AddSingleton(diagnostics);", source, StringComparison.Ordinal);
        Assert.Contains("using var stoppingRegistration = lifetime.ApplicationStopping.Register(StopDebugTraceResources);", source, StringComparison.Ordinal);
        var primary = source.IndexOf("preservePrimaryFailure = true;", StringComparison.Ordinal);
        var fatal = source.IndexOf("diagnostics.Report(exception, \"Program.Fatal\")", StringComparison.Ordinal);
        Assert.True(primary >= 0 && fatal > primary);
        var built = source.IndexOf("using var app = builder.Build();", StringComparison.Ordinal);
        var transferred = source.IndexOf("configuration = null;", built, StringComparison.Ordinal);
        Assert.True(built >= 0 && transferred > built);
    }

    /// <summary>真正組合接點讓 DI 借用同一 adapter／owner；容器停止不得先關閉最外層診斷或解除綁定。</summary>
    [Fact]
    public async Task Registration_publishes_one_borrowed_owner_and_adapter()
    {
        var directory = TemporaryDirectory();
        try
        {
            await using var diagnostics = new ExceptionDiagnostics(directory, outputOptions: new(true, false));
            var services = new ServiceCollection();
            using var adapter = Register()(services, diagnostics);
            await using (var provider = services.BuildServiceProvider())
            {
                Assert.Same(diagnostics, provider.GetRequiredService<ExceptionDiagnostics>());
                Assert.Same(adapter, provider.GetRequiredService<ExceptionReportingAdapter>());
                Assert.Single(services.Where(item => item.ServiceType == typeof(ExceptionDiagnostics)));
                Assert.Single(services.Where(item => item.ServiceType == typeof(ExceptionReportingAdapter)));
            }
            Assert.True(ExceptionReporting.IsActive);
            Assert.True(diagnostics.Report(new InvalidOperationException("synthetic-private"), "HostingComposition.AfterContainer"));
            adapter.Dispose();
            Assert.False(ExceptionReporting.IsActive);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>服務集合衝突時解除本次候選綁定，不留下全域 owner，也不部分發布或釋放外部 diagnostics。</summary>
    [Fact]
    public async Task Registration_failure_rolls_back_candidate_without_disposing_borrowed_owner()
    {
        var directory = TemporaryDirectory();
        try
        {
            await using var diagnostics = new ExceptionDiagnostics(directory, outputOptions: new(true, false));
            var services = new ServiceCollection();
            services.AddSingleton(new ExceptionOutputOptions(false, false));
            var before = services.ToArray();
            Assert.Throws<InvalidOperationException>(() => Register()(services, diagnostics));
            Assert.Equal(before, services.ToArray());
            Assert.False(ExceptionReporting.IsActive);
            Assert.True(diagnostics.Report(new InvalidOperationException("synthetic-private"), "HostingComposition.Rollback"));
            using var next = new ExceptionReportingAdapter(diagnostics);
            Assert.True(ExceptionReporting.IsActive);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>真實空白 WebApplication 的成功 Build 會接管組態；停止後 provider 只能被釋放一次。</summary>
    [Fact]
    public async Task Built_web_application_owns_configuration_disposal()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        var source = new ConfigurationProbeSource();
        ((IConfigurationBuilder)builder.Configuration).Add(source);
        builder.Services.AddSingleton<Microsoft.AspNetCore.Hosting.Server.IServer, UnstartedServer>();
        try
        {
            await using (var app = builder.Build())
                Assert.Equal(0, source.Provider.DisposeCalls);
            Assert.Equal(1, source.Provider.DisposeCalls);
        }
        finally
        {
            if (source.Provider.DisposeCalls == 0)
                ((IDisposable)builder.Configuration).Dispose();
        }
    }

    /// <summary>Build 在 Host lifetime 建構失敗時，尚未移交的組態仍須由外層 owner 清理。</summary>
    [Fact]
    public void Failed_web_application_build_leaves_configuration_for_outer_owner()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        var source = new ConfigurationProbeSource();
        ((IConfigurationBuilder)builder.Configuration).Add(source);
        builder.Services.AddSingleton<IHostLifetime>(_ => throw new InvalidOperationException("Synthetic build failure."));
        try
        {
            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Equal(0, source.Provider.DisposeCalls);
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
        Assert.Equal(1, source.Provider.DisposeCalls);
    }

    /// <summary>非同步 diagnostics 必須真正完成後才關閉 sender；四個 owner 各釋放一次且順序固定。</summary>
    [Fact]
    public void Cleanup_awaits_drain_then_closes_sender_and_untransferred_configuration()
    {
        var events = new ConcurrentQueue<string>();
        var resources = Resources(events);
        Cleanup()(resources[0], resources[1], resources[2], resources[3], false);
        Assert.Equal(["registration", "diagnostics.begin", "diagnostics.end", "sender", "configuration"], events);
        Assert.All(resources, resource => Assert.Equal(1, resource.DisposeCalls));
    }

    /// <summary>任一清理階段故障都不能跳過後續資源；保留原例外，stderr 不得複製敏感 Message。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Cleanup_failure_still_attempts_every_stage_and_keeps_original_failure(int failingStage)
    {
        var events = new ConcurrentQueue<string>();
        var resources = Resources(events);
        var failure = new InvalidOperationException("synthetic-private-cleanup");
        resources[failingStage].Failure = failure;
        WithCapturedError(output =>
        {
            var aggregate = Assert.Throws<AggregateException>(() =>
                Cleanup()(resources[0], resources[1], resources[2], resources[3], false));
            Assert.Same(failure, Assert.Single(aggregate.InnerExceptions));
            Assert.All(resources, resource => Assert.Equal(1, resource.DisposeCalls));
            Assert.Contains("CleanupFailed", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-private", output.ToString(), StringComparison.Ordinal);
        });
    }

    /// <summary>多重清理故障依原階段順序保留於有界 aggregate，不能以最後一個故障覆蓋前面的證據。</summary>
    [Fact]
    public void Cleanup_multiple_failures_are_bounded_and_preserved_in_stage_order()
    {
        var resources = Resources(new ConcurrentQueue<string>());
        foreach (var resource in resources) resource.Failure = new InvalidOperationException("synthetic-private-" + resource.Name);
        WithCapturedError(output =>
        {
            var aggregate = Assert.Throws<AggregateException>(() =>
                Cleanup()(resources[0], resources[1], resources[2], resources[3], false));
            Assert.Equal(resources.Select(resource => resource.Failure), aggregate.InnerExceptions);
            Assert.All(resources, resource => Assert.Equal(1, resource.DisposeCalls));
            Assert.Equal(4, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.DoesNotContain("synthetic-private", output.ToString(), StringComparison.Ordinal);
        });
    }

    /// <summary>原業務／啟動例外正在傳播時，四個關閉故障只能走固定主機監控，不可覆蓋原例外身分。</summary>
    [Fact]
    public void Cleanup_never_masks_an_already_propagating_primary_failure()
    {
        var resources = Resources(new ConcurrentQueue<string>());
        foreach (var resource in resources) resource.Failure = new InvalidOperationException("synthetic-private-cleanup");
        var primary = new InvalidOperationException("synthetic-private-primary");
        WithCapturedError(output =>
        {
            var actual = Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                try { throw primary; }
                finally { Cleanup()(resources[0], resources[1], resources[2], resources[3], true); }
            }));
            Assert.Same(primary, actual);
            Assert.All(resources, resource => Assert.Equal(1, resource.DisposeCalls));
            Assert.DoesNotContain("synthetic-private", output.ToString(), StringComparison.Ordinal);
        });
    }

    /// <summary>初始化只取得部分資源時，null 不代表失敗，不得因此漏掉已建立的組態 root。</summary>
    [Fact]
    public void Cleanup_handles_partial_initialization_without_creating_resources()
    {
        var configuration = new ResourceProbe("configuration", new ConcurrentQueue<string>());
        Cleanup()(null, null, null, configuration, false);
        Assert.Equal(1, configuration.DisposeCalls);
        Cleanup()(null, null, null, null, false);
    }

    /// <summary>獨立 stderr 也故障時仍清理所有資源，aggregate 必須保留清理原例外而非監控 writer 故障。</summary>
    [Fact]
    public void Cleanup_monitor_failure_does_not_skip_resources_or_replace_cleanup_failure()
    {
        var resources = Resources(new ConcurrentQueue<string>());
        var failure = new InvalidOperationException("synthetic-private-cleanup");
        resources[0].Failure = failure;
        var previous = Console.Error;
        using var writer = new FailingErrorWriter();
        try
        {
            Console.SetError(writer);
            var aggregate = Assert.Throws<AggregateException>(() =>
                Cleanup()(resources[0], resources[1], resources[2], resources[3], false));
            Assert.Same(failure, Assert.Single(aggregate.InnerExceptions));
            Assert.All(resources, resource => Assert.Equal(1, resource.DisposeCalls));
        }
        finally { Console.SetError(previous); }
    }

    /// <summary>真實 consumer 尚有通知時先解除全域入口，drain 完成才可釋放 sender，全部工作最後回到零。</summary>
    [Fact]
    public async Task Cleanup_drains_real_pending_notification_before_disposing_sender()
    {
        var directory = TemporaryDirectory();
        await using var diagnostics = new ExceptionDiagnostics(directory, outputOptions: new(false, true));
        using var adapter = Register()(new ServiceCollection(), diagnostics);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        Task? cleanup = null;
        var sender = new ResourceProbe("sender", new ConcurrentQueue<string>())
        {
            BeforeDispose = () => Assert.Equal(0, Volatile.Read(ref active))
        };
        var configuration = new ResourceProbe("configuration", new ConcurrentQueue<string>());
        using var registration = new CallbackDisposable(() => { adapter.Dispose(); detached.TrySetResult(); });
        diagnostics.StartNotifications(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref active);
            entered.TrySetResult();
            try { await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            finally { Interlocked.Decrement(ref active); }
        });
        try
        {
            Assert.True(diagnostics.Report(new InvalidOperationException("synthetic-private"), "HostingComposition.Pending"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cleanup = Task.Run(() => Cleanup()(registration, diagnostics, sender, configuration, false));
            await detached.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(ExceptionReporting.IsActive);
            Assert.False(cleanup.IsCompleted);
            Assert.Equal(0, sender.DisposeCalls);
        }
        finally
        {
            release.TrySetResult();
            if (cleanup is not null) await cleanup.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(0, active);
        Assert.Equal(1, sender.DisposeCalls);
        Assert.Equal(1, configuration.DisposeCalls);
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>取得真正 Program 註冊接點的 delegate；缺接點為紅燈，反射不包裝產品原始例外。</summary>
    private static Func<IServiceCollection, ExceptionDiagnostics, ExceptionReportingAdapter> Register()
    {
        var method = typeof(global::ChurchReport.Program).GetMethod("RegisterHostingDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method.CreateDelegate<Func<IServiceCollection, ExceptionDiagnostics, ExceptionReportingAdapter>>();
    }

    /// <summary>取得真正 Main 使用的清理接點，不在測試內複製產品清理政策或吞掉反射例外。</summary>
    private static Action<IDisposable?, IAsyncDisposable?, IDisposable?, IDisposable?, bool> Cleanup()
    {
        var method = typeof(global::ChurchReport.Program).GetMethod("CleanupHostingResources", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method.CreateDelegate<Action<IDisposable?, IAsyncDisposable?, IDisposable?, IDisposable?, bool>>();
    }

    /// <summary>建立四個只屬本案例的合成資源，沒有真實憑證、連線或檔案。</summary>
    private static ResourceProbe[] Resources(ConcurrentQueue<string> events) =>
        new[] { "registration", "diagnostics", "sender", "configuration" }
            .Select(name => new ResourceProbe(name, events)).ToArray();

    /// <summary>建立唯一暫存目錄名稱；只有實際啟用檔案輸出的診斷案例會建立該目錄。</summary>
    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "p2-host-composition-" + Guid.NewGuid().ToString("N"));

    /// <summary>獨占擷取固定 stderr 並於 finally 還原；StringWriter 只保存合成測試輸出。</summary>
    private static void WithCapturedError(Action<StringWriter> action)
    {
        var previous = Console.Error;
        using var output = new StringWriter();
        try { Console.SetError(output); action(output); }
        finally { Console.SetError(previous); }
    }

    /// <summary>只有合成事件／故障與釋放計數；非同步釋放工作由被測接點完整等待，不啟動常駐資源。</summary>
    private sealed class ResourceProbe(string name, ConcurrentQueue<string> events) : IDisposable, IAsyncDisposable
    {
        /// <summary>合成 owner 名稱，不含 request 或部署識別。</summary>
        public string Name { get; } = name;
        /// <summary>本案例注入的清理故障，僅保留至案例結束。</summary>
        public Exception? Failure { get; set; }
        /// <summary>可選的釋放前決定性斷言，只捕捉本案例的資源計數。</summary>
        public Action? BeforeDispose { get; init; }
        /// <summary>已嘗試清理次數，用來找出漏清理或重複 owner。</summary>
        public int DisposeCalls { get; private set; }

        /// <summary>記錄同步階段後注入可選故障；不用自己的 Dispose 隱藏漏清理。</summary>
        public void Dispose()
        {
            DisposeCalls++;
            BeforeDispose?.Invoke();
            events.Enqueue(Name);
            if (Failure is not null) throw Failure;
        }

        /// <summary>排入短暫自有工作，必須被產品等待完成後才能進入下一階段。</summary>
        public ValueTask DisposeAsync() => new(Task.Run(() =>
        {
            DisposeCalls++;
            events.Enqueue(Name + ".begin");
            events.Enqueue(Name + ".end");
            if (Failure is not null) throw Failure;
        }));
    }

    /// <summary>只屬單一案例的冪等解除轉接；執行前清空 callback，不保留已解除的 adapter。</summary>
    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        /// <summary>僅保留到唯一 Dispose，沒有訂閱程序級事件或啟動背景工作。</summary>
        private Action? action = callback;
        /// <summary>先交還 callback 參照再執行解除，重複 Dispose 不重入。</summary>
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }

    /// <summary>注入固定 stderr 寫入故障；無檔案、buffer 或背景資源，案例 owner 負責還原 Console。</summary>
    private sealed class FailingErrorWriter : StringWriter
    {
        /// <summary>直接失敗，不保存或轉送傳入文字，驗證監控故障不影響資源清理。</summary>
        public override void WriteLine(string? value) => throw new IOException("Synthetic stderr failure.");
    }

    /// <summary>只回傳本案例唯一組態 provider，不讀檔、環境或網路。</summary>
    private sealed class ConfigurationProbeSource : IConfigurationSource
    {
        /// <summary>本案例唯一可觀測 provider，所有權由 builder 移交 Host。</summary>
        internal ConfigurationProbe Provider { get; } = new();
        /// <summary>借用 builder 組合自有 provider，不保存 builder。</summary>
        public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
    }

    /// <summary>以純計數觀測真實 ConfigurationManager 的 Dispose；沒有原生資源或 reload callback。</summary>
    private sealed class ConfigurationProbe : ConfigurationProvider, IDisposable
    {
        /// <summary>清理次數，超過一表示兩個 owner 重複釋放。</summary>
        internal int DisposeCalls { get; private set; }
        /// <summary>只累加計數，不自行掩蓋組合根重複 Dispose。</summary>
        public void Dispose() => DisposeCalls++;
    }

    /// <summary>只供真實 WebApplication 建構需要的 metadata；沒有 socket，任何 Start 均立即使測試失敗。</summary>
    private sealed class UnstartedServer : Microsoft.AspNetCore.Hosting.Server.IServer
    {
        /// <summary>單案例空白 feature 集合，不保存 request 或跨案例狀態。</summary>
        public Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } =
            new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        /// <summary>測試禁止啟動網路服務；若產品探針誤進 Start 必須明確失敗。</summary>
        public Task StartAsync<TContext>(Microsoft.AspNetCore.Hosting.Server.IHttpApplication<TContext> application,
            CancellationToken cancellationToken) where TContext : notnull =>
            throw new InvalidOperationException("The composition probe must not start a server.");
        /// <summary>本替身沒有工作、連線或 timer，停止不需等待外部資源。</summary>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>沒有原生或背景資源可釋放；metadata 只隨本案例物件存活，不進入任何共用快取。</summary>
        public void Dispose() { }
    }
}
