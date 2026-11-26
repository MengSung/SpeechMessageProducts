/*
檔案整體說明
目的與定位：F6共用filter的離線驗證，保護Bearer唯一性、401／403分類、請求Items清理、零Session與Cookie，以及真實服務故障告警。
主要功能與流程：真實verifier使用單案合成HTTP handler，actor resolver回當案不可變身分；以不可存取Session探針、next故障及caller取消注入，核對端點僅在驗證成功後執行，Items於成功及失敗均回空。
使用邊界與維護：所有token、姓名與sub都是合成值，網路完全由handler替代；每案唯一擁有HttpClient、diagnostics及暫存目錄，先Dispose再清目錄。測試不把合成transport稱為LINE實機，不建立背景工作或共用快取；診斷只檢查安全型別及固定operation，原訊息不得洩漏。
*/
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Moq;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Liff.AspNetCore.Tests;

/// <summary>測試真實filter的成功、拒絕與清理邊界，不呼叫外部網路或讀User Secrets。</summary>
public sealed class LiffAuthenticationFilterTests
{
    /// <summary>身分只在next內可見，之後移除；任何Session存取都會由探針立即失敗，沒有Set-Cookie。</summary>
    [Fact]
    public async Task Filter_DoesNotWriteSessionOrCookie()
    {
        await using var f = new Fixture();
        f.Context.Features.Set<ISessionFeature>(new ForbiddenSession());
        var invoked = false;
        await f.Filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(f.Context), context =>
        {
            invoked = true;
            Assert.Same(f.Actor, context.HttpContext.Items["LiffActor"]);
            return ValueTask.FromResult<object?>(Results.Ok());
        });
        Assert.True(invoked);
        Assert.False(f.Context.Items.ContainsKey("LiffActor"));
        Assert.False(f.Context.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Equal(1, f.Resolutions);
    }

    /// <summary>缺少、相似scheme、空白及多值標頭全部在resolver前拒絕，既有未驗actor亦被清除。</summary>
    /// <param name="header">合成Authorization值；null代表完全沒有標頭。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic synthetic")]
    [InlineData("Bearer ")]
    [InlineData("Bearer a b")]
    [InlineData("Bearer synthetic, Bearer other")]
    public async Task Filter_InvalidBearer_Returns401AndDoesNotResolve(string? header)
    {
        await using var f = new Fixture();
        f.Context.Request.Headers.Remove("Authorization");
        if (header is not null) f.Context.Request.Headers.Authorization = header;
        f.Context.Items["LiffActor"] = f.Actor;
        var result = await f.Filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(f.Context),
            _ => throw new InvalidOperationException("Endpoint must not execute."));
        Assert.Equal(401, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(0, f.Resolutions);
        Assert.False(f.Context.Items.ContainsKey("LiffActor"));
    }

    /// <summary>多個實體標頭不能被合併解讀為任一Bearer，零外部驗證與actor解析。</summary>
    [Fact]
    public async Task Filter_MultipleHeaders_Returns401()
    {
        await using var f = new Fixture();
        f.Context.Request.Headers.Authorization = new StringValues(["Bearer a", "Bearer b"]);
        var result = await f.Filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(f.Context),
            _ => throw new InvalidOperationException());
        Assert.Equal(401, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(0, f.HandlerCalls);
    }

