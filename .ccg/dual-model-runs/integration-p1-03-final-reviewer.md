# CCG reviewer Task: integration-p1-03-final

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
P1-03 LINE SDK最終補審，僅唯讀本固定快照；禁止擴讀其他檔案/憑證、禁止執行/修改、禁止再呼叫模型。使用固定P0基準cb932c01列出當前全部修改SDK/測試/六Golden，部分原作者公開metadata在review副本省略，產品檔未改。
正式P103：六類逐字Golden先舊net10綠後dual；產品net462/net10，test宿主net48/net10且反射實net462(D21明確核定，不能要求改成net48產品或聲稱實機沙箱已測)；Sender長1..20/https、Background、webhookEventId/redelivery、與傳輸無關raw-byteHMAC fixedtime，保留既有HTTP入口；另外資料隔離/資源漏是零容忍。
前完整review20261105-091706 dualModelComplete=true，Claude C1 raw驗簽未接舊HTTP入口已修：現在ReadAsByteArrayAsync->LineSignature.Verify->strict UTF8解碼，相應獨立HMAC/charset/tamper測試已存在。W2 content雙Dispose已改成request唯一own；W1測試net48不等產品net462屬核定矩陣非缺陷，metadata反射和assets護欄證明實際載入net462。Gemini TryComputeLength disposed回false建議無具體失敗路徑，現保持明確ObjectDisposed拒絕；請判是否真有產品故障。舊BOM建議違反永久規則，不採。
請完整檢查此包的相容、安全、HMAC入口、per-request auth、多client共用HttpClient、stream/response/request/content owner/Dispose race/fault drain、例外不得洩漏body/token。不得因評分高就略過Critical，也不把新SDK抽取以外的業務搬移/階段3主機重寫當此WP缺口。真實最終證據：全方案16TRX1836/1836、SDK net48 96/96(net462輸出)／net10 101/101、0略過；Debug tracer7、打包10/6、編碼與duplicates通過。本輪沒有改SDK來源，只補最終審查收斂。原有NoWarn保留不新增，非新專案不擅自全repo嚴格化。P1-02已驗收1/12；P1-03待此次收斂，其餘P1仍未完成。
輸出Critical/High/Warning/Info，逐項說明舊C1/W是否關閉與必要證據限制。

## Line.Messaging.Tests/Golden/Flex.json
```
{"to":"synthetic-recipient","messages":[{"type":"flex","contents":{"type":"bubble","direction":"ltr","body":{"type":"box","layout":"vertical","contents":[{"type":"text","text":"合成標題","wrap":true,"weight":"bold"}]}},"altText":"合成卡片"}]}

```

## Line.Messaging.Tests/Golden/Image.json
```
{"to":"synthetic-recipient","messages":[{"type":"image","originalContentUrl":"https://example.invalid/full.png","previewImageUrl":"https://example.invalid/preview.png"}]}

```

## Line.Messaging.Tests/Golden/QuickReply.json
```
{"to":"synthetic-recipient","messages":[{"type":"text","quickReply":{"items":[{"type":"action","imageUrl":"https://example.invalid/icon.png","action":{"type":"message","label":"選擇","text":"SELECT"}}]},"text":"合成選項"}]}

```

## Line.Messaging.Tests/Golden/RichMenu.json
```
{"size":{"width":2500,"height":843},"selected":true,"name":"合成選單","chatBarText":"開啟","areas":[{"bounds":{"x":0,"y":0,"width":1250,"height":843},"action":{"type":"message","label":"合成操作","text":"OPEN"}}]}

```

## Line.Messaging.Tests/Golden/Template.json
```
{"to":"synthetic-recipient","messages":[{"type":"template","template":{"type":"confirm","text":"確認？","actions":[{"type":"message","label":"是","text":"YES"},{"type":"message","label":"否","text":"NO"}]},"altText":"合成確認"}]}

```

## Line.Messaging.Tests/Golden/Text.json
```
{"to":"synthetic-recipient","messages":[{"type":"text","text":"合成文字\n\"quote\" \\ 😀"}]}

```

## Line.Messaging.Tests/LiffAndStreamLifecycleTests.cs
```
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

```

## Line.Messaging.Tests/Line.Messaging.Tests.csproj
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net48;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Condition="'$(TargetFramework)' == 'net10.0'" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Condition="'$(TargetFramework)' == 'net10.0'" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Condition="'$(TargetFramework)' == 'net10.0'" />
    <PackageReference Include="Newtonsoft.Json" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
    <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" Condition="'$(TargetFramework)' == 'net10.0'" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Golden/*.json" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <!-- 此檔保護 net10 主機的設定來源；SDK 的全部既有端點與模型案例在兩個宿主都執行。 -->
    <Compile Remove="LineMessagingProcessorCredentialTests.cs" />
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <Reference Include="System.Net.Http" />
  </ItemGroup>
</Project>

```

## Line.Messaging.Tests/LineClientLifecycleTests.cs
```
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

```

## Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs
// 所屬區塊：LINE Messaging SDK 測試專案，驗證 API 端點、序列化與 Client 行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineMessagingClientP0EndpointTests、class CapturingHttpMessageHandler
// 主要成員：Get_message_delivery_uses_single_v2_segment、Get_content_stream_uses_api_data_host、Get_content_bytes_uses_api_data_host、Verify_content_preparation_uses_transcoding_endpoint_on_api_data_host、Verify_content_preparation_returns_false_until_transcoding_succeeds、Get_content_preview_uses_api_data_host、Rich_menu_image_download_and_upload_use_api_data_host、Mark_as_read_uses_official_chat_endpoint_and_token_payload、Legacy_mark_as_read_fails_clearly_instead_of_sending_chat_id_as_token、Get_rich_menu_batch_progress_uses_progress_query_endpoint
// 引用命名空間：FluentAssertions、Line.Messaging、Newtonsoft.Json.Linq、System.Net、System.Text、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace Line.Messaging.Tests;

/// <summary>
/// 鎖定 LINE Messaging Client 對官方 P0 端點的路徑組裝規則。
///
/// RichMenu 圖片、批次進度與批次驗證端點在 LINE 官方 API 中分散於
/// <c>api.line.me</c> 與 <c>api-data.line.me</c> 兩個 host。這些測試刻意只檢查
/// HTTP method 與 URL，避免 SDK 重構時把 RichMenu 專用端點誤接到一般訊息端點，
/// 造成上傳圖片或查詢批次狀態時被 LINE 拒絕。
/// </summary>
public sealed class LineMessagingClientP0EndpointTests
{
    [Fact]
    public async Task Get_message_delivery_uses_single_v2_segment()
    {
        var handler = new CapturingHttpMessageHandler("""{"status":"ready","success":0}""");
        var client = CreateClient(handler);

        await client.GetMessageDeliveryAsync(new DateTime(2026, 7, 2));

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.line.me/v2/bot/insight/message/delivery?date=20260702");
    }

    [Fact]
    public async Task Get_content_stream_uses_api_data_host()
    {
        var handler = new CapturingHttpMessageHandler("binary-content", mediaType: "application/octet-stream");
        var client = CreateClient(handler);

        using var result = await client.GetContentStreamAsync("message-123");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content");
    }

    [Fact]
    public async Task Get_content_bytes_uses_api_data_host()
    {
        var handler = new CapturingHttpMessageHandler("binary-content", mediaType: "application/octet-stream");
        var client = CreateClient(handler);

        await client.GetContentBytesAsync("message-123");

        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content");
    }

    [Fact]
    public async Task Verify_content_preparation_uses_transcoding_endpoint_on_api_data_host()
    {
        var handler = new CapturingHttpMessageHandler("""{"status":"succeeded"}""");
        var client = CreateClient(handler);

        var isReady = await client.VerifyContentPreparationAsync("message-123");

        isReady.Should().BeTrue();
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content/transcoding");
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("failed")]
    public async Task Verify_content_preparation_returns_false_until_transcoding_succeeds(string status)
    {
        var handler = new CapturingHttpMessageHandler($$"""{"status":"{{status}}"}""");
        var client = CreateClient(handler);

        var isReady = await client.VerifyContentPreparationAsync("message-123");

        isReady.Should().BeFalse();
    }

    [Fact]
    public async Task Get_content_preview_uses_api_data_host()
    {
        var handler = new CapturingHttpMessageHandler("preview-content", mediaType: "application/octet-stream");
        var client = CreateClient(handler);

        using var result = await client.GetContentPreviewAsync("message-123");

        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content/preview");
    }

    /// <summary>
    /// 驗證 RichMenu 圖片下載與 JPEG/PNG 上傳都走 LINE 的 API data host。
    ///
    /// RichMenu 圖片內容不是一般 JSON API；官方要求使用
    /// <c>https://api-data.line.me/v2/bot/richmenu/{richMenuId}/content</c>。
    /// 這裡同時覆蓋 GET 與兩種 POST 上傳格式，確保共用 URL 建構邏輯不會只修到其中一條路徑。
    /// </summary>
    [Fact]
    public async Task Rich_menu_image_download_and_upload_use_api_data_host()
    {
        var handler = new CapturingHttpMessageHandler("image-content", mediaType: "application/octet-stream");
        var client = CreateClient(handler);

        using var download = await client.DownloadRichMenuImageAsync("rich-1");
        await client.UploadRichMenuJpegImageAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "rich-1");
        await client.UploadRichMenuPngImageAsync(new MemoryStream(new byte[] { 4, 5, 6 }), "rich-1");

        handler.Requests.Select(request => request.RequestUri!.ToString()).Should().Equal(
            "https://api-data.line.me/v2/bot/richmenu/rich-1/content",
            "https://api-data.line.me/v2/bot/richmenu/rich-1/content",
            "https://api-data.line.me/v2/bot/richmenu/rich-1/content");
        handler.Requests.Select(request => request.Method).Should().Equal(HttpMethod.Get, HttpMethod.Post, HttpMethod.Post);
    }

    [Fact]
    public async Task Mark_as_read_uses_official_chat_endpoint_and_token_payload()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.MarkAsReadByTokenAsync("mark-token-123");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.line.me/v2/bot/chat/markAsRead");

        var body = JObject.Parse(handler.Bodies[0]);
        body["markAsReadToken"]!.Value<string>().Should().Be("mark-token-123");
        body["chatId"].Should().BeNull();
    }

    [Fact]
    public async Task Legacy_mark_as_read_fails_clearly_instead_of_sending_chat_id_as_token()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

#pragma warning disable CS0618
        Func<Task> action = () => client.MarkAsReadAsync("U1234567890abcdef");
#pragma warning restore CS0618

        await action.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*markAsReadToken*");
        handler.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// 驗證 RichMenu 批次操作進度查詢使用官方 progress query endpoint。
    ///
    /// LINE 的批次進度端點把 requestId 放在 query string，而不是 REST path。
    /// 這個測試保護批次同步流程，避免 provisioning workflow 查不到 LINE 回報的批次狀態。
    /// </summary>
    [Fact]
    public async Task Get_rich_menu_batch_progress_uses_progress_query_endpoint()
    {
        var handler = new CapturingHttpMessageHandler("""{"phase":"succeeded"}""");
        var client = CreateClient(handler);

        await client.GetRichMenuBatchProgressAsync("request-123");

        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.line.me/v2/bot/richmenu/progress/batch?requestId=request-123");
    }

    /// <summary>
    /// 驗證 RichMenu 批次請求驗證使用官方 validate/batch endpoint。
    ///
    /// provisioning 在送出大量 create/link/delete 前可先呼叫此端點做格式驗證；
    /// 若 URL 多了一層或少了一層 richmenu segment，LINE 會直接拒絕請求，
    /// 因此這裡把 POST method 與完整路徑一起鎖住。
    /// </summary>
    [Fact]
    public async Task Validate_rich_menu_batch_uses_official_validate_batch_endpoint()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.ValidateRichMenuBatchRequestAsync(new List<RichMenuBatchOperation>());

        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.line.me/v2/bot/richmenu/validate/batch");
    }

    [Fact]
    public async Task Insight_endpoints_do_not_duplicate_v2_segment()
    {
        var handler = new CapturingHttpMessageHandler("""{"status":"ready"}""");
        var client = CreateClient(handler);

        await client.GetMessageDeliveryAsync(new DateTime(2026, 7, 2));
        await client.GetFollowerStatisticsAsync(new DateTime(2026, 7, 2));
        await client.GetFriendDemographicsAsync();
        await client.GetUserInteractionStatisticsAsync("request-123");
        await client.GetStatisticsPerUnitAsync("unit one", "20260701", "20260702");
        await client.GetAggregationInfoAsync();
        await client.GetAggregationUnitNameListAsync(10, "next-token");

        handler.Requests.Select(request => request.RequestUri!.ToString()).Should().AllSatisfy(url =>
            url.Should().NotContain("/v2/v2/"));
    }

    [Fact]
    public async Task Coupon_and_membership_endpoints_do_not_duplicate_v2_segment()
    {
        var handler = new CapturingHttpMessageHandler("""{}""");
        var client = CreateClient(handler);

        await client.CreateCouponAsync(new CreateCouponRequest());
        await client.CloseCouponAsync("coupon-1");
        await client.GetCouponListAsync();
        await client.GetCouponAsync("coupon-1");
        await client.GetMembershipSubscriptionAsync("user-1");
        await client.GetMembershipUserIdsAsync("membership-1", 100, "next-token");
        await client.GetMembershipPlansAsync();

        handler.Requests.Select(request => request.RequestUri!.ToString()).Should().AllSatisfy(url =>
            url.Should().NotContain("/v2/v2/"));
    }

    [Fact]
    public async Task Custom_api_base_uri_is_normalized_to_one_v2_segment()
    {
        var handler = new CapturingHttpMessageHandler("""{"status":"ready","success":0}""");
        var client = CreateClient(handler, "https://gateway.internal/line");

        await client.GetMessageDeliveryAsync(new DateTime(2026, 7, 2));
        await client.MarkAsReadByTokenAsync("mark-token-123");

        handler.Requests.Select(request => request.RequestUri!.ToString()).Should().Equal(
            "https://gateway.internal/line/v2/bot/insight/message/delivery?date=20260702",
            "https://gateway.internal/line/v2/bot/chat/markAsRead");
    }

    [Fact]
    public async Task Custom_api_base_uri_is_reused_for_data_endpoints()
    {
        var handler = new CapturingHttpMessageHandler("binary-content", mediaType: "application/octet-stream");
        var client = CreateClient(handler, "https://gateway.internal/line");

        using var result = await client.GetContentStreamAsync("message-123");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://gateway.internal/line/v2/bot/message/message-123/content");
    }

    [Fact]
    public async Task Issue_channel_access_token_normalizes_custom_api_base_uri()
    {
        var handler = new CapturingHttpMessageHandler("""{"access_token":"issued-token","expires_in":3600}""");
        using var httpClient = new HttpClient(handler);

        var token = await LineMessagingClient.IssueChannelAccessTokenAsync(
            httpClient,
            "channel-id",
            "channel-secret",
            "https://gateway.internal/line");

        token.AccessToken.Should().Be("issued-token");
        token.ExpiresIn.Should().Be(3600);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://gateway.internal/line/v2/oauth/accessToken");
    }

    [Fact]
    public async Task Revoke_channel_access_token_normalizes_custom_api_base_uri()
    {
        var handler = new CapturingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        await LineMessagingClient.RevokeChannelAccessTokenAsync(
            httpClient,
            "token-to-revoke",
            "https://gateway.internal/line");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://gateway.internal/line/v2/oauth/revoke");

        var formValues = handler.Bodies[0]
            .Split('&')
            .Select(part => part.Split('='))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts[1]));
        formValues["access_token"].Should().Be("token-to-revoke");
    }

    private static LineMessagingClient CreateClient(CapturingHttpMessageHandler handler, string uri = "https://api.line.me/v2")
    {
        var httpClient = new HttpClient(handler);
        return new LineMessagingClient(httpClient, "test-token", uri);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly string _mediaType;

        public CapturingHttpMessageHandler(
            string responseBody = "{}",
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string mediaType = "application/json")
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
            _mediaType = mediaType;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync());

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, _mediaType)
            };
        }
    }
}

```

## Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs
// 所屬區塊：LINE Messaging SDK 測試專案，驗證 API 端點、序列化與 Client 行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineMessagingClientP1RetryKeyTests、class CapturingHttpMessageHandler
// 主要成員：Push_message_with_retry_key_sends_line_retry_header、Push_message_existing_overload_does_not_send_retry_header、Multicast_message_with_retry_key_sends_line_retry_header_and_keeps_body、Broadcast_message_with_retry_key_sends_line_retry_header、Empty_retry_key_does_not_send_retry_header、CreateClient、SendAsync、Requests、Bodies
// 引用命名空間：FluentAssertions、Line.Messaging、Newtonsoft.Json.Linq、System.Net、System.Text、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using Line.Messaging;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace Line.Messaging.Tests;

public sealed class LineMessagingClientP1RetryKeyTests
{
    [Fact]
    public async Task Push_message_with_retry_key_sends_line_retry_header()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") },
            "fee-1001-notification");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("fee-1001-notification");

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Value<string>().Should().Be("U1234567890abcdef");
        body["messages"]!.Should().HaveCount(1);
    }

    [Fact]
    public async Task Push_message_existing_overload_does_not_send_retry_header()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") });

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/push");
        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
    }

    [Fact]
    public async Task Multicast_message_with_retry_key_sends_line_retry_header_and_keeps_body()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.MultiCastMessageAsync(
            new List<string> { "U111", "U222" },
            new List<ISendMessage> { new TextMessage("batch notice") },
            "batch-20260702-001");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/multicast");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("batch-20260702-001");

        var body = JObject.Parse(handler.Bodies[0]);
        body["to"]!.Select(token => token.Value<string>()).Should().Equal("U111", "U222");
        body["messages"]!.Should().HaveCount(1);
    }

    [Fact]
    public async Task Broadcast_message_with_retry_key_sends_line_retry_header()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.BroadcastMessageAsync(
            new List<ISendMessage> { new TextMessage("global notice") },
            "broadcast-20260702-001");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.line.me/v2/bot/message/broadcast");
        handler.Requests[0].Headers.TryGetValues("X-Line-Retry-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("broadcast-20260702-001");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_retry_key_does_not_send_retry_header(string? retryKey)
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        await client.PushMessageAsync(
            "U1234567890abcdef",
            new List<ISendMessage> { new TextMessage("payment received") },
            retryKey);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Contains("X-Line-Retry-Key").Should().BeFalse();
    }

    private static LineMessagingClient CreateClient(CapturingHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new LineMessagingClient(httpClient, "test-token", "https://api.line.me/v2");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly string _mediaType;

        public CapturingHttpMessageHandler(string responseBody = "{}", HttpStatusCode statusCode = HttpStatusCode.OK, string mediaType = "application/json")
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
            _mediaType = mediaType;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync());
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, _mediaType)
            };
        }
    }
}

```

## Line.Messaging.Tests/LineSdkFoundationTests.cs
```
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Line.Messaging.Webhooks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Line.Messaging.Tests;

