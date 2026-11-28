/*
 * 檔案整體說明
 * 目的與定位：F12 部署命令所需的 Core LINE client lease 契約測試；僅在部署工具使用的
 * .NET 10 目標執行，以真正 SDK 配合合成傳輸驗證公開 API、隔離邊界與資源的唯一所有權。
 * 主要流程：從 Core 組件確認公開 lease 簽章，再以合成 handler 驗證 A/B/A 標頭、固定端點、
 * 逾時排空、建構故障、Dispose 順序、三十二輪並行 owner 及 256KiB JSON 容量邊界。
 * 故障注入包含非法 token、設定期限、HTTP 拒絕、傳輸及 handler 清理例外；決定性斷言要求
 * 未接受的依賴不被處置、已接受者恰處置一次、活躍操作歸零，以及已關閉的 SDK 不再帶認證。
 * 資源與隔離：僅使用合成 token 與有界觀察佇列，不開 socket、不連外網、不建立脫離案例的工作。
 * 驗證邊界：離線測試不代表正式或測試 Channel 實機結果；所有測試依賴由個別案例擁有，
 * 必須先 await 完成操作，再由唯一 lease owner 釋放 handler，避免把資源提早關閉當作排空。
 */
#if NET10_0_OR_GREATER
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Xunit;

namespace LineMessagingProcessor.Tests.TestTenants;

/// <summary>保護部署命令可借用、由 Core 獨占傳輸生命週期的公開 lease 契約。</summary>
public sealed class LineMessagingClientLeaseTests
{
    /// <summary>缺少真正傳輸 owner 時必須紅燈，避免工具被迫使用過時 client 或自行建立 HTTP。</summary>
    [Fact]
    public void PublicLease_ExistsInCore()
    {
        var type = typeof(LineChannelUsageReader).Assembly.GetType("LineMessagingProcessor.Core.LineMessagingClientLease");
        Assert.NotNull(type);
        Assert.True(type.IsPublic && type.IsSealed);
        Assert.True(typeof(IDisposable).IsAssignableFrom(type));
        Assert.Equal(typeof(string), Assert.Single(Assert.Single(type.GetConstructors()).GetParameters()).ParameterType);
        Assert.Equal(typeof(ILineMessagingClient), type.GetProperty("Client")!.PropertyType);
        Assert.Null(type.GetProperty("Client")!.SetMethod);
    }

    /// <summary>公開入口須建立 Sockets 管線，關閉 cookie／轉址並設定連線、緩衝及期限；只讀設定不發網路。</summary>
    [Fact]
    public void PublicConstructor_ConfiguresBoundedSocketsTransport()
    {
        using var lease = new LineMessagingClientLease("synthetic-A");
        var http = ReadOwnedField<HttpClient>(lease);
        var handler = Assert.IsType<SocketsHttpHandler>(ReadOwnedField<HttpMessageHandler>(http));
        Assert.Equal(TimeSpan.FromSeconds(15), http.Timeout);
        Assert.Equal(256L * 1024, http.MaxResponseContentBufferSize);
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(2, handler.MaxConnectionsPerServer);
        Assert.Null(handler.Credentials);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
        Assert.DoesNotContain("synthetic-A", lease.ToString());
    }

    /// <summary>A/B/A 交錯送出各自的 bearer；B 先釋放也不能污染或中止 A。</summary>
    /// <returns>三次真正 SDK 查詢完成並排空。</returns>
    [Fact]
    public async Task AbaRequests_KeepChannelHeadersAndFixedEndpoints()
    {
        var a = new ProbeHandler(); var b = new ProbeHandler();
        using var first = new LineMessagingClientLease("synthetic-A", a);
        using var second = new LineMessagingClientLease("synthetic-B", b);
        await first.Client.GetBotInfoAsync();
        await second.Client.GetBotInfoAsync();
        second.Dispose();
        await first.Client.GetBotInfoAsync();
        Assert.Equal(new[] { "synthetic-A", "synthetic-A" }, a.Tokens);
        Assert.Equal(new[] { "synthetic-B" }, b.Tokens);
        Assert.All(a.Schemes.Concat(b.Schemes), value => Assert.Equal("Bearer", value));
        Assert.All(a.Uris.Concat(b.Uris), uri => Assert.Equal("https://api.line.me/v2/bot/info", uri));
        Assert.Null(ReadOwnedField<HttpClient>(first).DefaultRequestHeaders.Authorization);
        Assert.Null(ReadOwnedField<HttpClient>(second).DefaultRequestHeaders.Authorization);
        first.Dispose();
        Assert.Equal(1, a.Disposals); Assert.Equal(1, b.Disposals);
        Assert.Equal(0, a.Active); Assert.Equal(0, b.Active);
    }

