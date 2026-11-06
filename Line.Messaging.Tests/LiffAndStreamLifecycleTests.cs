/*
 * 檔案整體說明
 *
 * 目的與定位：保護同 SDK 的 LIFF 認證隔離與串流生命週期，不讓主 Messaging client 的修正留下另一個共用 transport 漏洞。 故障全部在記憶體 handler 注入；檢查實際 request
 * credential、內容釋放及非同步讀取委派，不連 LINE。
 *
 * 主要功能與流程：
 * 本檔宣告 LiffAndStreamLifecycleTests、LiffHandler、LiffContent、AsyncOnlyStream。
 * - 兩個 LIFF channel 共用 transport 交錯一千次；每筆 URI 的合成範圍須與 header 相符，排空後不保留預設 credential。
 * - LIFF 四種入口在成功及遠端故障都必須檢查 status 並釋放 response；JSON request 也須在完成後釋放。
 * - LIFF Dispose 不關閉借入 transport，但必須拒絕之後的每筆 API，且重複 Dispose 冪等。
 * - 下載 wrapper 必須直接呼叫底層非同步讀取並傳同 token，不能退回 Stream 基底的同步讀取加 ThreadPool 包裝。
 * 測試案例入口包含 Liff_SharedTransport_ThousandRequestsRemainIsolated、
 * Liff_EveryEndpoint_DisposesResponseAndReportsFailure、Liff_Dispose_StopsSendingWithoutDisposingBorrowedTransport、
 * ContentStream_ReadAsync_ForwardsUnderlyingOperationAndCancellation、LiffHandler、SendAsync、LiffContent、
 * SerializeToStreamAsync；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging.Liff;
using Xunit;

namespace Line.Messaging.Tests;

/// <summary>
/// 保護同 SDK 的 LIFF 認證隔離與串流生命週期，不讓主 Messaging client 的修正留下另一個共用 transport 漏洞。
/// 故障全部在記憶體 handler 注入；檢查實際 request credential、內容釋放及非同步讀取委派，不連 LINE。
/// </summary>
public sealed class LiffAndStreamLifecycleTests
{
    /// <summary>兩個 LIFF channel 共用 transport 交錯一千次；每筆 URI 的合成範圍須與 header 相符，排空後不保留預設 credential。</summary>
    /// <returns>完成全部有界交錯操作的工作。</returns>
    [Fact]
    public async Task Liff_SharedTransport_ThousandRequestsRemainIsolated()
    {
        using var handler = new LiffHandler();
        using var transport = new HttpClient(handler);
        using var first = new LiffClient(transport, "synthetic-A", "https://example.invalid/A");
        using var second = new LiffClient(transport, "synthetic-B", "https://example.invalid/B");
        var tasks = new Task[1000];
        for (var index = 0; index < tasks.Length; index++)
            tasks[index] = (index % 2 == 0 ? first : second).GetAllLiffAppAsync();
        await Task.WhenAll(tasks);
        Assert.Equal(1000, handler.Calls);
        Assert.Equal(0, handler.Mismatches);
        Assert.Equal(1000, handler.DisposedContents);
        Assert.Null(transport.DefaultRequestHeaders.Authorization);
        Assert.True(handler.Content!.Disposed);
    }

    /// <summary>LIFF 四種入口在成功及遠端故障都必須檢查 status 並釋放 response；JSON request 也須在完成後釋放。</summary>
    /// <param name="operation">新增、修改、查詢或刪除。</param><param name="fail">是否注入 HTTP 失敗。</param>
    /// <returns>完成且可斷言所有權的操作。</returns>
    [Theory]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("update", false)]
    [InlineData("update", true)]
    [InlineData("get", false)]
    [InlineData("get", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    public async Task Liff_EveryEndpoint_DisposesResponseAndReportsFailure(string operation, bool fail)
    {
        using var handler = new LiffHandler(fail);
        using var transport = new HttpClient(handler);
        using var client = new LiffClient(transport, "synthetic-A", "https://example.invalid/A");
        var exception = await Record.ExceptionAsync(() => operation switch
        {
            "add" => client.AddLiffAppAsync(ViewType.Full, "https://example.invalid/app"),
            "update" => client.UpdateLiffAppAsync("synthetic", ViewType.Full, "https://example.invalid/app"),
            "get" => client.GetAllLiffAppAsync(),
            _ => client.DeleteLiffAppAsync("synthetic")
        });
        if (fail)
        {
            var responseException = Assert.IsType<LineResponseException>(exception);
            Assert.DoesNotContain("sensitive-synthetic-detail", responseException.ToString());
        }
        else
        {
            Assert.Null(exception);
        }
        Assert.True(handler.Content!.Disposed);
        Assert.Equal(1, handler.DisposedContents);
        if (handler.Request!.Content != null)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request.Content.ReadAsStringAsync());
        if (operation == "add")
            Assert.Equal("{\"view\":{\"type\":\"full\",\"url\":\"https://example.invalid/app\"}}", handler.LastBody);
        if (operation == "update")
            Assert.Equal("{\"type\":\"full\",\"url\":\"https://example.invalid/app\"}", handler.LastBody);
    }

    /// <summary>LIFF Dispose 不關閉借入 transport，但必須拒絕之後的每筆 API，且重複 Dispose 冪等。</summary>
    /// <returns>已停止 SDK 且仍能使用外部 transport 的工作。</returns>
    [Fact]
    public async Task Liff_Dispose_StopsSendingWithoutDisposingBorrowedTransport()
    {
        using var handler = new LiffHandler();
        using var transport = new HttpClient(handler);
        var client = new LiffClient(transport, "synthetic-A", "https://example.invalid/A");
        client.Dispose();
        client.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAllLiffAppAsync());
        Assert.Equal(0, handler.Calls);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/A");
        using var response = await transport.SendAsync(request);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>下載 wrapper 必須直接呼叫底層非同步讀取並傳同 token，不能退回 Stream 基底的同步讀取加 ThreadPool 包裝。</summary>
    /// <returns>已完成且沒有被遺棄同步 I/O 的讀取。</returns>
    [Fact]
    public async Task ContentStream_ReadAsync_ForwardsUnderlyingOperationAndCancellation()
    {
        using var source = new AsyncOnlyStream();
        using var wrapper = new ContentStream(source, null!);
        using var cancellation = new CancellationTokenSource();
        var buffer = new byte[8];
        Assert.Equal(1, await wrapper.ReadAsync(buffer, 0, buffer.Length, cancellation.Token));
        Assert.Equal(cancellation.Token, source.Token);
        Assert.Equal(1, source.AsyncReads);
        Assert.Equal(42, buffer[0]);
    }

    /// <summary>有界 handler 每次只保留最後一筆合成 request/content；外部 using 擁有 transport，沒有網路、背景工作或認證快取。</summary>
    private sealed class LiffHandler(bool fail = false) : HttpMessageHandler
    {
        internal int Calls;
        internal int Mismatches;
        internal int DisposedContents;
        internal HttpRequestMessage? Request;
        internal LiffContent? Content;
        internal string LastBody = string.Empty;

        /// <summary>依合成 URI 檢查 channel，回傳可觀察 Dispose 的短 JSON；不輸出、記錄或保存 token。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Calls);
            var expected = request.RequestUri!.AbsolutePath.StartsWith("/A", StringComparison.Ordinal) ? "synthetic-A" : "synthetic-B";
            if (request.Headers.Authorization?.Parameter != expected) Interlocked.Increment(ref Mismatches);
            Request = request;
            if (request.Content != null) LastBody = await request.Content.ReadAsStringAsync();
            Content = new LiffContent("{\"apps\":[],\"liffId\":\"synthetic\",\"message\":\"sensitive-synthetic-detail\"}", () => Interlocked.Increment(ref DisposedContents));
            return new HttpResponseMessage(fail ? HttpStatusCode.BadRequest : HttpStatusCode.OK) { Content = Content };
        }
    }

    /// <summary>每次 response 獨立的固定短內容，只保留合成 byte[]，處置後可觀察所有權終止。</summary>
    private sealed class LiffContent(string text, Action onDispose) : HttpContent
    {
        private readonly byte[] bytes = Encoding.UTF8.GetBytes(text);
        internal bool Disposed { get; private set; }
        /// <summary>寫入 transport 借入的目的地，不取得目的地所有權。</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes, 0, bytes.Length);
        /// <summary>固定內容已知長度，不額外保留大緩衝。</summary>
        protected override bool TryComputeLength(out long length) { length = bytes.Length; return true; }
        /// <summary>記錄釋放並讓基底關閉其緩衝，供每筆 response 的斷言。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !Disposed)
            {
                Disposed = true;
                onDispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>只允許非同步讀取的故障注入串流；同步讀取立即失敗，沒有 I/O、timer 或待完成 task。</summary>
    private sealed class AsyncOnlyStream : MemoryStream
    {
        internal CancellationToken Token;
        internal int AsyncReads;
        /// <summary>若 wrapper 誤走同步 I/O 即明確失敗，不讓基底 Task.Run 假裝真正的非同步轉送。</summary>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous read is forbidden by this fixture.");
        /// <summary>記錄實際轉送的 token 並完成一個 byte；完成後沒有資源或背景工作需要等待。</summary>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            AsyncReads++;
            cancellationToken.ThrowIfCancellationRequested();
            buffer[offset] = 42;
            return Task.FromResult(1);
        }
    }
}