/// <summary>
/// 以離線官方形狀樣本與 RFC HMAC 向量保護 P1-03；樣本全為合成值，不連 LINE、不含實際收件者。
/// 相同案例在 net48 宿主（載入 net462）及 net10 上執行，防止僅編譯成功卻遺漏 Framework 行為。
/// </summary>
public sealed class LineSdkFoundationTests
{
    /// <summary>檢查實際載入 DLL 的框架屬性；不能把 net48 測試宿主誤算成產品 net48。</summary>
    [Fact]
    public void Assembly_TargetFramework_MatchesProduct()
    {
        var framework = typeof(LineMessagingClient).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false).Cast<TargetFrameworkAttribute>().Single();
#if NETFRAMEWORK
        Assert.Equal(".NETFramework,Version=v4.6.2", framework.FrameworkName);
#else
        Assert.Equal(".NETCoreApp,Version=v10.0", framework.FrameworkName);
#endif
    }

    /// <summary>建構時拒絕空或超過二十 UTF-16 單元的名稱；不可等遠端 API 才發現錯誤。</summary>
    /// <param name="length">名稱長度的邊界反例。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Sender_InvalidNameLength_Throws(int length) => Assert.Throws<ArgumentException>(() => new Sender(new string('a', length), "https://example.invalid/icon.png"));

    /// <summary>圖示網址只接受絕對 HTTPS；http、相對路徑與其他 scheme 都不可透過。</summary>
    /// <param name="url">刻意不合格的圖示 URI。</param>
    [Theory]
    [InlineData("http://example.invalid/icon.png")]
    [InlineData("/icon.png")]
    [InlineData("file:///icon.png")]
    [InlineData("")]
    public void Sender_NonHttpsIcon_Throws(string url) => Assert.Throws<ArgumentException>(() => new Sender("合成教會", url));

    /// <summary>一及二十字邊界合法，透過真實 SDK 傳輸序列化後 sender 位於訊息內；不更改未設定 sender 的黃金檔。</summary>
    /// <param name="length">合法名稱的兩個邊界。</param><returns>已排空的離線捕獲作業。</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public async Task Sender_Serializes_NameAndIconUrl(int length)
    {
        using var handler = new LineSdkGoldenTests.GoldenHandler();
        using var transport = new System.Net.Http.HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        var sender = new Sender(new string('a', length), "https://example.invalid/icon.png");
        await client.PushMessageAsync("synthetic-recipient", new[] { new TextMessage("合成") { Sender = sender } });
        var json = JObject.Parse(handler.Body)["messages"]![0]!["sender"]!;
        Assert.Equal(sender.Name, (string?)json["name"]);
        Assert.Equal(sender.IconUrl, (string?)json["iconUrl"]);
    }

    /// <summary>漸層完整欄位透過 Flex Box 真正進入請求；沒有背景的既有 Flex 黃金字串保持不變。</summary>
    /// <returns>已釋放 transport 及 client 的捕獲作業。</returns>
    [Fact]
    public async Task Background_LinearGradient_Serializes()
    {
        using var handler = new LineSdkGoldenTests.GoldenHandler();
        using var transport = new System.Net.Http.HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        var background = new Background { Angle = "90deg", StartColor = "#FF0000", CenterColor = "#00FF00", EndColor = "#0000FF", CenterPosition = "25%" };
        var box = new BoxComponent(BoxLayout.Vertical) { Background = background };
        await client.PushMessageAsync("synthetic-recipient", new[] { new FlexMessage("合成") { Contents = new BubbleContainer { Body = box } } });
        var json = JObject.Parse(handler.Body)["messages"]![0]!["contents"]!["body"]!["background"]!;
        Assert.Equal("{\"type\":\"linearGradient\",\"angle\":\"90deg\",\"startColor\":\"#FF0000\",\"centerColor\":\"#00FF00\",\"endColor\":\"#0000FF\",\"centerPosition\":\"25%\"}", json.ToString(Formatting.None));
    }

    /// <summary>LINE 官方事件外形的新增欄位必須由共同解析入口讀取，舊事件缺欄位時維持安全預設。</summary>
    /// <param name="type">可解析的事件類別。</param><param name="extra">該事件既有必填欄位。</param>
    [Theory]
    [InlineData("message", "\"message\":{\"id\":\"synthetic-message\",\"type\":\"text\",\"text\":\"hello\"},")]
    [InlineData("follow", "")]
    [InlineData("unfollow", "")]
    [InlineData("join", "")]
    [InlineData("leave", "")]
    [InlineData("postback", "\"postback\":{\"data\":\"OPEN\"},")]
    public void WebhookEvent_Deserialize_ReadsWebhookEventIdAndRedelivery(string type, string extra)
    {
        var prefix = "{\"events\":[{\"type\":\"" + type + "\",\"source\":{\"type\":\"user\",\"userId\":\"synthetic-user\"},\"timestamp\":1462629479859,\"replyToken\":\"synthetic-reply\"," + extra;
        var current = Assert.Single(WebhookEventParser.Parse(prefix + "\"webhookEventId\":\"01SYNTHETICEVENT\",\"deliveryContext\":{\"isRedelivery\":true}}]}"));
        Assert.Equal("01SYNTHETICEVENT", current.WebhookEventId);
        Assert.True(current.DeliveryContext.IsRedelivery);
        var legacy = Assert.Single(WebhookEventParser.Parse(prefix.TrimEnd(',') + "}]}"));
        Assert.Null(legacy.WebhookEventId);
        Assert.False(legacy.DeliveryContext.IsRedelivery);
    }

    /// <summary>RFC 4231 case 2 的固定結果，不使用產品演算法自行產生 expected；修改任意 digest 位元組均須拒絕。</summary>
    [Fact]
    public void Signature_ValidAndEveryTamperedByte_AreVerified()
    {
        var body = Encoding.UTF8.GetBytes("what do ya want for nothing?");
        var secret = "Jefe";
        const string expected = "W9zBRr9gdU5qBCQmCJV1x1oAPwidJzmDnexYuWTsOEM=";
        Assert.True(LineSignature.Verify(body, expected, secret));
        var digest = Convert.FromBase64String(expected);
        for (var index = 0; index < digest.Length; index++)
        {
            var changed = (byte[])digest.Clone();
            changed[index] ^= 1;
            Assert.False(LineSignature.Verify(body, Convert.ToBase64String(changed), secret));
        }
        Assert.False(LineSignature.Verify(Encoding.UTF8.GetBytes("what do ya want for nothing?\n"), expected, secret));
        Assert.Equal("what do ya want for nothing?", Encoding.UTF8.GetString(body));
    }

    /// <summary>非法 Base64、非 32-byte digest、缺 header 都回 false，不拋出攻擊者可控格式例外。</summary>
    /// <param name="signature">不合格的 header。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("YQ==")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void Signature_InvalidHeader_ReturnsFalse(string? signature) => Assert.False(LineSignature.Verify(new byte[] { 1 }, signature!, "synthetic-secret"));

    /// <summary>空 secret 是主機設定錯誤，必須明確失敗而不是接受以空密鑰計算的簽章。</summary>
    /// <param name="secret">空或只有空白的設定。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Signature_EmptySecret_Throws(string? secret) => Assert.Throws<ArgumentException>(() => LineSignature.Verify(Array.Empty<byte>(), "", secret!));

    /// <summary>兩個 channel 交錯一千次驗證不能共用 HMAC 或密鑰；所有有限作業以 WhenAll 排空，不留背景工作。</summary>
    /// <returns>已等待全部一千次驗證的工作。</returns>
    [Fact]
    public async Task Signature_TwoChannelsParallel_NoSharedState()
    {
        var body = Encoding.UTF8.GetBytes("what do ya want for nothing?");
        var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() => Assert.Equal(index % 2 == 0,
            LineSignature.Verify(body, "W9zBRr9gdU5qBCQmCJV1x1oAPwidJzmDnexYuWTsOEM=", index % 2 == 0 ? "Jefe" : "different-channel")))).ToArray();
        await Task.WhenAll(tasks);
        Assert.All(tasks, task => Assert.True(task.IsCompleted));
    }
}

```

## Line.Messaging.Tests/LineSdkGoldenTests.cs
```
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Xunit;

namespace Line.Messaging.Tests;

/// <summary>
/// 以舊版 net10 SDK 實際 HTTP 請求鎖住六類 JSON；樣本只含合成文字及 example.invalid。
/// 比對包含欄位順序與逸出字元的完整字串，不能以解析後相等掩蓋相容性變更。
/// </summary>
public sealed class LineSdkGoldenTests
{
    /// <summary>逐類送入記憶體 handler；不連網，並將 SDK 原始請求與固定 golden 逐字比較。</summary>
    /// <param name="kind">六類既有訊息或 RichMenu 建立請求。</param>
    /// <returns>完成單次捕獲並釋放客戶端的工作。</returns>
    [Theory]
    [InlineData("Text")]
    [InlineData("Flex")]
    [InlineData("Template")]
    [InlineData("Image")]
    [InlineData("QuickReply")]
    [InlineData("RichMenu")]
    public async Task Serialize_AllMessageTypes_MatchGolden(string kind)
    {
        using var handler = new GoldenHandler();
        using var transport = new HttpClient(handler);
        using var client = new LineMessagingClient(transport, "synthetic-token");
        if (kind == "RichMenu")
        {
            await client.CreateRichMenuAsync(new RichMenu
            {
                Size = new ImagemapSize(2500, 843),
                Selected = true,
                Name = "合成選單",
                ChatBarText = "開啟",
                Areas = new List<ActionArea>
                {
                    new() { Bounds = new ImagemapArea(0, 0, 1250, 843), Action = new MessageTemplateAction("合成操作", "OPEN") }
                }
            });
        }
        else
        {
            await client.PushMessageAsync("synthetic-recipient", new List<ISendMessage> { CreateMessage(kind) });
        }

        using var stream = typeof(LineSdkGoldenTests).Assembly.GetManifestResourceStream("Line.Messaging.Tests.Golden." + kind + ".json")
            ?? throw new InvalidOperationException("缺少固定黃金資源。");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        // fixture 以 CRLF 結尾符合倉庫文件格式；只移除這一個檔尾封套，不 Trim 真正請求內的任何字元。
        var expected = await reader.ReadToEndAsync();
        Assert.EndsWith("\r\n", expected);
        Assert.Equal(expected.Substring(0, expected.Length - 2), handler.Body);
    }

    /// <summary>建立每次操作獨享的模型，不共用可變清單或任何收件者狀態。</summary>
    /// <param name="kind">合成訊息類別。</param><returns>只屬於此次測試的訊息圖。</returns>
    internal static ISendMessage CreateMessage(string kind) => kind switch
    {
        "Text" => new TextMessage("合成文字\n\"quote\" \\ 😀"),
        "Flex" => new FlexMessage("合成卡片")
        {
            Contents = new BubbleContainer
            {
                Body = new BoxComponent(BoxLayout.Vertical)
                {
                    Contents = new List<IFlexComponent> { new TextComponent("合成標題") { Weight = Weight.Bold, Wrap = true } }
                }
            }
        },
        "Template" => new TemplateMessage("合成確認", new ConfirmTemplate("確認？", new List<ITemplateAction>
        {
            new MessageTemplateAction("是", "YES"), new MessageTemplateAction("否", "NO")
        })),
        "Image" => new ImageMessage("https://example.invalid/full.png", "https://example.invalid/preview.png"),
        "QuickReply" => new TextMessage("合成選項", new QuickReply(new List<QuickReplyButtonObject>
        {
            new(new MessageTemplateAction("選擇", "SELECT"), "https://example.invalid/icon.png")
        })),
        _ => throw new ArgumentException("未知合成訊息類別。", nameof(kind))
    };

    /// <summary>只捕獲單筆請求文字；由測試 Dispose，無 socket、queue、timer 或背景工作。</summary>
    internal sealed class GoldenHandler : HttpMessageHandler
    {
        /// <summary>本次請求的原始序列化文字；只保留最後一筆，測試結束即可回收。</summary>
        internal string Body { get; private set; } = string.Empty;

        /// <summary>同步複製請求文字後回傳合成成功；回應所有權交還 SDK／HttpClient。</summary>
        /// <param name="request">由 SDK 建立的借用請求，不保留物件。</param>
        /// <param name="cancellationToken">HttpClient 的取消界線。</param><returns>合成回應。</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"richMenuId\":\"synthetic-menu\"}", Encoding.UTF8, "application/json")
            };
        }
    }
}

```

## Line.Messaging.Tests/WebhookRawSignatureTests.cs
```
using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Line.Messaging.Webhooks;
using Xunit;

namespace Line.Messaging.Tests;

/// <summary>
/// 保護 webhook 在 JSON 解碼前以 wire 原始 UTF-8 bytes 驗證簽章，避免 Content-Type charset 或正規化改變驗簽內容。
/// 故障注入刻意讓 header charset 與實際 LINE UTF-8 body 不一致；正確結果須按 bytes 驗證並依 UTF-8 解析。
/// </summary>
public sealed class WebhookRawSignatureTests
{
    /// <summary>錯誤 charset 不得在驗簽前重新解碼 body；簽章由測試獨立 HMAC 實作從原始 bytes 產生。</summary>
    /// <returns>成功解析空事件清單的已排空 webhook 工作。</returns>
    [Fact]
    public async Task Webhook_ValidRawSignature_IgnoresContentCharsetAndParsesUtf8()
    {
        const string secret = "synthetic-channel-secret";
        var body = Encoding.UTF8.GetBytes("{\"destination\":\"synthetic-bot\",\"events\":[]}");
        using var request = Request(body, Signature(body, secret), "utf-16");

        var events = await request.GetWebhookEventsAsync(secret, "synthetic-bot");

        Assert.Empty(events);
    }

    /// <summary>在原始已簽章內容任一 byte 被竄改後必須拒絕；錯誤須發生在 JSON 事件回傳之前。</summary>
    /// <returns>已完成遭竄改 webhook 的拒絕檢查。</returns>
    [Fact]
    public async Task Webhook_TamperedRawBody_IsRejectedBeforeParsing()
    {
        const string secret = "synthetic-channel-secret";
        var body = Encoding.UTF8.GetBytes("{\"destination\":\"synthetic-bot\",\"events\":[]}");
        var signature = Signature(body, secret);
        body[body.Length - 2] ^= 1;
        using var request = Request(body, signature, "utf-8");

        await Assert.ThrowsAsync<InvalidSignatureException>(() => request.GetWebhookEventsAsync(secret, "synthetic-bot"));
    }

    /// <summary>案例擁有 body/header 和 request；內容只有合成事件欄位，不存檔、不連 LINE。</summary>
    private static HttpRequestMessage Request(byte[] body, string signature, string charset)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.invalid/webhook")
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
        {
            CharSet = charset
        };
        request.Headers.TryAddWithoutValidation("X-Line-Signature", signature);
        return request;
    }

    /// <summary>獨立使用 BCL HMAC 建立合成測試簽章；密鑰與 digest 僅存活於單一測試操作。</summary>
    private static string Signature(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }
}

```

## Line.Messaging/BorrowedStreamContent.cs
```
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Line.Messaging;

/// <summary>
/// 上傳期間借用呼叫者串流的 HttpContent；不同於 StreamContent，不取得輸入串流所有權。
/// 每次上傳建立一個，方法完成／故障即 Dispose 並清參照；不緩衝整個檔案，不建立背景工作。
/// 呼叫者必須等上傳完成再修改或 Dispose 輸入，內容不可跨上傳重用。
/// </summary>
internal sealed class BorrowedStreamContent : HttpContent
{
    private Stream _stream;
    /// <summary>只借入可讀串流，傳輸目的地由 HttpClient 擁有，兩者均不由本類別關閉。</summary>
    /// <param name="stream">呼叫端維持開啟到傳輸完成的輸入。</param>
    internal BorrowedStreamContent(Stream stream) => _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <summary>直接以有界複製緩衝傳輸；等待完整複製，沒有 Task.Run 或遺棄後續工作。</summary>
    /// <param name="stream">transport 擁有的目的地。</param><param name="context">底層傳輸內容，不保存。</param><returns>完整複製作業。</returns>
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => (_stream ?? throw new ObjectDisposedException(nameof(BorrowedStreamContent))).CopyToAsync(stream);

#if !NETFRAMEWORK
    /// <summary>net10 transport 的取消 token 傳到每次讀寫；不以取消後遺棄 CopyToAsync 代替確定性排空。</summary>
    /// <param name="stream">transport 目的地。</param><param name="context">不保存的傳輸內容。</param>
    /// <param name="cancellationToken">由 HttpClient timeout／呼叫取消導出的傳輸界線。</param><returns>實際複製結束的工作。</returns>
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext context, CancellationToken cancellationToken) =>
        (_stream ?? throw new ObjectDisposedException(nameof(BorrowedStreamContent))).CopyToAsync(stream, 81920, cancellationToken);
#endif

    /// <summary>可 seek 時提供剩餘長度，否則交由 transport 串流傳送，不為計算長度讀完整內容。</summary>
    /// <param name="length">已知剩餘位元組數或零。</param><returns>是否能不消耗輸入取得長度。</returns>
    protected override bool TryComputeLength(out long length)
    {
        var source = _stream ?? throw new ObjectDisposedException(nameof(BorrowedStreamContent));
        length = source.CanSeek ? source.Length - source.Position : 0;
        return source.CanSeek;
    }

    /// <summary>清掉借用參照並釋放 HttpContent 自有緩衝；不關閉輸入串流，由原呼叫者 using 負責。</summary>
    /// <param name="disposing">是否為明確處置。</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing) _stream = null;
        base.Dispose(disposing);
    }
}

```

## Line.Messaging/ChannelHttpTransport.cs
```
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Line.Messaging;

/// <summary>
/// Messaging 與 LIFF 共用的單一 channel 傳輸與 request 所有權實作。
/// credential 僅屬此實例且只附加於當次 request，不修改共用 HttpClient 的預設標頭。
/// 不建立 queue、timer 或背景 task；借入 transport 必須由組合根設定有限逾時、無使用者 cookie，
/// 並先等待全部操作結束再 Dispose SDK。回應唯一交回呼叫方法 using 或 ContentStream。
/// </summary>
internal sealed class ChannelHttpTransport : IDisposable
{
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private AuthenticationHeaderValue authorization;

    /// <summary>借入由主機管理的 transport；不取得其 Dispose 責任，也不保存 request 或 response。</summary>
    /// <param name="client">主機擁有且不含其他使用者預設狀態的 transport。</param>
    /// <param name="token">伺服器端已驗證 channel 的 credential，存活至本實例 Dispose。</param>
    internal ChannelHttpTransport(HttpClient client, string token)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>相容舊 API 的自有 transport；先驗證 header 再配置 client，避免建構失敗留下無 owner 的資源。</summary>
    /// <param name="token">此實例獨享、Dispose 時清除參照的 channel credential。</param>
    internal ChannelHttpTransport(string token)
    {
        authorization = new AuthenticationHeaderValue("Bearer", token);
        client = new HttpClient();
        ownsClient = true;
    }

    /// <summary>
    /// 接管當次 request，等待 handler 完成後確定性釋放 request/content；沒有延遲或背景重用。
    /// 自訂 handler 不得在回傳 response 後繼續存取 request。response 由上層擁有，不由此處提前關閉。
    /// </summary>
    /// <param name="request">只可傳送一次且所有權轉交本方法的 request。</param>
    /// <param name="completion">一般 API 讀完整 body；媒體使用 ResponseHeadersRead 交給串流 owner。</param>
    /// <returns>呼叫方法必須確定性處置或轉交唯一 owner 的 response。</returns>
    internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using (request)
        {
            var credential = Volatile.Read(ref authorization);
            if (credential == null) throw new ObjectDisposedException(nameof(ChannelHttpTransport));
            request.Headers.Authorization = credential;
            return await client.SendAsync(request, completion).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 冪等禁止新操作並清掉 credential；不關閉借入 client，不主動遺棄仍在進行的作業。
    /// 呼叫端須依公開 owner 契約先排空，再結束本實例；自有 client 才由此方法關閉。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref authorization, null) == null) return;
        if (ownsClient) client.Dispose();
    }
}

```

## Line.Messaging/ContentStream.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/ContentStream.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class ContentStream
// 主要成員：Flush、Read、Seek、SetLength、Write、Dispose、BaseStream、ContentHeaders、Position
// 引用命名空間：System、System.IO、System.Net.Http.Headers
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.IO;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Line.Messaging
{
    /// <summary>
    /// 表示 LINE Messaging API 回傳的內容串流（如圖片、影片、音訊等媒體檔案）
    /// Stream object for content such as image, video, audio, and other media files from LINE Messaging API.
    /// </summary>
    /// <remarks>
    /// 此類別封裝了底層的 Stream 物件，並提供 HTTP Content Headers 的存取。
    /// 主要用於從 LINE 伺服器下載媒體內容時的資料傳輸。
    /// </remarks>
    public class ContentStream : Stream
    {
        /// <summary>
        /// 底層的串流物件
        /// The underlying stream object
        /// </summary>
        protected Stream _baseStream;

        /// <summary>
        /// 取得底層串流物件，如果已釋放則拋出例外
        /// Gets the base stream, throws ObjectDisposedException if already disposed
        /// </summary>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        protected Stream BaseStream
        {
            get
            {
                if (_baseStream == null) { throw new ObjectDisposedException(nameof(BaseStream)); }
                return _baseStream;
            }
        }

        /// <summary>
        /// 取得 HTTP Content 的標頭資訊（如 Content-Type、Content-Length 等）
        /// Gets the HTTP content headers (e.g., Content-Type, Content-Length)
        /// </summary>
        public HttpContentHeaders ContentHeaders => _contentHeaders ?? throw new ObjectDisposedException(nameof(ContentStream));

        /// <summary>最多持有到 Dispose 的內容標頭；釋放後清掉，避免標頭容器間接保留 HttpContent。</summary>
        private HttpContentHeaders _contentHeaders;
        /// <summary>由下載 helper 轉交的唯一 response owner；舊公開建構式則只 owns baseStream。</summary>
        private IDisposable _responseOwner;
        /// <summary>僅保護一次性所有權轉交，不序列化串流讀寫；Stream 本身不支援並行讀寫。</summary>
        private readonly object _disposeGate = new object();

        /// <summary>
        /// 初始化 ContentStream 的新執行個體
        /// Initializes a new instance of the ContentStream class
        /// </summary>
        /// <param name="baseStream">底層的串流物件 / The underlying stream object</param>
        /// <param name="contentHeaders">HTTP Content 標頭 / HTTP content headers</param>
        public ContentStream(Stream baseStream, HttpContentHeaders contentHeaders)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            _contentHeaders = contentHeaders;
        }

        /// <summary>由 SDK 內部一次性交接 response 所有權；外部仍可沿用原雙參數建構式。</summary>
        /// <param name="baseStream">response 內容串流，不獨立重複 Dispose。</param>
        /// <param name="contentHeaders">只在串流存活期間有效的內容標頭。</param><param name="responseOwner">唯一 response owner，會連同 content 與串流一起釋放。</param>
        internal ContentStream(Stream baseStream, HttpContentHeaders contentHeaders, IDisposable responseOwner) : this(baseStream, contentHeaders)
        {
            _responseOwner = responseOwner ?? throw new ArgumentNullException(nameof(responseOwner));
        }

        /// <summary>
        /// 取得值，指出目前的串流是否支援讀取
        /// Gets a value indicating whether the current stream supports reading
        /// </summary>
        public override bool CanRead => _baseStream?.CanRead ?? false;

        /// <summary>
        /// 取得值，指出目前的串流是否支援搜尋
        /// Gets a value indicating whether the current stream supports seeking
        /// </summary>
        public override bool CanSeek => _baseStream?.CanSeek ?? false;

        /// <summary>
        /// 取得值，指出目前的串流是否支援寫入
        /// Gets a value indicating whether the current stream supports writing
        /// </summary>
        public override bool CanWrite => _baseStream?.CanWrite ?? false;

        /// <summary>
        /// 取得串流的位元組長度
        /// Gets the length in bytes of the stream
        /// </summary>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override long Length => BaseStream.Length;

        /// <summary>
        /// 取得或設定串流中的目前位置
        /// Gets or sets the current position within the stream
        /// </summary>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override long Position
        {
            get => BaseStream.Position;
            set => BaseStream.Position = value;
        }

        /// <summary>
        /// 清除此串流的所有緩衝區，並將所有緩衝資料寫入至底層裝置
        /// Clears all buffers for this stream and causes any buffered data to be written to the underlying device
        /// </summary>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override void Flush() => BaseStream.Flush();

        /// <summary>
        /// 從目前串流讀取位元組序列，並將串流中的位置往前移動讀取的位元組數
        /// Reads a sequence of bytes from the current stream and advances the position within the stream
        /// </summary>
        /// <param name="buffer">位元組陣列 / Byte array buffer</param>
        /// <param name="offset">buffer 中以零為起始的位元組位移 / Zero-based byte offset in buffer</param>
        /// <param name="count">最多要讀取的位元組數 / Maximum number of bytes to read</param>
        /// <returns>讀入緩衝區的總位元組數 / Total number of bytes read into the buffer</returns>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override int Read(byte[] buffer, int offset, int count) => BaseStream.Read(buffer, offset, count);

        /// <summary>直接轉交底層非同步讀取與 caller token，不以 ThreadPool 包裝同步 Read，也不建立額外工作或保留 buffer。</summary>
        /// <param name="buffer">僅在本次讀取期間借用的 caller 緩衝。</param><param name="offset">起點。</param>
        /// <param name="count">最大讀取數。</param><param name="cancellationToken">底層實際 I/O 使用的 caller token。</param>
        /// <returns>底層唯一讀取作業；caller 須等待完成再 Dispose 串流。</returns>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            BaseStream.ReadAsync(buffer, offset, count, cancellationToken);

        /// <summary>直接轉交底層非同步寫入與取消，不讓 Stream 基底另建執行緒工作；支援能力仍由底層串流決定。</summary>
        /// <param name="buffer">本次操作借用的內容。</param><param name="offset">起點。</param><param name="count">寫入數。</param>
        /// <param name="cancellationToken">轉交底層 I/O 的 caller token。</param><returns>實際寫入作業。</returns>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            BaseStream.WriteAsync(buffer, offset, count, cancellationToken);

        /// <summary>轉交底層真正的非同步 flush；不保存 caller token 或建立假非同步排程。</summary>
        /// <param name="cancellationToken">當次 flush 的 caller 取消。</param><returns>底層 flush 作業。</returns>
        public override Task FlushAsync(CancellationToken cancellationToken) => BaseStream.FlushAsync(cancellationToken);

        /// <summary>
        /// 設定目前串流中的位置
        /// Sets the position within the current stream
        /// </summary>
        /// <param name="offset">相對於 origin 參數的位元組位移 / Byte offset relative to origin</param>
        /// <param name="origin">型別 SeekOrigin 的值，指出用來取得新位置的參考點 / Reference point for obtaining new position</param>
        /// <returns>目前串流中的新位置 / New position within the current stream</returns>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override long Seek(long offset, SeekOrigin origin) => BaseStream.Seek(offset, origin);

        /// <summary>
        /// 設定目前串流的長度
        /// Sets the length of the current stream
        /// </summary>
        /// <param name="value">目前串流的所需長度（以位元組為單位）/ Desired length of current stream in bytes</param>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override void SetLength(long value) => BaseStream.SetLength(value);

        /// <summary>
        /// 將位元組序列寫入至目前的串流，並將這個串流中的目前位置往前移動寫入的位元組數
        /// Writes a sequence of bytes to the current stream and advances the current position
        /// </summary>
        /// <param name="buffer">位元組陣列 / Byte array</param>
        /// <param name="offset">buffer 中以零為起始的位元組位移 / Zero-based byte offset in buffer</param>
        /// <param name="count">要寫入目前串流的位元組數 / Number of bytes to write</param>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override void Write(byte[] buffer, int offset, int count) => BaseStream.Write(buffer, offset, count);

        /// <summary>
        /// 釋放 ContentStream 所使用的 Unmanaged 資源，並選擇性地釋放 Managed 資源
        /// Releases unmanaged resources used by ContentStream and optionally releases managed resources
        /// </summary>
        /// <param name="disposing">true 表示釋放 Managed 和 Unmanaged 資源；false 則只釋放 Unmanaged 資源</param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    Stream stream;
                    IDisposable owner;
                    lock (_disposeGate)
                    {
                        stream = _baseStream;
                        owner = _responseOwner;
                        _baseStream = null;
                        _responseOwner = null;
                        _contentHeaders = null;
                    }
                    // Response owns 它的 content 與串流；無 response 的舊建構式才獨立釋放 stream。
                    // 先清參照再釋放，重複或重入 Dispose 不會留下 owner，也不雙重處置借來的目的地。
                    if (owner != null) owner.Dispose();
                    else stream?.Dispose();
                }
            }
            finally { base.Dispose(disposing); }
        }
    }
}

```

