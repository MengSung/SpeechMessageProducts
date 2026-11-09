/*
檔案整體說明
目的與定位：階段三真 Kestrel 的離線測試 owner，集中建立 ephemeral 本機 Host、注入唯一三個替身及管理合成證據目錄。記憶體去重與 queue 僅存在本測試組件，絕不作正式服務。
主要功能與流程：替身以頻道加事件複合鍵保留 claim，模擬 commit／rollback 及入列失敗。HTTP 經產品 Program 與端點，檢查狀態、原始 bytes、事件快照與資源計數；每個案例最多二百筆合成事件，沒有無界壓測或實際 SQL／LINE 連線。
使用邊界與維護：測試以 await using 擁有 Host／client，Host 先停止再 Dispose 容器，最後清空替身集合。evidence 留在本工作樹 ignored task 路徑，不寫 TEMP；原始證據留存不刪除。logger 僅輸出產品共享入口的去識別事件，不把 body、signature 或憑證寫進回報。
*/
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using LineMessagingProcessor.AspNetCore.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>每個案例唯一的真 Host owner；靜態方法只尋找工作樹，不保存主機或使用者狀態。</summary>
internal sealed class WebhookTestHost : IAsyncDisposable
{
    /// <summary>測試擁有的真 Kestrel 主機；未 Start 時亦必須 Dispose，以證明啟動拒絕不漏容器。</summary>
    internal WebApplication App { get; }
    /// <summary>測試擁有的 HTTP client，沒有 cookie 或部署憑證，不提供給產品。</summary>
    internal HttpClient Client { get; private set; } = new();
    /// <summary>合成驗簽替身，只在本案例保存最後 bytes 供精確比較，Dispose 後清除。</summary>
    internal SignatureDouble Signature { get; } = new();
    /// <summary>合成 SQL lease owner，用計數證明 rollback 與 Dispose，不宣稱真交易。</summary>
    internal DeduplicatorDouble Dedupe { get; } = new();
    /// <summary>合成有界接收替身，測試只注入滿載／故障與 commit 交接，不執行背景業務。</summary>
    internal QueueDouble Queue { get; } = new();
    /// <summary>目前案例唯一原始證據目錄，不包含秘密；不供 HTTP 決定路徑。</summary>
    internal string DirectoryPath { get; }
    /// <summary>共享 Hosting 的實際安全日誌，尚無事件時回空，不讀其他案例目錄。</summary>
    internal string ExceptionLog => File.Exists(Path.Combine(DirectoryPath, "Logs", "Exception.log"))
        ? File.ReadAllText(Path.Combine(DirectoryPath, "Logs", "Exception.log")) : string.Empty;

