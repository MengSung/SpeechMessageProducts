using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Moq;
using SpeechMessage.Dynamics.Plugins.Diagnostics;
using Newtonsoft.Json.Linq;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>
/// 完全離線驗證外掛邊界；以 Moq 提供 SDK 服務、假 handler 注入 HTTP 狀態及原始例外。
/// 每案例獨佔 provider、追蹤集合與 HttpClient，確認不輸出機密、不吞錯、傳輸及服務參考不跨執行保留。
/// </summary>
public sealed class DiagnosticsPingPluginTests
{
    /// <summary>只在測試中使用的合成憑證，不對應任何 LINE channel。</summary>
    private const string Token = "synthetic-plugin-token";
    /// <summary>符合單人推播格式的合成收件者，不對應真實聯絡人。</summary>
    private const string UserId = "U11111111111111111111111111111111";

    /// <summary>
    /// 保護部署外殼只能以伺服器已接受的 .NET Framework 4.6.2 為目標；
    /// 直接讀取測試實際載入的 Plugin 組件屬性，避免只看專案設定或測試專案本身而產生假綠燈。
    /// </summary>
    [Fact(DisplayName = "Plugin 參考的正式組件目標為 Framework 4.6.2")]
    public void Assembly_TargetFramework_IsNet462()
    {
        var attribute = typeof(DiagnosticsPingPlugin).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
            .Cast<TargetFrameworkAttribute>().Single();

        Assert.Equal(".NETFramework,Version=v4.6.2", attribute.FrameworkName);
    }

    /// <summary>trace 模式不得送 HTTP；斷言標記、組件、執行環境、觸發事件及兩語系實際取字結果。</summary>
    [Fact(DisplayName = "追蹤模式寫入診斷與雙語文字")]
    public void Execute_TraceMode_WritesPingAndBothCultures()
    {
        using var fixture = new Fixture();
        fixture.Create("trace").Execute(fixture.Provider.Object);
        var trace = string.Join("\n", fixture.Traces);
        Assert.Contains("P0-PING OK", trace, StringComparison.Ordinal);
        Assert.Contains("SpeechMessage.Dynamics.Plugins, Version=1.0.0.0", trace, StringComparison.Ordinal);
        Assert.Contains("FrameworkFileVersion=", trace, StringComparison.Ordinal);
        Assert.Contains("EnvironmentVersion=", trace, StringComparison.Ordinal);
        Assert.Contains("連線測試成功", trace, StringComparison.Ordinal);
        Assert.Contains("［連線測試成功ǂǂǂ］", trace, StringComparison.Ordinal);
        Assert.Contains("account", trace, StringComparison.Ordinal);
        Assert.Contains("Update", trace, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Handler.Calls);
    }