## Line.Messaging/HttpResponseMessageExtensions.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/HttpResponseMessageExtensions.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class HttpResponseMessageExtensions
// 主要成員：EnsureSuccessStatusCodeAsync
// 引用命名空間：Newtonsoft.Json、System.Net.Http、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Line.Messaging
{
    internal static class HttpResponseMessageExtensions
    {
        /// <summary>
        /// 驗證遠端 HTTP 狀態。錯誤 body 是不受信任的遠端資料，可能帶入個資或憑證；
        /// 不放進 Exception.Message、公開 DTO 或 ToString，避免上層記錄錯誤時跨請求洩漏。
        /// </summary>
        /// <param name="response">由單次 API 呼叫擁有、稍後確定性 Dispose 的 response。</param>
        /// <returns>成功時原 response；失敗時拋只含 status code 的安全錯誤。</returns>
        internal static Task<HttpResponseMessage> EnsureSuccessStatusCodeAsync(this HttpResponseMessage response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (response.IsSuccessStatusCode) return Task.FromResult(response);

            // 僅保留 HTTP 狀態分類；不解析、回顯或保存遠端錯誤 body。
            var safeError = new ErrorResponseMessage
            {
                Message = "LINE API request failed.",
                Details = Array.Empty<ErrorResponseMessage.ErrorDetails>()
            };
            throw new LineResponseException($"{safeError.Message} HTTP {(int)response.StatusCode}.")
            {
                StatusCode = response.StatusCode,
                ResponseMessage = safeError
            };
        }
    }
}

```

## Line.Messaging/Liff/LiffClient.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Liff/LiffClient.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LiffClient
// 主要成員：AddLiffAppAsync、UpdateLiffAppAsync、GetAllLiffAppAsync、DeleteLiffAppAsync、Dispose
// 引用命名空間：Newtonsoft.Json、System、System.Collections.Generic、System.Net.Http、System.Net.Http.Headers、System.Text、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Line.Messaging.Liff
{
    /// <summary>
    /// LINE LIFF 管理 API，保留既有 JSON 與公開入口。
    /// 一個實例只代表一個伺服器端 channel；與 Messaging client 共用隔離 transport，
    /// 不修改 HttpClient 預設 credential、不快取 request／response 或 LIFF 使用者資料。
    /// 方法完成即釋放 request/content/response，原始失敗保持向外傳遞。
    /// </summary>
    public class LiffClient : IDisposable
    {
        private readonly ChannelHttpTransport _transport;
        private readonly JsonSerializerSettings _settings = new CamelCaseJsonSerializerSettings();
        private readonly string _requestUri;

        /// <summary>
        /// 借入由 Host 管理生命週期及有限 Timeout 的 HttpClient；本實例 Dispose 不會關閉其他 channel 共用的 transport。
        /// </summary>
        /// <param name="httpClient">不得含使用者 cookie 或預設認證的共用 transport。</param>
        /// <param name="channelAccessToken">此伺服器端 channel 的 credential，不接受使用者輸入當授權。</param>
        /// <param name="requestUri">由受信任組合根指定的 LIFF API 端點，不是 request 路由輸入。</param>
        public LiffClient(HttpClient httpClient, string channelAccessToken, string requestUri = "https://api.line.me/liff/v1/apps")
        {
            _transport = new ChannelHttpTransport(httpClient, channelAccessToken);
            _requestUri = requestUri;
        }

        /// <summary>
        /// 舊式自有 HttpClient 入口；保留相容性，呼叫者必須先排空所有操作再 Dispose，建議改用主機共用 transport。
        /// </summary>
        /// <param name="channelAccessToken">只供此實例使用的 channel credential。</param>
        /// <param name="requestUri">受信任的 LIFF API 端點。</param>
        [Obsolete("Use the constructor accepting HttpClient to avoid socket exhaustion.")]
        public LiffClient(string channelAccessToken, string requestUri = "https://api.line.me/liff/v1/apps")
        {
            _transport = new ChannelHttpTransport(channelAccessToken);
            _requestUri = requestUri;
        }

        /// <summary>
        /// 新增 LIFF app；保留既有 view 包裝序列化，讀完 app ID 才釋放回應，不保留新 app 資料。
        /// </summary>
        /// <param name="viewType">既有 LIFF 尺寸。</param>
        /// <param name="url">呼叫端已驗證的 HTTPS app 網址。</param>
        /// <returns>LINE 回傳的 LIFF ID。</returns>
        public async Task<string> AddLiffAppAsync(ViewType viewType, string url)
        {
            using var response = await _transport.SendAsync(JsonRequest(HttpMethod.Post, _requestUri, new { view = new View(viewType, url) })).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(result, new { liffId = "" }).liffId;
        }

        /// <summary>
        /// 更新 LIFF view；成功或失敗都釋放 response，真實失敗保持向外傳遞，不偽裝成功。
        /// </summary>
        /// <param name="liffId">呼叫端已授權的 app ID。</param>
        /// <param name="viewType">尺寸。</param>
        /// <param name="url">呼叫端已驗證的 HTTPS app 網址。</param>
        /// <returns>完整更新作業。</returns>
        public async Task UpdateLiffAppAsync(string liffId, ViewType viewType, string url)
        {
            using var response = await _transport.SendAsync(JsonRequest(HttpMethod.Put, $"{_requestUri}/{liffId}/view", new { type = viewType, url })).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得本 channel 的 app 清單；結果只屬此次操作，不保存為共用集合，response 解析後確定性關閉。
        /// </summary>
        /// <returns>呼叫端擁有的 detached app 清單。</returns>
        public async Task<IList<LiffApp>> GetAllLiffAppAsync()
        {
            using var response = await _transport.SendAsync(new HttpRequestMessage(HttpMethod.Get, _requestUri)).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(content, new { apps = new LiffApp[0] }).apps;
        }

        /// <summary>
        /// 刪除已授權 app；等待並驗證 HTTP 結果，不再把未檢查的 response 當成功且遺失 owner。
        /// </summary>
        /// <param name="liffId">呼叫端已授權的 app ID。</param>
        /// <returns>完整、已清理 response 的刪除作業。</returns>
        public async Task DeleteLiffAppAsync(string liffId)
        {
            using var response = await _transport.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{_requestUri}/{liffId}")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>先完成可能失敗的 JSON 組裝再配置 request；成功後唯一轉交 transport，避免序列化錯誤留下 request。</summary>
        /// <param name="method">固定 API 方法。</param>
        /// <param name="url">受信任且已組裝的端點。</param>
        /// <param name="value">僅屬當次操作的 body model。</param>
        /// <returns>呼叫者必須立刻交給 transport 的 request。</returns>
        private HttpRequestMessage JsonRequest(HttpMethod method, string url, object value)
        {
            var body = JsonConvert.SerializeObject(value, _settings);
            return new HttpRequestMessage(method, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        /// <summary>
        /// 冪等清除 channel credential 並拒絕新請求；只有舊建構式的自有 transport 會被釋放。呼叫端必須先排空既有作業。
        /// </summary>
        public void Dispose() => _transport.Dispose();
    }
}

```

## Line.Messaging/Line.Messaging.csproj
```
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net462;net10.0</TargetFrameworks>
    <Product>Line Messaging API</Product>
    <Description>SDK for the LINE Messaging API for C# - Upgraded to .NET 10</Description>
    <Version>1.4.6</Version>
    <PackageLicenseUrl>https://github.com/pierre3/LineMessagingApi/blob/master/LICENSE</PackageLicenseUrl>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <PackageTags>Line Messaging API;line;bot;.NET 10</PackageTags>
    <PackageProjectUrl>https://github.com/pierre3/LineMessagingApi/</PackageProjectUrl>
    <IncludeSymbols>true</IncludeSymbols>
    <RepositoryUrl>https://github.com/pierre3/LineMessagingApi/</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageReleaseNotes>
Upgraded to .NET 10
- Target Framework: .NET 10.0
- Updated language version to latest
- Enabled nullable reference types
- Updated Newtonsoft.Json to 13.0.3
- Improved async/await patterns
- Enhanced performance and memory management
- Compatible with modern .NET applications

Original release notes (1.4.5):
1. Supports the API update at 1/17/2019
https://developers.line.biz/en/news/2019/01/
- New endpoints added to Messaging API

2. We reviewed the implementation of the LineMessagingClient class.
- Added interface ILineMessagingClient.
- Changed methods of LineMessagingClient class to virtual method.
    </PackageReleaseNotes>
    <AssemblyVersion>1.4.6.0</AssemblyVersion>
    <FileVersion>1.4.6.0</FileVersion>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <NoWarn>$(NoWarn);1701;1702;1705;1591;CS0618;CS1570;CS1573;CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625;CS8765;SYSLIB0013</NoWarn>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|AnyCPU'">
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|AnyCPU'">
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net462'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <Reference Include="System.Net.Http" />
    <Reference Include="Microsoft.CSharp" />
  </ItemGroup>
</Project>

```

## Line.Messaging/LineMessagingClient.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/LineMessagingClient.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineMessagingClient
// 主要成員：NormalizeLineApiBaseUri、DeriveDataUri、ApiUrl、DataUrl、ApplyRetryKeyHeader、CombineBaseAndPath、IssueChannelAccessTokenAsync、RevokeChannelAccessTokenAsync、CreateAsync、ReplyMessageAsync
// 引用命名空間：Newtonsoft.Json、Newtonsoft.Json.Serialization、System、System.Collections.Generic、System.IO、System.Linq、System.Net.Http、System.Net.Http.Headers
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq; // RichMenu list endpoint 回傳包在 richmenus 陣列中，需用 JObject/JArray 解析。

namespace Line.Messaging
{
    /// <summary>
    /// LINE Messaging API 客戶端，集中處理與 LINE 伺服器的請求和回應。
    /// </summary>
    /// <remarks>
    /// 此類別提供完整的 LINE Messaging API 功能，包括：
    /// - 訊息傳送（回覆、推播、多播、廣播）
    /// - 使用者資料管理
    /// - 群組與聊天室管理
    /// - Rich Menu 管理
    /// - Webhook 設定
    /// - 訊息配額查詢
    /// <para>
    /// 官方文件：https://developers.line.biz/en/reference/messaging-api/
    /// </para>
    /// </remarks>
    /// <example>
    /// 基本使用範例：
    /// <code>
    /// // 建立客戶端
    /// var client = new LineMessagingClient("YOUR_CHANNEL_ACCESS_TOKEN");
    ///
    /// // 回覆訊息
    /// await client.ReplyMessageAsync(replyToken, new TextMessage("Hello!"));
    ///
    /// // 推播訊息
    /// await client.PushMessageAsync(userId, new TextMessage("Hi!"));
    ///
    /// // 取得使用者資料
    /// var profile = await client.GetUserProfileAsync(userId);
    /// </code>
    /// </example>
    /// <seealso cref="ILineMessagingClient"/>
    public class LineMessagingClient : ILineMessagingClient, IDisposable
    {
        /// <summary>
        /// LINE JSON API 預設 URI。
        /// </summary>
        private const string DEFAULT_URI = "https://api.line.me/v2";

        /// <summary>
        /// LINE binary data API 預設 URI。
        /// Content 與 Rich Menu 圖檔端點官方要求使用 api-data.line.me，不可共用一般 JSON API host。
        /// </summary>
        private const string DEFAULT_DATA_URI = "https://api-data.line.me/v2";

        /// <summary>此 SDK 實例唯一的 channel 傳輸 owner；Messaging 與 LIFF 共用同一認證隔離契約。</summary>
        private readonly ChannelHttpTransport _transport;

        /// <summary>
        /// JSON 序列化設定（使用 Camel Case 命名）
        /// JSON serializer settings (using Camel Case naming)
        /// </summary>
        private JsonSerializerSettings _jsonSerializerSettings;

        /// <summary>
        /// LINE API 的基礎 URI
        /// Base URI for LINE API
        /// </summary>
        private string _uri;

        /// <summary>
        /// LINE binary data API 的基礎 URI。
        /// </summary>
        private string _dataUri;

        /// <summary>
        /// 初始化 LineMessagingClient - 使用外部提供的 HttpClient（建議用於 DI 情境）
        /// Initializes LineMessagingClient using externally provided HttpClient (recommended for DI scenarios)
        /// </summary>
        /// <param name="httpClient">外部提供的 HttpClient 實例（不會被 Dispose）</param>
        /// <param name="channelAccessToken">Channel Access Token</param>
        /// <param name="uri">LINE API 的基礎 URI（選用）</param>
        public LineMessagingClient(HttpClient httpClient, string channelAccessToken, string uri = DEFAULT_URI)
        {
            _transport = new ChannelHttpTransport(httpClient, channelAccessToken);
            _jsonSerializerSettings = new CamelCaseJsonSerializerSettings();
            _uri = NormalizeLineApiBaseUri(uri);
            _dataUri = DeriveDataUri(_uri);
        }

        /// <summary>
        /// 初始化 LineMessagingClient - 建立內部 HttpClient（向後相容，但不建議用於生產環境）
        /// Initializes LineMessagingClient with internal HttpClient (backward compatible, not recommended for production)
        /// </summary>
        /// <param name="channelAccessToken">Channel Access Token</param>
        /// <param name="uri">LINE API 的基礎 URI（選用）</param>
        [Obsolete("建議使用接受 HttpClient 參數的建構函式，以避免 Socket 耗盡問題。Use constructor with HttpClient parameter to avoid socket exhaustion.")]
        public LineMessagingClient(string channelAccessToken, string uri = DEFAULT_URI)
        {
            _transport = new ChannelHttpTransport(channelAccessToken);
            _jsonSerializerSettings = new CamelCaseJsonSerializerSettings();
            _uri = NormalizeLineApiBaseUri(uri);
            _dataUri = DeriveDataUri(_uri);
        }

        private static string NormalizeLineApiBaseUri(string apiUri)
        {
            if (string.IsNullOrWhiteSpace(apiUri))
            {
                return DEFAULT_URI;
            }

            var normalizedUri = apiUri.TrimEnd('/');
            return normalizedUri.EndsWith("/v2", StringComparison.OrdinalIgnoreCase)
                ? normalizedUri
                : normalizedUri + "/v2";
        }

        private static string DeriveDataUri(string apiUri)
        {
            if (string.IsNullOrWhiteSpace(apiUri))
            {
                return DEFAULT_DATA_URI;
            }

            return apiUri.Replace("https://api.line.me", "https://api-data.line.me");
        }

        private string ApiUrl(string path)
        {
            return CombineBaseAndPath(_uri, path);
        }

        private string DataUrl(string path)
        {
            return CombineBaseAndPath(_dataUri, path);
        }

        private static void ApplyRetryKeyHeader(HttpRequestMessage request, string? retryKey)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(retryKey))
            {
                return;
            }

