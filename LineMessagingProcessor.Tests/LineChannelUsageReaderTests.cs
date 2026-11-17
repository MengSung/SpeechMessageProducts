/*
檔案整體說明
目的與定位：共用LINE用量reader的net10離線傳輸契約，用合成handler驗固定端點及每請求認證隔離。
主要流程：A/B/A三次查詢讀真正parser；故障注入重複JSON鍵、負值、缺值、超量body及非成功狀態。
資源與隔離：每案reader獨占handler，Dispose次數有斷言；合成token不是秘密，不連外網，沒有背景工作。
決定性斷言：解析值、三個Authorization相互獨立、固定URI、取消零送出與handler恰釋放一次；
失敗不能偽裝成0或把原body傳到輸出，測試不代表測試Channel實機用量。
*/
#if NET10_0_OR_GREATER
using System.Net;
using System.Net.Http;
using LineMessagingProcessor.Core;
using Xunit;
namespace LineMessagingProcessor.Tests;

/// <summary>以HTTP合成邊界驗產品的真正有界parser，不模擬產品輸出。</summary>
public sealed class LineChannelUsageReaderTests
{
    /// <summary>每次token只進自身request，reader釋放自己handler，不留下認證跨Channel。</summary>
    [Fact]
    public void Usage_AbaIsolation_AndOwnerDisposes()
    {
        var handler = new Handler("{\"totalUsage\":7}");
        using (var reader = new LineChannelUsageReader(handler))
        { Assert.Equal(7, reader.Read("synthetic-A")); Assert.Equal(7, reader.Read("synthetic-B")); Assert.Equal(7, reader.Read("synthetic-A")); }
        Assert.Equal(new[] { "synthetic-A", "synthetic-B", "synthetic-A" }, handler.Tokens);
        Assert.Equal(1, handler.Disposals);
    }
    /// <summary>未知、不完整或超量回應明確失敗，不能當成用量零。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"totalUsage\":-1}")]
    [InlineData("{\"totalUsage\":1,\"totalUsage\":2}")]
    [InlineData("{\"totalUsage\":\"1\"}")]
    public void Usage_InvalidBody_IsFailure(string body)
    {
        using var reader = new LineChannelUsageReader(new Handler(body));
        Assert.ThrowsAny<Exception>(() => reader.Read("synthetic-A"));
    }
    /// <summary>硬上限及HTTP失敗在發布數字前拒絕，避免無界記憶體或假綠。</summary>
    [Fact]
    public void Usage_OversizeAndHttpFailure_Refused()
    {
        using var large = new LineChannelUsageReader(new Handler(new string('x', 4097)));
        Assert.Throws<InvalidDataException>(() => large.Read("synthetic-A"));
        using var denied = new LineChannelUsageReader(new Handler("{}") { Status = HttpStatusCode.Forbidden });
        Assert.Throws<InvalidDataException>(() => denied.Read("synthetic-A"));
    }
    /// <summary>caller已取消在送出前終止，不啟動不可回收背景I/O。</summary>
    [Fact]
    public void Usage_CancelledCaller_NoSend()
    {
        var handler = new Handler("{\"totalUsage\":7}"); using var reader = new LineChannelUsageReader(handler);
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => reader.Read("synthetic-A", source.Token));
        Assert.Empty(handler.Tokens);
    }
    /// <summary>只回傳固定記憶體body，驗URI、Authorization，沒有真正網路。</summary>
    private sealed class Handler(string body) : HttpMessageHandler
    {
        internal List<string> Tokens { get; } = new();
        internal HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        internal int Disposals { get; private set; }
        /// <summary>同步傳輸對應產品reader，所有token只記合成值，不保留request。</summary>
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("https://api.line.me/v2/bot/message/quota/consumption", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Tokens.Add(request.Headers.Authorization.Parameter!);
            return new(Status) { Content = new StringContent(body) };
        }
        /// <summary>非本入口async能力明確拒絕，不建立background task。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>產品reader唯一擁有handler的釋放。</summary>
        protected override void Dispose(bool disposing) { if (disposing) Disposals++; base.Dispose(disposing); }
    }
}
#endif