    /// <summary>三十二輪並行 A/B 查詢先 await 再 Dispose，逐輪確認活躍資源歸零且 handler 恰關閉一次。</summary>
    /// <returns>六十四筆查詢及各輪 owner 皆已完成清理。</returns>
    [Fact]
    public async Task ConcurrentLeaseCycles_DrainOwnedResourcesToBaseline()
    {
        for (var cycle = 0; cycle < 32; cycle++)
        {
            var a = new ProbeHandler(); var b = new ProbeHandler();
            using (var first = new LineMessagingClientLease("synthetic-A", a))
            using (var second = new LineMessagingClientLease("synthetic-B", b))
            {
                await Task.WhenAll(first.Client.GetBotInfoAsync(), second.Client.GetBotInfoAsync());
                Assert.Equal(new[] { "synthetic-A" }, a.Tokens);
                Assert.Equal(new[] { "synthetic-B" }, b.Tokens);
                Assert.Equal(0, a.Active); Assert.Equal(0, b.Active);
            }
            Assert.Equal(1, a.Disposals); Assert.Equal(1, b.Disposals);
            Assert.Equal(0, a.ActiveAtDispose); Assert.Equal(0, b.ActiveAtDispose);
            Assert.Equal(1, a.LastContent!.Disposals); Assert.Equal(1, b.LastContent!.Disposals);
        }
    }

    /// <summary>handler 關閉時 SDK 已清掉 credential；反轉處置次序將改由 HttpClient 拒絕並使斷言失敗。</summary>
    /// <returns>操作先完成，再驗證 credential 清除與重複 Dispose 安全。</returns>
    [Fact]
    public async Task Dispose_ClearsCredentialBeforeTransportExactlyOnce()
    {
        var handler = new ProbeHandler();
        using var lease = new LineMessagingClientLease("synthetic-A", handler);
        var borrowed = lease.Client;
        await borrowed.GetBotInfoAsync();
        handler.BeforeDispose = () => AssertCredentialAlreadyCleared(borrowed);
        lease.Dispose(); lease.Dispose();
        Assert.Equal(1, handler.Disposals);
        Assert.Equal(0, handler.ActiveAtDispose);
        Assert.Single(handler.Tokens);
    }

