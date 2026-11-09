/*
檔案整體說明
目的與定位：為 Webhook queue 測試提供唯一資源 owner 與可推進的合成時鐘，避免 sleep、外部連線或共享使用者狀態。
主要功能與流程：每案例建立真 DI async scope、真 diagnostics 與產品 queue；handler 只注入行為與計數，registration 只模擬借用提交。手動 timer 以同步通知等待建立，測試才推進期限並觀察清理。
使用邊界與維護：fixture 先取消排空 queue，再 Dispose 容器與 diagnostics。原始診斷僅寫本 task evidence，時鐘移除每個已 Dispose timer，不以靜態集合保存事件或服務。替身不代表 SQL 原子性或 LINE 實機成功，所有資料及 callback 僅留本案例。
*/
using System.Text.Json;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.Extensions.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>單案例 scope、queue 與診斷的唯一 owner；停止故障也須繼續釋放其餘外借依賴。</summary>
internal sealed class WebhookQueueFixture : IAsyncDisposable
{
    /// <summary>測試故障的有限 watchdog，不作正式期限判斷。</summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private readonly ServiceProvider provider;
    private readonly ExceptionDiagnostics diagnostics;
    private int liveHandlers;
    /// <summary>產品 queue，測試直接驗證其公開交接與 Host 生命週期。</summary>
    internal BoundedWebhookEventQueue Queue { get; }
    /// <summary>本案例獨立時鐘；不依賴真實三十秒或修改主機時區。</summary>
    internal WebhookQueueClock Clock { get; } = new();
    /// <summary>安全日誌只讀本案例證據，不接觸其他工作樹或秘密。</summary>
    internal string LogPath { get; }
    /// <summary>讀本案例去識別日誌，沒有紀錄時為空；不能從 exception.Message 產生斷言值。</summary>
    internal string ExceptionLog => File.Exists(Path.Combine(LogPath, "Exception.log"))
        ? File.ReadAllText(Path.Combine(LogPath, "Exception.log")) : string.Empty;

    /// <summary>注入 scoped handler 行為與容量；容器只擁有 handler，queue 及 diagnostics 由 fixture 單獨關閉。</summary>
    internal WebhookQueueFixture(Func<WebhookEnvelope, CancellationToken, ValueTask> handle, int capacity = 1000,
        int partitions = 4, bool includeHandler = true, Exception? disposeFailure = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SpeechMessageProducts.sln"))) root = root.Parent;
        LogPath = Path.Combine(root?.FullName ?? throw new InvalidOperationException("Worktree not found."),
            ".trellis", "tasks", "11-09-p3-05-bounded-queue", "evidence", "unit", Guid.NewGuid().ToString("N"));
        diagnostics = new ExceptionDiagnostics(LogPath, outputOptions: new ExceptionOutputOptions(true, false));
        var services = new ServiceCollection();
        if (includeHandler) services.AddScoped<IWebhookEnvelopeHandler>(_ => new Handler(this, handle, disposeFailure));
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Queue = new BoundedWebhookEventQueue(new WebhookQueueOptions(capacity, partitions),
            provider.GetRequiredService<IServiceScopeFactory>(), diagnostics, Clock);
    }

    /// <summary>啟動正式 consumer，缺 handler 的 fail-closed 亦經相同入口測試。</summary>
    internal Task StartAsync() => Queue.StartAsync(default);

    /// <summary>以同一 Exception 再報一次，回 false 證明 queue 傳遞原物件而非丟失身分的字串或包裝。</summary>
    internal bool ReportAgain(Exception exception) => diagnostics.Report(exception, "WebhookQueueTests.VerifyIdentity");

    /// <summary>每次呼叫自己擁有 claim 並在 queue 返回後 Dispose，證明 queue 只是短期借用。</summary>
    internal async Task EnqueueAsync(string id, string user = "A", string channel = "123")
    {
        await using var claim = new WebhookQueueClaim();
        Assert.True(await Queue.TryEnqueueAsync(Envelope(id, user, channel), claim, default));
        Assert.Equal(1, claim.Commits); Assert.Equal(0, claim.Disposals);
    }