            request.Headers.TryAddWithoutValidation("X-Line-Retry-Key", retryKey);
        }

        private static string CombineBaseAndPath(string baseUri, string path)
        {
            if (string.IsNullOrWhiteSpace(baseUri))
            {
                throw new ArgumentException("Base URI is required.", nameof(baseUri));
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is required.", nameof(path));
            }

            var normalizedBase = baseUri.TrimEnd('/');
            var normalizedPath = path.TrimStart('/');

            return normalizedBase + "/" + normalizedPath;
        }

        #region OAuth
        /// <summary>
        /// 發行 Channel Access Token（靜態方法）
        /// Issues a Channel Access Token (static method)
        /// </summary>
        /// <param name="httpClient">
        /// HTTP 客戶端實例
        /// HTTP client instance
        /// </param>
        /// <param name="channelId">
        /// Channel ID（從 LINE Developers Console 取得）
        /// Channel ID (from LINE Developers Console)
        /// </param>
        /// <param name="channelAccessToken">
        /// Channel Secret（從 LINE Developers Console 取得）
        /// Channel Secret (from LINE Developers Console)
        /// </param>
        /// <param name="uri">
        /// LINE API 的基礎 URI（選用）
        /// Base URI for LINE API (optional)
        /// </param>
        /// <returns>
        /// Channel Access Token 物件，包含 access token 和過期時間
        /// Channel Access Token object containing access token and expiration time
        /// </returns>
        /// <remarks>
        /// 此方法使用 OAuth 2.0 Client Credentials 流程來取得 Channel Access Token。
        /// Token 有效期為 30 天（v2.1）或作為無狀態 token（v3）。
        /// <para>
        /// This method uses OAuth 2.0 Client Credentials flow to obtain Channel Access Token.
        /// Token is valid for 30 days (v2.1) or as stateless token (v3).
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#issue-channel-access-token
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#issue-channel-access-token
        /// </remarks>
        /// <example>
        /// <code>
        /// using (var httpClient = new HttpClient())
        /// {
        ///     var token = await LineMessagingClient.IssueChannelAccessTokenAsync(
        ///         httpClient,
        ///         "YOUR_CHANNEL_ID",
        ///         "YOUR_CHANNEL_SECRET"
        ///     );
        ///     Console.WriteLine($"Access Token: {token.AccessToken}");
        ///     Console.WriteLine($"Expires In: {token.ExpiresIn} seconds");
        /// }
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public static async Task<ChannelAccessToken> IssueChannelAccessTokenAsync(HttpClient httpClient, string channelId, string channelAccessToken, string uri = DEFAULT_URI)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = channelId,
                    ["client_secret"] = channelAccessToken
                });
            using var response = await httpClient.PostAsync(CombineBaseAndPath(NormalizeLineApiBaseUri(uri), "/oauth/accessToken"), content).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<ChannelAccessToken>(json,
                new JsonSerializerSettings
                {
                    ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() }
                });
        }

        /// <summary>
        /// 撤銷 Channel Access Token（靜態方法）
        /// Revokes a Channel Access Token (static method)
        /// </summary>
        /// <param name="httpClient">
        /// HTTP 客戶端實例
        /// HTTP client instance
        /// </param>
        /// <param name="channelAccessToken">
        /// 要撤銷的 Channel Access Token
        /// Channel Access Token to revoke
        /// </param>
        /// <param name="uri">
        /// LINE API 的基礎 URI（選用）
        /// Base URI for LINE API (optional)
        /// </param>
        /// <remarks>
        /// 撤銷後，該 Access Token 將立即失效，無法再用於 API 呼叫。
        /// <para>
        /// After revocation, the Access Token becomes immediately invalid and cannot be used for API calls.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#revoke-channel-access-token
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#revoke-channel-access-token
        /// </remarks>
        /// <example>
        /// <code>
        /// using (var httpClient = new HttpClient())
        /// {
        ///     await LineMessagingClient.RevokeChannelAccessTokenAsync(
        ///         httpClient,
        ///         "YOUR_ACCESS_TOKEN_TO_REVOKE"
        ///     );
        ///     Console.WriteLine("Access Token has been revoked.");
        /// }
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public static async Task RevokeChannelAccessTokenAsync(HttpClient httpClient, string channelAccessToken, string uri = DEFAULT_URI)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["access_token"] = channelAccessToken });
            using var response = await httpClient.PostAsync(CombineBaseAndPath(NormalizeLineApiBaseUri(uri), "/oauth/revoke"), content).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 建立 LineMessagingClient 實例（使用 Channel ID 和 Secret 自動取得 Access Token）
        /// Creates a LineMessagingClient instance (automatically obtains Access Token using Channel ID and Secret)
        /// </summary>
        /// <param name="channelId">
        /// Channel ID（從 LINE Developers Console 取得）
        /// Channel ID (from LINE Developers Console)
        /// </param>
        /// <param name="channelSecret">
        /// Channel Secret（從 LINE Developers Console 取得）
        /// Channel Secret (from LINE Developers Console)
        /// </param>
        /// <param name="uri">
        /// LINE API 的基礎 URI（選用）
        /// Base URI for LINE API (optional)
        /// </param>
        /// <returns>
        /// 已設定好的 LineMessagingClient 實例
        /// Configured LineMessagingClient instance
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// 當 channelId 或 channelSecret 為 null 或空字串時拋出
        /// Thrown when channelId or channelSecret is null or empty
        /// </exception>
        /// <remarks>
        /// 此方法會自動執行以下步驟：
        /// 1. 使用 Channel ID 和 Secret 取得 Access Token
        /// 2. 建立並回傳 LineMessagingClient 實例
        /// <para>
        /// This method automatically performs the following steps:
        /// 1. Obtains Access Token using Channel ID and Secret
        /// 2. Creates and returns LineMessagingClient instance
        /// </remarks>
        /// <example>
        /// <code>
        /// // 自動取得 Access Token 並建立客戶端
        /// var client = await LineMessagingClient.CreateAsync(
        ///     "YOUR_CHANNEL_ID",
        ///     "YOUR_CHANNEL_SECRET"
        /// );
        ///
        /// // 立即使用客戶端
        /// await client.PushMessageAsync(userId, new TextMessage("Hello!"));
        /// </code>
        /// </example>
        public static async Task<LineMessagingClient> CreateAsync(HttpClient httpClient, string channelId, string channelSecret, string uri = DEFAULT_URI)
        {
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            if (string.IsNullOrEmpty(channelId)) throw new ArgumentNullException(nameof(channelId));
            if (string.IsNullOrEmpty(channelSecret)) throw new ArgumentNullException(nameof(channelSecret));

            var accessToken = await IssueChannelAccessTokenAsync(httpClient, channelId, channelSecret, uri).ConfigureAwait(false);
            return new LineMessagingClient(httpClient, accessToken.AccessToken, uri);
        }

        /// <summary>
        /// 建立 LineMessagingClient 實例（向後相容版本，不建議用於生產環境）
        /// </summary>
        [Obsolete("建議使用接受 HttpClient 參數的 CreateAsync 方法，以避免 Socket 耗盡問題")]
        public static async Task<LineMessagingClient> CreateAsync(string channelId, string channelSecret, string uri = DEFAULT_URI)
        {
            if (string.IsNullOrEmpty(channelId)) throw new ArgumentNullException(nameof(channelId));
            if (string.IsNullOrEmpty(channelSecret)) throw new ArgumentNullException(nameof(channelSecret));
            using (var client = new HttpClient())
            {
                var accessToken = await IssueChannelAccessTokenAsync(client, channelId, channelSecret, uri).ConfigureAwait(false);
#pragma warning disable CS0618 // 使用過時的建構函式（向後相容）
                return new LineMessagingClient(accessToken.AccessToken, uri);
#pragma warning restore CS0618
            }
        }
        #endregion

        #region Message
        /// <summary>
        /// 回覆訊息給使用者、群組或聊天室
        /// Responds to events from users, groups, and rooms
        /// </summary>
        /// <param name="replyToken">
        /// Reply Token，從 Webhook 事件中取得（有效期 30 秒，只能使用一次）
        /// Reply Token obtained from webhook event (valid for 30 seconds, can only be used once)
        /// </param>
        /// <param name="messages">
        /// 要回覆的訊息清單（最多 5 則）
        /// List of messages to reply (maximum 5 messages)
        /// </param>
        /// <remarks>
        /// 注意事項：
        /// - Reply Token 只能使用一次，且必須在收到 Webhook 後 30 秒內使用
        /// - 最多可以回覆 5 則訊息
        /// - 超過時間或重複使用會導致 API 錯誤
        /// <para>
        /// Important notes:
        /// - Reply Token can only be used once and must be used within 30 seconds after receiving webhook
        /// - Maximum 5 messages can be sent
        /// - Timeout or duplicate use will cause API error
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#send-reply-message
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#send-reply-message
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 回覆單一文字訊息
        /// await client.ReplyMessageAsync(replyToken, new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("感謝您的訊息！")
        /// });
        ///
        /// // 回覆多則訊息（文字 + 圖片）
        /// await client.ReplyMessageAsync(replyToken, new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("這是一張照片："),
        ///     new ImageMessage("https://example.com/image.jpg", "https://example.com/thumb.jpg")
        /// });
        /// </code>
        /// </example>
        /// <seealso cref="PushMessageAsync(string, IList{ISendMessage})"/>
        /// <seealso cref="MultiCastMessageAsync(IList{string}, IList{ISendMessage})"/>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task ReplyMessageAsync(string replyToken, IList<ISendMessage> messages)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/reply");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { replyToken, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 回覆文字訊息給使用者、群組或聊天室（簡化版）
        /// Responds with text messages to events from users, groups, and rooms (simplified version)
        /// </summary>
        /// <param name="replyToken">
        /// Reply Token，從 Webhook 事件中取得
        /// Reply Token obtained from webhook event
        /// </param>
        /// <param name="messages">
        /// 要回覆的文字訊息陣列（最多 5 則，每則最多 2000 字元）
        /// Array of text messages to reply (maximum 5 messages, each up to 2000 characters)
        /// </param>
        /// <remarks>
        /// 這是 ReplyMessageAsync 的簡化版本，自動將文字轉換為 TextMessage 物件。
        /// <para>
        /// This is a simplified version of ReplyMessageAsync that automatically converts text to TextMessage objects.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 回覆單一文字訊息
        /// await client.ReplyMessageAsync(replyToken, "謝謝！");
        ///
        /// // 回覆多則文字訊息
        /// await client.ReplyMessageAsync(replyToken, "第一則訊息", "第二則訊息", "第三則訊息");
        /// </code>
        /// </example>
        public virtual Task ReplyMessageAsync(string replyToken, params string[] messages)
            => ReplyMessageAsync(replyToken, messages.Select(m => new TextMessage(m)).ToArray());

        /// <summary>
        /// 使用 JSON 字串回覆訊息
        /// Replies with messages using JSON strings
        /// </summary>
        /// <param name="replyToken">
        /// Reply Token，從 Webhook 事件中取得
        /// Reply Token obtained from webhook event
        /// </param>
        /// <param name="messages">
        /// JSON 格式的訊息陣列（每個元素為完整的訊息 JSON 字串）
        /// Array of messages in JSON format (each element is a complete message JSON string)
        /// </param>
        /// <remarks>
        /// 此方法適合於需要完全控制 JSON 格式的進階情境。
        /// 一般情況建議使用 ReplyMessageAsync(string, IList&lt;ISendMessage&gt;)。
        /// <para>
        /// This method is suitable for advanced scenarios requiring full control of JSON format.
        /// For general use, ReplyMessageAsync(string, IList&lt;ISendMessage&gt;) is recommended.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.ReplyMessageWithJsonAsync(
        ///     replyToken,
        ///     "{\"type\":\"text\",\"text\":\"Hello\"}",
        ///     "{\"type\":\"sticker\",\"packageId\":\"1\",\"stickerId\":\"1\"}"
        /// );
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task ReplyMessageWithJsonAsync(string replyToken, params string[] messages)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/reply");
            var json = $"{{ \"replyToken\" : \"{replyToken}\", \"messages\" : [{string.Join(", ", messages)}] }}";
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 推播訊息給使用者、群組或聊天室
        /// Sends push messages to a user, group, or room at any time
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID（userId、groupId 或 roomId）
        /// ID of the receiver (userId, groupId, or roomId)
        /// </param>
        /// <param name="messages">
        /// 要傳送的訊息清單（最多 5 則）
        /// List of messages to send (maximum 5 messages)
        /// </param>
        /// <remarks>
        /// 推播訊息的限制：
        /// - 僅特定方案支援推播訊息功能
        /// - 需消耗訊息配額
        /// - 可隨時傳送，不需要 Reply Token
        /// - 最多可傳送 5 則訊息
        /// <para>
        /// Push message limitations:
        /// - Only available for certain plans
        /// - Consumes message quota
        /// - Can be sent at any time without Reply Token
        /// - Maximum 5 messages can be sent
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#send-push-message
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#send-push-message
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 推播給特定使用者
        /// await client.PushMessageAsync(userId, new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("這是一則推播訊息！")
        /// });
        ///
        /// // 推播給群組
        /// await client.PushMessageAsync(groupId, new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("群組公告"),
        ///     new ImageMessage("https://example.com/notice.jpg", "https://example.com/notice_thumb.jpg")
        /// });
        /// </code>
        /// </example>
        /// <seealso cref="ReplyMessageAsync(string, IList{ISendMessage})"/>
        /// <seealso cref="MultiCastMessageAsync(IList{string}, IList{ISendMessage})"/>
        public virtual Task PushMessageAsync(string to, IList<ISendMessage> messages)
            => PushMessageAsync(to, messages, retryKey: null);

        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task PushMessageAsync(string to, IList<ISendMessage> messages, string? retryKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/push"));
            ApplyRetryKeyHeader(request, retryKey);
            request.Content = new StringContent(JsonConvert.SerializeObject(new { to, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 使用 JSON 字串推播訊息
        /// Sends push messages using JSON strings
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID
        /// ID of the receiver
        /// </param>
        /// <param name="messages">
        /// JSON 格式的訊息陣列
        /// Array of messages in JSON format
        /// </param>
        /// <remarks>
        /// 此方法適用於需要完全控制 JSON 格式的進階情境。
        /// <para>
        /// This method is suitable for advanced scenarios requiring full control of JSON format.
        /// </para>
        /// </remarks>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task PushMessageWithJsonAsync(string to, params string[] messages)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/push");
            var json = $"{{ \"to\" : \"{to}\", \"messages\" : [{string.Join(", ", messages)}] }}";
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 推播文字訊息（簡化版）
        /// Sends push text messages (simplified version)
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID
        /// ID of the receiver
        /// </param>
        /// <param name="messages">
        /// 要推播的文字訊息陣列（最多 5 則）
        /// Array of text messages to send (maximum 5 messages)
        /// </param>
        /// <example>
        /// <code>
        /// await client.PushMessageAsync(userId, "通知訊息", "第二則訊息");
        /// </code>
        /// </example>
        public virtual Task PushMessageAsync(string to, params string[] messages)
            => PushMessageAsync(to, messages.Select(m => new TextMessage(m)).ToArray());

        /// <summary>
        /// 多播訊息給多位使用者
        /// Sends push messages to multiple users at any time
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID 清單（最多 500 位使用者）
        /// List of receiver IDs (maximum 500 users)
        /// </param>
        /// <param name="messages">
        /// 要傳送的訊息清單（最多 5 則）
        /// List of messages to send (maximum 5 messages)
        /// </param>
        /// <remarks>
        /// 多播訊息的限制：
        /// - 僅支援推播訊息的方案可用
        /// - 無法傳送至群組或聊天室
        /// - 最多 500 位接收者
        /// - 最多 5 則訊息
        /// <para>
        /// Multicast message limitations:
        /// - Only available for plans supporting push messages
        /// - Cannot be sent to groups or rooms
        /// - Maximum 500 receivers
        /// - Maximum 5 messages
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#send-multicast-message
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#send-multicast-message
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var userIds = new List&lt;string&gt; { "U1234567890", "U0987654321", "U1111111111" };
        /// await client.MultiCastMessageAsync(userIds, new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("群發訊息給多位使用者")
        /// });
        /// </code>
        /// </example>
        public virtual Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages)
            => MultiCastMessageAsync(to, messages, retryKey: null);

        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages, string? retryKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/multicast"));
            ApplyRetryKeyHeader(request, retryKey);
            request.Content = new StringContent(JsonConvert.SerializeObject(new { to, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 使用 JSON 字串多播訊息
        /// Sends multicast messages using JSON strings
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID 清單
        /// List of receiver IDs
        /// </param>
        /// <param name="messages">
        /// JSON 格式的訊息陣列
        /// Array of messages in JSON format
        /// </param>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task MultiCastMessageWithJsonAsync(IList<string> to, params string[] messages)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/multicast");
            var quotedIds = string.Join(", ", to.Select(x => $"\"{x}\""));
            var json = $"{{ \"to\" : [{quotedIds}], \"messages\" : [{string.Join(", ", messages)}] }}";
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 多播文字訊息（簡化版）
        /// Sends multicast text messages (simplified version)
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID 清單
        /// List of receiver IDs
        /// </param>
        /// <param name="messages">
        /// 要多播的文字訊息陣列
        /// Array of text messages to multicast
        /// </param>
        public virtual Task MultiCastMessageAsync(IList<string> to, params string[] messages)
            => MultiCastMessageAsync(to, messages.Select(m => new TextMessage(m)).ToArray());

        /// <summary>
        /// 廣播訊息給所有好友
        /// Broadcasts push messages to all friends
        /// </summary>
        /// <param name="messages">
        /// 要廣播的訊息清單（最多 5 則）
        /// List of messages to broadcast (maximum 5 messages)
        /// </param>
        /// <remarks>
        /// 廣播訊息會傳送給所有加入機器人為好友的使用者（包含群組和聊天室）。
        /// 注意：會消耗大量訊息配額，請謹慎使用。
        /// <para>
        /// Broadcast messages are sent to all users who have added the bot as a friend (including groups and rooms).
        /// Note: Consumes large message quota, use with caution.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#send-broadcast-message
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#send-broadcast-message
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 廣播重要公告
        /// await client.BroadcastMessageAsync(new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("重要公告：系統將於今晚維護")
        /// });
        /// </code>
        /// </example>
        public virtual Task BroadcastMessageAsync(IList<ISendMessage> messages)
            => BroadcastMessageAsync(messages, retryKey: null);

        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task BroadcastMessageAsync(IList<ISendMessage> messages, string? retryKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/broadcast"));
            ApplyRetryKeyHeader(request, retryKey);
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 窄播訊息給特定條件的使用者
        /// Sends narrowcast messages to users matching specific criteria
        /// </summary>
        /// <param name="messages">
        /// 要傳送的訊息清單（最多 5 則）
        /// List of messages to send (maximum 5 messages)
        /// </param>
        /// <param name="recipient">
        /// 接收者物件（選用）- 使用篩選器或受眾 ID 指定接收者（最多 10 個接收者物件）
        /// Recipient object (optional) - Specify recipients using filters or audience IDs (maximum 10 recipient objects)
        /// </param>
        /// <param name="filter">
        /// 篩選器物件（選用）- 人口統計篩選器物件，可使用 LINE 官方帳號的好友資料
        /// Filter object (optional) - Demographic filter object using LINE Official Account friends data
        /// </param>
        /// <param name="limit">
        /// 限制物件（選用）- 窄播訊息的最大傳送數量，用於限制接收訊息的人數
        /// Limit object (optional) - Maximum number of narrowcast messages to send
        /// </param>
        /// <returns>
        /// Request ID - 用於查詢窄播進度
        /// Request ID - Used to query narrowcast progress
        /// </returns>
        /// <remarks>
        /// 窄播訊息可根據以下條件篩選接收者：
        /// - 性別、年齡、作業系統、地區
        /// - 加好友的時間長度
        /// - 重新定向受眾（audiences）
        /// <para>
        /// Narrowcast messages can filter recipients based on:
        /// - Gender, age, OS, region
        /// - Friendship duration
        /// - Retargeting audiences
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#send-narrowcast-message
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#send-narrowcast-message
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 傳送給特定受眾
        /// var requestId = await client.NarrowcastMessageAsync(
        ///     new List&lt;ISendMessage&gt; { new TextMessage("窄播訊息") },
        ///     recipient: new { type = "audience", audienceGroupId = "12345" }
        /// );
        ///
        /// // 查詢傳送進度
        /// var progress = await client.GetNarrowcastProgressAsync(requestId);
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task<string> NarrowcastMessageAsync(IList<ISendMessage> messages, object recipient = null, object filter = null, object limit = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/narrowcast");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages, recipient, filter, limit }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            if (response.Headers.TryGetValues("X-Line-Request-Id", out var values)) return values.FirstOrDefault();
            return null;
        }

        /// <summary>
        /// 取得窄播訊息的傳送進度
        /// Gets the status of narrowcast message sending
        /// </summary>
        /// <param name="requestId">
        /// 窄播訊息傳送時回傳的 Request ID
        /// Request ID returned when sending narrowcast message
        /// </param>
        /// <returns>
        /// 窄播進度資訊，包含成功、失敗數量等
        /// Narrowcast progress information including success and failure counts
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-narrowcast-progress-status
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-narrowcast-progress-status
        /// </remarks>
        /// <example>
        /// <code>
        /// var progress = await client.GetNarrowcastProgressAsync(requestId);
        /// Console.WriteLine($"狀態: {progress.Phase}");
        /// Console.WriteLine($"成功: {progress.SuccessCount}");
        /// Console.WriteLine($"失敗: {progress.FailureCount}");
        /// </code>
        /// </example>
        public virtual async Task<NarrowcastProgress> GetNarrowcastProgressAsync(string requestId)
        {
            var json = await GetStringAsync($"{_uri}/bot/message/progress/narrowcast?requestId={requestId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<NarrowcastProgress>(json);
        }

        /// <summary>
        /// 將來自使用者的訊息標記為已讀
        /// Marks messages from users as read
        /// </summary>
        /// <param name="markAsReadToken">
        /// 已驗簽 webhook 內的 markAsReadToken，不接受 userId、groupId 或 roomId。
        /// 此操作只持有 token 到本次傳輸完成；request 與 response 均在方法結束時釋放。
        /// </param>
        /// <remarks>
        /// 標記為已讀後，使用者端會顯示訊息已讀取。
        /// <para>
        /// After marking as read, the user will see the message as read.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#mark-messages-as-read
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#mark-messages-as-read
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 標記一對一聊天為已讀
        /// await client.MarkAsReadByTokenAsync(markAsReadToken);
        ///
        /// // 標記群組訊息為已讀
        /// await client.MarkAsReadByTokenAsync(markAsReadToken);
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task MarkAsReadByTokenAsync(string markAsReadToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/chat/markAsRead"));
            request.Content = new StringContent(
                JsonConvert.SerializeObject(new { markAsReadToken }, _jsonSerializerSettings),
                Encoding.UTF8,
                "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        [Obsolete("Use MarkAsReadByTokenAsync(markAsReadToken). LINE official API uses markAsReadToken, not chatId.")]
        public virtual Task MarkAsReadAsync(string chatId)
        {
            throw new NotSupportedException(
                "LINE mark-as-read now requires markAsReadToken from webhook events. Use MarkAsReadByTokenAsync(markAsReadToken) instead of passing a chatId.");
        }

        /// <summary>
        /// 在聊天畫面顯示載入動畫
        /// Displays a loading animation on the chat screen
        /// </summary>
        /// <param name="chatId">
        /// 聊天識別碼 - 可以是 userId、groupId 或 roomId
        /// Chat identifier - Can be userId, groupId, or roomId
        /// </param>
        /// <param name="loadingSeconds">
        /// 顯示載入動畫的秒數（最多 60 秒，預設 20 秒）
        /// Number of seconds to display loading animation (maximum 60 seconds, default 20 seconds)
        /// </param>
        /// <remarks>
        /// 載入動畫適用於需要較長處理時間的情境，讓使用者知道機器人正在處理中。
        /// <para>
        /// Loading animation is suitable for scenarios requiring longer processing time, letting users know the bot is working.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#display-a-loading-animation
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#display-a-loading-animation
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 顯示預設 20 秒的載入動畫
        /// await client.ShowLoadingAnimationAsync(userId);
        ///
        /// // 顯示 30 秒的載入動畫
        /// await client.ShowLoadingAnimationAsync(userId, 30);
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task ShowLoadingAnimationAsync(string chatId, int loadingSeconds = 20)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/chat/loading/start");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { chatId, loadingSeconds }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得使用者傳送的圖片、影片、音訊等內容（以串流形式）
        /// Retrieves image, video, and audio data sent by users as a stream
        /// </summary>
        /// <param name="messageId">
        /// 訊息 ID（從 Webhook 事件中取得）
        /// Message ID (obtained from webhook event)
        /// </param>
        /// <returns>
        /// ContentStream 物件，包含內容資料和 HTTP 標頭
        /// ContentStream object containing content data and HTTP headers
        /// </returns>
        /// <remarks>
        /// 支援的內容類型：
        /// - 圖片（JPEG、PNG）
        /// - 影片（MP4）
        /// - 音訊（M4A）
        /// - 檔案
        /// <para>
        /// Supported content types:
        /// - Images (JPEG, PNG)
        /// - Videos (MP4)
        /// - Audio (M4A)
        /// - Files
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-content
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-content
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 下載使用者傳送的圖片
        /// using (var stream = await client.GetContentStreamAsync(messageId))
        /// {
        ///     var contentType = stream.ContentHeaders.ContentType.MediaType;
        ///     Console.WriteLine($"Content Type: {contentType}");
        ///
        ///     // 儲存到檔案
        ///     using (var fileStream = File.Create("downloaded_image.jpg"))
        ///     {
        ///         await stream.CopyToAsync(fileStream);
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <seealso cref="GetContentBytesAsync(string)"/>
        /// <remarks>成功時 response 唯一轉交回傳的 ContentStream；呼叫端必須 using，失敗時內部釋放。不將媒體或使用者來源存入共用快取。</remarks>
        public virtual async Task<ContentStream> GetContentStreamAsync(string messageId)
        {
            return await GetOwnedStreamAsync(DataUrl($"/bot/message/{messageId}/content")).ConfigureAwait(false);
        }

        /// <summary>
        /// 取得使用者傳送的圖片、影片、音訊等內容（以位元組陣列形式）
        /// Retrieves image, video, and audio data sent by users as a byte array
        /// </summary>
        /// <param name="messageId">
        /// 訊息 ID（從 Webhook 事件中取得）
        /// Message ID (obtained from webhook event)
        /// </param>
        /// <returns>
        /// 內容的位元組陣列
        /// Byte array of the content
        /// </returns>
        /// <remarks>
        /// 此方法適合較小的檔案。對於大型檔案，建議使用 GetContentStreamAsync。
        /// <para>
        /// This method is suitable for smaller files. For large files, GetContentStreamAsync is recommended.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 下載為位元組陣列
        /// var bytes = await client.GetContentBytesAsync(messageId);
        /// File.WriteAllBytes("downloaded_image.jpg", bytes);
        /// </code>
        /// </example>
        /// <seealso cref="GetContentStreamAsync(string)"/>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task<byte[]> GetContentBytesAsync(string messageId)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, DataUrl($"/bot/message/{messageId}/content"))).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 驗證影片或音訊內容的準備狀態
        /// Verifies the preparation status of video or audio content
        /// </summary>
        /// <param name="messageId">
        /// 訊息 ID
        /// Message ID
        /// </param>
        /// <returns>
        /// true 表示內容已就緒可下載；false 表示仍在處理中
        /// true if content is ready for download; false if still processing
        /// </returns>
        /// <remarks>
        /// 使用者上傳的影片或音訊可能需要一些時間進行處理。
        /// 在下載前先驗證可避免取得未完成處理的內容。
        /// <para>
        /// Videos or audio uploaded by users may require processing time.
        /// Verification before download prevents getting unprocessed content.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#verify-the-preparation-status
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#verify-the-preparation-status
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 等待內容準備完成
        /// while (!await client.VerifyContentPreparationAsync(messageId))
        /// {
        ///     await Task.Delay(1000); // 等待 1 秒後重試
        /// }
        ///
        /// // 內容已就緒，可以下載
        /// var content = await client.GetContentStreamAsync(messageId);
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task<bool> VerifyContentPreparationAsync(string messageId)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, DataUrl($"/bot/message/{messageId}/content/transcoding"))).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var anon = new { available = (bool?)null, status = "" };
                    try
                    {
                        var info = JsonConvert.DeserializeAnonymousType(json, anon);
                        if (info != null)
                        {
                            if (info.available.HasValue) return info.available.Value;

                            // LINE 官方 /content/transcoding 回傳的狀態只有 processing / succeeded / failed。
                            // 這裡只把 succeeded 視為可下載，其他狀態維持 false，讓呼叫端可以繼續等待或自行處理失敗狀態。
                            if (!string.IsNullOrEmpty(info.status)) return string.Equals(info.status, "succeeded", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch { }
                }
                return true;
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted) return false;
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            return false;
        }

        /// <summary>
        /// 取得圖片或影片的預覽圖
        /// Gets a preview image of an image or video
        /// </summary>
        /// <param name="messageId">
        /// 訊息 ID
        /// Message ID
        /// </param>
        /// <returns>
        /// 預覽圖的 ContentStream
        /// ContentStream of the preview image
        /// </returns>
        /// <remarks>
        /// 預覽圖通常是較小的縮圖，適合用於顯示預覽。
        /// <para>
        /// Preview images are usually smaller thumbnails suitable for display previews.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-a-preview-image-of-the-image-or-video
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-a-preview-image-of-the-image-or-video
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 取得預覽圖
        /// using (var preview = await client.GetContentPreviewAsync(messageId))
        /// {
        ///     using (var fileStream = File.Create("preview.jpg"))
        ///     {
        ///         await preview.CopyToAsync(fileStream);
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <remarks>成功時 response 唯一轉交回傳的 ContentStream；呼叫端必須 using，失敗時內部釋放。不將媒體或使用者來源存入共用快取。</remarks>
        public virtual async Task<ContentStream> GetContentPreviewAsync(string messageId)
        {
            return await GetOwnedStreamAsync(DataUrl($"/bot/message/{messageId}/content/preview")).ConfigureAwait(false);
        }
        #endregion

        #region Profile / Bot
        /// <summary>
        /// 取得使用者的個人資料
        /// Gets user profile information
        /// </summary>
        /// <param name="userId">
        /// 使用者 ID（從 Webhook 事件中取得，不要使用 LINE app 中的 LINE ID）
        /// User ID (obtained from webhook event, do not use LINE ID from LINE app)
        /// </param>
        /// <returns>
        /// UserProfile 物件，包含顯示名稱、圖片 URL、狀態訊息等
        /// UserProfile object containing display name, picture URL, status message, etc.
        /// </returns>
        /// <remarks>
        /// 可取得的個人資料包括：
        /// - displayName：顯示名稱
        /// - pictureUrl：大頭貼圖片 URL
        /// - statusMessage：狀態訊息
        /// - language：語言設定
        /// <para>
        /// Available profile information includes:
        /// - displayName: Display name
        /// - pictureUrl: Profile picture URL
        /// - statusMessage: Status message
        /// - language: Language settings
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-profile
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-profile
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var profile = await client.GetUserProfileAsync(userId);
        /// Console.WriteLine($"名稱: {profile.DisplayName}");
        /// Console.WriteLine($"圖片: {profile.PictureUrl}");
        /// Console.WriteLine($"狀態: {profile.StatusMessage}");
        /// </code>
        /// </example>
        public virtual async Task<UserProfile> GetUserProfileAsync(string userId)
        {
            var json = await GetStringAsync($"{_uri}/bot/profile/{userId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<UserProfile>(json);
        }

        /// <summary>
        /// 取得機器人的資訊
        /// Gets bot information
        /// </summary>
        /// <returns>
        /// BotInfo 物件，包含機器人的基本資訊
        /// BotInfo object containing basic bot information
        /// </returns>
        /// <remarks>
        /// 可取得的機器人資訊包括：
        /// - userId：機器人的使用者 ID
        /// - basicId：機器人的基本 ID
        /// - premiumId：機器人的進階 ID（如果有設定）
        /// - displayName：機器人的顯示名稱
        /// - pictureUrl：機器人的圖片 URL
        /// - chatMode：聊天設定（chat 或 bot）
        /// - markAsReadMode：已讀設定（auto 或 manual）
        /// <para>
        /// Available bot information includes:
        /// - userId: Bot's user ID
        /// - basicId: Bot's basic ID
        /// - premiumId: Bot's premium ID (if set)
        /// - displayName: Bot's display name
        /// - pictureUrl: Bot's picture URL
        /// - chatMode: Chat settings (chat or bot)
        /// - markAsReadMode: Read settings (auto or manual)
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-bot-info
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-bot-info
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var botInfo = await client.GetBotInfoAsync();
        /// Console.WriteLine($"機器人名稱: {botInfo.DisplayName}");
        /// Console.WriteLine($"Basic ID: {botInfo.BasicId}");
        /// Console.WriteLine($"聊天模式: {botInfo.ChatMode}");
        /// </code>
        /// </example>
        public virtual async Task<BotInfo> GetBotInfoAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/info").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<BotInfo>(json);
        }
        #endregion

        #region Group
        /// <summary>
        /// 取得群組成員的個人資料
        /// Gets the user profile of a group member
        /// </summary>
        /// <param name="groupId">
        /// 群組識別碼（從 Webhook 事件的 source 物件中取得）
        /// Group identifier (obtained from source object of webhook event)
        /// </param>
        /// <param name="userId">
        /// 使用者識別碼（從 Webhook 事件的 source 物件中取得，不要使用 LINE app 中的 LINE ID）
        /// User identifier (obtained from source object of webhook event, do not use LINE ID from LINE app)
        /// </param>
        /// <returns>
        /// UserProfile 物件，包含成員的個人資料
        /// UserProfile object containing member's profile information
        /// </returns>
        /// <remarks>
        /// 此方法可取得群組中成員的資料，包括未加機器人為好友或已封鎖機器人的使用者。
        /// <para>
        /// This method can get member profiles in a group, including users who have not added the bot as a friend or have blocked the bot.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-group-member-profile
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-group-member-profile
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var profile = await client.GetGroupMemberProfileAsync(groupId, userId);
        /// Console.WriteLine($"群組成員: {profile.DisplayName}");
        /// </code>
        /// </example>
        public virtual async Task<UserProfile> GetGroupMemberProfileAsync(string groupId, string userId)
        {
            var json = await GetStringAsync($"{_uri}/bot/group/{groupId}/member/{userId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<UserProfile>(json);
        }

        /// <summary>
        /// 取得群組成員的使用者 ID 清單
        /// Gets the user IDs of group members
        /// </summary>
        /// <param name="groupId">
        /// 群組識別碼
        /// Group identifier
        /// </param>
        /// <param name="continuationToken">
        /// 延續令牌（用於分頁，首次呼叫傳入 null）
        /// Continuation token (for pagination, pass null for first call)
        /// </param>
        /// <returns>
        /// GroupMemberIds 物件，包含使用者 ID 清單和下一頁的令牌
        /// GroupMemberIds object containing user ID list and next page token
        /// </returns>
        /// <remarks>
        /// 限制：
        /// - 此功能僅適用於 LINE@ 認證帳號或官方帳號
        /// - 每次最多回傳 100 個使用者 ID
        /// - 未同意官方帳號服務條款的使用者不會包含在結果中
        /// <para>
        /// Limitations:
        /// - This feature is only available for LINE@ Approved accounts or official accounts
        /// - Maximum 100 user IDs per response
        /// - Users who have not agreed to Official Accounts Terms of Use are not included
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-group-member-user-ids
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-group-member-user-ids
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 取得第一頁成員 ID
        /// var memberIds = await client.GetGroupMemberIdsAsync(groupId, null);
        /// foreach (var id in memberIds.MemberIds)
        /// {
        ///     Console.WriteLine($"Member ID: {id}");
        /// }
        ///
        /// // 如果有下一頁
        /// if (memberIds.Next != null)
        /// {
        ///     var nextPage = await client.GetGroupMemberIdsAsync(groupId, memberIds.Next);
        /// }
        /// </code>
        /// </example>
        public virtual async Task<GroupMemberIds> GetGroupMemberIdsAsync(string groupId, string continuationToken)
        {
            var url = $"{_uri}/bot/group/{groupId}/members/ids" + (continuationToken != null ? $"?start={continuationToken}" : "");
            var json = await GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<GroupMemberIds>(json);
        }

        /// <summary>
        /// 取得群組中所有成員的個人資料
        /// Gets the user profiles of all members in a group
        /// </summary>
        /// <param name="groupId">
        /// 群組識別碼
        /// Group identifier
        /// </param>
        /// <returns>
        /// 所有成員的 UserProfile 清單
        /// List of all members' UserProfile
        /// </returns>
        /// <remarks>
        /// 此方法會自動處理分頁，取得群組中所有成員的完整資料。
        /// 注意：對於大型群組，此操作可能需要較長時間。
        /// <para>
        /// This method automatically handles pagination to get complete data for all members in the group.
        /// Note: For large groups, this operation may take longer.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var profiles = await client.GetGroupMemberProfilesAsync(groupId);
        /// Console.WriteLine($"群組總人數: {profiles.Count}");
        /// foreach (var profile in profiles)
        /// {
        ///     Console.WriteLine($"- {profile.DisplayName}");
        /// }
        /// </code>
        /// </example>
        public virtual async Task<IList<UserProfile>> GetGroupMemberProfilesAsync(string groupId)
        {
            var result = new List<UserProfile>();
            string token = null;
            do
            {
                var ids = await GetGroupMemberIdsAsync(groupId, token).ConfigureAwait(false);
                var profiles = await Task.WhenAll(ids.MemberIds.Select(id => GetGroupMemberProfileAsync(groupId, id))).ConfigureAwait(false);
                result.AddRange(profiles);
                token = ids.Next;
            } while (token != null);
            return result;
        }

        /// <summary>
        /// 取得群組摘要資訊
        /// Gets group summary information
        /// </summary>
        /// <param name="groupId">
        /// 群組 ID
        /// Group ID
        /// </param>
        /// <returns>
        /// GroupSummary 物件，包含群組名稱、圖片等資訊
        /// GroupSummary object containing group name, picture, etc.
        /// </returns>
        /// <remarks>
        /// 群組摘要包含：
        /// - groupId：群組 ID
        /// - groupName：群組名稱
        /// - pictureUrl：群組圖示 URL（如果有設定）
        /// <para>
        /// Group summary includes:
        /// - groupId: Group ID
        /// - groupName: Group name
        /// - pictureUrl: Group icon URL (if set)
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-group-summary
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-group-summary
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var summary = await client.GetGroupSummaryAsync(groupId);
        /// Console.WriteLine($"群組名稱: {summary.GroupName}");
        /// Console.WriteLine($"群組圖示: {summary.PictureUrl}");
        /// </code>
        /// </example>
        public virtual async Task<GroupSummary> GetGroupSummaryAsync(string groupId)
        {
            var json = await GetStringAsync($"{_uri}/bot/group/{groupId}/summary").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<GroupSummary>(json);
        }

        /// <summary>
        /// 取得群組中的成員數量
        /// Gets the number of members in a group
        /// </summary>
        /// <param name="groupId">
        /// 群組 ID
        /// Group ID
        /// </param>
        /// <returns>
        /// 成員數量
        /// Member count
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-members-group-count
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-members-group-count
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetGroupMemberCountAsync(groupId);
        /// Console.WriteLine($"群組人數: {count}");
        /// </code>
        /// </example>
        public virtual async Task<int> GetGroupMemberCountAsync(string groupId)
        {
            var json = await GetStringAsync($"{_uri}/bot/group/{groupId}/members/count").ConfigureAwait(false);
            var mc = JsonConvert.DeserializeObject<MemberCount>(json);
            return mc != null ? mc.Count : 0;
        }

        /// <summary>
        /// 讓機器人離開群組
        /// Makes the bot leave a group
        /// </summary>
        /// <param name="groupId">
        /// 群組 ID（使用從 Webhook 回傳的 source 群組 ID）
        /// Group ID (use the ID returned via webhook from source group)
        /// </param>
        /// <remarks>
        /// 離開後，機器人將無法再接收該群組的訊息。
        /// <para>
        /// After leaving, the bot will no longer receive messages from that group.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#leave-group
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#leave-group
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.LeaveFromGroupAsync(groupId);
        /// Console.WriteLine("機器人已離開群組");
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task LeaveFromGroupAsync(string groupId)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/group/{groupId}/leave")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }
        #endregion

        #region Room
        /// <summary>
        /// 取得聊天室成員的個人資料
        /// Gets the user profile of a room member
        /// </summary>
        /// <param name="roomId">
        /// 聊天室識別碼（從 Webhook 事件的 source 物件中取得）
        /// Room identifier (obtained from source object of webhook event)
        /// </param>
        /// <param name="userId">
        /// 使用者識別碼（從 Webhook 事件的 source 物件中取得，不要使用 LINE app 中的 LINE ID）
        /// User identifier (obtained from source object of webhook event, do not use LINE ID from LINE app)
        /// </param>
        /// <returns>
        /// UserProfile 物件，包含成員的個人資料
        /// UserProfile object containing member's profile information
        /// </returns>
        /// <remarks>
        /// 此方法可取得聊天室中成員的資料，包括未加機器人為好友或已封鎖機器人的使用者。
        /// <para>
        /// This method can get member profiles in a room, including users who have not added the bot as a friend or have blocked the bot.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-room-member-profile
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-room-member-profile
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var profile = await client.GetRoomMemberProfileAsync(roomId, userId);
        /// Console.WriteLine($"聊天室成員: {profile.DisplayName}");
        /// </code>
        /// </example>
        public virtual async Task<UserProfile> GetRoomMemberProfileAsync(string roomId, string userId)
        {
            var json = await GetStringAsync($"{_uri}/bot/room/{roomId}/member/{userId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<UserProfile>(json);
        }

        /// <summary>
        /// 取得聊天室成員的使用者 ID 清單
        /// Gets the user IDs of room members
        /// </summary>
        /// <param name="roomId">
        /// 聊天室識別碼
        /// Room identifier
        /// </param>
        /// <param name="continuationToken">
        /// 延續令牌（用於分頁，首次呼叫傳入 null）
        /// Continuation token (for pagination, pass null for first call)
        /// </param>
        /// <returns>
        /// GroupMemberIds 物件，包含使用者 ID 清單和下一頁的令牌
        /// GroupMemberIds object containing user ID list and next page token
        /// </returns>
        /// <remarks>
        /// 限制：
        /// - 此功能僅適用於 LINE@ 認證帳號或官方帳號
        /// - 每次最多回傳 100 個使用者 ID
        /// - 未同意官方帳號服務條款的使用者不會包含在結果中
        /// <para>
        /// Limitations:
        /// - This feature is only available for LINE@ Approved accounts or official accounts
        /// - Maximum 100 user IDs per response
        /// - Users who have not agreed to Official Accounts Terms of Use are not included
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-room-member-user-ids
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-room-member-user-ids
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var memberIds = await client.GetRoomMemberIdsAsync(roomId, null);
        /// foreach (var id in memberIds.MemberIds)
        /// {
        ///     Console.WriteLine($"Member ID: {id}");
        /// }
        /// </code>
        /// </example>
        public virtual async Task<GroupMemberIds> GetRoomMemberIdsAsync(string roomId, string continuationToken = null)
        {
            var url = $"{_uri}/bot/room/{roomId}/members/ids" + (continuationToken != null ? $"?start={continuationToken}" : "");
            var json = await GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<GroupMemberIds>(json);
        }

        /// <summary>
        /// 取得聊天室中所有成員的個人資料
        /// Gets the user profiles of all members in a room
        /// </summary>
        /// <param name="roomId">
        /// 聊天室識別碼
        /// Room identifier
        /// </param>
        /// <returns>
        /// 所有成員的 UserProfile 清單
        /// List of all members' UserProfile
        /// </returns>
        /// <remarks>
        /// 此方法會自動處理分頁，取得聊天室中所有成員的完整資料。
        /// <para>
        /// This method automatically handles pagination to get complete data for all members in the room.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var profiles = await client.GetRoomMemberProfilesAsync(roomId);
        /// Console.WriteLine($"聊天室總人數: {profiles.Count}");
        /// </code>
        /// </example>
        public virtual async Task<IList<UserProfile>> GetRoomMemberProfilesAsync(string roomId)
        {
            var result = new List<UserProfile>();
            string token = null;
            do
            {
                var ids = await GetRoomMemberIdsAsync(roomId, token).ConfigureAwait(false);
                var profiles = await Task.WhenAll(ids.MemberIds.Select(id => GetRoomMemberProfileAsync(roomId, id))).ConfigureAwait(false);
                result.AddRange(profiles);
                token = ids.Next;
            } while (token != null);
            return result;
        }

        /// <summary>
        /// 取得聊天室中的成員數量
        /// Gets the number of members in a room
        /// </summary>
        /// <param name="roomId">
        /// 聊天室 ID
        /// Room ID
        /// </param>
        /// <returns>
        /// 成員數量
        /// Member count
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-members-room-count
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-members-room-count
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetRoomMemberCountAsync(roomId);
        /// Console.WriteLine($"聊天室人數: {count}");
        /// </code>
        /// </example>
        public virtual async Task<int> GetRoomMemberCountAsync(string roomId)
        {
            var json = await GetStringAsync($"{_uri}/bot/room/{roomId}/members/count").ConfigureAwait(false);
            var mc = JsonConvert.DeserializeObject<MemberCount>(json);
            return mc != null ? mc.Count : 0;
        }

        /// <summary>
        /// 讓機器人離開聊天室
        /// Makes the bot leave a room
        /// </summary>
        /// <param name="roomId">
        /// 聊天室 ID（使用從 Webhook 回傳的 source 聊天室 ID）
        /// Room ID (use the ID returned via webhook from source room)
        /// </param>
        /// <remarks>
        /// 離開後，機器人將無法再接收該聊天室的訊息。
        /// <para>
        /// After leaving, the bot will no longer receive messages from that room.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#leave-room
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#leave-room
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.LeaveFromRoomAsync(roomId);
        /// Console.WriteLine("機器人已離開聊天室");
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task LeaveFromRoomAsync(string roomId)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/room/{roomId}/leave")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }
        #endregion

        #region Webhook
        /// <summary>
        /// 設定 Webhook 端點 URL
        /// Sets webhook endpoint URL
        /// </summary>
        /// <param name="endpoint">
        /// Webhook URL（必須是 HTTPS，且可從網際網路存取）
        /// Webhook URL (must be HTTPS and accessible from the internet)
        /// </param>
        /// <remarks>
        /// Webhook URL 要求：
        /// - 必須使用 HTTPS 協定
        /// - 必須可從網際網路存取
        /// - 必須在 10 秒內回應
        /// - 必須回傳 HTTP 200 狀態碼
        /// <para>
        /// Webhook URL requirements:
        /// - Must use HTTPS protocol
        /// - Must be accessible from the internet
        /// - Must respond within 10 seconds
        /// - Must return HTTP 200 status code
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#set-webhook-endpoint-url
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#set-webhook-endpoint-url
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.SetWebhookEndpointAsync("https://example.com/webhook");
        /// Console.WriteLine("Webhook 端點已設定");
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task SetWebhookEndpointAsync(string endpoint)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{_uri}/bot/channel/webhook/endpoint");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { endpoint }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得 Webhook 端點資訊
        /// Gets webhook endpoint information
        /// </summary>
        /// <returns>
        /// WebhookEndpoint 物件，包含端點 URL 和啟用狀態
        /// WebhookEndpoint object containing endpoint URL and active status
        /// </returns>
        /// <remarks>
        /// 回傳資訊包括：
        /// - endpoint：Webhook URL
        /// - active：是否啟用
        /// <para>
        /// Returned information includes:
        /// - endpoint: Webhook URL
        /// - active: Whether it is enabled
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-webhook-endpoint-information
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-webhook-endpoint-information
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var webhookInfo = await client.GetWebhookEndpointAsync();
        /// Console.WriteLine($"Webhook URL: {webhookInfo.Endpoint}");
        /// Console.WriteLine($"啟用狀態: {webhookInfo.Active}");
        /// </code>
        /// </example>
        public virtual async Task<WebhookEndpoint> GetWebhookEndpointAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/channel/webhook/endpoint").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<WebhookEndpoint>(json);
        }

        /// <summary>
        /// 測試 Webhook 端點
        /// Tests webhook endpoint
        /// </summary>
        /// <param name="endpoint">
        /// 要測試的 Webhook URL（選用，未指定則使用已設定的端點）
        /// Webhook URL to test (optional, uses configured endpoint if not specified)
        /// </param>
        /// <returns>
        /// WebhookTestResult 物件，包含測試結果
        /// WebhookTestResult object containing test results
        /// </returns>
        /// <remarks>
        /// 測試會發送一個模擬的 Webhook 事件到指定的端點。
        /// 端點必須在 10 秒內回應 HTTP 200，測試才會通過。
        /// <para>
        /// Test sends a simulated webhook event to the specified endpoint.
        /// Endpoint must respond with HTTP 200 within 10 seconds for the test to pass.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#test-webhook-endpoint
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#test-webhook-endpoint
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 測試目前設定的端點
        /// var result = await client.TestWebhookEndpointAsync();
        /// Console.WriteLine($"測試結果: {result.Success}");
        ///
        /// // 測試特定端點
        /// var result2 = await client.TestWebhookEndpointAsync("https://example.com/webhook");
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task<WebhookTestResult> TestWebhookEndpointAsync(string endpoint = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/channel/webhook/test");
            string body = endpoint == null ? "{}" : JsonConvert.SerializeObject(new { endpoint }, _jsonSerializerSettings);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<WebhookTestResult>(json);
        }
        #endregion

        #region Rich Menu & Alias & Batch
        /// <summary>
        /// 透過 LINE provider 端的 richMenuId 取得 RichMenu 資訊。
        /// 這個端點回傳版面、chat bar 文字與 action area，不包含圖片 binary。
        /// </summary>
        /// <param name="richMenuId">
        /// 已上傳到 LINE 的 provider richMenuId。
        /// </param>
        /// <returns>
        /// RichMenu 物件，包含 LINE 回傳的選單設定。
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-rich-menu
        /// </remarks>
        /// <example>
        /// <code>
        /// var richMenu = await client.GetRichMenuAsync(richMenuId);
        /// Console.WriteLine($"Rich Menu 名稱: {richMenu.Name}");
        /// Console.WriteLine($"選擇區域數量: {richMenu.Areas.Count}");
        /// </code>
        /// </example>
        public virtual async Task<RichMenu> GetRichMenuAsync(string richMenuId)
        {
            var json = await GetStringAsync($"{_uri}/bot/richmenu/{richMenuId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<ResponseRichMenu>(json);
        }

        /// <summary>
        /// 建立 RichMenu metadata 與可點擊區域設定。
        /// 建立完成只會得到 provider richMenuId；使用者要看得到選單，還必須另外上傳圖片並綁定使用者或設定預設選單。
        /// </summary>
        /// <param name="richMenu">
        /// RichMenu 物件，定義選單尺寸、chat bar 文字與 action area。
        /// </param>
        /// <returns>
        /// LINE provider 產生的 richMenuId。
        /// </returns>
        /// <remarks>
        /// 注意事項：
        /// - 必須上傳 Rich Menu 圖片並連結到使用者才會顯示
        /// - 一個機器人最多可建立 1000 個 Rich Menu
        /// - Rich Menu 以物件形式表示
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#create-rich-menu
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var richMenu = new RichMenu
        /// {
        ///     Size = new RichMenuSize { Width = 2500, Height = 1686 },
        ///     Selected = false,
        ///     Name = "我的選單",
        ///     ChatBarText = "開啟選單",
        ///     Areas = new List&lt;ActionArea&gt;
        ///     {
        ///         new ActionArea
        ///         {
        ///             Bounds = new AreaBounds { X = 0, Y = 0, Width = 1250, Height = 1686 },
        ///             Action = new MessageTemplateAction("選項 A", "A")
        ///         }
        ///     }
        /// };
        /// var richMenuId = await client.CreateRichMenuAsync(richMenu);
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task<string> CreateRichMenuAsync(RichMenu richMenu)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu");
            request.Content = new StringContent(JsonConvert.SerializeObject(richMenu, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(json, new { richMenuId = "" }).richMenuId;
        }

        /// <summary>
        /// 驗證 RichMenu 物件是否符合 LINE 建立規則。
        /// 此方法不會建立遠端選單，適合 provisioning 前先檢查 catalog 定義。
        /// </summary>
        /// <param name="richMenu">
        /// 要驗證的 RichMenu 版面物件。
        /// </param>
        /// <remarks>
        /// 在建立 Rich Menu 前，可先使用此方法驗證設定是否正確。
        /// 驗證不通過會拋出例外。
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#validate-rich-menu-object
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// try
        /// {
        ///     await client.ValidateRichMenuAsync(richMenu);
        ///     Console.WriteLine("Rich Menu 設定有效");
        /// }
        /// catch (Exception ex)
        /// {
        ///     Console.WriteLine($"驗證失敗: {ex.Message}");
        /// }
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task ValidateRichMenuAsync(RichMenu richMenu)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/validate");
            request.Content = new StringContent(JsonConvert.SerializeObject(richMenu, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 刪除指定 provider richMenuId 的 RichMenu。
        /// 呼叫前應確認此選單不是多人共用選單，也沒有仍被 alias 或使用者綁定。
        /// </summary>
        /// <param name="richMenuId">
        /// 要刪除的 provider richMenuId。
        /// </param>
        /// <remarks>
        /// 刪除後，該 Rich Menu 將無法再使用，且已連結的使用者將不再看到此選單。
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.DeleteRichMenuAsync(richMenuId);
        /// Console.WriteLine("Rich Menu 已刪除");
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task DeleteRichMenuAsync(string richMenuId)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{_uri}/bot/richmenu/{richMenuId}")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 建立 RichMenu alias。
        /// alias 讓應用程式用穩定 ID 指向 LINE 產生的 provider richMenuId，方便未來輪替底層選單。
        /// </summary>
        /// <param name="richMenuId">
        /// alias 要關聯的 provider richMenuId。
        /// </param>
        /// <param name="richMenuAliasId">
        /// RichMenu 別名 ID，最多 100 字元。
        /// </param>
        /// <remarks>
        /// Rich Menu 別名可讓您使用自訂 ID 來管理 Rich Menu，而不需要記住系統產生的 Rich Menu ID。
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.CreateRichMenuAliasAsync(richMenuId, "summer-menu-2024");
        /// Console.WriteLine("Rich Menu 別名已建立");
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/alias");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { richMenuId, richMenuAliasId }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 刪除 RichMenu alias。
        /// 此操作只移除 alias 對照，不會刪除 alias 原本指向的 provider RichMenu。
        /// </summary>
        /// <param name="richMenuAliasId">
        /// 要刪除的 RichMenu 別名 ID。
        /// </param>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu-alias
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.DeleteRichMenuAliasAsync("summer-menu-2024");
        /// Console.WriteLine("Rich Menu 別名已刪除");
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task DeleteRichMenuAliasAsync(string richMenuAliasId)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{_uri}/bot/richmenu/alias/{richMenuAliasId}")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 更新 RichMenu alias 指向的 provider richMenuId。
        /// provisioning workflow 可用此方法維持 alias 穩定，同時把使用者導向新版圖稿或新版 action。
        /// </summary>
        /// <param name="richMenuAliasId">
        /// 要更新的 RichMenu 別名 ID。
        /// </param>
        /// <param name="richMenuId">
        /// alias 新的 provider richMenuId。
        /// </param>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#update-rich-menu-alias
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.UpdateRichMenuAliasAsync("summer-menu-2024", newRichMenuId);
        /// Console.WriteLine("Rich Menu 別名已更新");
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/alias/{richMenuAliasId}");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { richMenuId }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得 RichMenu alias 目前指向資訊。
        /// </summary>
        /// <param name="richMenuAliasId">
        /// RichMenu 別名 ID。
        /// </param>
        /// <returns>
        /// RichMenuAlias 物件，包含 alias 與其關聯的 provider richMenuId。
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-information
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var alias = await client.GetRichMenuAliasAsync("summer-menu-2024");
        /// Console.WriteLine($"別名: {alias.RichMenuAliasId}");
        /// Console.WriteLine($"關聯的 Rich Menu: {alias.RichMenuId}");
        /// </code>
        /// </example>
        public virtual async Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId)
        {
            var json = await GetStringAsync($"{_uri}/bot/richmenu/alias/{richMenuAliasId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<RichMenuAlias>(json);
        }

        /// <summary>
        /// 取得官方帳號底下所有 RichMenu alias 清單。
        /// </summary>
        public virtual async Task<RichMenuAliasList> GetRichMenuAliasListAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/richmenu/alias/list").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<RichMenuAliasList>(json);
        }

        /// <summary>
        /// 取得使用者目前直接綁定的 RichMenu ID。
        /// 回傳值是 provider richMenuId，不是應用程式 menu key 或 alias。
        /// </summary>
        public virtual async Task<string> GetRichMenuIdOfUserAsync(string userId)
        {
            var json = await GetStringAsync($"{_uri}/bot/user/{userId}/richmenu").ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(json, new { richMenuId = "" }).richMenuId;
        }

        /// <summary>
        /// 設定官方帳號層級的預設 RichMenu。
        /// 這會影響沒有個人 RichMenu 綁定的使用者。
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task SetDefaultRichMenuAsync(string richMenuId)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/user/all/richmenu/{richMenuId}")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得官方帳號目前設定的預設 RichMenu ID。
        /// </summary>
        public virtual async Task<string> GetDefaultRichMenuIdAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/user/all/richmenu").ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(json, new { richMenuId = "" }).richMenuId;
        }

        /// <summary>
        /// 取消官方帳號目前設定的預設 RichMenu。
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task CancelDefaultRichMenuAsync()
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{_uri}/bot/user/all/richmenu")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 將 RichMenu 直接連結到單一使用者。
        /// LINE 同一時間只允許一位使用者有一個直接綁定的 RichMenu。
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task LinkRichMenuToUserAsync(string userId, string richMenuId)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/user/{userId}/richmenu/{richMenuId}")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 將同一個 RichMenu 批次連結到多位使用者。
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task LinkRichMenuToUsersAsync(string richMenuId, IList<string> userIds)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/bulk/link");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { richMenuId, userIds }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 解除單一使用者目前直接連結的 RichMenu。
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task UnLinkRichMenuFromUserAsync(string userId)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{_uri}/bot/user/{userId}/richmenu")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 批次解除多位使用者目前直接連結的 RichMenu。
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task UnLinkRichMenuFromUsersAsync(IList<string> userIds)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/bulk/unlink");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { userIds }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 批次控制 RichMenu，支援 link、unlink 與 unlinkAll。
        /// LINE 會非同步處理此請求，呼叫端應使用 progress endpoint 追蹤結果。
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task RichMenuBatchOperationAsync(IList<RichMenuBatchOperation> operations)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/batch");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { operations }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得 RichMenu batch-control request 的處理進度。
        /// </summary>
        public virtual async Task<RichMenuBatchProgress> GetRichMenuBatchProgressAsync(string requestId)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/richmenu/progress/batch?requestId={Uri.EscapeDataString(requestId)}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<RichMenuBatchProgress>(json);
        }

        /// <summary>
        /// 驗證 RichMenu batch-control request 是否符合 LINE 官方格式。
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task ValidateRichMenuBatchRequestAsync(IList<RichMenuBatchOperation> operations)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/richmenu/validate/batch"));
            request.Content = new StringContent(JsonConvert.SerializeObject(new { operations }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 下載指定 RichMenu 的圖片內容。
        /// 圖片端點必須走 api-data.line.me，因此這裡使用 DataUrl。
        /// </summary>
        /// <remarks>成功時 response 唯一轉交回傳的 ContentStream；呼叫端必須 using，失敗時內部釋放。不將媒體或使用者來源存入共用快取。</remarks>
        public virtual async Task<ContentStream> DownloadRichMenuImageAsync(string richMenuId)
        {
            return await GetOwnedStreamAsync(DataUrl($"/bot/richmenu/{richMenuId}/content")).ConfigureAwait(false);
        }

        /// <summary>
        /// 上傳 JPEG RichMenu 圖片。
        /// LINE 不允許替換既有圖片；更新圖片時需建立新的 RichMenu。
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task UploadRichMenuJpegImageAsync(Stream stream, string richMenuId)
        {
            var content = new BorrowedStreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, DataUrl($"/bot/richmenu/{richMenuId}/content")) { Content = content }).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 上傳 PNG RichMenu 圖片。
        /// 共用 provisioning workflow 目前使用 PNG 圖稿時會走此端點。
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task UploadRichMenuPngImageAsync(Stream stream, string richMenuId)
        {
            var content = new BorrowedStreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, DataUrl($"/bot/richmenu/{richMenuId}/content")) { Content = content }).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得官方帳號底下所有已上傳 RichMenu 清單。
        /// LINE 回傳 JSON 外層是 richmenus 陣列，因此這裡手動解析每個項目。
        /// </summary>
        public virtual async Task<IList<ResponseRichMenu>> GetRichMenuListAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/richmenu/list").ConfigureAwait(false);
            var list = new List<ResponseRichMenu>();
            if (!string.IsNullOrWhiteSpace(json))
            {
                var obj = JObject.Parse(json);
                var richmenus = obj["richmenus"] as JArray;
                if (richmenus != null)
                {
                    foreach (var rm in richmenus)
                    {
                        list.Add(ResponseRichMenu.CreateFrom(rm));
                    }
                }
            }
            return list;
        }
        #endregion

        #region Insights
        /// <summary>
        /// 取得訊息傳送統計
        /// Gets message delivery statistics
        /// </summary>
        public virtual async Task<MessageDelivery> GetMessageDeliveryAsync(DateTime date)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/insight/message/delivery?date={date:yyyyMMdd}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MessageDelivery>(json);
        }

        /// <summary>
        /// 取得關注者統計
        /// Gets follower statistics
        /// </summary>
        public virtual async Task<FollowerStatistics> GetFollowerStatisticsAsync(DateTime date)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/insight/followers?date={date:yyyyMMdd}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<FollowerStatistics>(json);
        }

        /// <summary>
        /// 取得好友人口統計
        /// Gets friend demographics
        /// </summary>
        public virtual async Task<DemographicStatistics> GetFriendDemographicsAsync()
        {
            var json = await GetStringAsync(ApiUrl("/bot/insight/demographic")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<DemographicStatistics>(json);
        }

        /// <summary>
        /// 取得用戶互動統計
        /// Gets user interaction statistics
        /// </summary>
        public virtual async Task<UserInteractionStatistics> GetUserInteractionStatisticsAsync(string requestId)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/insight/message/event?requestId={requestId}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<UserInteractionStatistics>(json);
        }

        /// <summary>
        /// 取得單位統計
        /// Gets statistics per unit
        /// </summary>
        public virtual async Task<StatisticsPerUnit> GetStatisticsPerUnitAsync(string customAggregationUnit, string from, string to)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/insight/message/event/aggregation?customAggregationUnit={Uri.EscapeDataString(customAggregationUnit)}&from={from}&to={to}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<StatisticsPerUnit>(json);
        }

        /// <summary>
        /// 取得聚合資訊
        /// Gets aggregation info
        /// </summary>
        public virtual async Task<AggregationInfo> GetAggregationInfoAsync()
        {
            var json = await GetStringAsync(ApiUrl("/bot/message/aggregation/info")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<AggregationInfo>(json);
        }

        /// <summary>
        /// 取得聚合單位名稱清單
        /// Gets aggregation unit name list
        /// </summary>
        public virtual async Task<AggregationUnitNameList> GetAggregationUnitNameListAsync(int limit = 100, string start = null)
        {
            var url = ApiUrl($"/bot/message/aggregation/list?limit={limit}");
            if (!string.IsNullOrEmpty(start))
                url += $"&start={start}";
            var json = await GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<AggregationUnitNameList>(json);
        }
        #endregion

        #region Account Link
        /// <summary>
        /// 發行用於帳號連結功能的 Link Token
        /// Issues a link token used for the account link feature
        /// </summary>
        /// <param name="userId">
        /// 要連結的 LINE 帳號使用者 ID（從帳號連結事件物件的 source 中取得，不要使用 LINE app 中的 LINE ID）
        /// User ID for the LINE account to be linked (found in source object of account link event, do not use LINE ID from LINE app)
        /// </param>
        /// <returns>
        /// Link Token（有效期 10 分鐘，只能使用一次）
        /// Link token (valid for 10 minutes, can only be used once)
        /// </returns>
        /// <remarks>
        /// Link Token 的特性：
        /// - 有效期為 10 分鐘
        /// - 只能使用一次
        /// - 有效期可能會變更，恕不另行通知
        /// <para>
        /// Link token characteristics:
        /// - Valid for 10 minutes
        /// - Can only be used once
        /// - Validity period may change without notice
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#issue-link-token
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#issue-link-token
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var linkToken = await client.IssueLinkTokenAsync(userId);
        /// Console.WriteLine($"Link Token: {linkToken}");
        /// // 將 Link Token 傳送給使用者進行帳號連結
        /// </code>
        /// </example>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task<string> IssueLinkTokenAsync(string userId)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/user/{userId}/linkToken")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(json, new { linkToken = "" }).linkToken;
        }
        #endregion

        #region Number of sent messages / Quota
        /// <summary>
        /// 取得廣播訊息的傳送數量
        /// Gets the number of messages sent with the broadcast endpoint
        /// </summary>
        /// <param name="date">
        /// 訊息傳送日期（格式：yyyyMMdd，例如：20191231，時區：UTC+9）
        /// Date the messages were sent (format: yyyyMMdd, example: 20191231, timezone: UTC+9)
        /// </param>
        /// <returns>
        /// NumberOfSentMessages 物件，包含傳送數量統計
        /// NumberOfSentMessages object containing sent message statistics
        /// </returns>
        /// <remarks>
        /// 此操作取得的訊息數量不包含從 LINE 官方帳號管理後台傳送的訊息。
        /// <para>
        /// The number of messages retrieved by this operation does not include messages sent from LINE Official Account Manager.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-number-of-broadcast-messages
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-number-of-broadcast-messages
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetNumberOfSentBroadcastMessagesAsync(new DateTime(2024, 1, 15));
        /// Console.WriteLine($"廣播訊息數量: {count.TotalCount}");
        /// </code>
        /// </example>
        public virtual async Task<NumberOfSentMessages> GetNumberOfSentBroadcastMessagesAsync(DateTime date)
        {
            var json = await GetStringAsync($"{_uri}/bot/message/delivery/broadcast?date={date:yyyyMMdd}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<NumberOfSentMessages>(json);
        }

        /// <summary>
        /// 取得回覆訊息的傳送數量
        /// Gets the number of messages sent with the reply endpoint
        /// </summary>
        /// <param name="date">
        /// 訊息傳送日期（格式：yyyyMMdd，時區：UTC+9）
        /// Date the messages were sent (format: yyyyMMdd, timezone: UTC+9)
        /// </param>
        /// <returns>
        /// NumberOfSentMessages 物件，包含傳送數量統計
        /// NumberOfSentMessages object containing sent message statistics
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-number-of-reply-messages
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-number-of-reply-messages
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetNumberOfSentReplyMessagesAsync(DateTime.Today);
        /// Console.WriteLine($"回覆訊息數量: {count.TotalCount}");
        /// </code>
        /// </example>
        public virtual async Task<NumberOfSentMessages> GetNumberOfSentReplyMessagesAsync(DateTime date)
        {
            var json = await GetStringAsync($"{_uri}/bot/message/delivery/reply?date={date:yyyyMMdd}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<NumberOfSentMessages>(json);
        }

        /// <summary>
        /// 取得推播訊息的傳送數量
        /// Gets the number of messages sent with the push endpoint
        /// </summary>
        /// <param name="date">
        /// 訊息傳送日期（格式：yyyyMMdd，時區：UTC+9）
        /// Date the messages were sent (format: yyyyMMdd, timezone: UTC+9)
        /// </param>
        /// <returns>
        /// NumberOfSentMessages 物件，包含傳送數量統計
        /// NumberOfSentMessages object containing sent message statistics
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-number-of-push-messages
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-number-of-push-messages
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetNumberOfSentPushMessagesAsync(DateTime.Today);
        /// Console.WriteLine($"推播訊息數量: {count.TotalCount}");
        /// </code>
        /// </example>
        public virtual async Task<NumberOfSentMessages> GetNumberOfSentPushMessagesAsync(DateTime date)
        {
            var json = await GetStringAsync($"{_uri}/bot/message/delivery/push?date={date:yyyyMMdd}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<NumberOfSentMessages>(json);
        }

        /// <summary>
        /// 取得多播訊息的傳送數量
        /// Gets the number of messages sent with the multicast endpoint
        /// </summary>
        /// <param name="date">
        /// 訊息傳送日期（格式：yyyyMMdd，時區：UTC+9）
        /// Date the messages were sent (format: yyyyMMdd, timezone: UTC+9)
        /// </param>
        /// <returns>
        /// NumberOfSentMessages 物件，包含傳送數量統計
        /// NumberOfSentMessages object containing sent message statistics
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-number-of-multicast-messages
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-number-of-multicast-messages
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetNumberOfSentMulticastMessagesAsync(DateTime.Today);
        /// Console.WriteLine($"多播訊息數量: {count.TotalCount}");
        /// </code>
        /// </example>
        public virtual async Task<NumberOfSentMessages> GetNumberOfSentMulticastMessagesAsync(DateTime date)
        {
            var json = await GetStringAsync($"{_uri}/bot/message/delivery/multicast?date={date:yyyyMMdd}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<NumberOfSentMessages>(json);
        }

        /// <summary>
        /// 取得當月傳送訊息的目標上限
        /// Gets the target limit for sending messages in the current month
        /// </summary>
        /// <returns>
        /// MessageQuota 物件，包含配額類型和限制
        /// MessageQuota object containing quota type and limits
        /// </returns>
        /// <remarks>
        /// 配額類型：
        /// - none：無訊息數量限制
        /// - limited：有訊息數量限制
        /// <para>
        /// Quota types:
        /// - none: No limit on the number of messages
        /// - limited: There is a limit on the number of messages
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-quota
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-quota
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var quota = await client.GetMessageQuotaAsync();
        /// Console.WriteLine($"配額類型: {quota.Type}");
        /// if (quota.Type == QuotaType.Limited)
        /// {
        ///     Console.WriteLine($"總配額: {quota.Value}");
        /// }
        /// </code>
        /// </example>
        public virtual async Task<MessageQuota> GetMessageQuotaAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/message/quota"). ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MessageQuota>(json);
        }

        /// <summary>
        /// 取得當月已傳送的訊息數量
        /// Gets the number of sent messages in the current month
        /// </summary>
        /// <returns>
        /// MessageQuotaConsumption 物件，包含已使用的訊息數量
        /// MessageQuotaConsumption object containing the number of messages used
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-consumption
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-consumption
        /// </remarks>
        /// <example>
        /// <code>
        /// var consumption = await client.GetMessageQuotaConsumptionAsync();
        /// Console.WriteLine($"已使用訊息數: {consumption.TotalUsage}");
        /// </code>
        /// </example>
        public virtual async Task<MessageQuotaConsumption> GetMessageQuotaConsumptionAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/message/quota/consumption"). ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MessageQuotaConsumption>(json);
        }
        #endregion

        #region Coupon
        /// <summary>
        /// 建立優惠券
        /// Creates a coupon
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task<Coupon> CreateCouponAsync(CreateCouponRequest request)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/coupon"));
            httpRequest.Content = new StringContent(JsonConvert.SerializeObject(request, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(httpRequest).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<Coupon>(json);
        }

        /// <summary>
        /// 停止優惠券
        /// Closes a coupon
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task CloseCouponAsync(string couponId)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Put, ApiUrl($"/bot/coupon/{couponId}/close"));
            using var response = await SendAsync(httpRequest).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得優惠券清單
        /// Gets coupon list
        /// </summary>
        public virtual async Task<CouponList> GetCouponListAsync(int limit = 20, string next = null)
        {
            var url = ApiUrl($"/bot/coupon?limit={limit}");
            if (!string.IsNullOrEmpty(next))
                url += $"&next={next}";
            var json = await GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<CouponList>(json);
        }

        /// <summary>
        /// 取得優惠券詳情
        /// Gets coupon details
        /// </summary>
        public virtual async Task<Coupon> GetCouponAsync(string couponId)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/coupon/{couponId}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<Coupon>(json);
        }
        #endregion

        #region Membership
        /// <summary>
        /// 取得用戶會員資格訂閱狀態
        /// Gets user's membership subscription status
        /// </summary>
        public virtual async Task<MembershipSubscription> GetMembershipSubscriptionAsync(string userId)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/membership/subscription/{userId}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MembershipSubscription>(json);
        }

        /// <summary>
        /// 取得會員方案的用戶 ID 清單
        /// Gets list of user IDs who have joined a membership plan
        /// </summary>
        /// <param name="membershipId">
        /// 會員方案 ID
        /// Membership plan ID
        /// </param>
        /// <param name="limit">
        /// 每次查詢的最大數量（預設 100）
        /// Maximum number of items per query (default 100)
        /// </param>
        /// <param name="next">
        /// 延續令牌（用於分頁）
        /// Continuation token (for pagination)
        /// </param>
        /// <returns>
        /// 會員方案的用戶 ID 清單
        /// List of user IDs who joined the membership plan
        /// </returns>
        public virtual async Task<MembershipUserIds> GetMembershipUserIdsAsync(string membershipId, int limit = 100, string next = null)
        {
            var url = ApiUrl($"/bot/membership/{membershipId}/users/ids?limit={limit}");
            if (!string.IsNullOrEmpty(next))
                url += $"&next={next}";
            var json = await GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MembershipUserIds>(json);
        }

        /// <summary>
        /// 取得會員方案清單
        /// Gets list of membership plans
        /// </summary>
        /// <returns>
        /// 會員方案清單
        /// List of membership plans
        /// </returns>
        public virtual async Task<MembershipPlanList> GetMembershipPlansAsync()
        {
            var json = await GetStringAsync(ApiUrl("/bot/membership/list")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MembershipPlanList>(json);
        }
        #endregion

        #region Message Validation
        /// <summary>
        /// 驗證回覆訊息內容
        /// Validates reply message content
        /// </summary>
        /// <param name="messages">
        /// 要驗證的訊息清單
        /// List of messages to validate
        /// </param>
        /// <remarks>
        /// 此方法會檢查訊息格式是否正確，但不會實際傳送訊息。
        /// <para>
        /// This method checks if the message format is correct but does not actually send messages.
        /// </para>
        /// </remarks>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task ValidateReplyMessageAsync(IList<ISendMessage> messages)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/reply");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 驗證推播訊息內容
        /// Validates push message content
        /// </summary>
        /// <param name="messages">
        /// 要驗證的訊息清單
        /// List of messages to validate
        /// </param>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task ValidatePushMessageAsync(IList<ISendMessage> messages)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/push");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 驗證多播訊息內容
        /// Validates multicast message content
        /// </summary>
        /// <param name="messages">
        /// 要驗證的訊息清單
        /// List of messages to validate
        /// </param>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task ValidateMulticastMessageAsync(IList<ISendMessage> messages)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/multicast");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 驗證窄播訊息內容
        /// Validates narrowcast message content
        /// </summary>
        /// <param name="messages">
        /// 要驗證的訊息清單
        /// List of messages to validate
        /// </param>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task ValidateNarrowcastMessageAsync(IList<ISendMessage> messages)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/narrowcast");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 驗證廣播訊息內容
        /// Validates broadcast message content
        /// </summary>
        /// <param name="messages">
        /// 要驗證的訊息清單
        /// List of messages to validate
        /// </param>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        public virtual async Task ValidateBroadcastMessageAsync(IList<ISendMessage> messages)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/broadcast");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }
        #endregion

        #region Audience (Currently not implemented - placeholders for interface compliance)
        /// <summary>
        /// 建立上傳型受眾群組（目前未實作）
        /// Creates upload audience group (currently not implemented)
        /// </summary>
        public virtual Task<CreateAudienceGroupResponse> CreateUploadAudienceGroupAsync(CreateUploadAudienceGroupRequest request)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 透過檔案建立上傳型受眾群組（目前未實作）
        /// Creates upload audience group by file (currently not implemented)
        /// </summary>
        public virtual Task<CreateAudienceGroupResponse> CreateUploadAudienceGroupByFileAsync(string description, bool? isIfaAudience, string uploadDescription, Stream file)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 將受眾加入群組（目前未實作）
        /// Adds audience to group (currently not implemented)
        /// </summary>
        public virtual Task AddAudienceToGroupAsync(AddAudienceToGroupRequest request)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 透過檔案將受眾加入群組（目前未實作）
        /// Adds audience to group by file (currently not implemented)
        /// </summary>
        public virtual Task AddAudienceToGroupByFileAsync(long audienceGroupId, string uploadDescription, Stream file)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 建立點擊型受眾群組（目前未實作）
        /// Creates click-based audience group (currently not implemented)
        /// </summary>
        public virtual Task<CreateAudienceGroupResponse> CreateClickAudienceGroupAsync(CreateClickAudienceGroupRequest request)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 建立曝光型受眾群組（目前未實作）
        /// Creates impression-based audience group (currently not implemented)
        /// </summary>
        public virtual Task<CreateAudienceGroupResponse> CreateImpAudienceGroupAsync(CreateImpAudienceGroupRequest request)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 更新受眾群組描述（目前未實作）
        /// Updates audience group description (currently not implemented)
        /// </summary>
        public virtual Task UpdateAudienceGroupDescriptionAsync(long audienceGroupId, string description)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 刪除受眾群組（目前未實作）
        /// Deletes audience group (currently not implemented)
        /// </summary>
        public virtual Task DeleteAudienceGroupAsync(long audienceGroupId)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 取得受眾群組（目前未實作）
        /// Gets audience group (currently not implemented)
        /// </summary>
        public virtual Task<AudienceGroup> GetAudienceGroupAsync(long audienceGroupId)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 取得受眾群組清單（目前未實作）
        /// Gets list of audience groups (currently not implemented)
        /// </summary>
        public virtual Task<AudienceGroupList> GetAudienceGroupsAsync(long page, string description, string status, long size, bool includesExternalPublicGroups, string createRoute)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 取得受眾群組授權層級（目前未實作）
        /// Gets audience group authority level (currently not implemented)
        /// </summary>
        public virtual Task<string> GetAudienceGroupAuthorityLevelAsync()
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 變更受眾群組授權層級（目前未實作）
        /// Changes audience group authority level (currently not implemented)
        /// </summary>
        public virtual Task ChangeAudienceGroupAuthorityLevelAsync(string authorityLevel)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }
        #endregion

        #region Followers (custom extension)
        /// <summary>
        /// 取得關注者ID (輔助方法 - 非官方 API)
        /// Gets follower IDs (helper method - not official API)
        /// </summary>
        public virtual async Task<IList<string>> GetFollowersAsync()
        {
            // Note: This is a custom implementation
            // The actual implementation depends on your requirements
            return new List<string>();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// 唯一的具 credential 傳輸入口；request 只屬單次操作，完成或故障立即釋放。
        /// 不讀寫 HttpClient.DefaultRequestHeaders，避免共用 transport 的另一 channel 覆寫身分。
        /// 借入的 HttpClient 由呼叫者設定有限 Timeout、無使用者 cookie 的 handler 並負責關機排空；
        /// 此方法不建立額外 task、timer 或重試。已開始的傳輸依 HttpClient timeout 完成，不遺棄工作。
        /// </summary>
        /// <param name="request">所有權轉交此方法的單次 request；不可以再次傳送。</param>
        /// <param name="completion">一般 API 讀完整內容；媒體下載只讀 headers 並交由 ContentStream 擁有 response。</param>
        /// <returns>呼叫方法必須 using 或明確轉交所有權的 response。</returns>
        private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead) =>
            _transport.SendAsync(request, completion);

        /// <summary>
        /// 在串流建立成功前由此方法 owns response；失敗立即 Dispose 並重新拋原例外。
        /// 成功後所有權唯一轉交 ContentStream，呼叫者必須 using，避免 response content／連線滯留。
        /// 不緩衝整個媒體，內容讀取與取消由呼叫者的 Stream 操作管理，結束後 Dispose 歸還連線。
        /// </summary>
        /// <param name="url">已組裝的媒體端點，不記錄其使用者資料。</param><returns>擁有 response 的串流。</returns>
        private async Task<ContentStream> GetOwnedStreamAsync(string url)
        {
            var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            try
            {
                await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
                return new ContentStream(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), response.Content.Headers, response);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        /// <summary>
        /// HTTP GET 輔助方法
        /// HTTP GET helper method
        /// </summary>
        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
        private async Task<string> GetStringAsync(string url)
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url)).ConfigureAwait(false);

            // 不把遠端 body 或含使用者識別的 URL 附到例外；失敗仍由上層共用 notifier 記錄真實型別。
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("LINE API authentication failed (401). Verify the configured channel credential.");

            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        #endregion

        #region IDisposable
        /// <summary>
        /// 冪等停止此 SDK 的新請求並釋放自有 transport；借用 transport 不受影響。
        /// 不啟動非同步清理；呼叫者先排空 in-flight 操作後 Dispose，credential 參照於此清除。
        /// </summary>
        public void Dispose()
        {
            // 清除 credential 參照並禁止新工作；呼叫端應先等待既有操作完成，再處置此實例。
            // 借入 transport 永不由此處關閉，避免另一 channel 的正常請求被中止。
            _transport.Dispose();
        }
        #endregion
    }
}

```

## Line.Messaging/LineSignature.cs
```
using System;
using System.Security.Cryptography;
using System.Text;

namespace Line.Messaging;

/// <summary>與 HTTP 框架無關的原始位元組驗簽入口；無共用 HMAC、密鑰快取或使用者狀態。</summary>
public static class LineSignature
{
    /// <summary>驗證原始 request body 的 HMAC-SHA256，不先解碼 JSON、正規化文字或重新序列化。</summary>
    /// <param name="body">呼叫端擁有的原始內容，方法不修改或保存。</param>
    /// <param name="signatureHeader">X-Line-Signature 的 Base64 SHA256 digest。</param>
    /// <param name="channelSecret">本次已授權 channel 的密鑰；不允許空設定。</param>
    /// <returns>格式不合或 digest 不符回 false；密鑰及 body 的設定錯誤明確拋出。</returns>
    public static bool Verify(byte[] body, string signatureHeader, string channelSecret)
    {
        if (body == null) throw new ArgumentNullException(nameof(body));
        if (string.IsNullOrWhiteSpace(channelSecret)) throw new ArgumentException("Channel secret must not be blank.", nameof(channelSecret));
        // SHA256 的 Base64 恆為 44 字元；先限制長度避免不可信 header 造成任意大小配置。
        // 格式與長度不是秘密，只有相同長度 digest 的比較需要固定時間。
        if (signatureHeader == null || signatureHeader.Length != 44) return false;
        byte[] supplied;
        try { supplied = Convert.FromBase64String(signatureHeader); }
        catch (FormatException) { return false; }
        if (supplied.Length != 32) return false;

        var key = Encoding.UTF8.GetBytes(channelSecret);
        byte[] digest = null;
        try
        {
            // 每次操作獨享密鑰與 HMAC，沒有 static crypto instance。using 先處置 HMAC，
            // finally 再清掉呼叫內產生的密鑰與摘要；不修改或持有借入的原始 body。
            using (var hmac = new HMACSHA256(key)) digest = hmac.ComputeHash(body);
#if NETFRAMEWORK
            var difference = 0;
            for (var index = 0; index < 32; index++) difference |= digest[index] ^ supplied[index];
            return difference == 0;
#else
            return CryptographicOperations.FixedTimeEquals(digest, supplied);
#endif
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
            if (digest != null) Array.Clear(digest, 0, digest.Length);
            Array.Clear(supplied, 0, supplied.Length);
        }
    }
}

```

## Line.Messaging/Messages/Flex/Component/BoxComponent.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Flex/Component/BoxComponent.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class BoxComponent
// 主要成員：Layout、Contents、Flex、Spacing、Margin、Action
// 引用命名空間：System、System.Collections.Generic、System.Text
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging
{
    /// <summary>
    /// This is a component that defines the layout of child components. You can also include a box in a box.
    /// </summary>
    public class BoxComponent : IFlexComponent
    {
        /// <summary>選用的 Box 漸層背景；未指定時保持舊版 Flex JSON，不影響子元件的內容或順序。</summary>
        public Background Background { get; set; }

        /// <summary>
        /// Box
        /// </summary>
        public FlexComponentType Type => FlexComponentType.Box;

        /// <summary>
        /// The placement style of components in this box. Specify one of the following values
        /// </summary>
        /// <param name="layout">
        /// The placement style of components in this box. Specify one of the following values
        /// </param>
        public BoxComponent(BoxLayout layout)
        {
            Layout = layout;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public BoxComponent() { }

        /// <summary>
        /// The placement style of components in this box. Specify one of the following values
        /// <para>(Required)</para>
        /// </summary>
        public BoxLayout Layout { get; set; }

        /// <summary>
        /// Components in this box. Here are the types of components available:<para>
        /// - When the layout property is horizontal or vertical: Box, button, filler, image, separator, and text components</para><para>
        /// - When the layout property is baseline: filler, icon, and text components</para>
        /// <para>(Required)</para>
        /// </summary>
        public IList<IFlexComponent> Contents { get; set; } = new List<IFlexComponent>();

        /// <summary>
        /// The ratio of the width or height of this box within the parent box.
        /// The default value for the horizontal parent box is 1, and the default value for the vertical parent box is 0.
        /// <para>(Optional)</para>
        /// </summary>
        public int? Flex { get; set; }

        /// <summary>
        /// Minimum space between components in this box.
        /// You can specify one of the following values: none, xs, sm, md, lg, xl, or xxl.
        /// none does not set a space while the other values set a space whose size increases in the order of listing. <para>
        /// The default value is none.
        /// To override this setting for a specific component, set the margin property of that component.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public Spacing? Spacing { get; set; }

        /// <summary>
        /// Minimum space between this box and the previous component in the parent box.
        /// You can specify one of the following values: none, xs, sm, md, lg, xl, or xxl.
        /// none does not set a space while the other values set a space whose size increases in the order of listing. <para>
        /// The default value is the value of the spacing property of the parent box.
        /// If this box is the first component in the parent box, the margin property will be ignored.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public Spacing? Margin { get; set; }

        /// <summary>
        /// Action performed when this box is tapped.
        /// Specify an action object. This property is supported on the following versions of LINE.<para>
        /// / LINE for iOS and Android: 8.11.0 and later
        /// / LINE for Windows and macOS: 5.9.0 and later
        /// </para>
        /// </summary>
        public ITemplateAction Action { get; set; }
    }
}

```

## Line.Messaging/Messages/Flex/Style/Background.cs
```
namespace Line.Messaging;

/// <summary>Flex Box 線性漸層的傳輸資料，無資源與全域狀態；各訊息擁有自己的模型，建構後不要跨請求共用可變內容。</summary>
public sealed class Background
{
    /// <summary>LINE 的背景種類；目前只支援 linearGradient，不是 BlockStyle 的背景色。</summary>
    public string Type => "linearGradient";
    /// <summary>漸層方向，採 LINE 格式，例如 90deg。</summary>
    public string Angle { get; set; }
    /// <summary>起點十六進位色碼，例如 #FF0000。</summary>
    public string StartColor { get; set; }
    /// <summary>選用中間點色碼；未指定時由序列化設定省略。</summary>
    public string CenterColor { get; set; }
    /// <summary>終點十六進位色碼，例如 #0000FF。</summary>
    public string EndColor { get; set; }
    /// <summary>選用中間色位置百分比，例如 25%；此資料不改變 Box 內元件順序。</summary>
    public string CenterPosition { get; set; }
}

```

## Line.Messaging/Messages/Sender/Sender.cs
```
using System;

namespace Line.Messaging;

/// <summary>單則訊息的發送者品牌呈現，不代表 channel 身分或授權；只含不可變顯示資料，不保存憑證。</summary>
public sealed class Sender
{
    /// <summary>取得顯示名稱；在建構時驗證一至二十個 UTF-16 單元，避免進入傳輸階段才失敗。</summary>
    public string Name { get; }
    /// <summary>取得絕對 HTTPS 圖示網址；SDK 不下載圖片，亦不持有網路資源。</summary>
    public string IconUrl { get; }
    /// <summary>建立可供訊息獨立引用的品牌資訊；呼叫端不得以此顯示資料取代伺服器端 channel 授權。</summary>
    /// <param name="name">一至二十字的顯示名稱。</param><param name="iconUrl">絕對 HTTPS 圖示網址。</param>
    public Sender(string name, string iconUrl)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 20)
            throw new ArgumentException("Sender name must contain 1 to 20 UTF-16 code units.", nameof(name));
        if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Sender icon must be an absolute HTTPS URL.", nameof(iconUrl));
        Name = name;
        IconUrl = iconUrl;
    }
}

```

## Line.Messaging/Messages/TextMessage.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/TextMessage.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class TextMessage
// 主要成員：Type、QuickReply、Text
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace Line.Messaging
{
    /// <summary>
    /// 表示 LINE 文字訊息
    /// Represents a LINE text message
    /// </summary>
    /// <remarks>
    /// 文字訊息是 LINE Messaging API 中最基本且最常用的訊息類型。
    /// 可以傳送純文字內容給使用者，並支援換行、emoji 和 LINE 表情符號。
    /// <para>
    /// Text message is the most basic and commonly used message type in LINE Messaging API.
    /// Can send plain text content to users and supports line breaks, emoji, and LINE emoticons.
    /// </para>
    /// <para>
    /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#text-message
    /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#text-message
    /// </para>
    /// </remarks>
    /// <example>
    /// 基本使用範例：
    /// <code>
    /// // 建立簡單的文字訊息
    /// var message = new TextMessage("您好，歡迎使用 LINE Bot！");
    ///
    /// // 建立帶有快速回覆的文字訊息
    /// var messageWithQuickReply = new TextMessage("請選擇：", new QuickReply
    /// {
    ///     Items = new List&lt;QuickReplyItem&gt;
    ///     {
    ///         new QuickReplyItem(new MessageTemplateAction("選項 A", "A")),
    ///         new QuickReplyItem(new MessageTemplateAction("選項 B", "B"))
    ///     }
    /// });
    ///
    /// // 傳送訊息
    /// await client.ReplyMessageAsync(replyToken, message);
    /// </code>
    /// </example>
    /// <seealso cref="ISendMessage"/>
    /// <seealso cref="MessageType.Text"/>
    /// <seealso cref="QuickReply"/>
    public class TextMessage : ISendMessage
    {
        /// <summary>單則訊息選用的品牌呈現；未設定時不輸出 sender，維持既有 JSON。不得用它決定授權 channel。</summary>
        public Sender Sender { get; set; }

        /// <summary>
        /// 取得訊息類型，固定為 Text
        /// Gets the message type, always Text
        /// </summary>
        public MessageType Type { get; } = MessageType.Text;

        /// <summary>
        /// 取得或設定快速回覆選單
        /// Gets or sets the quick reply menu
        /// </summary>
        /// <value>
        /// 快速回覆選單物件，可包含最多 13 個快速回覆按鈕
        /// Quick reply object that can contain up to 13 quick reply buttons
        /// </value>
        /// <remarks>
        /// 快速回覆功能支援 LINE iOS 8.11.0 及 Android 8.11.0 以上版本。
        /// Quick reply feature is supported on LINE iOS 8.11.0 and Android 8.11.0 or later.
        /// </remarks>
        /// <seealso cref="QuickReply"/>
        public QuickReply QuickReply { get; set; }

        /// <summary>
        /// 取得或設定訊息文字內容
        /// Gets or sets the message text content
        /// </summary>
        /// <value>
        /// 訊息文字，最多 2000 字元（超過會自動截斷）
        /// Message text, maximum 2000 characters (automatically truncated if exceeded)
        /// </value>
        /// <remarks>
        /// 文字內容支援：
        /// - 換行符號（\n）
        /// - Unicode emoji（如 😀）
        /// - LINE 表情符號（使用 $ 符號，如 $(product_id)）
        /// <para>
        /// Text content supports:
        /// - Line breaks (\n)
        /// - Unicode emoji (e.g., 😀)
        /// - LINE emoticons (using $ symbol, e.g., $(product_id))
        /// </para>
        /// </remarks>
        public string Text { get; set; }

        /// <summary>
        /// 初始化 TextMessage 的新執行個體
        /// Initializes a new instance of the TextMessage class
        /// </summary>
        /// <param name="text">
        /// 訊息文字內容，最多 2000 字元。如果超過長度限制，會自動截斷至 2000 字元。
        /// Message text content, maximum 2000 characters. Automatically truncated to 2000 characters if exceeded.
        /// </param>
        /// <param name="quickReply">
        /// 快速回覆選單（選用），預設為 null
        /// Quick reply menu (optional), default is null
        /// </param>
        /// <remarks>
        /// 建構函式會自動處理文字長度限制：
        /// - 如果文字少於 2000 字元，保持原樣
        /// - 如果文字超過 2000 字元，自動截斷至 2000 字元
        /// <para>
        /// Constructor automatically handles text length limit:
        /// - If text is less than 2000 characters, keeps as is
        /// - If text exceeds 2000 characters, automatically truncates to 2000 characters
        /// </para>
        /// </remarks>
        /// <example>
        /// 使用範例：
        /// <code>
        /// // 基本用法
        /// var message1 = new TextMessage("Hello, LINE!");
        ///
        /// // 帶有換行的文字
        /// var message2 = new TextMessage("第一行\n第二行\n第三行");
        ///
        /// // 帶有 emoji
        /// var message3 = new TextMessage("感謝您的訂購！😊");
        ///
        /// // 帶有快速回覆
        /// var quickReply = new QuickReply { Items = new List&lt;QuickReplyItem&gt; { ... } };
        /// var message4 = new TextMessage("請選擇操作：", quickReply);
        ///
        /// // 超過 2000 字元會自動截斷
        /// var longText = new string('A', 3000); // 3000 個 'A'
        /// var message5 = new TextMessage(longText); // 實際只會包含 2000 個 'A'
        /// </code>
        /// </example>
        public TextMessage(string text, QuickReply quickReply = null)
        {
            Text = text.Substring(0, Math.Min(text.Length, 2000));
            QuickReply = quickReply;
        }
    }
}

```

## Line.Messaging/Webhooks/DeliveryContext.cs
```
namespace Line.Messaging.Webhooks;

/// <summary>單一 webhook 投遞的不可變資訊；重送不代表新業務事件，後續去重應使用相同 WebhookEventId。</summary>
public sealed class DeliveryContext
{
    /// <summary>是否為平台重送；缺少欄位的舊事件預設 false，不推測外部請求的信任程度。</summary>
    public bool IsRedelivery { get; }
    /// <summary>建立事件專屬的投遞資訊，不持有 request、來源 JSON 或身分憑證。</summary>
    /// <param name="isRedelivery">平台已驗簽內容中的重送旗標。</param>
    public DeliveryContext(bool isRedelivery) => IsRedelivery = isRedelivery;
}

```

## Line.Messaging/Webhooks/WebhookEvent.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Webhooks/WebhookEvent.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class WebhookEvent
// 主要成員：CreateFrom、Type、Source、Timestamp
// 引用命名空間：System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;

namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// LINE webhook 的單筆事件；保留原有型別、來源與時間建構契約，新投遞欄位僅由解析器設定。
    /// 模型不驗證簽章也不授予租戶權限；呼叫端必須先驗原始 body 的簽章再消費事件。
    /// </summary>
    public abstract class WebhookEvent
    {
        /// <summary>平台提供的穩定事件 ID；重送保持同值，缺少欄位的舊事件為 null；不取代後續業務資料庫 ID。</summary>
        public string WebhookEventId { get; private set; }

        /// <summary>本次投遞資訊；每個事件獨立持有，不保留 JSON、HTTP request、token 或跨事件的可變狀態。</summary>
        public DeliveryContext DeliveryContext { get; private set; } = new DeliveryContext(false);

        /// <summary>
        /// 事件類別，由已知 enum 判定；未知類別沿用舊行為略過，不猜測業務意圖。
        /// </summary>
        public WebhookEventType Type { get; }

        /// <summary>
        /// 此事件專屬的來源資料；只供路由提示，不能直接視為伺服器端已授權使用者。
        /// </summary>
        public WebhookEventSource Source { get; }

        /// <summary>
        /// 平台事件時間的 Unix 毫秒值；重送時不以接收時間覆寫，供後續去重與排序。
        /// </summary>
        public long Timestamp { get; }

        /// <summary>建立事件既有欄位；建構時沒有 HTTP、token 或共用可變狀態，投遞資訊預設為非重送。</summary>
        /// <param name="type">已知事件類別。</param><param name="source">事件專屬來源。</param><param name="timestamp">平台事件 Unix 毫秒。</param>
        public WebhookEvent(WebhookEventType type, WebhookEventSource source, long timestamp)
        {
            Type = type;
            Source = source;
            Timestamp = timestamp;
        }

        /// <summary>在既有各類事件建構後補入共同投遞欄位；所有類別共走此入口，避免遺漏某個 switch 分支。</summary>
        /// <param name="dynamicObject">呼叫端已完成驗簽及 JSON 解析的單筆物件；不保存原 JSON。</param><returns>可辨識的 detached 事件，或沿用舊行為回 null。</returns>
        internal static WebhookEvent CreateFrom(dynamic dynamicObject)
        {
            WebhookEvent result = CreateEvent(dynamicObject);
            if (result == null) return null;
            result.WebhookEventId = (string)dynamicObject.webhookEventId;
            result.DeliveryContext = new DeliveryContext((bool?)dynamicObject.deliveryContext?.isRedelivery ?? false);
            return result;
        }

        /// <summary>保留既有類別映射，不把投遞 ID 當成訊息或資料庫記錄的 ID；整個候選完成後才交還呼叫端。</summary>
        /// <param name="dynamicObject">單筆已解析事件。</param><returns>已知事件或 null。</returns>
        private static WebhookEvent CreateEvent(dynamic dynamicObject)
        {
            if (dynamicObject == null) { throw new ArgumentNullException(nameof(dynamicObject)); }

            var eventSource = WebhookEventSource.CreateFrom(dynamicObject?.source);

            if (eventSource == null)
            {
                return null;
            }
            if (!Enum.TryParse((string)dynamicObject.type, true, out WebhookEventType eventType))
            {
                return null;
            }

            switch (eventType)
            {
                case WebhookEventType.Message:
                    EventMessage eventMessage = EventMessage.CreateFrom(dynamicObject);
                    if (eventMessage == null) { return null; }
                    return new MessageEvent(eventSource, (long)dynamicObject.timestamp, eventMessage, (string)dynamicObject.replyToken);
                case WebhookEventType.Follow:
                    return new FollowEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken);
                case WebhookEventType.Unfollow:
                    return new UnfollowEvent(eventSource, (long)dynamicObject.timestamp);
                case WebhookEventType.Join:
                    return new JoinEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken);
                case WebhookEventType.Leave:
                    return new LeaveEvent(eventSource, (long)dynamicObject.timestamp);
                case WebhookEventType.Postback:
                    var postback = new Postback(
                        (string)dynamicObject.postback?.data,
                        (string)dynamicObject.postback?.@params?.date,
                        (string)dynamicObject.postback?.@params?.time,
                        (string)dynamicObject.postback?.@params?.datetime);
                    return new PostbackEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken, postback);
                case WebhookEventType.Beacon:
                    if (!Enum.TryParse((string)dynamicObject.beacon.type, true, out BeaconType beaconType))
                    {
                        return null;
                    }
                    return new BeaconEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken,
                        (string)dynamicObject.beacon.hwid, beaconType, (string)dynamicObject.beacon.dm);
                case WebhookEventType.AccountLink:
                    var link = new Link((string)dynamicObject.link?.result, (string)dynamicObject.link?.nonce);
                    return new AccountLinkEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken, link);
                case WebhookEventType.Things:
                    var thingsType = (ThingsType)Enum.Parse(typeof(ThingsType), (string)dynamicObject.things?.type, true);
                    var things = new Things((string)dynamicObject.things?.deviceId, thingsType);
                    return DeviceEvent.Create(eventSource, (long)dynamicObject.timestamp, things);
                case WebhookEventType.MemberJoined:

                    var joinedMembers = new List<WebhookEventSource>();
                    foreach (var member in dynamicObject.joined.members)
                    {
                        joinedMembers.Add(WebhookEventSource.CreateFrom(member));
                    }
                    return new MemberJoinEvent(eventSource, (long)dynamicObject.timestamp, (string)dynamicObject.replyToken, joinedMembers);

                case WebhookEventType.MemberLeft:
                    var leftMembers = new List<WebhookEventSource>();
                    foreach (var member in dynamicObject.left.members)
                    {
                        leftMembers.Add(WebhookEventSource.CreateFrom(member));
                    }
                    return new MemberLeaveEvent(eventSource, (long)dynamicObject.timestamp, leftMembers);
                default:
                    return null;
            }
        }
    }
}

```

## Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs
```
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class WebhookRequestMessageHelper
// 主要成員：GetWebhookEventsAsync
// 引用命名空間：Newtonsoft.Json、System、System.Collections.Generic、System.Linq、System.Net.Http、System.Text、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if !NETSTANDARD1_4
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Line.Messaging.Webhooks
{
    public static class WebhookRequestMessageHelper
    {
        /// <summary>
        /// 先對傳輸收到的原始 body 位元組驗證 HMAC，再以 UTF-8 解析同一份內容；不受 Content-Type charset 影響。
        /// body 與解析字串僅存活於本次呼叫，request 及其 content 仍由呼叫端擁有，本方法不保存跨請求狀態。
        /// </summary>
        /// <param name="request">由呼叫端擁有且不可在處理期間並行修改的 HTTP request。</param>
        /// <param name="channelSecret">伺服器端已授權 channel 的密鑰。</param>
        /// <param name="botUserId">選用的預期 bot ID；不匹配時 fail closed。</param>
        /// <returns>此次 request 的已解析事件集合。</returns>
        public static async Task<IEnumerable<WebhookEvent>> GetWebhookEventsAsync(this HttpRequestMessage request, string channelSecret, string botUserId = null)
        {
            if (request == null) { throw new ArgumentNullException(nameof(request)); }
            if (channelSecret == null) { throw new ArgumentNullException(nameof(channelSecret)); }
            if (request.Content == null) { throw new ArgumentException("Webhook request content is required.", nameof(request)); }

            var body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            var xLineSignature = request.Headers.TryGetValues("X-Line-Signature", out var signatures)
                ? signatures.FirstOrDefault()
                : null;
            if (string.IsNullOrEmpty(xLineSignature) || !LineSignature.Verify(body, xLineSignature, channelSecret))
            {
                throw new InvalidSignatureException("Signature validation failed.");
            }

            // LINE body 是 UTF-8 JSON；忽略可被中介層錯誤設定的 Content-Type charset，並拒絕非法 UTF-8。
            var content = new UTF8Encoding(false, true).GetString(body);
            dynamic json = JsonConvert.DeserializeObject(content);

            if (!string.IsNullOrEmpty(botUserId))
            {
                if(botUserId != (string)json.destination)
                {
                    throw new UserIdMismatchException("Bot user ID does not match.");
                }
            }
            return WebhookEventParser.ParseEvents(json.events);
        }
    }
}
#endif

```

## 固定修改清單與來源SHA
[
  {
    "file": "Line.Messaging.Tests/Golden/Flex.json",
    "sha256": "ccb2f937136cbd28bc5f86d1b0235178336488f8c812cde655ec4156f9c90435"
  },
  {
    "file": "Line.Messaging.Tests/Golden/Image.json",
    "sha256": "be2bc6d62a408baa8be37f40d57feeb8b18dad163363281a4701a9f0a2b85cb8"
  },
  {
    "file": "Line.Messaging.Tests/Golden/QuickReply.json",
    "sha256": "3b7e6f127382a62cfb3f662c0b75d210e0b09e5e5471f499a0456ca3a2a576b7"
  },
  {
    "file": "Line.Messaging.Tests/Golden/RichMenu.json",
    "sha256": "a597cd94e9c92977d33429b4fa11073b3be942bed527c1d268959ef056bba257"
  },
  {
    "file": "Line.Messaging.Tests/Golden/Template.json",
    "sha256": "fdc9ebf8b461822e79448743b27562cfd46be954457f4c5984feece9fb7f27be"
  },
  {
    "file": "Line.Messaging.Tests/Golden/Text.json",
    "sha256": "dede8ca38e2482d758ff5af6625812f600fed2cc9c2a4a5aae120e86d0fdb5ff"
  },
  {
    "file": "Line.Messaging.Tests/LiffAndStreamLifecycleTests.cs",
    "sha256": "1aa05244a4715ff1ac77d6839a94ade8edcec3bda9d6cb0779a4813353d3d5a9"
  },
  {
    "file": "Line.Messaging.Tests/Line.Messaging.Tests.csproj",
    "sha256": "c37333b6957e2aa7657fcc345bc5669f42a9a7919c2d260bf55ea8d7d98ad5db"
  },
  {
    "file": "Line.Messaging.Tests/LineClientLifecycleTests.cs",
    "sha256": "3d6aaf4dcfd964da7956ad6b27d79fc339b9c5896ba373153e9aba52dbc957bc"
  },
  {
    "file": "Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs",
    "sha256": "b145ce4a91ca89d04580d22036a2ce2eb4e535d0de7b0683f53d3a88fcba3071"
  },
  {
    "file": "Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs",
    "sha256": "0c8d01bd4e14a76235f35bf5872c1e961e6b6ce8218199022a842e35d7d57004"
  },
  {
    "file": "Line.Messaging.Tests/LineSdkFoundationTests.cs",
    "sha256": "329131d14430f7efafadff1f717d43fee86431b18e668c1f807ead983a6bb687"
  },
  {
    "file": "Line.Messaging.Tests/LineSdkGoldenTests.cs",
    "sha256": "dbd4ca03b2d232c626c228879a8fd91c6de5abaadcbf386fb08788992c7a5864"
  },
  {
    "file": "Line.Messaging.Tests/WebhookRawSignatureTests.cs",
    "sha256": "f5a5b3598d4b7df66dffd7f2545bdfba23cb8db1daf0a90632cb181874dc7f80"
  },
  {
    "file": "Line.Messaging/BorrowedStreamContent.cs",
    "sha256": "e8df1c696ccb3d2dccfda4c248222462eb6b9b089e55ab55b27d57843cbe1463"
  },
  {
    "file": "Line.Messaging/ChannelHttpTransport.cs",
    "sha256": "65651c7b1f8eed55515dbb0f4fba9b15ad7412220d3f6234b0bf8d62317fa932"
  },
  {
    "file": "Line.Messaging/ContentStream.cs",
    "sha256": "849c7c49c1144caf28ab58ade05da0557d70bed46a8548d91ced0730d84ff94c"
  },
  {
    "file": "Line.Messaging/HttpResponseMessageExtensions.cs",
    "sha256": "ef1ab397161decbc78d058e47c77981fb52ab3df88eec72563a4ffb5b914c903"
  },
  {
    "file": "Line.Messaging/Liff/LiffClient.cs",
    "sha256": "c30d0b9aa5dfd809d082b43c91884240080c4d92ddcc1228c39dbd55da3f6bae"
  },
  {
    "file": "Line.Messaging/Line.Messaging.csproj",
    "sha256": "68a0202628e38affaa0a6a49cba354402e62eec59698bdb0a64163abf891959c"
  },
  {
    "file": "Line.Messaging/LineMessagingClient.cs",
    "sha256": "73caa23cecd5283ac522705f791b89a657723e0760846bd0d15f765feb2196de"
  },
  {
    "file": "Line.Messaging/LineSignature.cs",
    "sha256": "af6ac05c4d4769a9b58c232095e1f3107669d69aeaaa43950cd6193493b1f6af"
  },
  {
    "file": "Line.Messaging/Messages/Flex/Component/BoxComponent.cs",
    "sha256": "0f242093bf53a9f4650e7407743449b6064d286ffcf74c68970723aa649f827d"
  },
  {
    "file": "Line.Messaging/Messages/Flex/Style/Background.cs",
    "sha256": "e5dd92f6e3d315dbd1361eebc50b8ec40bc7541bf9a0f6c24241db3a06eb335e"
  },
  {
    "file": "Line.Messaging/Messages/Sender/Sender.cs",
    "sha256": "004fb31d02ccdccc661cb1fe5f525e5f85bbd75585af7a92a0e9c3807a6c5394"
  },
  {
    "file": "Line.Messaging/Messages/TextMessage.cs",
    "sha256": "f459fa0d96ad47355479418d51ffb594f84c0eac88c94628dd9037ca2cf4ed21"
  },
  {
    "file": "Line.Messaging/Webhooks/DeliveryContext.cs",
    "sha256": "3b2707de649bf47e1328b3f0f2106da2df5cb7baf79a4a5396b4c5da0bbe621b"
  },
  {
    "file": "Line.Messaging/Webhooks/WebhookEvent.cs",
    "sha256": "e17431861b34af24dd78507827d8e24469b660f374c20931f5efdbcb3176b677"
  },
  {
    "file": "Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs",
    "sha256": "c99798296f8ce73d69bd08ccf115695f71b9f890b7e093a8d908eea2891efee3"
  }
]

## 實際全方案執行摘要
{
  "reports": 16,
  "totals": {
    "total": 1836,
    "executed": 1836,
    "passed": 1836,
    "failed": 0,
    "notExecuted": 0
  },
  "runs": [
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_36_net10.0.trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\LineMessagingProcessor.RichMenus.Tests\\bin\\Release\\net10.0\\LineMessagingProcessor.RichMenus.Tests.dll"
      ],
      "counters": {
        "total": "34",
        "executed": "34",
        "passed": "34",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_36_net10.0[1].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\LineMessagingProcessor.Workflows.Tests\\bin\\Release\\net10.0\\LineMessagingProcessor.Workflows.Tests.dll"
      ],
      "counters": {
        "total": "33",
        "executed": "33",
        "passed": "33",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_36_net10.0[2].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\SpeechMessage.Payments.Tests\\bin\\Release\\net10.0\\SpeechMessage.Payments.Tests.dll"
      ],
      "counters": {
        "total": "55",
        "executed": "55",
        "passed": "55",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_36_net10.0[3].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\LineMessagingProcessor.Tests\\bin\\Release\\net10.0\\LineMessagingProcessor.Tests.dll"
      ],
      "counters": {
        "total": "79",
        "executed": "79",
        "passed": "79",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_36_net10.0[4].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\LineMessagingProcessor.AspNetCore.Tests\\bin\\Release\\net10.0\\LineMessagingProcessor.AspNetCore.Tests.dll"
      ],
      "counters": {
        "total": "4",
        "executed": "4",
        "passed": "4",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_36_net10.0[5].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\Line.Messaging.Tests\\bin\\Release\\net10.0\\Line.Messaging.Tests.dll"
      ],
      "counters": {
        "total": "101",
        "executed": "101",
        "passed": "101",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_36_net10.0[6].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\ToolUtility.Tests\\bin\\Release\\net10.0\\ToolUtility.Tests.dll"
      ],
      "counters": {
        "total": "289",
        "executed": "289",
        "passed": "289",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_36_net10.0[7].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\SpeechMessage.Localization.Tests\\bin\\Release\\net10.0\\SpeechMessage.Localization.Tests.dll"
      ],
      "counters": {
        "total": "67",
        "executed": "67",
        "passed": "67",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_37_net10.0.trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\SpeechMessage.Dynamics.Deployment.Tests\\bin\\Release\\net10.0\\SpeechMessage.Dynamics.Deployment.Tests.dll"
      ],
      "counters": {
        "total": "62",
        "executed": "62",
        "passed": "62",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_37_net10.0[1].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\ToolUtility.Dataverse.Tests\\bin\\Release\\net10.0\\ToolUtility.Dataverse.Tests.dll"
      ],
      "counters": {
        "total": "79",
        "executed": "79",
        "passed": "79",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_37_net10.0[2].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\ChurchReport.MemberInfo.Tests\\bin\\Release\\net10.0\\ChurchReport.MemberInfo.Tests.dll"
      ],
      "counters": {
        "total": "612",
        "executed": "612",
        "passed": "612",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_37_net48.trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\SpeechMessage.Localization.Tests\\bin\\Release\\net48\\SpeechMessage.Localization.Tests.dll"
      ],
      "counters": {
        "total": "43",
        "executed": "43",
        "passed": "43",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_37_net48[1].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\ToolUtility.Tests\\bin\\Release\\net48\\ToolUtility.Tests.dll"
      ],
      "counters": {
        "total": "211",
        "executed": "211",
        "passed": "211",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_37_net48[2].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\Line.Messaging.Tests\\bin\\Release\\net48\\Line.Messaging.Tests.dll"
      ],
      "counters": {
        "total": "96",
        "executed": "96",
        "passed": "96",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_38_net48.trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\LineMessagingProcessor.Tests\\bin\\Release\\net48\\LineMessagingProcessor.Tests.dll"
      ],
      "counters": {
        "total": "46",
        "executed": "46",
        "passed": "46",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_56_38_net48[1].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\SpeechMessage.Dynamics.Plugins.Tests\\bin\\Release\\net48\\SpeechMessage.Dynamics.Plugins.Tests.dll"
      ],
      "counters": {
        "total": "25",
        "executed": "25",
        "passed": "25",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    }
  ]
}


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