    /// <summary>已釋放的 getter 與舊借用 SDK 都拒絕新操作，handler 不再收到任何請求。</summary>
    /// <returns>拒絕 task 已被 await 觀察，不留下未觀察例外。</returns>
    [Fact]
    public async Task DisposedLease_RejectsGetterAndPreviouslyBorrowedClient()
    {
        var handler = new ProbeHandler();
        using var lease = new LineMessagingClientLease("synthetic-A", handler);
        var borrowed = lease.Client;
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lease.Client);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => borrowed.GetBotInfoAsync());
        Assert.Empty(handler.Tokens); Assert.Equal(1, handler.Disposals);
    }

    /// <summary>非法 token 在接受依賴前拒絕，不能關閉 caller 的 handler，也不能在錯誤中回顯秘密。</summary>
    /// <param name="token">故障注入用合成非法 token。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("synthetic has-space")]
    [InlineData("synthetic\r\nInjected: value")]
    [InlineData("synthetic\tvalue")]
    [InlineData("synthetic\0value")]
    [InlineData("合成秘密")]
    public void InvalidToken_LeavesUnacceptedHandlerWithCaller(string? token)
    {
        var handler = new ProbeHandler();
        try
        {
            var error = Assert.Throws<ArgumentException>(() => new LineMessagingClientLease(token!, handler));
            Assert.Equal(0, handler.Disposals); Assert.Empty(handler.Tokens);
            if (!string.IsNullOrWhiteSpace(token)) Assert.DoesNotContain(token, error.ToString());
            Assert.Throws<ArgumentException>(() => new LineMessagingClientLease(token!));
        }
        finally { handler.Dispose(); }
        Assert.Equal(1, handler.Disposals);
    }

    /// <summary>空 handler 不屬於可接受的相依，必須在建立傳輸前拒絕。</summary>
    [Fact]
    public void NullHandler_IsRejectedBeforeOwnershipTransfer() =>
        Assert.Throws<ArgumentNullException>(() => new LineMessagingClientLease("synthetic-A", null!));

    /// <summary>接管後的期限設定故障須回收 client／handler；無限或超過正式期限亦不能放行。</summary>
    /// <param name="milliseconds">合成無效期限，故意在接管後的設定步驟觸發失敗。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(15001)]
    public void ConstructorConfigurationFault_DisposesAcceptedHandler(int milliseconds)
    {
        var handler = new ProbeHandler();
        Assert.Throws<ArgumentOutOfRangeException>(() => new LineMessagingClientLease(
            "synthetic-A", handler, TimeSpan.FromMilliseconds(milliseconds)));
        Assert.Equal(1, handler.Disposals); Assert.Equal(0, handler.ActiveAtDispose);
        Assert.Empty(handler.Tokens);
    }

    /// <summary>接管後的期限錯誤與 handler 清理錯誤同時發生時，必須保留兩個真正例外，且只清理一次。</summary>
    /// <remarks>注入無效期限及 Dispose 故障；Aggregate 第一項須是原期限錯誤，第二項為清理錯誤，不能將原因遮掉。</remarks>
    [Fact]
    public void ConstructorAndCleanupFault_PreservesBothErrorsAndDisposesOnce()
    {
        var handler = new ProbeHandler { ThrowOnDispose = true };
        var error = Assert.Throws<AggregateException>(() => new LineMessagingClientLease(
            "synthetic-A", handler, TimeSpan.FromMilliseconds(-1)));
        Assert.Collection(error.InnerExceptions,
            original => Assert.IsType<ArgumentOutOfRangeException>(original),
            cleanup => Assert.Equal("Synthetic handler disposal failure.", Assert.IsType<InvalidOperationException>(cleanup).Message));
        Assert.Equal(1, handler.Disposals);
        Assert.Equal(0, handler.ActiveAtDispose);
        Assert.Empty(handler.Tokens);
    }

    /// <summary>短期限必須傳入 handler；取消後 await 返回時活躍 request 已歸零，不遺棄 I/O。</summary>
    /// <returns>取消與 handler 清理皆完成的故障操作。</returns>
    [Fact]
    public async Task Timeout_CancelsAndDrainsRequestBeforeDisposal()
    {
        var handler = new ProbeHandler { WaitForCancellation = true };
        using (var lease = new LineMessagingClientLease("synthetic-A", handler, TimeSpan.FromMilliseconds(100)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease.Client.GetBotInfoAsync());
            Assert.Equal(1, handler.Cancellations); Assert.Equal(0, handler.Active);
            Assert.Equal(0, handler.Disposals); Assert.Single(handler.Tokens);
        }
        Assert.Equal(1, handler.Disposals); Assert.Equal(0, handler.ActiveAtDispose);
    }

    /// <summary>HTTP 拒絕仍釋放 response 並排空，例外不得包含遠端本文或 credential。</summary>
    /// <param name="status">注入的非成功狀態。</param>
    /// <returns>已觀察真實 SDK 例外且完成清理的操作。</returns>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task HttpFailure_DrainsAndDisposesResponse(HttpStatusCode status)
    {
        var handler = new ProbeHandler { Status = status, Body = "{\"message\":\"synthetic-private-body\"}" };
        using (var lease = new LineMessagingClientLease("synthetic-A", handler))
        {
            var error = await Record.ExceptionAsync(() => lease.Client.GetBotInfoAsync());
            Assert.NotNull(error);
            Assert.DoesNotContain("synthetic-private-body", error.ToString());
            Assert.DoesNotContain("synthetic-A", error.ToString());
            Assert.Equal(0, handler.Active); Assert.Equal(1, handler.LastContent!.Disposals);
        }
        Assert.Equal(1, handler.Disposals); Assert.Equal(0, handler.ActiveAtDispose);
    }

    /// <summary>response 產生前的傳輸故障也必須先 await 原始例外，才能安全結束 owner。</summary>
    /// <returns>沒有存活操作及遺留 handler 的故障路徑。</returns>
    [Fact]
    public async Task TransportFailure_DrainsBeforeOwnerDisposal()
    {
        var handler = new ProbeHandler { FailSend = true };
        using (var lease = new LineMessagingClientLease("synthetic-A", handler))
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => lease.Client.GetBotInfoAsync());
            Assert.Equal(0, handler.Active); Assert.Null(handler.LastContent);
        }
        Assert.Equal(1, handler.Disposals); Assert.Equal(0, handler.ActiveAtDispose);
    }

    /// <summary>256KiB 的合法 JSON 成功，超一 byte 在緩衝層失敗，避免靠解析錯誤假通過容量測試。</summary>
    /// <param name="bytes">確切的 ASCII 回應長度。</param>
    /// <param name="oversize">是否必須觸發容量拒絕。</param>
    /// <returns>回應內容已關閉且操作已排空。</returns>
    [Theory]
    [InlineData(262144, false)]
    [InlineData(262145, true)]
    public async Task JsonBuffer_EnforcesExactCapacity(int bytes, bool oversize)
    {
        var handler = new ProbeHandler { Body = "{\"userId\":\"" + new string('x', bytes - 13) + "\"}" };
        using (var lease = new LineMessagingClientLease("synthetic-A", handler))
        {
            if (oversize) await Assert.ThrowsAsync<HttpRequestException>(() => lease.Client.GetBotInfoAsync());
            else Assert.Equal(bytes - 13, (await lease.Client.GetBotInfoAsync()).UserId.Length);
            Assert.Equal(0, handler.Active); Assert.Equal(1, handler.LastContent!.Disposals);
        }
        Assert.Equal(1, handler.Disposals);
    }

    /// <summary>handler 的清理故障不得重開 lease；認證仍失效，再 Dispose 不能重複處置或再次拋錯。</summary>
    [Fact]
    public void HandlerDisposeFailure_LeavesLeaseClosedAndCleanupIdempotent()
    {
        var handler = new ProbeHandler { ThrowOnDispose = true };
        using var lease = new LineMessagingClientLease("synthetic-A", handler);
        var borrowed = lease.Client;
        handler.BeforeDispose = () => AssertCredentialAlreadyCleared(borrowed);
        Assert.Throws<InvalidOperationException>(() => lease.Dispose());
        Assert.Throws<ObjectDisposedException>(() => lease.Client);
        AssertCredentialAlreadyCleared(borrowed);
        lease.Dispose(); Assert.Equal(1, handler.Disposals);
    }

    /// <summary>沿繼承層讀取唯一指定型別資源，避免依賴 .NET 私有 handler 欄位名稱。</summary>
    /// <typeparam name="T">要觀察的資源型別。</typeparam>
    /// <param name="owner">案例擁有的實際 owner。</param>
    /// <returns>唯一符合型別的資源；缺少或重複即失敗。</returns>
    private static T ReadOwnedField<T>(object owner) where T : class
    {
        var candidates = new List<T>();
        for (var type = owner.GetType(); type != null; type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                if (typeof(T).IsAssignableFrom(field.FieldType) && field.GetValue(owner) is T resource) candidates.Add(resource);
        return Assert.Single(candidates);
    }

    /// <summary>請求須在 SDK 清掉 credential 的邊界立即拒絕；消費已完成 task 的例外，不遺棄工作。</summary>
    /// <param name="borrowed">已排空且正由 lease 清理或已清理的 SDK。</param>
    private static void AssertCredentialAlreadyCleared(ILineMessagingClient borrowed)
    {
        var rejected = borrowed.GetBotInfoAsync();
        Assert.True(rejected.IsCompleted); Assert.True(rejected.IsFaulted);
        var error = Assert.IsType<ObjectDisposedException>(Assert.Single(rejected.Exception!.InnerExceptions));
        Assert.Equal("ChannelHttpTransport", error.ObjectName);
    }

    /// <summary>無 socket 合成傳輸；每案最多三筆合成標頭、一份有界內容，lease 接受後獨占 Dispose。</summary>
    private sealed class ProbeHandler : HttpMessageHandler
    {
        private int active;
        /// <summary>有界的合成 bearer 觀察，不保存 request 或真實秘密。</summary>
        internal ConcurrentQueue<string?> Tokens { get; } = new();
        /// <summary>合成標頭認證種類。</summary>
        internal ConcurrentQueue<string?> Schemes { get; } = new();
        /// <summary>合成請求端點，用以驗證無任意 URI。</summary>
        internal ConcurrentQueue<string?> Uris { get; } = new();
        /// <summary>固定合成內容，最多容量邊界加一 byte。</summary>
        internal string Body { get; init; } = "{}";
        /// <summary>遠端拒絕故障注入點。</summary>
        internal HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        /// <summary>只等待真正傳入的取消 token，不另建 timer 或背景工作。</summary>
        internal bool WaitForCancellation { get; init; }
        /// <summary>建立 response 前的傳輸故障注入。</summary>
        internal bool FailSend { get; init; }
        /// <summary>基底清理完成後的 Dispose 故障注入。</summary>
        internal bool ThrowOnDispose { get; init; }
        /// <summary>短命同步清理觀察器，執行後即清除參照。</summary>
        internal Action? BeforeDispose { get; set; }
        /// <summary>操作返回之前必須由 finally 歸零的活躍計數。</summary>
        internal int Active => Volatile.Read(ref active);
        /// <summary>清理時的活躍數，區分正常排空與提前中止。</summary>
        internal int ActiveAtDispose { get; private set; }
        /// <summary>真正 Dispose 次數，不用旗標掩蓋產品重複處置。</summary>
        internal int Disposals { get; private set; }
        /// <summary>真正觀察到傳輸取消的次數。</summary>
        internal int Cancellations { get; private set; }
        /// <summary>唯一有界內容觀察參照，供 SDK 清理斷言。</summary>
        internal ProbeContent? LastContent { get; private set; }

        /// <summary>在真實 SDK 邊界注入合成結果；成功、故障及取消均在回傳前排空。</summary>
        /// <param name="request">由 SDK 擁有的借用請求，不保存此參照。</param>
        /// <param name="cancellationToken">HttpClient 擁有的有限期限，取消延遲隨 await 一起結束。</param>
        /// <returns>唯一交給 SDK 擁有的合成 response。</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref active);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Tokens.Enqueue(request.Headers.Authorization?.Parameter);
                Schemes.Enqueue(request.Headers.Authorization?.Scheme);
                Uris.Enqueue(request.RequestUri?.AbsoluteUri);
                if (WaitForCancellation)
                {
                    try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                    catch (OperationCanceledException) { Cancellations++; throw; }
                }
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                if (FailSend) throw new HttpRequestException("Synthetic transport failure.");
                LastContent = new ProbeContent(Body);
                return new HttpResponseMessage(Status) { Content = LastContent };
            }
            finally { Interlocked.Decrement(ref active); }
        }

        /// <summary>先觀察 SDK 清理順序，再確定性處置基底；即使觀察失敗也清除回呼參照。</summary>
        /// <param name="disposing">明確 Dispose 才記錄次數與注入合成故障。</param>
        protected override void Dispose(bool disposing)
        {
            if (!disposing) { base.Dispose(false); return; }
            Disposals++; ActiveAtDispose = Active;
            try { BeforeDispose?.Invoke(); }
            finally { BeforeDispose = null; base.Dispose(true); }
            if (ThrowOnDispose) throw new InvalidOperationException("Synthetic handler disposal failure.");
        }
    }

    /// <summary>有界合成內容，觀察 SDK 在成功、HTTP 拒絕與超量時是否確定性處置 response。</summary>
    private sealed class ProbeContent : StringContent
    {
        /// <summary>建立案例限定的 UTF-8 內容，不包含真正秘密或外部資源。</summary>
        /// <param name="body">最大 256KiB 加一 byte 的 ASCII JSON。</param>
        internal ProbeContent(string body) : base(body) { }
        /// <summary>真正內容 Dispose 次數。</summary>
        internal int Disposals { get; private set; }
        /// <summary>先記錄再委派基底，釋放內容持有的緩衝與串流。</summary>
        /// <param name="disposing">明確清理 managed 資源時增加計數。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }
}
#endif
