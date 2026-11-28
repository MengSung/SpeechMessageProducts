/*
檔案整體說明
目的與定位：F11 主機 HTTP 批次觀測的離線生命週期測試，保護 F5 真傳輸到 response 清理的完整計數期間，不連 LINE 或任何正式服務。
主要功能與流程：真 HttpClient 接受合成 transport，故意延遲回應、保留 response、注入傳輸故障及 caller 取消；斷言在途為一、response content 仍存活時不得歸零、每項資源只清理一次。
使用邊界與維護：每案唯一擁有 Meter、client、request、response 與 barrier；finally 解除等待並 await 全部傳輸後才關 Meter。合成 body 無 userId／token；測試證明觀測所有權，不能代表遠端平台接受或手機送達。
*/
using System.Net;
using SpeechMessage.Hosting.Observability;
using SpeechMessageProducts.GoodShepherd2.Web.Observability;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Health;

/// <summary>透過真 HttpClient 的傳輸與回應釋放，保護單批票據的確定歸還。</summary>
public sealed class PushBatchObservationTests
{
    /// <summary>傳輸開始、回應到達及內容讀完都仍為一；唯一 response Dispose 後才歸零，重入不重扣。</summary>
    [Fact]
    public async Task Batch_RemainsInflightUntilResponseContentDisposed()
    {
        using var meters = new HostingMeters();
        using var transport = new SyntheticTransport();
        using var client = new HttpClient(new PushBatchTrackingHandler(meters, transport));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push");
        var pending = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        HttpResponseMessage? response = null;
        try
        {
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, meters.GetSnapshot().PushBatchesInflight);
            transport.Release.TrySetResult();
            response = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, meters.GetSnapshot().PushBatchesInflight);
            Assert.Equal("{}", await response.Content.ReadAsStringAsync());
            Assert.Equal(1, meters.GetSnapshot().PushBatchesInflight);
            Assert.Equal(0, transport.Content.Disposals);
            response.Dispose();
            response.Dispose();
            Assert.Equal(1, transport.Content.Disposals);
            Assert.Equal(0, meters.GetSnapshot().PushBatchesInflight);
        }
        finally
        {
            transport.Release.TrySetResult();
            response ??= await pending.WaitAsync(TimeSpan.FromSeconds(5));
            response.Dispose();
        }
    }

    /// <summary>傳輸故障或 caller 取消沒有 response 可接手；handler 必須保留原例外並自行歸還票據。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_TransportFailureOrCancellation_ReturnsToBaseline(bool cancel)
    {
        using var meters = new HostingMeters();
        using var transport = new SyntheticTransport { Fail = !cancel };
        using var client = new HttpClient(new PushBatchTrackingHandler(meters, transport));
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/multicast");
        var pending = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        try
        {
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, meters.GetSnapshot().PushBatchesInflight);
        }
        finally
        {
            if (cancel) cancellation.Cancel();
            transport.Release.TrySetResult();
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            else Assert.Same(transport.Failure, await Assert.ThrowsAsync<HttpRequestException>(() => pending));
        }
        Assert.Equal(0, meters.GetSnapshot().PushBatchesInflight);
    }

    /// <summary>單案持有的有界傳輸替身，barrier 由案例 finally 解除；不建立背景 worker 或網路。</summary>
    private sealed class SyntheticTransport : HttpMessageHandler
    {
        private int closed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HttpRequestException Failure { get; } = new("synthetic-transport-failure");
        internal CountingContent Content { get; } = new();
        internal bool Fail { get; init; }

        /// <summary>只借當次 request 與 caller token；沒有 response 的故障不得留下 Meter 票據。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw Failure;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = Content };
        }

        /// <summary>沒有 response 移交的故障仍由測試 transport 清理 content；已移交的 body 不重複關閉。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref closed, 1) == 0 && Content.Disposals == 0) Content.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>可重入清理的合成 response body，驗真正內層資源只被 wrapper 釋放一次。</summary>
    private sealed class CountingContent : ByteArrayContent
    {
        internal int Disposals { get; private set; }
        internal CountingContent() : base("{}"u8.ToArray()) { }
        /// <summary>只有第一次實際清理計數；基底負責釋放合成 body 及已開啟的讀取 stream。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }
}
