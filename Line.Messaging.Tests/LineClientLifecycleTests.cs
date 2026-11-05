using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Line.Messaging.Tests;

/// <summary>
/// 保護共用雙目標 SDK 的 channel 隔離與 HTTP 資源所有權；全部 handler 離線、資料合成。
/// 故障注入涵蓋遠端失敗與傳輸失敗，決定性檢查內容 Dispose、借用串流仍開啟及並行作業排空。
/// </summary>
public sealed class LineClientLifecycleTests
{
    /// <summary>同一 transport 給 A/B channel 共用時，每次請求都必須使用自己的 credential，不能修改共用預設 header。</summary>
    /// <returns>完成並排空一千筆交錯推播。</returns>
    [Fact]
    public async Task SharedTransport_TwoChannelsParallel_NoCredentialLeak()
    {
        using var handler = new IsolationHandler();
        using var transport = new HttpClient(handler);
        using var first = new LineMessagingClient(transport, "synthetic-A");
        using var second = new LineMessagingClient(transport, "synthetic-B");
        Assert.Null(transport.DefaultRequestHeaders.Authorization);
        var tasks = Enumerable.Range(0, 1000).Select(index => (index % 2 == 0 ? first : second)
            .PushMessageAsync(index % 2 == 0 ? "A" : "B", new[] { new TextMessage("合成") })).ToArray();
        await Task.WhenAll(tasks);
        Assert.Equal(1000, handler.Completed);
        Assert.Equal(0, handler.Mismatches);
        Assert.Equal(0, handler.Active);
        Assert.Null(transport.DefaultRequestHeaders.Authorization);
    }