    /// <summary>token有效但沒有有效租戶／目前選擇時403，不能降級採用Items裡的前一個actor。</summary>
    [Fact]
    public async Task Filter_NoActor_Returns403()
    {
        await using var f = new Fixture();
        f.NoActor = true;
        var result = await f.Filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(f.Context),
            _ => throw new InvalidOperationException());
        Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(1, f.Resolutions);
        Assert.False(f.Context.Items.ContainsKey("LiffActor"));
    }

    /// <summary>端點本身故障仍保持原例外交Host，filter不能留下actor供後續callback重用。</summary>
    [Fact]
    public async Task Filter_EndpointThrows_PreservesFailureAndClearsActor()
    {
        await using var f = new Fixture();
        var failure = new IOException("synthetic-endpoint");
        var actual = await Assert.ThrowsAsync<IOException>(async () =>
            await f.Filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(f.Context), _ => throw failure));
        Assert.Same(failure, actual);
        Assert.False(f.Context.Items.ContainsKey("LiffActor"));
    }

    /// <summary>服務500需記安全真實故障且503，不回供應商內容；token拒絕400另為401且不建立告警檔。</summary>
    /// <param name="status">合成官方驗證端點狀態。</param>
    /// <param name="expected">預期HTTP回應。</param>
    /// <param name="logExpected">是否屬服務事故而必須落檔。</param>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, 503, true)]
    [InlineData(HttpStatusCode.BadRequest, 401, false)]
    public async Task Filter_ProviderFailure_ReportsOnlyServiceFault(HttpStatusCode status, int expected, bool logExpected)
    {
        await using var f = new Fixture(status);
        var result = await f.Filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(f.Context),
            _ => throw new InvalidOperationException());
        Assert.Equal(expected, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var logPath = Path.Combine(f.Directory, "Exception.log");
        Assert.Equal(logExpected, File.Exists(logPath));
        if (logExpected)
        {
            var text = await File.ReadAllTextAsync(logPath);
            Assert.Contains("Liff.Authentication.Verify", text, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-sensitive", text, StringComparison.Ordinal);
        }
    }

    /// <summary>caller已取消時不啟動HTTP、不當服務事故，Items仍移除。</summary>
    [Fact]
    public async Task Filter_CallerCancelled_DoesNotSendOrRetain()
    {
        await using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        f.Context.RequestAborted = cancellation.Token;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await f.Filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(f.Context), _ => throw new InvalidOperationException()));
        Assert.Equal(0, f.HandlerCalls);
        Assert.False(f.Context.Items.ContainsKey("LiffActor"));
    }

    /// <summary>每案的DI依賴owner，清理client及diagnostics後才刪本案目錄。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        /// <summary>只含合成日誌的唯一暫存目錄。</summary>
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "3p-f6-filter-" + Guid.NewGuid().ToString("N"));
        /// <summary>單案請求，從未跨案共享。</summary>
        internal DefaultHttpContext Context { get; } = new();
        /// <summary>當案權威身分快照。</summary>
        internal LiffActor Actor { get; } = new(Guid.NewGuid(), Guid.NewGuid(), Array.Empty<string>());
        /// <summary>真實產品filter。</summary>
        internal LiffAuthenticationFilter Filter { get; }
        /// <summary>控制缺少actor的業務拒絕。</summary>
        internal bool NoActor { get; set; }
        /// <summary>當案resolver次數。</summary>
        internal int Resolutions { get; private set; }
        /// <summary>當案HTTP呼叫次數。</summary>
        internal int HandlerCalls { get; private set; }
        /// <summary>當案唯一client owner，不含真實token。</summary>
        private readonly HttpClient client;
        /// <summary>當案安全診斷owner，未綁定任何程序全域入口。</summary>
        private readonly ExceptionDiagnostics diagnostics;

        /// <summary>借入合成handler與受控clock，所有真實服務均以可觀察離線依賴替代。</summary>
        internal Fixture(HttpStatusCode status = HttpStatusCode.OK)
        {
            System.IO.Directory.CreateDirectory(Directory);
            client = new HttpClient(new Handler(() =>
            {
                HandlerCalls++;
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(
                        """{"aud":"2012805181","sub":"synthetic-person","exp":1893456000,"name":"synthetic-sensitive"}""",
                        Encoding.UTF8, "application/json")
                };
            }));
            var clock = new Mock<IChurchClock>(MockBehavior.Strict);
            clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero));
            var resolver = new Mock<ILiffActorResolver>(MockBehavior.Strict);
            resolver.Setup(r => r.ResolveAsync(It.IsAny<LiffIdentity>(), It.IsAny<CancellationToken>()))
                .Returns(() => { Resolutions++; return Task.FromResult(NoActor ? null : Actor); });
            diagnostics = new ExceptionDiagnostics(Directory, outputOptions: new ExceptionOutputOptions(true, false));
            Filter = new LiffAuthenticationFilter(new LiffIdTokenVerifier(client, clock.Object),
                resolver.Object, new LiffOptions("2012805181", "2012804957", ""), diagnostics);
            Context.Request.Headers.Authorization = "Bearer synthetic-sensitive";
        }

        /// <summary>完整釋放當案資源，無延後清理或無界重試。</summary>
        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await diagnostics.DisposeAsync();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    /// <summary>借入單案函式的合成transport，只在SendAsync建回應；client唯一擁有handler。</summary>
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        /// <summary>完全無網路的回應來源，保留caller正常取消。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response());
        }
    }

    /// <summary>任何Session存取皆拋錯，避免測試只看回應卻漏掉filter已經讀寫Session。</summary>
    private sealed class ForbiddenSession : ISessionFeature
    {
        /// <summary>拒絕讀寫Session，測試只能在完全不碰Session時通過。</summary>
        public ISession Session { get => throw new InvalidOperationException("Session access is forbidden."); set => throw new InvalidOperationException("Session access is forbidden."); }
    }
}