    /// <summary>固定 UTC 時鐘與實體 ID，斷言真實 Core HTTP 標頭及單則推播內容，不驗證替身自行設定的結果。</summary>
    [Fact(DisplayName = "推播重試鍵來自主實體識別")]
    public void Execute_PushMode_UsesPrimaryEntityIdAsRetryKey()
    {
        using var fixture = new Fixture();
        fixture.Create().Execute(fixture.Provider.Object);
        Assert.Equal(fixture.EntityId.ToString("D", CultureInfo.InvariantCulture), fixture.Handler.RetryKey);
        Assert.Contains("P0 推播測試 2026-11-03T00:00:00.0000000+00:00", fixture.Handler.Body!, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.True(fixture.Handler.ContentDisposed);
    }

    /// <summary>以合成 token／userId 送訊，追蹤只允許分類、安全狀態與請求診斷鍵，不能夾帶憑證或收件者。</summary>
    [Theory(DisplayName = "推播追蹤永不包含憑證與收件者")]
    [InlineData(200)]
    [InlineData(401)]
    [InlineData(429)]
    public void Execute_PushMode_TraceNeverContainsToken(int status)
    {
        using var fixture = new Fixture(status);
        var error = Record.Exception(() => fixture.Create().Execute(fixture.Provider.Object));
        var trace = string.Join("\n", fixture.Traces);
        Assert.DoesNotContain(Token, trace, StringComparison.Ordinal);
        Assert.DoesNotContain(UserId, trace, StringComparison.Ordinal);
        Assert.DoesNotContain("P0 推播測試", trace, StringComparison.Ordinal);
        if (error is not null) Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.Contains(status.ToString(CultureInfo.InvariantCulture), trace, StringComparison.Ordinal);
    }

    /// <summary>以 429／5xx 注入可重試結果，必須追蹤分類並以 SDK 例外退出；訊息只有通用文字與事件鍵。</summary>
    [Theory(DisplayName = "可重試 HTTP 故障交系統作業處理")]
    [InlineData(429)]
    [InlineData(500)]
    public void Execute_Retryable_ThrowsInvalidPluginExecutionException(int status)
    {
        using var fixture = new Fixture(status);
        var error = Assert.Throws<InvalidPluginExecutionException>(() => fixture.Create().Execute(fixture.Provider.Object));
        Assert.Contains(fixture.EntityId.ToString("D", CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
        Assert.Contains("Retryable", string.Join("\n", fixture.Traces), StringComparison.Ordinal);
    }

    /// <summary>以 401 注入永久失敗，外掛只追蹤分類且不拋出，避免非同步作業盲目重試。</summary>
    [Fact(DisplayName = "永久失敗只追蹤不拋出")]
    public void Execute_Permanent_DoesNotThrow()
    {
        using var fixture = new Fixture(401);
        fixture.Create().Execute(fixture.Provider.Object);
        Assert.Contains("Permanent", string.Join("\n", fixture.Traces), StringComparison.Ordinal);
        Assert.Equal(1, fixture.Handler.Calls);
    }

    /// <summary>以重入深度二注入遞迴事件，斷言沒有追蹤與 HTTP，且不讀取實體內容。</summary>
    [Fact(DisplayName = "深度大於一直接返回")]
    public void Execute_DepthGreaterThanOne_DoesNothing()
    {
        using var fixture = new Fixture();
        fixture.Context.SetupGet(value => value.Depth).Returns(2);
        fixture.Create().Execute(fixture.Provider.Object);
        Assert.Empty(fixture.Traces);
        Assert.Equal(0, fixture.Handler.Calls);
        fixture.Context.VerifyGet(value => value.PrimaryEntityId, Times.Never);
    }

    /// <summary>注入損毀 JSON／無效模式語系；拒絕訊息必須為繁中固定文字，不能回顯輸入或 JSON 解析例外。</summary>
    [Theory(DisplayName = "不合法註冊設定以安全繁中訊息拒絕")]
    [InlineData("{synthetic-secret")]
    [InlineData("null")]
    [InlineData("{\"mode\":\"invalid\",\"culture\":\"zh-TW\"}")]
    [InlineData("{\"mode\":\"trace\",\"culture\":\"invalid\"}")]
    public void Ctor_InvalidUnsecureJson_ThrowsWithChineseMessage(string config)
    {
        var error = Assert.Throws<InvalidPluginExecutionException>(() => new DiagnosticsPingPlugin(config, Token));
        Assert.Contains("設定", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    /// <summary>Core 的無回應故障保留原始 HttpRequestException；外掛必須以同一實例作 InnerException，不輸出訊息。</summary>
    [Fact(DisplayName = "可重試傳輸例外保留原始內層例外")]
    public void Execute_Retryable_PreservesTransportExceptionAsInner()
    {
        using var fixture = new Fixture();
        var original = new HttpRequestException("synthetic-sensitive-network-detail");
        fixture.Handler.Reply = (_, _) => throw original;
        var error = Assert.Throws<InvalidPluginExecutionException>(() => fixture.Create().Execute(fixture.Provider.Object));
        Assert.Same(original, error.InnerException);
        var trace = string.Join("\n", fixture.Traces);
        Assert.Contains(typeof(HttpRequestException).FullName!, trace, StringComparison.Ordinal);
        Assert.DoesNotContain(original.Message, trace + error.Message, StringComparison.Ordinal);
    }

    /// <summary>非預期程式故障先追蹤型別再以 throw 重拋；同一例外必須離開外掛，訊息不可進追蹤。</summary>
    [Fact(DisplayName = "非預期例外追蹤後保持原始實例重拋")]
    public void Execute_UnexpectedException_TracesAndRethrows()
    {
        using var fixture = new Fixture();
        var original = new InvalidOperationException("synthetic-sensitive-programming-detail");
        fixture.Handler.Reply = (_, _) => throw original;
        Assert.Same(original, Assert.Throws<InvalidOperationException>(() => fixture.Create().Execute(fixture.Provider.Object)));
        var trace = string.Join("\n", fixture.Traces);
        Assert.Contains(typeof(InvalidOperationException).FullName!, trace, StringComparison.Ordinal);
        Assert.DoesNotContain(original.Message, trace, StringComparison.Ordinal);
    }

    /// <summary>注入重複鍵、錯誤型別、過長設定與非法收件者，固定拒絕且不保留敏感輸入作內層例外。</summary>
    /// <param name="fault">固定故障選擇，不接受外部設定。</param>
    [Theory(DisplayName = "診斷設定邊界拒絕不合法值")]
    [InlineData("empty")]
    [InlineData("long")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("wrong-type")]
    [InlineData("user")]
    [InlineData("token")]
    public void Ctor_ConfigurationBoundary_FailsClosed(string fault)
    {
        var config = "{\"mode\":\"push\",\"culture\":\"zh-TW\",\"lineUserId\":\"" + UserId + "\"}";
        config = fault switch
        {
            "empty" => "",
            "long" => new string('x', 4097),
            "duplicate" => "{\"mode\":\"push\",\"mode\":\"trace\",\"culture\":\"zh-TW\"}",
            "unknown" => "{\"mode\":\"trace\",\"culture\":\"zh-TW\",\"other\":\"synthetic-sensitive\"}",
            "wrong-type" => "{\"mode\":2,\"culture\":\"zh-TW\"}",
            "user" => config.Replace(UserId, "synthetic-sensitive-user"),
            _ => config
        };
        var error = Assert.Throws<InvalidPluginExecutionException>(() => new DiagnosticsPingPlugin(config, fault == "token" ? "" : Token));
        Assert.Equal("診斷外掛設定格式錯誤。", error.Message);
        Assert.Null(error.InnerException);
    }

    /// <summary>
    /// 同一無狀態 HTTP client 供 A／B 外掛重用，各自執行二十次；真正 Core 必須每次取到正確憑證、收件者及事件鍵。
    /// 兩路並行且每次更換 provider，所有回應內容在 Execute 返回前釋放，停止後活躍數回到零。
    /// </summary>
    [Fact(DisplayName = "重用外掛與共用傳輸維持步驟隔離並排空資源")]
    public async Task Execute_ConcurrentRegistrationReuse_IsolatedAndDrained()
    {
        using var handler = new IsolationHandler();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new LinePushClient(http);
        var pluginA = new DiagnosticsPingPlugin("{\"mode\":\"push\",\"culture\":\"zh-TW\",\"lineUserId\":\"" + UserId + "\"}", Token,
            client, () => new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        var pluginB = new DiagnosticsPingPlugin("{\"mode\":\"push\",\"culture\":\"qps-ploc\",\"lineUserId\":\"U22222222222222222222222222222222\"}",
            "synthetic-plugin-token-B", client, () => new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        await Task.WhenAll(Task.Run(() => Run(pluginA, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
            Task.Run(() => Run(pluginB, "bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee")));
        Assert.Equal(40, handler.Calls);
        Assert.Equal(0, handler.Active);
        Assert.Equal(0, handler.Mismatches);

        static void Run(DiagnosticsPingPlugin plugin, string id)
        {
            for (var index = 0; index < 20; index++)
            {
                using var fixture = new Fixture();
                fixture.Context.SetupGet(value => value.PrimaryEntityId).Returns(Guid.Parse(id));
                plugin.Execute(fixture.Provider.Object);
                Assert.DoesNotContain("synthetic-plugin-token", string.Join("\n", fixture.Traces), StringComparison.Ordinal);
            }
        }
    }

    /// <summary>會丟棄 Post 的同步內容代表不能回到原執行緒；外掛須在有界等待內完成且釋放回應。</summary>
    [Fact(DisplayName = "同步沙箱內容不會攔住非同步推播完成")]
    public void Execute_SynchronizationContext_CompletesAndDisposes()
    {
        using var fixture = new Fixture();
        using var content = new TrackedContent();
        fixture.Handler.Reply = async (_, _) =>
        {
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        var previous = SynchronizationContext.Current;
        var context = new RejectingContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            fixture.Create().Execute(fixture.Provider.Object);
            Assert.Equal(1, fixture.Handler.Calls);
            Assert.Equal(0, context.Posts);
            Assert.True(content.Disposed);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    /// <summary>被驗證的沙箱同步內容，任何回到呼叫執行緒的排程都直接拒絕，不建立背景執行緒。</summary>
    private sealed class RejectingContext : SynchronizationContext
    {
        /// <summary>排程次數應維持零，避免靠等待逾時判斷死結。</summary>
        public int Posts { get; private set; }
        /// <summary>故障注入不執行 callback，若產品捕獲此外部內容便立刻失敗。</summary>
        /// <param name="d">不應被捕獲的回呼。</param>
        /// <param name="state">不應滯留的呼叫狀態。</param>
        public override void Post(SendOrPostCallback d, object? state)
        {
            Posts++;
            throw new InvalidOperationException("Sandbox synchronization context was captured.");
        }
    }

    /// <summary>共用連線池的無狀態替身；只保留有界計數，不保存 request、憑證或訊息原文。</summary>
    private sealed class IsolationHandler : HttpMessageHandler
    {
        /// <summary>以原子操作計算傳輸次數。</summary>
        private int calls;
        /// <summary>已建立但尚未 Dispose 的回應內容數量。</summary>
        private int active;
        /// <summary>隔離契約失敗次數，不保存失敗原文。</summary>
        private int mismatches;
        /// <summary>停止後的總操作數。</summary>
        public int Calls => calls;
        /// <summary>停止後必須為零的資源數。</summary>
        public int Active => active;
        /// <summary>停止後必須為零的配對錯誤數。</summary>
        public int Mismatches => mismatches;
        /// <summary>以真實 Core 組出的標頭及 JSON 檢查 A／B 配對，每次建立獨立的回應內容。</summary>
        /// <param name="request">只在這次 await 期間存活，不存入欄位。</param>
        /// <param name="cancellationToken">由 Core 擁有的十秒取消鏈。</param>
        /// <returns>交回 Core using owner 的獨立回應。</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
            var tokenA = string.Equals(request.Headers.Authorization!.Parameter, Token, StringComparison.Ordinal);
            var expectedId = tokenA ? UserId : "U22222222222222222222222222222222";
            var expectedKey = tokenA ? "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" : "bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee";
            if (!string.Equals((string?)body["to"], expectedId, StringComparison.Ordinal) ||
                !string.Equals(request.Headers.GetValues("X-Line-Retry-Key").Single(), expectedKey, StringComparison.Ordinal))
                Interlocked.Increment(ref mismatches);
            Interlocked.Increment(ref active);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new DrainContent(() => Interlocked.Decrement(ref active))
            };
        }
    }

    /// <summary>由 Core 唯一擁有的回應內容；Dispose 只回報一次並解除回呼，不能滯留測試 owner。</summary>
    private sealed class DrainContent : StringContent
    {
        /// <summary>最多存活至這次回應 Dispose 的資源釋放回呼。</summary>
        private Action? release;
        /// <summary>建立單次回應資源計數。</summary>
        /// <param name="release">測試 owner 的有界計數回呼。</param>
        public DrainContent(Action release) : base("{}") => this.release = release;
        /// <summary>原子解除回呼避免重複扣數，再交基底釋放串流。</summary>
        /// <param name="disposing">是否由 owner 明確釋放。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Interlocked.Exchange(ref release, null)?.Invoke();
            base.Dispose(disposing);
        }
    }

    /// <summary>單案例的 SDK／HTTP owner；不建立真實組織服務或任何網路 fallback。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>固定合成業務事件鍵，每個 fixture 各自持有且不來自真實 D365。</summary>
        public Guid EntityId { get; } = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        /// <summary>追蹤資料僅限此案例；Dispose 清空，不跨測試共享。</summary>
        public List<string> Traces { get; } = new();
        /// <summary>Moq SDK 服務提供者，只提供本案例的執行內容與追蹤服務。</summary>
        public Mock<IServiceProvider> Provider { get; } = new(MockBehavior.Strict);
        /// <summary>合成執行內容，讓深度與 RetryKey 契約可被決定性斷言。</summary>
        public Mock<IPluginExecutionContext> Context { get; } = new(MockBehavior.Strict);
        /// <summary>可注入故障的離線傳輸，所有回應資源由 Core 釋放。</summary>
        public Handler Handler { get; }
        /// <summary>此 fixture 的唯一連線池 owner，結束時 Dispose。</summary>
        private readonly HttpClient http;
        /// <summary>建立最小 SDK 情境；追蹤格式使用明確 invariant culture，不依賴主機文化。</summary>
        public Fixture(int status = 200)
        {
            Context.SetupGet(value => value.Depth).Returns(1);
            Context.SetupGet(value => value.PrimaryEntityId).Returns(EntityId);
            Context.SetupGet(value => value.PrimaryEntityName).Returns("account");
            Context.SetupGet(value => value.MessageName).Returns("Update");
            var tracing = new Mock<ITracingService>(MockBehavior.Strict);
            tracing.Setup(value => value.Trace(It.IsAny<string>(), It.IsAny<object[]>()))
                .Callback<string, object[]>((format, args) => Traces.Add(string.Format(CultureInfo.InvariantCulture, format, args)));
            Provider.Setup(value => value.GetService(typeof(ITracingService))).Returns(tracing.Object);
            Provider.Setup(value => value.GetService(typeof(IPluginExecutionContext))).Returns(Context.Object);
            Handler = new Handler(status);
            http = new HttpClient(Handler) { Timeout = Timeout.InfiniteTimeSpan };
        }
        /// <summary>使用真實 Core 配合假 handler；時間固定，不連線或等待系統時間。</summary>
        /// <param name="mode">trace 或 push，依案例選定。</param>
        /// <returns>只在此案例存活的外掛實例。</returns>
        public DiagnosticsPingPlugin Create(string mode = "push") =>
            new("{\"mode\":\"" + mode + "\",\"culture\":\"zh-TW\",\"lineUserId\":\"" + UserId + "\"}", Token,
                new LinePushClient(http), () => new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        /// <summary>由單一 owner 關閉 HttpClient／handler 並清除合成追蹤資料，沒有 fire-and-forget。</summary>
        public void Dispose() { http.Dispose(); Traces.Clear(); }
    }

    /// <summary>擷取單次 HTTP 的合成資料與內容生命週期；不得落到預設網路 handler。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        /// <summary>故障注入委派只存活於此測試 fixture。</summary>
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply { get; set; }
        /// <summary>送出次數，保護深度守門及沒有內部重試。</summary>
        public int Calls { get; private set; }
        /// <summary>當次事件鍵快照，不保留完整 request。</summary>
        public string? RetryKey { get; private set; }
        /// <summary>合成訊息快照，Dispose 清除，不寫入證據。</summary>
        public string? Body { get; private set; }
        /// <summary>追蹤回應是否由 Core 明確釋放，與 GC 無關。</summary>
        public bool ContentDisposed => content?.Disposed == true;
        /// <summary>僅持有合成回應內容供決定性釋放斷言。</summary>
        private readonly TrackedContent content = new();
        /// <summary>配置固定狀態回應，不提供真實端點或驗證機密。</summary>
        public Handler(int status) => Reply = (_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = content };
            response.Headers.Add("x-line-request-id", "11111111-2222-3333-4444-555555555555");
            return Task.FromResult(response);
        };
        /// <summary>先複製必要合成資料再交給故障注入，回應由 Core 的 using owner 接管。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RetryKey = string.Join("", request.Headers.GetValues("X-Line-Retry-Key"));
            Body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
            return await Reply(request, cancellationToken).ConfigureAwait(false);
        }
        /// <summary>測試 owner 結束時釋放未送出的內容，並丟棄合成快照。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { content.Dispose(); Body = RetryKey = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>單次回應內容的明確 Dispose 證據，不以程序結束代替清理。</summary>
    private sealed class TrackedContent : StringContent
    {
        /// <summary>內容是否已被擁有者確定性釋放。</summary>
        public bool Disposed { get; private set; }
        /// <summary>固定合成內容不含任何真實資料。</summary>
        public TrackedContent() : base("{}") { }
        /// <summary>釋放前留下布林計數，再交基底清理串流。</summary>
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
}