    /// <summary>建構一次產品組合根，選擇性省略正式必要介面；建立失敗仍釋放未移交組態。</summary>
    /// <param name="omit">只供測試選擇省略的介面類型；null 表示三個替身皆註冊。</param>
    /// <param name="configuration">合成非秘密部署值，覆寫 legacy 或輸出策略。</param>
    /// <param name="configureServices">僅測試容器的 scoped 生命週期故障注入，不修改產品組合入口。</param>
    internal WebhookTestHost(Type? omit = null, Dictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SpeechMessageProducts.sln"))) root = root.Parent;
        DirectoryPath = Path.Combine(root?.FullName ?? throw new InvalidOperationException("Worktree not found."),
            ".trellis", "tasks", "11-09-p3-02-host-endpoints", "evidence", "http", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = DirectoryPath,
            ApplicationName = typeof(Program).Assembly.GetName().Name
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ExceptionNotifications:WriteExceptionLog"] = "true",
            ["ExceptionNotifications:SendLine"] = "false"
        });
        if (configuration is not null) builder.Configuration.AddInMemoryCollection(configuration);
        if (omit != typeof(IWebhookSignatureVerifier)) builder.Services.AddSingleton<IWebhookSignatureVerifier>(_ => Signature);
        if (omit != typeof(IWebhookEventDeduplicator)) builder.Services.AddSingleton<IWebhookEventDeduplicator>(_ => Dedupe);
        if (omit != typeof(IWebhookEventQueue)) builder.Services.AddSingleton<IWebhookEventQueue>(_ => Queue);
        configureServices?.Invoke(builder.Services);
        try { App = Program.BuildApplication(builder); }
        catch
        {
            ((IDisposable)builder.Configuration).Dispose();
            Client.Dispose(); Signature.Dispose(); Dedupe.Dispose(); Queue.Dispose();
            throw;
        }
    }

    /// <summary>只在成功 StartAsync 之後使用主機公布的實際 ephemeral URL；不猜測或占用固定埠。</summary>
    internal async Task StartAsync()
    {
        await App.StartAsync();
        Client.Dispose();
        Client = new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(App.Urls.Single()), Timeout = TimeSpan.FromSeconds(10)
        };
    }

    /// <summary>送出合成 body 與選用簽章；request／content 在完成後立即釋放，response 移交案例擁有。</summary>
    internal async Task<HttpResponseMessage> PostAsync(string body, string path = "/api/line/webhook/123", string? signature = "valid", bool chunked = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = chunked ? new UnknownLengthContent(Encoding.UTF8.GetBytes(body)) :
            new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        if (signature is not null) request.Headers.TryAddWithoutValidation("X-Line-Signature", signature);
        return await Client.SendAsync(request);
    }

    /// <summary>依序停止 HTTP、關閉 client 及 Dispose Host；finally 清空替身，啟動失敗也不保留記憶體集合。</summary>
    public async ValueTask DisposeAsync()
    {
        try { await App.StopAsync(); }
        finally
        {
            Client.Dispose();
            try { await App.DisposeAsync(); }
            finally { Signature.Dispose(); Dedupe.Dispose(); Queue.Dispose(); }
        }
    }

    /// <summary>沒有 Content-Length 的合成 HTTP content，迫使 Kestrel 真正走 chunked body 上限判定。</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        /// <summary>只寫入本案例合成 bytes，request owner 結束後釋放；不借用產品 body 緩衝。</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        /// <summary>明確拒絕預先宣告長度，測試才不會只覆蓋 Content-Length 的提早拒絕。</summary>
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}

/// <summary>只在測試 DI 存活的合成 verifier；固定 channel 白名單模擬部署邊界，不是真 HMAC。</summary>
internal sealed class SignatureDouble : IWebhookSignatureVerifier, IDisposable
{
    /// <summary>呼叫次數用於證明過大／無效標頭在驗簽前就拒絕。</summary>
    internal int Calls;
    /// <summary>短期測試比對資料，由 Dispose 清除；正式介面嚴禁保存 body。</summary>
    internal byte[] LastBody = [];
    /// <summary>故障注入仍由真正 endpoint catch 及共用 diagnostics 處理。</summary>
    internal Exception? Failure;
    /// <summary>在邊界處注入真正 caller 取消等待；來源由測試 owner 釋放。</summary>
    internal bool WaitForCancellation;
    /// <summary>表示 HTTP 已到 verifier 的決定性同步點，測試此時才取消 client。</summary>
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>合成檢查僅允許兩個已配置 channel 與 valid 簽章，必要時等待 HTTP abort，返回後不借用輸入記憶體。</summary>
    public async ValueTask<bool> VerifyAsync(string channelId, ReadOnlyMemory<byte> body, string signature, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        LastBody = body.ToArray();
        Entered.TrySetResult();
        if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (Failure is not null) throw Failure;
        return channelId is "123" or "456" && string.Equals(signature, "valid", StringComparison.Ordinal);
    }
    /// <summary>清空合成本文參照，避免測試 fixture 在結束後繼續保留資料。</summary>
    public void Dispose() { Array.Clear(LastBody); LastBody = []; }
}