    /// <summary>只生成小型合成 JSON，離開方法前 Dispose 文件；envelope 必須保持可讀的 clone。</summary>
    internal static WebhookEnvelope Envelope(string id, string user = "A", string channel = "123")
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            type = "follow", webhookEventId = id, source = new { type = "user", userId = user }
        }));
        return new WebhookEnvelope(channel, document.RootElement);
    }

    /// <summary>所有同步 barrier 使用非同步 continuation，避免在產品鎖內執行測試或反向重入。</summary>
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>停止後不容許殘留事件、在途 claim、scope 或 consumer；另驗實際 scoped handler 已釋放。</summary>
    internal void AssertBaseline()
    {
        Assert.Equal(new WebhookQueueSnapshot(0, 0, 0, 0, 0, 0), Queue.GetSnapshot());
        Assert.Equal(0, Volatile.Read(ref liveHandlers));
    }

    /// <summary>測試失敗也用已取消停止訊號要求合作 handler 結束，再依相依順序釋放所有 owner。</summary>
    public async ValueTask DisposeAsync()
    {
        try { await Queue.StopAsync(new CancellationToken(true)); }
        finally
        {
            try { await Queue.DisposeAsync(); }
            finally { try { await provider.DisposeAsync(); } finally { await diagnostics.DisposeAsync(); } }
        }
    }

    /// <summary>真正 scoped 的計數探針，一個 scope 只能呼叫一次，不保存上次事件。</summary>
    private sealed class Handler : IWebhookEnvelopeHandler, IAsyncDisposable
    {
        private readonly WebhookQueueFixture owner;
        private readonly Func<WebhookEnvelope, CancellationToken, ValueTask> handle;
        private readonly Exception? disposeFailure;
        private int calls;
        private int disposed;
        /// <summary>建立時增加實際活躍 instance 計數，Dispose 必須精確抵銷。</summary>
        internal Handler(WebhookQueueFixture owner, Func<WebhookEnvelope, CancellationToken, ValueTask> handle, Exception? disposeFailure)
        { this.owner = owner; this.handle = handle; this.disposeFailure = disposeFailure; Interlocked.Increment(ref owner.liveHandlers); }
        /// <summary>同一 scoped instance 重用會失敗，保護跨事件及跨頻道 mutable state 隔離。</summary>
        public ValueTask HandleAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
        { Assert.Equal(1, Interlocked.Increment(ref calls)); return handle(envelope, cancellationToken); }
        /// <summary>只減一次並拒絕重複 Dispose，避免測試以冪等掩蓋雙 owner。</summary>
        public ValueTask DisposeAsync()
        {
            Assert.Equal(0, Interlocked.Exchange(ref disposed, 1)); Interlocked.Decrement(ref owner.liveHandlers);
            if (calls != 0 && disposeFailure is not null) throw disposeFailure;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>本次交接的合成 claim，只記錄提交及釋放；測試可阻塞 Commit 以精確安排取消／停止競態。</summary>
internal sealed class WebhookQueueClaim(Func<CancellationToken, ValueTask>? commit = null) : IWebhookEventRegistration
{
    /// <summary>Commit 實際呼叫次數，用來確認滿載不進入 SQL。</summary>
    internal int Commits;
    /// <summary>Dispose 實際呼叫次數，用來確認 queue 不接管 endpoint 的所有權。</summary>
    internal int Disposals;
    /// <summary>只在 callback 成功後回傳，保留注入的同一例外；不把取消假裝提交成功。</summary>
    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    { Interlocked.Increment(ref Commits); if (commit is not null) await commit(cancellationToken); }
    /// <summary>測試呼叫端唯一 Dispose，不保存事件或 credential。</summary>
    public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
}

/// <summary>測試專用有界 timer owner；每個產品 timer 必須 Dispose，否則 TimerCount 直接顯示漏失。</summary>
internal sealed class WebhookQueueClock : TimeProvider
{
    private readonly object gate = new();
    private readonly HashSet<ClockTimer> timers = [];
    private TaskCompletionSource changed = WebhookQueueFixture.Signal();
    private TimeSpan elapsed;
    /// <summary>仍由產品持有的 timer，包含已觸發但未 Dispose 者。</summary>
    internal int TimerCount { get { lock (gate) return timers.Count; } }
    /// <summary>固定 UTC 起點，不使用電腦本地時間或全域文化。</summary>
    public override DateTimeOffset GetUtcNow() { lock (gate) return DateTimeOffset.UnixEpoch + elapsed; }
    /// <summary>手動時間戳用 ticks，支援標準 WaitAsync 及 CTS 的計時需求。</summary>
    public override long GetTimestamp() { lock (gate) return elapsed.Ticks; }
    /// <summary>時間戳頻率明確以每秒 ticks 表示。</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    /// <summary>產品建立 timer 時保留 callback 至 Dispose；測試最多固定事件數，不建立全域執行緒。</summary>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (gate)
        {
            var timer = new ClockTimer(this, callback, state, dueTime, period);
            timers.Add(timer); Pulse(); return timer;
        }
    }
    /// <summary>等到產品確實安裝期限才讓測試推進時間，避免快慢機器影響判斷。</summary>
    internal async Task WaitForTimersAsync(TimeSpan duration, int count = 1)
    {
        while (true)
        {
            Task wait;
            lock (gate)
            { if (timers.Count(t => t.Duration == duration) >= count) return; wait = changed.Task; }
            await wait.WaitAsync(WebhookQueueFixture.Limit);
        }
    }
    /// <summary>只同步觸發已到期 callback；continuation 自行排程，沒有 sleep 或輪詢等待。</summary>
    internal void Advance(TimeSpan amount)
    {
        List<(TimerCallback Callback, object? State)> callbacks = [];
        lock (gate)
        {
            elapsed += amount;
            foreach (var timer in timers)
                if (timer.Due is { } due && due <= elapsed)
                {
                    callbacks.Add((timer.Callback!, timer.State));
                    timer.Due = timer.Period == Timeout.InfiniteTimeSpan ? null : elapsed + timer.Period;
                }
        }
        foreach (var (callback, state) in callbacks) callback(state);
    }
    /// <summary>鎖內只發布非同步訊號，不在 timer owner 鎖內執行測試。</summary>
    private void Pulse() { var old = changed; changed = WebhookQueueFixture.Signal(); old.TrySetResult(); }

    /// <summary>單一產品 timer 的測試 handle；清理移除集合並清空 callback／state，以便弱參照驗證。</summary>
    private sealed class ClockTimer : ITimer
    {
        private readonly WebhookQueueClock owner;
        internal TimerCallback? Callback;
        internal object? State;
        internal TimeSpan Duration;
        internal TimeSpan Period;
        internal TimeSpan? Due;
        /// <summary>只在時鐘鎖內建立，時間與週期由產品 timer 呼叫原樣提供。</summary>
        internal ClockTimer(WebhookQueueClock owner, TimerCallback callback, object? state, TimeSpan duration, TimeSpan period)
        { this.owner = owner; Callback = callback; State = state; Duration = duration; Period = period; Due = owner.elapsed + duration; }
        /// <summary>修改期限不產生第二個 handle，已釋放時拒絕變更。</summary>
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                if (Callback is null) return false;
                Duration = dueTime; Period = period; Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.elapsed + dueTime;
                owner.Pulse(); return true;
            }
        }
        /// <summary>移除唯一 handle 並清空參照；重入安全且不保留產品 CTS。</summary>
        public void Dispose()
        { lock (owner.gate) { owner.timers.Remove(this); Callback = null; State = null; Due = null; owner.Pulse(); } }
        /// <summary>本替身沒有進行中的背景 callback，非同步清理直接委派同步 owner。</summary>
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