    /// <summary>成功、HTTP 失敗與 transport 失敗都要釋放自己的 request/content；回傳值完全讀完後才處置 response。</summary>
    /// <param name="kind">代表各傳輸組裝方式的操作。</param><param name="fault">none、http 或 transport 故障。</param><returns>排空的單筆操作。</returns>
    [Theory]
    [InlineData("Push", "none")]
    [InlineData("Push", "http")]
    [InlineData("Push", "transport")]
    [InlineData("Profile", "none")]
    [InlineData("Profile", "http")]
    [InlineData("Delete", "none")]
    [InlineData("Delete", "http")]
    [InlineData("EmptyPost", "none")]
    [InlineData("EmptyPost", "http")]
    [InlineData("RichMenu", "none")]
    [InlineData("RichMenu", "http")]
    [InlineData("Bytes", "none")]
    [InlineData("Bytes", "http")]
    [InlineData("OAuth", "none")]
    [InlineData("OAuth", "http")]
    public async Task RequestsAndResponses_AreDisposed(string kind, string fault)
    {
        using var handler = new OwnershipHandler(fault);
        using var transport = new HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        var exception = await Record.ExceptionAsync(() => Invoke(kind, client, transport));
        if (fault == "none") Assert.Null(exception); else Assert.NotNull(exception);
        if (handler.Content != null) Assert.True(handler.Content.Disposed);
        if (handler.Request?.Content != null)
            await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request.Content.ReadAsStringAsync());
    }

    /// <summary>下載成功時 response 所有權交給 ContentStream；串流釋放前內容仍可讀，重複 Dispose 不重複保留資源。</summary>
    /// <param name="kind">三種媒體下載入口。</param><returns>排空且已釋放內容的下載作業。</returns>
    [Theory]
    [InlineData("Content")]
    [InlineData("Preview")]
    [InlineData("RichMenu")]
    public async Task Download_StreamOwnsResponse_UntilDisposed(string kind)
    {
        using var handler = new OwnershipHandler("none");
        using var transport = new HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        var stream = kind == "Content" ? await client.GetContentStreamAsync("synthetic") : kind == "Preview" ? await client.GetContentPreviewAsync("synthetic") : await client.DownloadRichMenuImageAsync("synthetic");
        try
        {
            Assert.False(handler.Content!.Disposed);
            Assert.True(stream.CanRead);
            Assert.NotEqual(-1, stream.ReadByte());
        }
        finally { stream.Dispose(); }
        Assert.True(handler.Content!.Disposed);
        stream.Dispose();
        Assert.False(stream.CanRead);
    }

    /// <summary>上傳內容由 SDK 釋放，但輸入串流是呼叫端借入；成功或遠端失敗均不能提前關閉呼叫端檔案。</summary>
    /// <param name="fault">成功或遠端故障。</param><returns>排空的上傳作業。</returns>
    [Theory]
    [InlineData("none")]
    [InlineData("http")]
    public async Task Upload_BorrowedStream_RemainsOpen(string fault)
    {
        using var handler = new OwnershipHandler(fault);
        using var transport = new HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        using var input = new MemoryStream(new byte[] { 1, 2, 3 });
        var exception = await Record.ExceptionAsync(() => client.UploadRichMenuPngImageAsync(input, "synthetic"));
        if (fault == "none") Assert.Null(exception); else Assert.NotNull(exception);
        Assert.True(input.CanRead);
        Assert.True(handler.Content!.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
    }

    /// <summary>借入 transport 不由 SDK Dispose；已釋放的 SDK 則不能再帶 credential 發送新請求。</summary>
    /// <returns>已完成的生命週期檢查。</returns>
    [Fact]
    public async Task Dispose_BorrowedTransportSurvives_ClientStopsSending()
    {
        using var handler = new OwnershipHandler("none");
        using var transport = new HttpClient(handler);
        var client = new LineMessagingClient(transport, "synthetic-token");
        client.Dispose();
        client.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetBotInfoAsync());
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/transport-check");
        using var response = await transport.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>對外例外不能帶出整個遠端 body 或含使用者 ID 的 URL；仍保留可辨識的未授權型別。</summary>
    /// <returns>已完成的錯誤路徑。</returns>
    [Fact]
    public async Task Unauthorized_ErrorDoesNotExposeRemoteBodyOrUrl()
    {
        using var handler = new OwnershipHandler("unauthorized");
        using var transport = new HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.GetUserProfileAsync("private-synthetic-user"));
        Assert.DoesNotContain("sensitive-synthetic-detail", exception.Message);
        Assert.DoesNotContain("private-synthetic-user", exception.Message);
        Assert.True(handler.Content!.Disposed);
    }

    /// <summary>一般非 401 遠端錯誤亦不得把未受信任 response body 帶進 Exception.Message、ToString 或公開錯誤 DTO。</summary>
    /// <returns>已釋放 response 且只保留 HTTP 狀態碼的錯誤作業。</returns>
    [Fact]
    public async Task RemoteError_DoesNotExposeUntrustedBody()
    {
        using var handler = new OwnershipHandler("http");
        using var transport = new HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        var exception = await Assert.ThrowsAsync<LineResponseException>(() => client.GetUserProfileAsync("private-synthetic-user"));
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.DoesNotContain("sensitive-synthetic-detail", exception.Message);
        Assert.DoesNotContain("private-synthetic-user", exception.ToString());
        Assert.DoesNotContain("sensitive-synthetic-detail", exception.ToString());
        Assert.Equal("LINE API request failed.", exception.ResponseMessage.Message);
        Assert.Empty(exception.ResponseMessage.Details);
        Assert.True(handler.Content!.Disposed);
    }

    /// <summary>固定案例涵蓋 JSON、GET、DELETE、空 POST、bytes 與 OAuth form，不接觸外部端點。</summary>
    private static Task Invoke(string kind, LineMessagingClient client, HttpClient transport) => kind switch
    {
        "Push" => client.PushMessageAsync("synthetic", new[] { new TextMessage("合成") }),
        "Profile" => client.GetUserProfileAsync("synthetic"),
        "Delete" => client.DeleteRichMenuAsync("synthetic"),
        "EmptyPost" => client.LeaveFromGroupAsync("synthetic"),
        "RichMenu" => client.CreateRichMenuAsync(new RichMenu()),
        "Bytes" => client.GetContentBytesAsync("synthetic"),
        "OAuth" => LineMessagingClient.IssueChannelAccessTokenAsync(transport, "synthetic-channel", "synthetic-secret"),
        _ => throw new ArgumentException("未知測試操作。", nameof(kind))
    };

    /// <summary>只保留有界計數，不保存任何請求或 token；測試擁有 handler 並等待全部作業後 Dispose。</summary>
    private sealed class IsolationHandler : HttpMessageHandler
    {
        internal int Completed;
        internal int Mismatches;
        internal int Active;
        /// <summary>以本筆 body 的合成 A/B 範圍比對 header，完成後不保留 body 或 credential。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Active);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var body = await request.Content!.ReadAsStringAsync();
                var expected = body.Contains("\"to\":\"A\"") ? "synthetic-A" : "synthetic-B";
                if (request.Headers.Authorization?.Parameter != expected) Interlocked.Increment(ref Mismatches);
                Interlocked.Increment(ref Completed);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }

    /// <summary>每案例最多保留一筆 request 及 content 供 Dispose 斷言，不開 socket 或背景工作。</summary>
    private sealed class OwnershipHandler : HttpMessageHandler
    {
        private readonly string fault;
        internal HttpRequestMessage? Request;
        internal TrackedContent? Content;
        /// <summary>指定固定故障；測試結束時由外部 HttpClient 及 using 持有並處置 handler。</summary>
        internal OwnershipHandler(string fault) => this.fault = fault;
        /// <summary>不解碼或記錄 credential；回應只含合成欄位，傳輸故障在交回 response 前注入。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            if (fault == "transport") throw new HttpRequestException("synthetic transport fault");
            Content = new TrackedContent("{\"richMenuId\":\"synthetic\",\"access_token\":\"synthetic\",\"message\":\"sensitive-synthetic-detail\"}");
            return Task.FromResult(new HttpResponseMessage(fault == "http" ? HttpStatusCode.BadRequest : fault == "unauthorized" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) { Content = Content });
        }
    }

    /// <summary>可觀察確定性釋放的合成 HttpContent；不保存 request、credential 或外部串流。</summary>
    private sealed class TrackedContent : HttpContent
    {
        private readonly byte[] bytes;
        internal bool Disposed { get; private set; }
        /// <summary>建立固定 UTF-8 回應，容量只限案例提供的短合成 JSON。</summary>
        internal TrackedContent(string text) => bytes = Encoding.UTF8.GetBytes(text);
        /// <summary>複製到 transport 借入的目的串流，不持有或 Dispose 該目的地。</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes, 0, bytes.Length);
        /// <summary>固定內容可確定長度，避免任何無界緩衝或等待。</summary>
        protected override bool TryComputeLength(out long length) { length = bytes.Length; return true; }
        /// <summary>先記錄釋放再交基底處置緩衝，供成功／失敗／stream owner 的一致斷言。</summary>
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
}