/// <summary>本案例唯一的合成 transaction owner；複合鍵隔離 channel，未提交 lease 不可誤當已處理。</summary>
internal sealed class DeduplicatorDouble : IWebhookEventDeduplicator, IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<(string, string)> committed = [];
    private readonly HashSet<(string, string)> pending = [];
    /// <summary>可控的 SQL 登記故障，不讀實際連線。</summary>
    internal Exception? Failure;
    /// <summary>可控的 SQL commit 故障，證明 queue 未發布且可重送。</summary>
    internal Exception? CommitFailure;
    /// <summary>可控的 rollback 故障，測試診斷仍收到真實例外。</summary>
    internal Exception? DisposeFailure;
    /// <summary>已 Dispose 的 lease 累積數，用來斷言 endpoint 沒有漏釋放。</summary>
    internal int DisposedLeases;
    /// <summary>在途 claim 的精確基線，成功、拒絕及取消後都必須歸零。</summary>
    internal int Active { get { lock (gate) return pending.Count; } }

    /// <summary>在本案例鎖內建立 claim；已提交重複回 null，未決競態回故障而非永久吞事件。</summary>
    public ValueTask<IWebhookEventRegistration?> TryRegisterAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null) throw Failure;
        var key = (envelope.ChannelId, envelope.WebhookEventId);
        lock (gate)
        {
            if (committed.Contains(key)) return ValueTask.FromResult<IWebhookEventRegistration?>(null);
            if (!pending.Add(key)) throw new IOException("Synthetic unresolved transaction.");
            return ValueTask.FromResult<IWebhookEventRegistration?>(new Registration(this, key));
        }
    }
    /// <summary>釋放全部案例鍵，沒有正式跨程序儲存；必須在 Host 停止後才執行。</summary>
    public void Dispose() { lock (gate) { pending.Clear(); committed.Clear(); } }

    /// <summary>單一合成 lease，只保存 key 與 owner；不保存 envelope、HTTP 或憑證，Dispose 冪等。</summary>
    private sealed class Registration(DeduplicatorDouble owner, (string, string) key) : IWebhookEventRegistration
    {
        private bool disposed;
        /// <summary>提交原子更新本案例集合；注入 commit 故障時仍保留 pending，交由 finally rollback。</summary>
        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.CommitFailure is not null) throw owner.CommitFailure;
            lock (owner.gate) { owner.pending.Remove(key); owner.committed.Add(key); }
            return ValueTask.CompletedTask;
        }
        /// <summary>確定移除未提交 pending 並記錄釋放，清理故障於移除後注入，防止測試自己產生資源漏失。</summary>
        public ValueTask DisposeAsync()
        {
            if (disposed) return ValueTask.CompletedTask;
            disposed = true;
            lock (owner.gate) owner.pending.Remove(key);
            Interlocked.Increment(ref owner.DisposedLeases);
            if (owner.DisposeFailure is not null) throw owner.DisposeFailure;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>合成 queue owner，只有提交成功才保存 immutable envelope；集合只活至目前案例結束。</summary>
internal sealed class QueueDouble : IWebhookEventQueue, IDisposable
{
    /// <summary>產品實際移交的事件快照，測試以頻道及 provider ID 檢查隔離與去重。</summary>
    internal ConcurrentQueue<WebhookEnvelope> Events { get; } = new();
    /// <summary>預期滿載拒絕，不是 exception，不應產生告警。</summary>
    internal bool Reject;
    /// <summary>真失敗注入，必須被端點交共用入口且返回 503。</summary>
    internal Exception? Failure;
    /// <summary>在第二次入列失敗，保護批次部分成功後重送不重複已完成項。</summary>
    internal int FailOnCall;
    /// <summary>交接呼叫次數，僅在測試注入第二筆故障使用。</summary>
    internal int Calls;
    /// <summary>保持 claim 直到真正 HTTP caller 取消，用來測 deterministic rollback。</summary>
    internal bool WaitForCancellation;
    /// <summary>已取得 claim 但尚未提交的決定性同步點。</summary>
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>先決定拒絕／故障，通過才借用 claim 提交並發布；提交後不再檢查 caller 取消。</summary>
    public async ValueTask<bool> TryEnqueueAsync(WebhookEnvelope envelope, IWebhookEventRegistration registration, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref Calls);
        Entered.TrySetResult();
        if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (Failure is not null) throw Failure;
        if (call == FailOnCall) throw new IOException("Synthetic queue fault containing private-body-marker.");
        if (Reject) return false;
        await registration.CommitAsync(cancellationToken);
        Events.Enqueue(envelope);
        return true;
    }
    /// <summary>Host 停止後清除事件參照，不保留任何合成個資或 replyToken。</summary>
    public void Dispose() { while (Events.TryDequeue(out _)) { } }
}
