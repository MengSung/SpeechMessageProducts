/*
檔案整體說明
目的與定位：這是 .NET 10／11 離線效能研究的單一程序入口，並非產品啟動入口；只借用獨立 Release 產物與固定合成資料，不讀產品組態、秘密或外部服務。
主要功能與流程：micro 模式先驗證正式純函式結果，再暖機與記錄七個樣本；server 模式建立 loopback Kestrel 並映射正式 Webhook；client 固定由 .NET 10 執行，記錄有限併行與有限樣本延遲。JSON 僅寫到 stdout 供父程序保存。
使用邊界與維護：每個程序最多一個工作負載執行器／Host；合成頻道與使用者不是正式授權資料。父程序以逾時管理唯一子程序；Host、HttpClient、回應、診斷器與取消來源皆有明確 Dispose。所有量測只代表本機受控輸入，不保證真實 SQL／Dataverse 效能或長期無洩漏。
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Signatures;
using SpeechMessageProducts.LineWebhook;
using SpeechMessage.Tenancy;
using ToolUtilityNameSpace.Diagnostics;

/// <summary>只接受固定模式及受控數值；失敗以非零碼與例外型別終止，由父程序保存證據。</summary>
internal static class Research
{
    /// <summary>以固定參數選擇離線 micro、server 或 client；不呼叫產品 Program.Main。</summary>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args[0] == "micro") RunMicro();
            else if (args[0] == "server") await RunServer(int.Parse(args[1]));
            else if (args[0] == "client") await RunClient(args[1], int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
            else throw new ArgumentException("Unknown research mode.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    /// <summary>記錄實際已載入組件與 GC／JIT 設定；版本由執行中的 runtime 取得，不依目錄名稱猜測。</summary>
    internal static object EnvironmentInfo() => new
    {
        runtime = RuntimeInformation.FrameworkDescription,
        version = Environment.Version.ToString(),
        os = RuntimeInformation.OSDescription,
        arch = RuntimeInformation.ProcessArchitecture.ToString(),
        processors = Environment.ProcessorCount,
        serverGC = GCSettings.IsServerGC,
        latencyMode = GCSettings.LatencyMode.ToString(),
        affinity = Process.GetCurrentProcess().ProcessorAffinity.ToInt64(),
        dynamicCode = RuntimeFeature.IsDynamicCodeSupported,
        libraries = new[] { typeof(object), typeof(JsonDocument), typeof(WebApplication),
            typeof(Newtonsoft.Json.JsonConvert), typeof(WebhookEnvelope), typeof(Workloads) }
            .Select(t => new { name = t.Assembly.GetName().Name,
                version = t.Assembly.GetName().Version?.ToString(), path = t.Assembly.Location }).ToArray()
    };

    /// <summary>每個案例獨立暖機至少一秒；樣本含牆鐘、CPU、配置及 GC，輸出不放在計時迴圈內。</summary>
    private static void RunMicro()
    {
        var workloads = new Workloads();
        workloads.AssertCorrectness();
        var results = new List<object>();
        using var process = Process.GetCurrentProcess();
        foreach (var item in workloads.Cases)
        {
            var batch = 1;
            var pilot = Stopwatch.StartNew();
            do
            {
                Execute(item.Run, batch);
                batch = Math.Min(batch * 2, 1_048_576);
            } while (pilot.Elapsed.TotalMilliseconds < 100 && batch < 1_048_576);
            // 每個 batch 以約十毫秒為上限，避免慢案例的校準量造成長時間單次停頓。
            batch = Math.Max(1, (int)(batch / 2.0 * 10 / Math.Max(pilot.Elapsed.TotalMilliseconds / 2, 0.1)));
            var warm = Stopwatch.StartNew();
            while (warm.Elapsed.TotalSeconds < 1.25) Execute(item.Run, batch);
            Thread.Sleep(150); // 讓已排程的 tiered compilation 有時間完成；不宣稱逐方法證明 Tier1。
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            var samples = new List<object>();
            for (var sample = 0; sample < 7; sample++)
            {
                var before = GC.GetTotalAllocatedBytes(true);
                var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                process.Refresh();
                var cpu = process.TotalProcessorTime.TotalMilliseconds;
                var clock = Stopwatch.StartNew();
                long operations = 0;
                long checksum = 0;
                do { checksum += Execute(item.Run, batch); operations += batch; }
                while (clock.Elapsed.TotalMilliseconds < 200);
                clock.Stop();
                process.Refresh();
                var allocated = GC.GetTotalAllocatedBytes(true) - before;
                samples.Add(new { ns = clock.Elapsed.TotalNanoseconds / operations,
                    bytes = allocated / (double)operations, operations,
                    cpuNs = (process.TotalProcessorTime.TotalMilliseconds - cpu) * 1e6 / operations,
                    gc = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - collections[g]).ToArray(),
                    checksum });
            }
            results.Add(new { name = item.Name, inputHash = item.InputHash,
                outputHash = item.OutputHash, samples });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "micro", environment = EnvironmentInfo(), results }));
    }

    /// <summary>加總可觀察結果以避免無用工作刪除；沒有跨案例持有輸出或配置的集合。</summary>
    private static long Execute(Func<int> action, int count)
    {
        long sum = 0;
        for (var index = 0; index < count; index++) sum += action();
        return sum;
    }

    /// <summary>只在 loopback 建立正式 endpoint；合成依賴不接 SQL、不保留事件，EOF 後依序關閉 Host 與診斷器。</summary>
    private static async Task RunServer(int delay)
    {
        if (delay is not (0 or 10)) throw new ArgumentException("Unexpected delay.");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        { Args = Array.Empty<string>(), EnvironmentName = "Research", ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var sink = new SyntheticSink(delay);
        await using var diagnostics = new ExceptionDiagnostics(AppContext.BaseDirectory,
            outputOptions: new ExceptionOutputOptions(false, false));
        builder.Services.AddSingleton(new WebhookOptions());
        builder.Services.AddSingleton(diagnostics);
        builder.Services.AddSingleton<IWebhookSignatureVerifier>(new LineWebhookSignatureVerifier(new SyntheticResolver()));
        builder.Services.AddSingleton<IWebhookEventDeduplicator>(sink);
        builder.Services.AddSingleton<IWebhookEventQueue>(sink);
        await using var app = builder.Build();
        app.MapLineWebhook();
        app.MapGet("/research/metrics", () => sink.Snapshot());
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "ready", url = addresses.Addresses.Single(),
            environment = EnvironmentInfo() }));
        await Console.In.ReadLineAsync();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await app.StopAsync(stop.Token);
        if (sink.Active != 0) throw new InvalidOperationException("Registration not drained.");
    }

    /// <summary>固定 runtime 的獨立 client；最多 64 併行、每 worker 最多一百萬延遲樣本，完整讀完回應再釋放。</summary>
    private static async Task RunClient(string url, int concurrency, int milliseconds, int verify)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IPAddress.IsLoopback(IPAddress.Parse(uri.Host))
            || concurrency is < 1 or > 64 || milliseconds is < 1 or > 30_000)
            throw new ArgumentException("Client is restricted to bounded loopback.");
        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = concurrency, UseProxy = false };
        using var client = new HttpClient(handler) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15) };
        if (verify != 0) await AssertEndpoint(client);
        var before = await client.GetStringAsync("/research/metrics");
        var clock = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, concurrency).Select(worker => Task.Run(async () =>
        {
            var times = new List<double>(16_384);
            long errors = 0;
            var channel = worker % 2 == 0 ? "10001" : "10002";
            var body = Workloads.Body(1, channel);
            var signature = Workloads.Sign(body, channel);
            do
            {
                var start = Stopwatch.GetTimestamp();
                using var request = Request(channel, body, signature);
                using var response = await client.SendAsync(request);
                await response.Content.CopyToAsync(System.IO.Stream.Null);
                if (response.StatusCode != HttpStatusCode.OK) errors++;
                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                if (times.Count >= 1_000_000) throw new InvalidOperationException("Client sample budget exceeded.");
            } while (clock.ElapsedMilliseconds < milliseconds);
            return (times, errors);
        })).ToArray();
        var outputs = await Task.WhenAll(workers);
        clock.Stop();
        var after = await client.GetStringAsync("/research/metrics");
        var latencies = outputs.SelectMany(o => o.times).OrderBy(t => t).ToArray();
        var errorCount = outputs.Sum(o => o.errors);
        if (errorCount != 0) throw new InvalidOperationException("Non-success load response.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "http", concurrency, elapsed = clock.Elapsed.TotalSeconds,
            count = latencies.Length, errors = errorCount, rps = latencies.Length / clock.Elapsed.TotalSeconds,
            p50 = Percentile(latencies, 0.50), p95 = Percentile(latencies, 0.95), p99 = Percentile(latencies, 0.99),
            before = JsonSerializer.Deserialize<JsonElement>(before), after = JsonSerializer.Deserialize<JsonElement>(after) }));
    }

    /// <summary>以兩個合成頻道檢查正確驗簽與 scope，並注入壞簽章、跨頻道簽章、重複 ID 及 queue 拒絕；不聲稱測過 SQL 去重。</summary>
    private static async Task AssertEndpoint(HttpClient client)
    {
        foreach (var channel in new[] { "10001", "10002" })
        {
            var body = Workloads.Body(1, channel);
            await Expect(client, channel, body, Workloads.Sign(body, channel), HttpStatusCode.OK);
            await Expect(client, channel, body, "bad", HttpStatusCode.Unauthorized);
            await Expect(client, channel, body, Workloads.Sign(body, channel == "10001" ? "10002" : "10001"), HttpStatusCode.Unauthorized);
        }
        var duplicate = Encoding.UTF8.GetBytes("{\"events\":[" + Workloads.Event(0, "10001") + "," + Workloads.Event(0, "10001") + "]}");
        await Expect(client, "10001", duplicate, Workloads.Sign(duplicate, "10001"), HttpStatusCode.BadRequest);
        var rejected = Workloads.Body(1, "10001", true);
        await Expect(client, "10001", rejected, Workloads.Sign(rejected, "10001"), HttpStatusCode.ServiceUnavailable);
        var state = JsonDocument.Parse(await client.GetStringAsync("/research/metrics"));
        using (state)
        {
            var root = state.RootElement;
            if (root.GetProperty("active").GetInt64() != 0 || root.GetProperty("scopeErrors").GetInt64() != 0
                || root.GetProperty("accepted").GetInt64() < 2 || root.GetProperty("rejected").GetInt64() < 1)
                throw new InvalidOperationException("Endpoint correctness/drain assertion failed.");
        }
    }

    /// <summary>單次狀態斷言擁有 request／response；取消及逾時由 HttpClient 上限處理並使研究失敗。</summary>
    private static async Task Expect(HttpClient client, string channel, byte[] body, string signature, HttpStatusCode expected)
    {
        using var request = Request(channel, body, signature);
        using var response = await client.SendAsync(request);
        if (response.StatusCode != expected) throw new InvalidOperationException($"Expected {expected}, got {response.StatusCode}.");
    }

    /// <summary>每次建立獨立 content，只借用固定不再改寫的合成 bytes；Dispose request 會釋放 content。</summary>
    private static HttpRequestMessage Request(string channel, byte[] body, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/line/webhook/" + channel)
        { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Line-Signature", signature);
        return request;
    }

    /// <summary>使用明載的最近秩百分位；只供單輪描述，信賴區間由父程序以整輪配對計算。</summary>
    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * fraction) - 1, 0, sorted.Length - 1)];
}

/// <summary>固定兩個測試頻道的唯讀 resolver；常值是假資料，不存取產品秘密或授權來源。</summary>
internal sealed class SyntheticResolver : IWebhookChannelSecretResolver
{
    /// <summary>僅固定兩項，呼叫端不得將它當產品設定。</summary>
    public IReadOnlyList<string> ConfiguredChannelIds => new[] { "10001", "10002" };
    /// <summary>只解析已知合成頻道；未知頻道 fail closed，不新增字典或保留路由。</summary>
    public bool TryResolveSecret(string channelId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? secret)
    {
        secret = channelId switch { "10001" => "synthetic-channel-A-secret", "10002" => "synthetic-channel-B-secret", _ => null };
        return secret is not null;
    }
    /// <summary>本工具沒有外部組態，固定資料已由正確性斷言驗證。</summary>
    public void ValidateConfiguration() { }
}

/// <summary>有界即時消費器，模擬 claim 等待及 queue 接受，不提供持久性或跨請求去重；只保留計數器。</summary>
internal sealed class SyntheticSink : IWebhookEventDeduplicator, IWebhookEventQueue
{
    private readonly int delay;
    private long active, accepted, rejected, scopeErrors, committed;
    /// <summary>目前尚未 Dispose 的 registration 數量，排空時必須為零。</summary>
    public long Active => Interlocked.Read(ref active);
    /// <summary>固定最多十毫秒的非同步合成等待，不模擬真實 SQL／網路協定。</summary>
    public SyntheticSink(int delay) => this.delay = delay;
    /// <summary>等待 caller token 後建立單一 owner claim；容量硬上限 1024，超限即使本輪失敗。</summary>
    public async ValueTask<IWebhookEventRegistration?> TryRegisterAsync(WebhookEnvelope envelope, CancellationToken token)
    {
        if (delay > 0) await Task.Delay(delay, token);
        if (Interlocked.Increment(ref active) > 1024)
        {
            Interlocked.Decrement(ref active);
            throw new InvalidOperationException("Synthetic in-flight bound exceeded.");
        }
        return new Registration(this);
    }
    /// <summary>驗證已驗簽 envelope 內的合成 scope，立即消費且不保存內容；拒絕路徑不 Commit。</summary>
    public async ValueTask<bool> TryEnqueueAsync(WebhookEnvelope envelope, IWebhookEventRegistration registration, CancellationToken token)
    {
        if (envelope.Event.GetProperty("source").GetProperty("userId").GetString() != "synthetic-user-" + envelope.ChannelId)
        {
            Interlocked.Increment(ref scopeErrors);
            throw new InvalidOperationException("Synthetic cross-scope event.");
        }
        if (envelope.WebhookEventId == "reject")
        {
            Interlocked.Increment(ref rejected);
            return false;
        }
        await registration.CommitAsync(token);
        Interlocked.Increment(ref accepted);
        return true;
    }
    /// <summary>只輸出計數器及本程序資源計量；不輸出 event／user／secret，metrics 不屬於正式產品路由。</summary>
    public object Snapshot()
    {
        using var process = Process.GetCurrentProcess();
        return new { active = Active, accepted = Interlocked.Read(ref accepted), rejected = Interlocked.Read(ref rejected),
            scopeErrors = Interlocked.Read(ref scopeErrors), committed = Interlocked.Read(ref committed),
            allocated = GC.GetTotalAllocatedBytes(true), cpuMs = process.TotalProcessorTime.TotalMilliseconds,
            heap = GC.GetTotalMemory(false), pending = ThreadPool.PendingWorkItemCount,
            gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray() };
    }
    /// <summary>endpoint 唯一擁有，Commit 最多一次，Dispose 以交換確保任何失敗路徑最多釋放一次。</summary>
    private sealed class Registration : IWebhookEventRegistration
    {
        private SyntheticSink? owner;
        private bool done;
        /// <summary>只保留目前 sink 計數器 owner，不持有 envelope、request 或憑證。</summary>
        internal Registration(SyntheticSink owner) => this.owner = owner;
        /// <summary>同步檢查取消與重複 Commit；實測不含交易或持久化。</summary>
        public ValueTask CommitAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (done || owner is null) throw new InvalidOperationException("Invalid registration state.");
            done = true;
            Interlocked.Increment(ref owner.committed);
            return ValueTask.CompletedTask;
        }
        /// <summary>清掉唯一 owner 引用並遞減存活數，不建立背景工作；拒絕／例外同樣必須走此路徑。</summary>
        public ValueTask DisposeAsync()
        {
            var current = Interlocked.Exchange(ref owner, null);
            if (current is not null) Interlocked.Decrement(ref current.active);
            return ValueTask.CompletedTask;
        }
    }
}
