ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-p1-sdk

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
Active task: .trellis/tasks/11-05-integration-p1
唯讀 code review P1-03 LINE SDK 全部實作（含已commit與未commit）。固定P0基準 cb932c01，不能只審 git diff HEAD。禁止修改檔案／呼叫額外模型。請讀完整來源，報告 Critical/High/Warning/Info，檔案行號、可重現原因、最小修正。沒有finding需明說。審：雙目標net462/net10相容；六golden逐字舊行為；Sender/Background/Webhook附加欄位；原始bytes HMAC固定時間；多channel共用HttpClient每request auth隔離；所有request/response/content/stream所有權；取消、故障、Dispose races、錯誤不洩漏credential/user detail；upload stream借入不能Dispose。所有修改區繁中XML文件與UTF8/CRLF。
現有證據：全方案Release build 0errors17既有ToolUtility.Tests warnings；SDK net10 87/87 net48 82/82；其中23 lifecycle cases各target，A/B 1000；請找測試未覆蓋的實際缺陷，不把mock等同live。
測試提供借用transport，client.Dispose不得Dispose外部transport；下載ContentStream須持有response直到stream.Dispose；一般API完成即Dispose response。無實際LINE呼叫。
下面為固定基準完整diff（含新檔）。
diff --git a/Line.Messaging.Tests/Golden/Flex.json b/Line.Messaging.Tests/Golden/Flex.json
new file mode 100644
index 000000000..e6e060f31
--- /dev/null
+++ b/Line.Messaging.Tests/Golden/Flex.json
@@ -0,0 +1 @@
+{"to":"synthetic-recipient","messages":[{"type":"flex","contents":{"type":"bubble","direction":"ltr","body":{"type":"box","layout":"vertical","contents":[{"type":"text","text":"合成標題","wrap":true,"weight":"bold"}]}},"altText":"合成卡片"}]}
diff --git a/Line.Messaging.Tests/Golden/Image.json b/Line.Messaging.Tests/Golden/Image.json
new file mode 100644
index 000000000..226f0b0d5
--- /dev/null
+++ b/Line.Messaging.Tests/Golden/Image.json
@@ -0,0 +1 @@
+{"to":"synthetic-recipient","messages":[{"type":"image","originalContentUrl":"https://example.invalid/full.png","previewImageUrl":"https://example.invalid/preview.png"}]}
diff --git a/Line.Messaging.Tests/Golden/QuickReply.json b/Line.Messaging.Tests/Golden/QuickReply.json
new file mode 100644
index 000000000..a0d1c974f
--- /dev/null
+++ b/Line.Messaging.Tests/Golden/QuickReply.json
@@ -0,0 +1 @@
+{"to":"synthetic-recipient","messages":[{"type":"text","quickReply":{"items":[{"type":"action","imageUrl":"https://example.invalid/icon.png","action":{"type":"message","label":"選擇","text":"SELECT"}}]},"text":"合成選項"}]}
diff --git a/Line.Messaging.Tests/Golden/RichMenu.json b/Line.Messaging.Tests/Golden/RichMenu.json
new file mode 100644
index 000000000..4c75b4205
--- /dev/null
+++ b/Line.Messaging.Tests/Golden/RichMenu.json
@@ -0,0 +1 @@
+{"size":{"width":2500,"height":843},"selected":true,"name":"合成選單","chatBarText":"開啟","areas":[{"bounds":{"x":0,"y":0,"width":1250,"height":843},"action":{"type":"message","label":"合成操作","text":"OPEN"}}]}
diff --git a/Line.Messaging.Tests/Golden/Template.json b/Line.Messaging.Tests/Golden/Template.json
new file mode 100644
index 000000000..131fee3e9
--- /dev/null
+++ b/Line.Messaging.Tests/Golden/Template.json
@@ -0,0 +1 @@
+{"to":"synthetic-recipient","messages":[{"type":"template","template":{"type":"confirm","text":"確認？","actions":[{"type":"message","label":"是","text":"YES"},{"type":"message","label":"否","text":"NO"}]},"altText":"合成確認"}]}
diff --git a/Line.Messaging.Tests/Golden/Text.json b/Line.Messaging.Tests/Golden/Text.json
new file mode 100644
index 000000000..5169146fa
--- /dev/null
+++ b/Line.Messaging.Tests/Golden/Text.json
@@ -0,0 +1 @@
+{"to":"synthetic-recipient","messages":[{"type":"text","text":"合成文字\n\"quote\" \\ 😀"}]}
diff --git a/Line.Messaging.Tests/Line.Messaging.Tests.csproj b/Line.Messaging.Tests/Line.Messaging.Tests.csproj
index 2334d450a..161256664 100644
--- a/Line.Messaging.Tests/Line.Messaging.Tests.csproj
+++ b/Line.Messaging.Tests/Line.Messaging.Tests.csproj
@@ -1,6 +1,9 @@
 <Project Sdk="Microsoft.NET.Sdk">
   <PropertyGroup>
-    <TargetFramework>net10.0</TargetFramework>
+    <TargetFrameworks>net48;net10.0</TargetFrameworks>
+    <LangVersion>latest</LangVersion>
+    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
+    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
     <ImplicitUsings>enable</ImplicitUsings>
     <Nullable>enable</Nullable>
     <IsPackable>false</IsPackable>
@@ -14,13 +17,22 @@
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
     <PackageReference Include="FluentAssertions" />
-    <PackageReference Include="Microsoft.Extensions.Configuration" />
-    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" />
-    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
+    <PackageReference Include="Microsoft.Extensions.Configuration" Condition="'$(TargetFramework)' == 'net10.0'" />
+    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Condition="'$(TargetFramework)' == 'net10.0'" />
+    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Condition="'$(TargetFramework)' == 'net10.0'" />
     <PackageReference Include="Newtonsoft.Json" />
   </ItemGroup>
   <ItemGroup>
     <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
-    <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />
+    <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" Condition="'$(TargetFramework)' == 'net10.0'" />
+  </ItemGroup>
+  <ItemGroup>
+    <EmbeddedResource Include="Golden/*.json" />
+  </ItemGroup>
+  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
+    <!-- 此檔保護 net10 主機的設定來源；SDK 的全部既有端點與模型案例在兩個宿主都執行。 -->
+    <Compile Remove="LineMessagingProcessorCredentialTests.cs" />
+    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
+    <Reference Include="System.Net.Http" />
   </ItemGroup>
 </Project>
diff --git a/Line.Messaging.Tests/LineClientLifecycleTests.cs b/Line.Messaging.Tests/LineClientLifecycleTests.cs
new file mode 100644
index 000000000..53fb9f263
--- /dev/null
+++ b/Line.Messaging.Tests/LineClientLifecycleTests.cs
@@ -0,0 +1,211 @@
+using System;
+using System.Collections.Generic;
+using System.IO;
+using System.Linq;
+using System.Net;
+using System.Net.Http;
+using System.Net.Http.Headers;
+using System.Text;
+using System.Threading;
+using System.Threading.Tasks;
+using Xunit;
+
+namespace Line.Messaging.Tests;
+
+/// <summary>
+/// 保護共用雙目標 SDK 的 channel 隔離與 HTTP 資源所有權；全部 handler 離線、資料合成。
+/// 故障注入涵蓋遠端失敗與傳輸失敗，決定性檢查內容 Dispose、借用串流仍開啟及並行作業排空。
+/// </summary>
+public sealed class LineClientLifecycleTests
+{
+    /// <summary>同一 transport 給 A/B channel 共用時，每次請求都必須使用自己的 credential，不能修改共用預設 header。</summary>
+    /// <returns>完成並排空一千筆交錯推播。</returns>
+    [Fact]
+    public async Task SharedTransport_TwoChannelsParallel_NoCredentialLeak()
+    {
+        using var handler = new IsolationHandler();
+        using var transport = new HttpClient(handler);
+        using var first = new LineMessagingClient(transport, "synthetic-A");
+        using var second = new LineMessagingClient(transport, "synthetic-B");
+        Assert.Null(transport.DefaultRequestHeaders.Authorization);
+        var tasks = Enumerable.Range(0, 1000).Select(index => (index % 2 == 0 ? first : second)
+            .PushMessageAsync(index % 2 == 0 ? "A" : "B", new[] { new TextMessage("合成") })).ToArray();
+        await Task.WhenAll(tasks);
+        Assert.Equal(1000, handler.Completed);
+        Assert.Equal(0, handler.Mismatches);
+        Assert.Equal(0, handler.Active);
+        Assert.Null(transport.DefaultRequestHeaders.Authorization);
+    }
+
+    /// <summary>成功、HTTP 失敗與 transport 失敗都要釋放自己的 request/content；回傳值完全讀完後才處置 response。</summary>
+    /// <param name="kind">代表各傳輸組裝方式的操作。</param><param name="fault">none、http 或 transport 故障。</param><returns>排空的單筆操作。</returns>
+    [Theory]
+    [InlineData("Push", "none")]
+    [InlineData("Push", "http")]
+    [InlineData("Push", "transport")]
+    [InlineData("Profile", "none")]
+    [InlineData("Profile", "http")]
+    [InlineData("Delete", "none")]
+    [InlineData("Delete", "http")]
+    [InlineData("EmptyPost", "none")]
+    [InlineData("EmptyPost", "http")]
+    [InlineData("RichMenu", "none")]
+    [InlineData("RichMenu", "http")]
+    [InlineData("Bytes", "none")]
+    [InlineData("Bytes", "http")]
+    [InlineData("OAuth", "none")]
+    [InlineData("OAuth", "http")]
+    public async Task RequestsAndResponses_AreDisposed(string kind, string fault)
+    {
+        using var handler = new OwnershipHandler(fault);
+        using var transport = new HttpClient(handler);
+        using var client = new LineMessagingClient(transport, "synthetic-token");
+        var exception = await Record.ExceptionAsync(() => Invoke(kind, client, transport));
+        if (fault == "none") Assert.Null(exception); else Assert.NotNull(exception);
+        if (handler.Content != null) Assert.True(handler.Content.Disposed);
+        if (handler.Request?.Content != null)
+            await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request.Content.ReadAsStringAsync());
+    }
+
+    /// <summary>下載成功時 response 所有權交給 ContentStream；串流釋放前內容仍可讀，重複 Dispose 不重複保留資源。</summary>
+    /// <param name="kind">三種媒體下載入口。</param><returns>排空且已釋放內容的下載作業。</returns>
+    [Theory]
+    [InlineData("Content")]
+    [InlineData("Preview")]
+    [InlineData("RichMenu")]
+    public async Task Download_StreamOwnsResponse_UntilDisposed(string kind)
+    {
+        using var handler = new OwnershipHandler("none");
+        using var transport = new HttpClient(handler);
+        using var client = new LineMessagingClient(transport, "synthetic-token");
+        var stream = kind == "Content" ? await client.GetContentStreamAsync("synthetic") : kind == "Preview" ? await client.GetContentPreviewAsync("synthetic") : await client.DownloadRichMenuImageAsync("synthetic");
+        try
+        {
+            Assert.False(handler.Content!.Disposed);
+            Assert.True(stream.CanRead);
+            Assert.NotEqual(-1, stream.ReadByte());
+        }
+        finally { stream.Dispose(); }
+        Assert.True(handler.Content!.Disposed);
+        stream.Dispose();
+        Assert.False(stream.CanRead);
+    }
+
+    /// <summary>上傳內容由 SDK 釋放，但輸入串流是呼叫端借入；成功或遠端失敗均不能提前關閉呼叫端檔案。</summary>
+    /// <param name="fault">成功或遠端故障。</param><returns>排空的上傳作業。</returns>
+    [Theory]
+    [InlineData("none")]
+    [InlineData("http")]
+    public async Task Upload_BorrowedStream_RemainsOpen(string fault)
+    {
+        using var handler = new OwnershipHandler(fault);
+        using var transport = new HttpClient(handler);
+        using var client = new LineMessagingClient(transport, "synthetic-token");
+        using var input = new MemoryStream(new byte[] { 1, 2, 3 });
+        var exception = await Record.ExceptionAsync(() => client.UploadRichMenuPngImageAsync(input, "synthetic"));
+        if (fault == "none") Assert.Null(exception); else Assert.NotNull(exception);
+        Assert.True(input.CanRead);
+        Assert.True(handler.Content!.Disposed);
+        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
+    }
+
+    /// <summary>借入 transport 不由 SDK Dispose；已釋放的 SDK 則不能再帶 credential 發送新請求。</summary>
+    /// <returns>已完成的生命週期檢查。</returns>
+    [Fact]
+    public async Task Dispose_BorrowedTransportSurvives_ClientStopsSending()
+    {
+        using var handler = new OwnershipHandler("none");
+        using var transport = new HttpClient(handler);
+        var client = new LineMessagingClient(transport, "synthetic-token");
+        client.Dispose();
+        client.Dispose();
+        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetBotInfoAsync());
+        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/transport-check");
+        using var response = await transport.SendAsync(request);
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    /// <summary>對外例外不能帶出整個遠端 body 或含使用者 ID 的 URL；仍保留可辨識的未授權型別。</summary>
+    /// <returns>已完成的錯誤路徑。</returns>
+    [Fact]
+    public async Task Unauthorized_ErrorDoesNotExposeRemoteBodyOrUrl()
+    {
+        using var handler = new OwnershipHandler("unauthorized");
+        using var transport = new HttpClient(handler);
+        using var client = new LineMessagingClient(transport, "synthetic-token");
+        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.GetUserProfileAsync("private-synthetic-user"));
+        Assert.DoesNotContain("sensitive-synthetic-detail", exception.Message);
+        Assert.DoesNotContain("private-synthetic-user", exception.Message);
+        Assert.True(handler.Content!.Disposed);
+    }
+
+    /// <summary>固定案例涵蓋 JSON、GET、DELETE、空 POST、bytes 與 OAuth form，不接觸外部端點。</summary>
+    private static Task Invoke(string kind, LineMessagingClient client, HttpClient transport) => kind switch
+    {
+        "Push" => client.PushMessageAsync("synthetic", new[] { new TextMessage("合成") }),
+        "Profile" => client.GetUserProfileAsync("synthetic"),
+        "Delete" => client.DeleteRichMenuAsync("synthetic"),
+        "EmptyPost" => client.LeaveFromGroupAsync("synthetic"),
+        "RichMenu" => client.CreateRichMenuAsync(new RichMenu()),
+        "Bytes" => client.GetContentBytesAsync("synthetic"),
+        "OAuth" => LineMessagingClient.IssueChannelAccessTokenAsync(transport, "synthetic-channel", "synthetic-secret"),
+        _ => throw new ArgumentException("未知測試操作。", nameof(kind))
+    };
+
+    /// <summary>只保留有界計數，不保存任何請求或 token；測試擁有 handler 並等待全部作業後 Dispose。</summary>
+    private sealed class IsolationHandler : HttpMessageHandler
+    {
+        internal int Completed;
+        internal int Mismatches;
+        internal int Active;
+        /// <summary>以本筆 body 的合成 A/B 範圍比對 header，完成後不保留 body 或 credential。</summary>
+        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
+        {
+            Interlocked.Increment(ref Active);
+            try
+            {
+                cancellationToken.ThrowIfCancellationRequested();
+                var body = await request.Content!.ReadAsStringAsync();
+                var expected = body.Contains("\"to\":\"A\"") ? "synthetic-A" : "synthetic-B";
+                if (request.Headers.Authorization?.Parameter != expected) Interlocked.Increment(ref Mismatches);
+                Interlocked.Increment(ref Completed);
+                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
+            }
+            finally { Interlocked.Decrement(ref Active); }
+        }
+    }
+
+    /// <summary>每案例最多保留一筆 request 及 content 供 Dispose 斷言，不開 socket 或背景工作。</summary>
+    private sealed class OwnershipHandler : HttpMessageHandler
+    {
+        private readonly string fault;
+        internal HttpRequestMessage? Request;
+        internal TrackedContent? Content;
+        /// <summary>指定固定故障；測試結束時由外部 HttpClient 及 using 持有並處置 handler。</summary>
+        internal OwnershipHandler(string fault) => this.fault = fault;
+        /// <summary>不解碼或記錄 credential；回應只含合成欄位，傳輸故障在交回 response 前注入。</summary>
+        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
+        {
+            cancellationToken.ThrowIfCancellationRequested();
+            Request = request;
+            if (fault == "transport") throw new HttpRequestException("synthetic transport fault");
+            Content = new TrackedContent("{\"richMenuId\":\"synthetic\",\"access_token\":\"synthetic\",\"message\":\"sensitive-synthetic-detail\"}");
+            return Task.FromResult(new HttpResponseMessage(fault == "http" ? HttpStatusCode.BadRequest : fault == "unauthorized" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) { Content = Content });
+        }
+    }
+
+    /// <summary>可觀察確定性釋放的合成 HttpContent；不保存 request、credential 或外部串流。</summary>
+    private sealed class TrackedContent : HttpContent
+    {
+        private readonly byte[] bytes;
+        internal bool Disposed { get; private set; }
+        /// <summary>建立固定 UTF-8 回應，容量只限案例提供的短合成 JSON。</summary>
+        internal TrackedContent(string text) => bytes = Encoding.UTF8.GetBytes(text);
+        /// <summary>複製到 transport 借入的目的串流，不持有或 Dispose 該目的地。</summary>
+        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes, 0, bytes.Length);
+        /// <summary>固定內容可確定長度，避免任何無界緩衝或等待。</summary>
+        protected override bool TryComputeLength(out long length) { length = bytes.Length; return true; }
+        /// <summary>先記錄釋放再交基底處置緩衝，供成功／失敗／stream owner 的一致斷言。</summary>
+        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
+    }
+}
diff --git a/Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs b/Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs
index 8dd4f0a9c..2092acb3e 100644
--- a/Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs
+++ b/Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs
@@ -15,6 +15,7 @@ using FluentAssertions;
 using Line.Messaging;
 using Newtonsoft.Json.Linq;
 using System.Net;
+using System.Net.Http;
 using System.Text;
 using Xunit;
 
@@ -336,8 +337,9 @@ public sealed class LineMessagingClientP0EndpointTests
 
         protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
         {
+            cancellationToken.ThrowIfCancellationRequested();
             Requests.Add(request);
-            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
+            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync());
 
             return new HttpResponseMessage(_statusCode)
             {
diff --git a/Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs b/Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs
index f08e37d40..7e91cc2ef 100644
--- a/Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs
+++ b/Line.Messaging.Tests/LineMessagingClientP1RetryKeyTests.cs
@@ -15,6 +15,7 @@ using FluentAssertions;
 using Line.Messaging;
 using Newtonsoft.Json.Linq;
 using System.Net;
+using System.Net.Http;
 using System.Text;
 using Xunit;
 
@@ -141,8 +142,9 @@ public sealed class LineMessagingClientP1RetryKeyTests
 
         protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
         {
+            cancellationToken.ThrowIfCancellationRequested();
             Requests.Add(request);
-            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
+            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync());
             return new HttpResponseMessage(_statusCode)
             {
                 Content = new StringContent(_responseBody, Encoding.UTF8, _mediaType)
diff --git a/Line.Messaging.Tests/LineSdkFoundationTests.cs b/Line.Messaging.Tests/LineSdkFoundationTests.cs
new file mode 100644
index 000000000..c86e90f4b
--- /dev/null
+++ b/Line.Messaging.Tests/LineSdkFoundationTests.cs
@@ -0,0 +1,147 @@
+using System;
+using System.Linq;
+using System.Runtime.Versioning;
+using System.Text;
+using System.Threading.Tasks;
+using Line.Messaging.Webhooks;
+using Newtonsoft.Json;
+using Newtonsoft.Json.Linq;
+using Xunit;
+
+namespace Line.Messaging.Tests;
+
+/// <summary>
+/// 以離線官方形狀樣本與 RFC HMAC 向量保護 P1-03；樣本全為合成值，不連 LINE、不含實際收件者。
+/// 相同案例在 net48 宿主（載入 net462）及 net10 上執行，防止僅編譯成功卻遺漏 Framework 行為。
+/// </summary>
+public sealed class LineSdkFoundationTests
+{
+    /// <summary>檢查實際載入 DLL 的框架屬性；不能把 net48 測試宿主誤算成產品 net48。</summary>
+    [Fact]
+    public void Assembly_TargetFramework_MatchesProduct()
+    {
+        var framework = typeof(LineMessagingClient).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false).Cast<TargetFrameworkAttribute>().Single();
+#if NETFRAMEWORK
+        Assert.Equal(".NETFramework,Version=v4.6.2", framework.FrameworkName);
+#else
+        Assert.Equal(".NETCoreApp,Version=v10.0", framework.FrameworkName);
+#endif
+    }
+
+    /// <summary>建構時拒絕空或超過二十 UTF-16 單元的名稱；不可等遠端 API 才發現錯誤。</summary>
+    /// <param name="length">名稱長度的邊界反例。</param>
+    [Theory]
+    [InlineData(0)]
+    [InlineData(21)]
+    public void Sender_InvalidNameLength_Throws(int length) => Assert.Throws<ArgumentException>(() => new Sender(new string('a', length), "https://example.invalid/icon.png"));
+
+    /// <summary>圖示網址只接受絕對 HTTPS；http、相對路徑與其他 scheme 都不可透過。</summary>
+    /// <param name="url">刻意不合格的圖示 URI。</param>
+    [Theory]
+    [InlineData("http://example.invalid/icon.png")]
+    [InlineData("/icon.png")]
+    [InlineData("file:///icon.png")]
+    [InlineData("")]
+    public void Sender_NonHttpsIcon_Throws(string url) => Assert.Throws<ArgumentException>(() => new Sender("合成教會", url));
+
+    /// <summary>一及二十字邊界合法，透過真實 SDK 傳輸序列化後 sender 位於訊息內；不更改未設定 sender 的黃金檔。</summary>
+    /// <param name="length">合法名稱的兩個邊界。</param><returns>已排空的離線捕獲作業。</returns>
+    [Theory]
+    [InlineData(1)]
+    [InlineData(20)]
+    public async Task Sender_Serializes_NameAndIconUrl(int length)
+    {
+        using var handler = new LineSdkGoldenTests.GoldenHandler();
+        using var transport = new System.Net.Http.HttpClient(handler);
+        using var client = new LineMessagingClient(transport, "synthetic-token");
+        var sender = new Sender(new string('a', length), "https://example.invalid/icon.png");
+        await client.PushMessageAsync("synthetic-recipient", new[] { new TextMessage("合成") { Sender = sender } });
+        var json = JObject.Parse(handler.Body)["messages"]![0]!["sender"]!;
+        Assert.Equal(sender.Name, (string?)json["name"]);
+        Assert.Equal(sender.IconUrl, (string?)json["iconUrl"]);
+    }
+
+    /// <summary>漸層完整欄位透過 Flex Box 真正進入請求；沒有背景的既有 Flex 黃金字串保持不變。</summary>
+    /// <returns>已釋放 transport 及 client 的捕獲作業。</returns>
+    [Fact]
+    public async Task Background_LinearGradient_Serializes()
+    {
+        using var handler = new LineSdkGoldenTests.GoldenHandler();
+        using var transport = new System.Net.Http.HttpClient(handler);
+        using var client = new LineMessagingClient(transport, "synthetic-token");
+        var background = new Background { Angle = "90deg", StartColor = "#FF0000", CenterColor = "#00FF00", EndColor = "#0000FF", CenterPosition = "25%" };
+        var box = new BoxComponent(BoxLayout.Vertical) { Background = background };
+        await client.PushMessageAsync("synthetic-recipient", new[] { new FlexMessage("合成") { Contents = new BubbleContainer { Body = box } } });
+        var json = JObject.Parse(handler.Body)["messages"]![0]!["contents"]!["body"]!["background"]!;
+        Assert.Equal("{\"type\":\"linearGradient\",\"angle\":\"90deg\",\"startColor\":\"#FF0000\",\"centerColor\":\"#00FF00\",\"endColor\":\"#0000FF\",\"centerPosition\":\"25%\"}", json.ToString(Formatting.None));
+    }
+
+    /// <summary>LINE 官方事件外形的新增欄位必須由共同解析入口讀取，舊事件缺欄位時維持安全預設。</summary>
+    /// <param name="type">可解析的事件類別。</param><param name="extra">該事件既有必填欄位。</param>
+    [Theory]
+    [InlineData("message", "\"message\":{\"id\":\"synthetic-message\",\"type\":\"text\",\"text\":\"hello\"},")]
+    [InlineData("follow", "")]
+    [InlineData("unfollow", "")]
+    [InlineData("join", "")]
+    [InlineData("leave", "")]
+    [InlineData("postback", "\"postback\":{\"data\":\"OPEN\"},")]
+    public void WebhookEvent_Deserialize_ReadsWebhookEventIdAndRedelivery(string type, string extra)
+    {
+        var prefix = "{\"events\":[{\"type\":\"" + type + "\",\"source\":{\"type\":\"user\",\"userId\":\"synthetic-user\"},\"timestamp\":1462629479859,\"replyToken\":\"synthetic-reply\"," + extra;
+        var current = Assert.Single(WebhookEventParser.Parse(prefix + "\"webhookEventId\":\"01SYNTHETICEVENT\",\"deliveryContext\":{\"isRedelivery\":true}}]}"));
+        Assert.Equal("01SYNTHETICEVENT", current.WebhookEventId);
+        Assert.True(current.DeliveryContext.IsRedelivery);
+        var legacy = Assert.Single(WebhookEventParser.Parse(prefix.TrimEnd(',') + "}]}"));
+        Assert.Null(legacy.WebhookEventId);
+        Assert.False(legacy.DeliveryContext.IsRedelivery);
+    }
+
+    /// <summary>RFC 4231 case 2 的固定結果，不使用產品演算法自行產生 expected；修改任意 digest 位元組均須拒絕。</summary>
+    [Fact]
+    public void Signature_ValidAndEveryTamperedByte_AreVerified()
+    {
+        var body = Encoding.UTF8.GetBytes("what do ya want for nothing?");
+        var secret = "Jefe";
+        const string expected = "W9zBRr9gdU5qBCQmCJV1x1oAPwidJzmDnexYuWTsOEM=";
+        Assert.True(LineSignature.Verify(body, expected, secret));
+        var digest = Convert.FromBase64String(expected);
+        for (var index = 0; index < digest.Length; index++)
+        {
+            var changed = (byte[])digest.Clone();
+            changed[index] ^= 1;
+            Assert.False(LineSignature.Verify(body, Convert.ToBase64String(changed), secret));
+        }
+        Assert.False(LineSignature.Verify(Encoding.UTF8.GetBytes("what do ya want for nothing?\n"), expected, secret));
+        Assert.Equal("what do ya want for nothing?", Encoding.UTF8.GetString(body));
+    }
+
+    /// <summary>非法 Base64、非 32-byte digest、缺 header 都回 false，不拋出攻擊者可控格式例外。</summary>
+    /// <param name="signature">不合格的 header。</param>
+    [Theory]
+    [InlineData(null)]
+    [InlineData("")]
+    [InlineData("not-base64")]
+    [InlineData("YQ==")]
+    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
+    public void Signature_InvalidHeader_ReturnsFalse(string? signature) => Assert.False(LineSignature.Verify(new byte[] { 1 }, signature!, "synthetic-secret"));
+
+    /// <summary>空 secret 是主機設定錯誤，必須明確失敗而不是接受以空密鑰計算的簽章。</summary>
+    /// <param name="secret">空或只有空白的設定。</param>
+    [Theory]
+    [InlineData(null)]
+    [InlineData("")]
+    [InlineData(" ")]
+    public void Signature_EmptySecret_Throws(string? secret) => Assert.Throws<ArgumentException>(() => LineSignature.Verify(Array.Empty<byte>(), "", secret!));
+
+    /// <summary>兩個 channel 交錯一千次驗證不能共用 HMAC 或密鑰；所有有限作業以 WhenAll 排空，不留背景工作。</summary>
+    /// <returns>已等待全部一千次驗證的工作。</returns>
+    [Fact]
+    public async Task Signature_TwoChannelsParallel_NoSharedState()
+    {
+        var body = Encoding.UTF8.GetBytes("what do ya want for nothing?");
+        var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() => Assert.Equal(index % 2 == 0,
+            LineSignature.Verify(body, "W9zBRr9gdU5qBCQmCJV1x1oAPwidJzmDnexYuWTsOEM=", index % 2 == 0 ? "Jefe" : "different-channel")))).ToArray();
+        await Task.WhenAll(tasks);
+        Assert.All(tasks, task => Assert.True(task.IsCompleted));
+    }
+}
diff --git a/Line.Messaging.Tests/LineSdkGoldenTests.cs b/Line.Messaging.Tests/LineSdkGoldenTests.cs
new file mode 100644
index 000000000..6db2c2546
--- /dev/null
+++ b/Line.Messaging.Tests/LineSdkGoldenTests.cs
@@ -0,0 +1,104 @@
+using System.Net;
+using System.Net.Http;
+using System.Reflection;
+using System.Text;
+using Xunit;
+
+namespace Line.Messaging.Tests;
+
+/// <summary>
+/// 以舊版 net10 SDK 實際 HTTP 請求鎖住六類 JSON；樣本只含合成文字及 example.invalid。
+/// 比對包含欄位順序與逸出字元的完整字串，不能以解析後相等掩蓋相容性變更。
+/// </summary>
+public sealed class LineSdkGoldenTests
+{
+    /// <summary>逐類送入記憶體 handler；不連網，並將 SDK 原始請求與固定 golden 逐字比較。</summary>
+    /// <param name="kind">六類既有訊息或 RichMenu 建立請求。</param>
+    /// <returns>完成單次捕獲並釋放客戶端的工作。</returns>
+    [Theory]
+    [InlineData("Text")]
+    [InlineData("Flex")]
+    [InlineData("Template")]
+    [InlineData("Image")]
+    [InlineData("QuickReply")]
+    [InlineData("RichMenu")]
+    public async Task Serialize_AllMessageTypes_MatchGolden(string kind)
+    {
+        using var handler = new GoldenHandler();
+        using var transport = new HttpClient(handler);
+        using var client = new LineMessagingClient(transport, "synthetic-token");
+        if (kind == "RichMenu")
+        {
+            await client.CreateRichMenuAsync(new RichMenu
+            {
+                Size = new ImagemapSize(2500, 843),
+                Selected = true,
+                Name = "合成選單",
+                ChatBarText = "開啟",
+                Areas = new List<ActionArea>
+                {
+                    new() { Bounds = new ImagemapArea(0, 0, 1250, 843), Action = new MessageTemplateAction("合成操作", "OPEN") }
+                }
+            });
+        }
+        else
+        {
+            await client.PushMessageAsync("synthetic-recipient", new List<ISendMessage> { CreateMessage(kind) });
+        }
+
+        using var stream = typeof(LineSdkGoldenTests).Assembly.GetManifestResourceStream("Line.Messaging.Tests.Golden." + kind + ".json")
+            ?? throw new InvalidOperationException("缺少固定黃金資源。");
+        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
+        // fixture 以 CRLF 結尾符合倉庫文件格式；只移除這一個檔尾封套，不 Trim 真正請求內的任何字元。
+        var expected = await reader.ReadToEndAsync();
+        Assert.EndsWith("\r\n", expected);
+        Assert.Equal(expected.Substring(0, expected.Length - 2), handler.Body);
+    }
+
+    /// <summary>建立每次操作獨享的模型，不共用可變清單或任何收件者狀態。</summary>
+    /// <param name="kind">合成訊息類別。</param><returns>只屬於此次測試的訊息圖。</returns>
+    internal static ISendMessage CreateMessage(string kind) => kind switch
+    {
+        "Text" => new TextMessage("合成文字\n\"quote\" \\ 😀"),
+        "Flex" => new FlexMessage("合成卡片")
+        {
+            Contents = new BubbleContainer
+            {
+                Body = new BoxComponent(BoxLayout.Vertical)
+                {
+                    Contents = new List<IFlexComponent> { new TextComponent("合成標題") { Weight = Weight.Bold, Wrap = true } }
+                }
+            }
+        },
+        "Template" => new TemplateMessage("合成確認", new ConfirmTemplate("確認？", new List<ITemplateAction>
+        {
+            new MessageTemplateAction("是", "YES"), new MessageTemplateAction("否", "NO")
+        })),
+        "Image" => new ImageMessage("https://example.invalid/full.png", "https://example.invalid/preview.png"),
+        "QuickReply" => new TextMessage("合成選項", new QuickReply(new List<QuickReplyButtonObject>
+        {
+            new(new MessageTemplateAction("選擇", "SELECT"), "https://example.invalid/icon.png")
+        })),
+        _ => throw new ArgumentException("未知合成訊息類別。", nameof(kind))
+    };
+
+    /// <summary>只捕獲單筆請求文字；由測試 Dispose，無 socket、queue、timer 或背景工作。</summary>
+    internal sealed class GoldenHandler : HttpMessageHandler
+    {
+        /// <summary>本次請求的原始序列化文字；只保留最後一筆，測試結束即可回收。</summary>
+        internal string Body { get; private set; } = string.Empty;
+
+        /// <summary>同步複製請求文字後回傳合成成功；回應所有權交還 SDK／HttpClient。</summary>
+        /// <param name="request">由 SDK 建立的借用請求，不保留物件。</param>
+        /// <param name="cancellationToken">HttpClient 的取消界線。</param><returns>合成回應。</returns>
+        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
+        {
+            cancellationToken.ThrowIfCancellationRequested();
+            Body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
+            return new HttpResponseMessage(HttpStatusCode.OK)
+            {
+                Content = new StringContent("{\"richMenuId\":\"synthetic-menu\"}", Encoding.UTF8, "application/json")
+            };
+        }
+    }
+}
diff --git a/Line.Messaging/BorrowedStreamContent.cs b/Line.Messaging/BorrowedStreamContent.cs
new file mode 100644
index 000000000..13e0966de
--- /dev/null
+++ b/Line.Messaging/BorrowedStreamContent.cs
@@ -0,0 +1,50 @@
+using System;
+using System.IO;
+using System.Net;
+using System.Net.Http;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace Line.Messaging;
+
+/// <summary>
+/// 上傳期間借用呼叫者串流的 HttpContent；不同於 StreamContent，不取得輸入串流所有權。
+/// 每次上傳建立一個，方法完成／故障即 Dispose 並清參照；不緩衝整個檔案，不建立背景工作。
+/// 呼叫者必須等上傳完成再修改或 Dispose 輸入，內容不可跨上傳重用。
+/// </summary>
+internal sealed class BorrowedStreamContent : HttpContent
+{
+    private Stream _stream;
+    /// <summary>只借入可讀串流，傳輸目的地由 HttpClient 擁有，兩者均不由本類別關閉。</summary>
+    /// <param name="stream">呼叫端維持開啟到傳輸完成的輸入。</param>
+    internal BorrowedStreamContent(Stream stream) => _stream = stream ?? throw new ArgumentNullException(nameof(stream));
+
+    /// <summary>直接以有界複製緩衝傳輸；等待完整複製，沒有 Task.Run 或遺棄後續工作。</summary>
+    /// <param name="stream">transport 擁有的目的地。</param><param name="context">底層傳輸內容，不保存。</param><returns>完整複製作業。</returns>
+    protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => (_stream ?? throw new ObjectDisposedException(nameof(BorrowedStreamContent))).CopyToAsync(stream);
+
+#if !NETFRAMEWORK
+    /// <summary>net10 transport 的取消 token 傳到每次讀寫；不以取消後遺棄 CopyToAsync 代替確定性排空。</summary>
+    /// <param name="stream">transport 目的地。</param><param name="context">不保存的傳輸內容。</param>
+    /// <param name="cancellationToken">由 HttpClient timeout／呼叫取消導出的傳輸界線。</param><returns>實際複製結束的工作。</returns>
+    protected override Task SerializeToStreamAsync(Stream stream, TransportContext context, CancellationToken cancellationToken) =>
+        (_stream ?? throw new ObjectDisposedException(nameof(BorrowedStreamContent))).CopyToAsync(stream, 81920, cancellationToken);
+#endif
+
+    /// <summary>可 seek 時提供剩餘長度，否則交由 transport 串流傳送，不為計算長度讀完整內容。</summary>
+    /// <param name="length">已知剩餘位元組數或零。</param><returns>是否能不消耗輸入取得長度。</returns>
+    protected override bool TryComputeLength(out long length)
+    {
+        var source = _stream ?? throw new ObjectDisposedException(nameof(BorrowedStreamContent));
+        length = source.CanSeek ? source.Length - source.Position : 0;
+        return source.CanSeek;
+    }
+
+    /// <summary>清掉借用參照並釋放 HttpContent 自有緩衝；不關閉輸入串流，由原呼叫者 using 負責。</summary>
+    /// <param name="disposing">是否為明確處置。</param>
+    protected override void Dispose(bool disposing)
+    {
+        if (disposing) _stream = null;
+        base.Dispose(disposing);
+    }
+}
diff --git a/Line.Messaging/ContentStream.cs b/Line.Messaging/ContentStream.cs
index 4039f6a12..a7ca08cde 100644
--- a/Line.Messaging/ContentStream.cs
+++ b/Line.Messaging/ContentStream.cs
@@ -51,7 +51,14 @@ namespace Line.Messaging
         /// 取得 HTTP Content 的標頭資訊（如 Content-Type、Content-Length 等）
         /// Gets the HTTP content headers (e.g., Content-Type, Content-Length)
         /// </summary>
-        public HttpContentHeaders ContentHeaders { get; }
+        public HttpContentHeaders ContentHeaders => _contentHeaders ?? throw new ObjectDisposedException(nameof(ContentStream));
+
+        /// <summary>最多持有到 Dispose 的內容標頭；釋放後清掉，避免標頭容器間接保留 HttpContent。</summary>
+        private HttpContentHeaders _contentHeaders;
+        /// <summary>由下載 helper 轉交的唯一 response owner；舊公開建構式則只 owns baseStream。</summary>
+        private IDisposable _responseOwner;
+        /// <summary>僅保護一次性所有權轉交，不序列化串流讀寫；Stream 本身不支援並行讀寫。</summary>
+        private readonly object _disposeGate = new object();
 
         /// <summary>
         /// 初始化 ContentStream 的新執行個體
@@ -61,8 +68,16 @@ namespace Line.Messaging
         /// <param name="contentHeaders">HTTP Content 標頭 / HTTP content headers</param>
         public ContentStream(Stream baseStream, HttpContentHeaders contentHeaders)
         {
-            _baseStream = baseStream;
-            ContentHeaders = contentHeaders;
+            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
+            _contentHeaders = contentHeaders;
+        }
+
+        /// <summary>由 SDK 內部一次性交接 response 所有權；外部仍可沿用原雙參數建構式。</summary>
+        /// <param name="baseStream">response 內容串流，不獨立重複 Dispose。</param>
+        /// <param name="contentHeaders">只在串流存活期間有效的內容標頭。</param><param name="responseOwner">唯一 response owner，會連同 content 與串流一起釋放。</param>
+        internal ContentStream(Stream baseStream, HttpContentHeaders contentHeaders, IDisposable responseOwner) : this(baseStream, contentHeaders)
+        {
+            _responseOwner = responseOwner ?? throw new ArgumentNullException(nameof(responseOwner));
         }
 
         /// <summary>
@@ -154,12 +169,27 @@ namespace Line.Messaging
         /// <param name="disposing">true 表示釋放 Managed 和 Unmanaged 資源；false 則只釋放 Unmanaged 資源</param>
         protected override void Dispose(bool disposing)
         {
-            base.Dispose(disposing);
-            if (disposing)
+            try
             {
-                _baseStream?.Dispose();
-                _baseStream = null;
+                if (disposing)
+                {
+                    Stream stream;
+                    IDisposable owner;
+                    lock (_disposeGate)
+                    {
+                        stream = _baseStream;
+                        owner = _responseOwner;
+                        _baseStream = null;
+                        _responseOwner = null;
+                        _contentHeaders = null;
+                    }
+                    // Response owns 它的 content 與串流；無 response 的舊建構式才獨立釋放 stream。
+                    // 先清參照再釋放，重複或重入 Dispose 不會留下 owner，也不雙重處置借來的目的地。
+                    if (owner != null) owner.Dispose();
+                    else stream?.Dispose();
+                }
             }
+            finally { base.Dispose(disposing); }
         }
     }
 }
diff --git a/Line.Messaging/Line.Messaging.csproj b/Line.Messaging/Line.Messaging.csproj
index ffeee03a8..2f9a169bb 100644
--- a/Line.Messaging/Line.Messaging.csproj
+++ b/Line.Messaging/Line.Messaging.csproj
@@ -1,7 +1,7 @@
 <Project Sdk="Microsoft.NET.Sdk">
 
   <PropertyGroup>
-    <TargetFramework>net10.0</TargetFramework>
+    <TargetFrameworks>net462;net10.0</TargetFrameworks>
     <Authors>pierre3, kenamanu</Authors>
     <Company></Company>
     <Product>Line Messaging API</Product>
@@ -43,7 +43,7 @@ https://developers.line.biz/en/news/2019/01/
   </PropertyGroup>
 
   <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|AnyCPU'">
-    <DocumentationFile>bin\Release\net10.0\Line.Messaging.xml</DocumentationFile>
+    <GenerateDocumentationFile>true</GenerateDocumentationFile>
   </PropertyGroup>
 
   <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|AnyCPU'">
@@ -53,4 +53,9 @@ https://developers.line.biz/en/news/2019/01/
     <PackageReference Include="Newtonsoft.Json" />
   </ItemGroup>
 
+  <ItemGroup Condition="'$(TargetFramework)' == 'net462'">
+    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
+    <Reference Include="System.Net.Http" />
+    <Reference Include="Microsoft.CSharp" />
+  </ItemGroup>
 </Project>
diff --git a/Line.Messaging/Line.Messaging_Net10.csproj b/Line.Messaging/Line.Messaging_Net10.csproj
deleted file mode 100644
index ffeee03a8..000000000
--- a/Line.Messaging/Line.Messaging_Net10.csproj
+++ /dev/null
@@ -1,56 +0,0 @@
-<Project Sdk="Microsoft.NET.Sdk">
-
-  <PropertyGroup>
-    <TargetFramework>net10.0</TargetFramework>
-    <Authors>pierre3, kenamanu</Authors>
-    <Company></Company>
-    <Product>Line Messaging API</Product>
-    <Description>SDK for the LINE Messaging API for C# - Upgraded to .NET 10</Description>
-    <Version>1.4.6</Version>
-    <Copyright>? 2018 pierre3, ? 2025 Upgraded to .NET 10</Copyright>
-    <PackageLicenseUrl>https://github.com/pierre3/LineMessagingApi/blob/master/LICENSE</PackageLicenseUrl>
-    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
-    <PackageTags>Line Messaging API;line;bot;.NET 10</PackageTags>
-    <PackageProjectUrl>https://github.com/pierre3/LineMessagingApi/</PackageProjectUrl>
-    <IncludeSymbols>true</IncludeSymbols>
-    <RepositoryUrl>https://github.com/pierre3/LineMessagingApi/</RepositoryUrl>
-    <RepositoryType>git</RepositoryType>
-    <PackageReleaseNotes>
-Upgraded to .NET 10
-- Target Framework: .NET 10.0
-- Updated language version to latest
-- Enabled nullable reference types
-- Updated Newtonsoft.Json to 13.0.3
-- Improved async/await patterns
-- Enhanced performance and memory management
-- Compatible with modern .NET applications
-
-Original release notes (1.4.5):
-1. Supports the API update at 1/17/2019
-https://developers.line.biz/en/news/2019/01/
-- New endpoints added to Messaging API
-
-2. We reviewed the implementation of the LineMessagingClient class.
-- Added interface ILineMessagingClient.
-- Changed methods of LineMessagingClient class to virtual method.
-    </PackageReleaseNotes>
-    <AssemblyVersion>1.4.6.0</AssemblyVersion>
-    <FileVersion>1.4.6.0</FileVersion>
-    <LangVersion>latest</LangVersion>
-    <Nullable>enable</Nullable>
-    <ImplicitUsings>disable</ImplicitUsings>
-    <NoWarn>$(NoWarn);1701;1702;1705;1591;CS0618;CS1570;CS1573;CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625;CS8765;SYSLIB0013</NoWarn>
-  </PropertyGroup>
-
-  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|AnyCPU'">
-    <DocumentationFile>bin\Release\net10.0\Line.Messaging.xml</DocumentationFile>
-  </PropertyGroup>
-
-  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|AnyCPU'">
-  </PropertyGroup>
-
-  <ItemGroup>
-    <PackageReference Include="Newtonsoft.Json" />
-  </ItemGroup>
-
-</Project>
diff --git a/Line.Messaging/LineMessagingClient.cs b/Line.Messaging/LineMessagingClient.cs
index f3fbb2fbf..3c2f36c5b 100644
--- a/Line.Messaging/LineMessagingClient.cs
+++ b/Line.Messaging/LineMessagingClient.cs
@@ -20,6 +20,7 @@ using System.Linq;
 using System.Net.Http;
 using System.Net.Http.Headers;
 using System.Text;
+using System.Threading;
 using System.Threading.Tasks;
 using Newtonsoft.Json.Linq; // RichMenu list endpoint 回傳包在 richmenus 陣列中，需用 JObject/JArray 解析。
 
@@ -80,6 +81,9 @@ namespace Line.Messaging
         /// </summary>
         private readonly bool _disposeClient;
 
+        /// <summary>此 SDK 實例唯一的 channel credential；僅附加到單次 request，Dispose 以原子交換清除參照。</summary>
+        private AuthenticationHeaderValue _authorization;
+
         /// <summary>
         /// JSON 序列化設定（使用 Camel Case 命名）
         /// JSON serializer settings (using Camel Case naming)
@@ -108,7 +112,7 @@ namespace Line.Messaging
         {
             _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
             _disposeClient = false;
-            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", channelAccessToken);
+            _authorization = new AuthenticationHeaderValue("Bearer", channelAccessToken);
             _jsonSerializerSettings = new CamelCaseJsonSerializerSettings();
             _uri = NormalizeLineApiBaseUri(uri);
             _dataUri = DeriveDataUri(_uri);
@@ -125,7 +129,7 @@ namespace Line.Messaging
         {
             _client = new HttpClient();
             _disposeClient = true;
-            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", channelAccessToken);
+            _authorization = new AuthenticationHeaderValue("Bearer", channelAccessToken);
             _jsonSerializerSettings = new CamelCaseJsonSerializerSettings();
             _uri = NormalizeLineApiBaseUri(uri);
             _dataUri = DeriveDataUri(_uri);
@@ -247,15 +251,16 @@ namespace Line.Messaging
         /// }
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public static async Task<ChannelAccessToken> IssueChannelAccessTokenAsync(HttpClient httpClient, string channelId, string channelAccessToken, string uri = DEFAULT_URI)
         {
-            var response = await httpClient.PostAsync(CombineBaseAndPath(NormalizeLineApiBaseUri(uri), "/oauth/accessToken"),
-                new FormUrlEncodedContent(new Dictionary<string, string>
+            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                 {
                     ["grant_type"] = "client_credentials",
                     ["client_id"] = channelId,
                     ["client_secret"] = channelAccessToken
-                })).ConfigureAwait(false);
+                });
+            using var response = await httpClient.PostAsync(CombineBaseAndPath(NormalizeLineApiBaseUri(uri), "/oauth/accessToken"), content).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
             var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
             return JsonConvert.DeserializeObject<ChannelAccessToken>(json,
@@ -302,10 +307,11 @@ namespace Line.Messaging
         /// }
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public static async Task RevokeChannelAccessTokenAsync(HttpClient httpClient, string channelAccessToken, string uri = DEFAULT_URI)
         {
-            var response = await httpClient.PostAsync(CombineBaseAndPath(NormalizeLineApiBaseUri(uri), "/oauth/revoke"),
-                new FormUrlEncodedContent(new Dictionary<string, string> { ["access_token"] = channelAccessToken })).ConfigureAwait(false);
+            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["access_token"] = channelAccessToken });
+            using var response = await httpClient.PostAsync(CombineBaseAndPath(NormalizeLineApiBaseUri(uri), "/oauth/revoke"), content).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -429,11 +435,12 @@ namespace Line.Messaging
         /// </example>
         /// <seealso cref="PushMessageAsync(string, IList{ISendMessage})"/>
         /// <seealso cref="MultiCastMessageAsync(IList{string}, IList{ISendMessage})"/>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task ReplyMessageAsync(string replyToken, IList<ISendMessage> messages)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/reply");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/reply");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { replyToken, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -496,12 +503,13 @@ namespace Line.Messaging
         /// );
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task ReplyMessageWithJsonAsync(string replyToken, params string[] messages)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/reply");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/reply");
             var json = $"{{ \"replyToken\" : \"{replyToken}\", \"messages\" : [{string.Join(", ", messages)}] }}";
             request.Content = new StringContent(json, Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -556,12 +564,13 @@ namespace Line.Messaging
         public virtual Task PushMessageAsync(string to, IList<ISendMessage> messages)
             => PushMessageAsync(to, messages, retryKey: null);
 
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task PushMessageAsync(string to, IList<ISendMessage> messages, string? retryKey)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/push"));
+            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/push"));
             ApplyRetryKeyHeader(request, retryKey);
             request.Content = new StringContent(JsonConvert.SerializeObject(new { to, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -583,12 +592,13 @@ namespace Line.Messaging
         /// This method is suitable for advanced scenarios requiring full control of JSON format.
         /// </para>
         /// </remarks>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task PushMessageWithJsonAsync(string to, params string[] messages)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/push");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/push");
             var json = $"{{ \"to\" : \"{to}\", \"messages\" : [{string.Join(", ", messages)}] }}";
             request.Content = new StringContent(json, Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -654,12 +664,13 @@ namespace Line.Messaging
         public virtual Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages)
             => MultiCastMessageAsync(to, messages, retryKey: null);
 
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages, string? retryKey)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/multicast"));
+            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/multicast"));
             ApplyRetryKeyHeader(request, retryKey);
             request.Content = new StringContent(JsonConvert.SerializeObject(new { to, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -675,13 +686,14 @@ namespace Line.Messaging
         /// JSON 格式的訊息陣列
         /// Array of messages in JSON format
         /// </param>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task MultiCastMessageWithJsonAsync(IList<string> to, params string[] messages)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/multicast");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/multicast");
             var quotedIds = string.Join(", ", to.Select(x => $"\"{x}\""));
             var json = $"{{ \"to\" : [{quotedIds}], \"messages\" : [{string.Join(", ", messages)}] }}";
             request.Content = new StringContent(json, Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -732,12 +744,13 @@ namespace Line.Messaging
         public virtual Task BroadcastMessageAsync(IList<ISendMessage> messages)
             => BroadcastMessageAsync(messages, retryKey: null);
 
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task BroadcastMessageAsync(IList<ISendMessage> messages, string? retryKey)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/broadcast"));
+            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/message/broadcast"));
             ApplyRetryKeyHeader(request, retryKey);
             request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -793,11 +806,12 @@ namespace Line.Messaging
         /// var progress = await client.GetNarrowcastProgressAsync(requestId);
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task<string> NarrowcastMessageAsync(IList<ISendMessage> messages, object recipient = null, object filter = null, object limit = null)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/narrowcast");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/narrowcast");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { messages, recipient, filter, limit }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
             if (response.Headers.TryGetValues("X-Line-Request-Id", out var values)) return values.FirstOrDefault();
             return null;
@@ -837,9 +851,9 @@ namespace Line.Messaging
         /// 將來自使用者的訊息標記為已讀
         /// Marks messages from users as read
         /// </summary>
-        /// <param name="chatId">
-        /// 聊天識別碼 - 可以是 userId（一對一聊天）、groupId（群組）或 roomId（聊天室）
-        /// Chat identifier - Can be userId (one-on-one chat), groupId (group), or roomId (room)
+        /// <param name="markAsReadToken">
+        /// 已驗簽 webhook 內的 markAsReadToken，不接受 userId、groupId 或 roomId。
+        /// 此操作只持有 token 到本次傳輸完成；request 與 response 均在方法結束時釋放。
         /// </param>
         /// <remarks>
         /// 標記為已讀後，使用者端會顯示訊息已讀取。
@@ -854,20 +868,21 @@ namespace Line.Messaging
         /// <example>
         /// <code>
         /// // 標記一對一聊天為已讀
-        /// await client.MarkAsReadAsync(userId);
+        /// await client.MarkAsReadByTokenAsync(markAsReadToken);
         ///
         /// // 標記群組訊息為已讀
-        /// await client.MarkAsReadAsync(groupId);
+        /// await client.MarkAsReadByTokenAsync(markAsReadToken);
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task MarkAsReadByTokenAsync(string markAsReadToken)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/chat/markAsRead"));
+            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/chat/markAsRead"));
             request.Content = new StringContent(
                 JsonConvert.SerializeObject(new { markAsReadToken }, _jsonSerializerSettings),
                 Encoding.UTF8,
                 "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -909,11 +924,12 @@ namespace Line.Messaging
         /// await client.ShowLoadingAnimationAsync(userId, 30);
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task ShowLoadingAnimationAsync(string chatId, int loadingSeconds = 20)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/chat/loading/start");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/chat/loading/start");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { chatId, loadingSeconds }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -964,11 +980,10 @@ namespace Line.Messaging
         /// </code>
         /// </example>
         /// <seealso cref="GetContentBytesAsync(string)"/>
+        /// <remarks>成功時 response 唯一轉交回傳的 ContentStream；呼叫端必須 using，失敗時內部釋放。不將媒體或使用者來源存入共用快取。</remarks>
         public virtual async Task<ContentStream> GetContentStreamAsync(string messageId)
         {
-            var response = await _client.GetAsync(DataUrl($"/bot/message/{messageId}/content")).ConfigureAwait(false);
-            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
-            return new ContentStream(await response.Content.ReadAsStreamAsync(), response.Content.Headers);
+            return await GetOwnedStreamAsync(DataUrl($"/bot/message/{messageId}/content")).ConfigureAwait(false);
         }
 
         /// <summary>
@@ -997,9 +1012,10 @@ namespace Line.Messaging
         /// </code>
         /// </example>
         /// <seealso cref="GetContentStreamAsync(string)"/>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task<byte[]> GetContentBytesAsync(string messageId)
         {
-            var response = await _client.GetAsync(DataUrl($"/bot/message/{messageId}/content")).ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, DataUrl($"/bot/message/{messageId}/content"))).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
             return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
         }
@@ -1040,9 +1056,10 @@ namespace Line.Messaging
         /// var content = await client.GetContentStreamAsync(messageId);
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task<bool> VerifyContentPreparationAsync(string messageId)
         {
-            var response = await _client.GetAsync(DataUrl($"/bot/message/{messageId}/content/transcoding")).ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, DataUrl($"/bot/message/{messageId}/content/transcoding"))).ConfigureAwait(false);
             if (response.StatusCode == System.Net.HttpStatusCode.OK)
             {
                 var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
@@ -1104,11 +1121,10 @@ namespace Line.Messaging
         /// }
         /// </code>
         /// </example>
+        /// <remarks>成功時 response 唯一轉交回傳的 ContentStream；呼叫端必須 using，失敗時內部釋放。不將媒體或使用者來源存入共用快取。</remarks>
         public virtual async Task<ContentStream> GetContentPreviewAsync(string messageId)
         {
-            var response = await _client.GetAsync(DataUrl($"/bot/message/{messageId}/content/preview")).ConfigureAwait(false);
-            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
-            return new ContentStream(await response.Content.ReadAsStreamAsync(), response.Content.Headers);
+            return await GetOwnedStreamAsync(DataUrl($"/bot/message/{messageId}/content/preview")).ConfigureAwait(false);
         }
         #endregion
 
@@ -1436,9 +1452,10 @@ namespace Line.Messaging
         /// Console.WriteLine("機器人已離開群組");
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task LeaveFromGroupAsync(string groupId)
         {
-            var response = await _client.PostAsync($"{_uri}/bot/group/{groupId}/leave", null).ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/group/{groupId}/leave")).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
         #endregion
@@ -1621,9 +1638,10 @@ namespace Line.Messaging
         /// Console.WriteLine("機器人已離開聊天室");
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task LeaveFromRoomAsync(string roomId)
         {
-            var response = await _client.PostAsync($"{_uri}/bot/room/{roomId}/leave", null).ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/room/{roomId}/leave")).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
         #endregion
@@ -1661,11 +1679,12 @@ namespace Line.Messaging
         /// Console.WriteLine("Webhook 端點已設定");
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task SetWebhookEndpointAsync(string endpoint)
         {
-            var request = new HttpRequestMessage(HttpMethod.Put, $"{_uri}/bot/channel/webhook/endpoint");
+            using var request = new HttpRequestMessage(HttpMethod.Put, $"{_uri}/bot/channel/webhook/endpoint");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { endpoint }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -1738,12 +1757,13 @@ namespace Line.Messaging
         /// var result2 = await client.TestWebhookEndpointAsync("https://example.com/webhook");
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task<WebhookTestResult> TestWebhookEndpointAsync(string endpoint = null)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/channel/webhook/test");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/channel/webhook/test");
             string body = endpoint == null ? "{}" : JsonConvert.SerializeObject(new { endpoint }, _jsonSerializerSettings);
             request.Content = new StringContent(body, Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
             var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
             return JsonConvert.DeserializeObject<WebhookTestResult>(json);
@@ -1816,11 +1836,12 @@ namespace Line.Messaging
         /// var richMenuId = await client.CreateRichMenuAsync(richMenu);
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task<string> CreateRichMenuAsync(RichMenu richMenu)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu");
             request.Content = new StringContent(JsonConvert.SerializeObject(richMenu, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
             var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
             return JsonConvert.DeserializeAnonymousType(json, new { richMenuId = "" }).richMenuId;
@@ -1853,11 +1874,12 @@ namespace Line.Messaging
         /// }
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task ValidateRichMenuAsync(RichMenu richMenu)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/validate");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/validate");
             request.Content = new StringContent(JsonConvert.SerializeObject(richMenu, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -1880,9 +1902,10 @@ namespace Line.Messaging
         /// Console.WriteLine("Rich Menu 已刪除");
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task DeleteRichMenuAsync(string richMenuId)
         {
-            var response = await _client.DeleteAsync($"{_uri}/bot/richmenu/{richMenuId}").ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{_uri}/bot/richmenu/{richMenuId}")).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -1908,11 +1931,12 @@ namespace Line.Messaging
         /// Console.WriteLine("Rich Menu 別名已建立");
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/alias");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/alias");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { richMenuId, richMenuAliasId }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -1933,9 +1957,10 @@ namespace Line.Messaging
         /// Console.WriteLine("Rich Menu 別名已刪除");
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task DeleteRichMenuAliasAsync(string richMenuAliasId)
         {
-            var response = await _client.DeleteAsync($"{_uri}/bot/richmenu/alias/{richMenuAliasId}").ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{_uri}/bot/richmenu/alias/{richMenuAliasId}")).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -1959,11 +1984,12 @@ namespace Line.Messaging
         /// Console.WriteLine("Rich Menu 別名已更新");
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/alias/{richMenuAliasId}");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/alias/{richMenuAliasId}");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { richMenuId }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2016,9 +2042,10 @@ namespace Line.Messaging
         /// 設定官方帳號層級的預設 RichMenu。
         /// 這會影響沒有個人 RichMenu 綁定的使用者。
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task SetDefaultRichMenuAsync(string richMenuId)
         {
-            var response = await _client.PostAsync($"{_uri}/bot/user/all/richmenu/{richMenuId}", null).ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/user/all/richmenu/{richMenuId}")).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2034,9 +2061,10 @@ namespace Line.Messaging
         /// <summary>
         /// 取消官方帳號目前設定的預設 RichMenu。
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task CancelDefaultRichMenuAsync()
         {
-            var response = await _client.DeleteAsync($"{_uri}/bot/user/all/richmenu").ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{_uri}/bot/user/all/richmenu")).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2044,40 +2072,44 @@ namespace Line.Messaging
         /// 將 RichMenu 直接連結到單一使用者。
         /// LINE 同一時間只允許一位使用者有一個直接綁定的 RichMenu。
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task LinkRichMenuToUserAsync(string userId, string richMenuId)
         {
-            var response = await _client.PostAsync($"{_uri}/bot/user/{userId}/richmenu/{richMenuId}", null).ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/user/{userId}/richmenu/{richMenuId}")).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
         /// <summary>
         /// 將同一個 RichMenu 批次連結到多位使用者。
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task LinkRichMenuToUsersAsync(string richMenuId, IList<string> userIds)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/bulk/link");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/bulk/link");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { richMenuId, userIds }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
         /// <summary>
         /// 解除單一使用者目前直接連結的 RichMenu。
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task UnLinkRichMenuFromUserAsync(string userId)
         {
-            var response = await _client.DeleteAsync($"{_uri}/bot/user/{userId}/richmenu").ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{_uri}/bot/user/{userId}/richmenu")).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
         /// <summary>
         /// 批次解除多位使用者目前直接連結的 RichMenu。
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task UnLinkRichMenuFromUsersAsync(IList<string> userIds)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/bulk/unlink");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/bulk/unlink");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { userIds }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2085,11 +2117,12 @@ namespace Line.Messaging
         /// 批次控制 RichMenu，支援 link、unlink 與 unlinkAll。
         /// LINE 會非同步處理此請求，呼叫端應使用 progress endpoint 追蹤結果。
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task RichMenuBatchOperationAsync(IList<RichMenuBatchOperation> operations)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/batch");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/batch");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { operations }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2105,11 +2138,12 @@ namespace Line.Messaging
         /// <summary>
         /// 驗證 RichMenu batch-control request 是否符合 LINE 官方格式。
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task ValidateRichMenuBatchRequestAsync(IList<RichMenuBatchOperation> operations)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/richmenu/validate/batch"));
+            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/richmenu/validate/batch"));
             request.Content = new StringContent(JsonConvert.SerializeObject(new { operations }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2117,22 +2151,22 @@ namespace Line.Messaging
         /// 下載指定 RichMenu 的圖片內容。
         /// 圖片端點必須走 api-data.line.me，因此這裡使用 DataUrl。
         /// </summary>
+        /// <remarks>成功時 response 唯一轉交回傳的 ContentStream；呼叫端必須 using，失敗時內部釋放。不將媒體或使用者來源存入共用快取。</remarks>
         public virtual async Task<ContentStream> DownloadRichMenuImageAsync(string richMenuId)
         {
-            var response = await _client.GetAsync(DataUrl($"/bot/richmenu/{richMenuId}/content")).ConfigureAwait(false);
-            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
-            return new ContentStream(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), response.Content.Headers);
+            return await GetOwnedStreamAsync(DataUrl($"/bot/richmenu/{richMenuId}/content")).ConfigureAwait(false);
         }
 
         /// <summary>
         /// 上傳 JPEG RichMenu 圖片。
         /// LINE 不允許替換既有圖片；更新圖片時需建立新的 RichMenu。
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task UploadRichMenuJpegImageAsync(Stream stream, string richMenuId)
         {
-            var content = new StreamContent(stream);
+            using var content = new BorrowedStreamContent(stream);
             content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
-            var response = await _client.PostAsync(DataUrl($"/bot/richmenu/{richMenuId}/content"), content).ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, DataUrl($"/bot/richmenu/{richMenuId}/content")) { Content = content }).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2140,11 +2174,12 @@ namespace Line.Messaging
         /// 上傳 PNG RichMenu 圖片。
         /// 共用 provisioning workflow 目前使用 PNG 圖稿時會走此端點。
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task UploadRichMenuPngImageAsync(Stream stream, string richMenuId)
         {
-            var content = new StreamContent(stream);
+            using var content = new BorrowedStreamContent(stream);
             content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
-            var response = await _client.PostAsync(DataUrl($"/bot/richmenu/{richMenuId}/content"), content).ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, DataUrl($"/bot/richmenu/{richMenuId}/content")) { Content = content }).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2283,9 +2318,10 @@ namespace Line.Messaging
         /// // 將 Link Token 傳送給使用者進行帳號連結
         /// </code>
         /// </example>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task<string> IssueLinkTokenAsync(string userId)
         {
-            var response = await _client.PostAsync($"{_uri}/bot/user/{userId}/linkToken", null).ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/user/{userId}/linkToken")).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
             var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
             return JsonConvert.DeserializeAnonymousType(json, new { linkToken = "" }).linkToken;
@@ -2479,11 +2515,12 @@ namespace Line.Messaging
         /// 建立優惠券
         /// Creates a coupon
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task<Coupon> CreateCouponAsync(CreateCouponRequest request)
         {
-            var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/coupon"));
+            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/coupon"));
             httpRequest.Content = new StringContent(JsonConvert.SerializeObject(request, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(httpRequest).ConfigureAwait(false);
+            using var response = await SendAsync(httpRequest).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
             var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
             return JsonConvert.DeserializeObject<Coupon>(json);
@@ -2493,10 +2530,11 @@ namespace Line.Messaging
         /// 停止優惠券
         /// Closes a coupon
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task CloseCouponAsync(string couponId)
         {
-            var httpRequest = new HttpRequestMessage(HttpMethod.Put, ApiUrl($"/bot/coupon/{couponId}/close"));
-            var response = await _client.SendAsync(httpRequest).ConfigureAwait(false);
+            using var httpRequest = new HttpRequestMessage(HttpMethod.Put, ApiUrl($"/bot/coupon/{couponId}/close"));
+            using var response = await SendAsync(httpRequest).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2594,11 +2632,12 @@ namespace Line.Messaging
         /// This method checks if the message format is correct but does not actually send messages.
         /// </para>
         /// </remarks>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task ValidateReplyMessageAsync(IList<ISendMessage> messages)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/reply");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/reply");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2610,11 +2649,12 @@ namespace Line.Messaging
         /// 要驗證的訊息清單
         /// List of messages to validate
         /// </param>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task ValidatePushMessageAsync(IList<ISendMessage> messages)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/push");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/push");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2626,11 +2666,12 @@ namespace Line.Messaging
         /// 要驗證的訊息清單
         /// List of messages to validate
         /// </param>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task ValidateMulticastMessageAsync(IList<ISendMessage> messages)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/multicast");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/multicast");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2642,11 +2683,12 @@ namespace Line.Messaging
         /// 要驗證的訊息清單
         /// List of messages to validate
         /// </param>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task ValidateNarrowcastMessageAsync(IList<ISendMessage> messages)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/narrowcast");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/narrowcast");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
 
@@ -2658,11 +2700,12 @@ namespace Line.Messaging
         /// 要驗證的訊息清單
         /// List of messages to validate
         /// </param>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         public virtual async Task ValidateBroadcastMessageAsync(IList<ISendMessage> messages)
         {
-            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/broadcast");
+            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/broadcast");
             request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
-            var response = await _client.SendAsync(request).ConfigureAwait(false);
+            using var response = await SendAsync(request).ConfigureAwait(false);
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
         }
         #endregion
@@ -2791,24 +2834,59 @@ namespace Line.Messaging
         #endregion
 
         #region Helper Methods
+        /// <summary>
+        /// 唯一的具 credential 傳輸入口；request 只屬單次操作，完成或故障立即釋放。
+        /// 不讀寫 HttpClient.DefaultRequestHeaders，避免共用 transport 的另一 channel 覆寫身分。
+        /// 借入的 HttpClient 由呼叫者設定有限 Timeout、無使用者 cookie 的 handler 並負責關機排空；
+        /// 此方法不建立額外 task、timer 或重試。已開始的傳輸依 HttpClient timeout 完成，不遺棄工作。
+        /// </summary>
+        /// <param name="request">所有權轉交此方法的單次 request；不可以再次傳送。</param>
+        /// <param name="completion">一般 API 讀完整內容；媒體下載只讀 headers 並交由 ContentStream 擁有 response。</param>
+        /// <returns>呼叫方法必須 using 或明確轉交所有權的 response。</returns>
+        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
+        {
+            using (request)
+            {
+                var authorization = Volatile.Read(ref _authorization);
+                if (authorization == null) throw new ObjectDisposedException(nameof(LineMessagingClient));
+                request.Headers.Authorization = authorization;
+                return await _client.SendAsync(request, completion).ConfigureAwait(false);
+            }
+        }
+
+        /// <summary>
+        /// 在串流建立成功前由此方法 owns response；失敗立即 Dispose 並重新拋原例外。
+        /// 成功後所有權唯一轉交 ContentStream，呼叫者必須 using，避免 response content／連線滯留。
+        /// 不緩衝整個媒體，內容讀取與取消由呼叫者的 Stream 操作管理，結束後 Dispose 歸還連線。
+        /// </summary>
+        /// <param name="url">已組裝的媒體端點，不記錄其使用者資料。</param><returns>擁有 response 的串流。</returns>
+        private async Task<ContentStream> GetOwnedStreamAsync(string url)
+        {
+            var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
+            try
+            {
+                await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
+                return new ContentStream(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), response.Content.Headers, response);
+            }
+            catch
+            {
+                response.Dispose();
+                throw;
+            }
+        }
+
         /// <summary>
         /// HTTP GET 輔助方法
         /// HTTP GET helper method
         /// </summary>
+        /// <remarks>單次操作 owns request／content 與完整讀取的 response；成功及例外皆由 using 釋放，不保存回應或跨 channel 狀態。故障交回呼叫端共用告警入口。</remarks>
         private async Task<string> GetStringAsync(string url)
         {
-            var response = await _client.GetAsync(url).ConfigureAwait(false);
+            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url)).ConfigureAwait(false);
 
-            // Enhanced error handling for authentication issues
+            // 不把遠端 body 或含使用者識別的 URL 附到例外；失敗仍由上層共用 notifier 記錄真實型別。
             if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
-            {
-                var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
-                throw new UnauthorizedAccessException(
-                    $"LINE API authentication failed. Status: 401 Unauthorized. " +
-                    $"Error: {errorContent}. " +
-                    $"Please verify your Channel Access Token is valid and not expired. " +
-                    $"URL: {url}");
-            }
+                throw new UnauthorizedAccessException("LINE API authentication failed (401). Verify the configured channel credential.");
 
             await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
             return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
@@ -2817,15 +2895,15 @@ namespace Line.Messaging
 
         #region IDisposable
         /// <summary>
-        /// 釋放資源
-        /// Dispose resources
+        /// 冪等停止此 SDK 的新請求並釋放自有 transport；借用 transport 不受影響。
+        /// 不啟動非同步清理；呼叫者先排空 in-flight 操作後 Dispose，credential 參照於此清除。
         /// </summary>
         public void Dispose()
         {
-            if (_disposeClient)
-            {
-                _client?.Dispose();
-            }
+            // 清除 credential 參照並禁止新工作；呼叫端應先等待既有操作完成，再處置此實例。
+            // 借入 transport 永不由此處關閉，避免另一 channel 的正常請求被中止。
+            if (Interlocked.Exchange(ref _authorization, null) == null) return;
+            if (_disposeClient) _client.Dispose();
         }
         #endregion
     }
diff --git a/Line.Messaging/LineSignature.cs b/Line.Messaging/LineSignature.cs
new file mode 100644
index 000000000..b59fb7f4d
--- /dev/null
+++ b/Line.Messaging/LineSignature.cs
@@ -0,0 +1,49 @@
+using System;
+using System.Security.Cryptography;
+using System.Text;
+
+namespace Line.Messaging;
+
+/// <summary>與 HTTP 框架無關的原始位元組驗簽入口；無共用 HMAC、密鑰快取或使用者狀態。</summary>
+public static class LineSignature
+{
+    /// <summary>驗證原始 request body 的 HMAC-SHA256，不先解碼 JSON、正規化文字或重新序列化。</summary>
+    /// <param name="body">呼叫端擁有的原始內容，方法不修改或保存。</param>
+    /// <param name="signatureHeader">X-Line-Signature 的 Base64 SHA256 digest。</param>
+    /// <param name="channelSecret">本次已授權 channel 的密鑰；不允許空設定。</param>
+    /// <returns>格式不合或 digest 不符回 false；密鑰及 body 的設定錯誤明確拋出。</returns>
+    public static bool Verify(byte[] body, string signatureHeader, string channelSecret)
+    {
+        if (body == null) throw new ArgumentNullException(nameof(body));
+        if (string.IsNullOrWhiteSpace(channelSecret)) throw new ArgumentException("Channel secret 不可空白。", nameof(channelSecret));
+        // SHA256 的 Base64 恆為 44 字元；先限制長度避免不可信 header 造成任意大小配置。
+        // 格式與長度不是秘密，只有相同長度 digest 的比較需要固定時間。
+        if (signatureHeader == null || signatureHeader.Length != 44) return false;
+        byte[] supplied;
+        try { supplied = Convert.FromBase64String(signatureHeader); }
+        catch (FormatException) { return false; }
+        if (supplied.Length != 32) return false;
+
+        var key = Encoding.UTF8.GetBytes(channelSecret);
+        byte[] digest = null;
+        try
+        {
+            // 每次操作獨享密鑰與 HMAC，沒有 static crypto instance。using 先處置 HMAC，
+            // finally 再清掉呼叫內產生的密鑰與摘要；不修改或持有借入的原始 body。
+            using (var hmac = new HMACSHA256(key)) digest = hmac.ComputeHash(body);
+#if NETFRAMEWORK
+            var difference = 0;
+            for (var index = 0; index < 32; index++) difference |= digest[index] ^ supplied[index];
+            return difference == 0;
+#else
+            return CryptographicOperations.FixedTimeEquals(digest, supplied);
+#endif
+        }
+        finally
+        {
+            Array.Clear(key, 0, key.Length);
+            if (digest != null) Array.Clear(digest, 0, digest.Length);
+            Array.Clear(supplied, 0, supplied.Length);
+        }
+    }
+}
diff --git a/Line.Messaging/Messages/Flex/Component/BoxComponent.cs b/Line.Messaging/Messages/Flex/Component/BoxComponent.cs
index 0e7e6159a..049bba004 100644
--- a/Line.Messaging/Messages/Flex/Component/BoxComponent.cs
+++ b/Line.Messaging/Messages/Flex/Component/BoxComponent.cs
@@ -22,6 +22,9 @@ namespace Line.Messaging
     /// </summary>
     public class BoxComponent : IFlexComponent
     {
+        /// <summary>選用的 Box 漸層背景；未指定時保持舊版 Flex JSON，不影響子元件的內容或順序。</summary>
+        public Background Background { get; set; }
+
         /// <summary>
         /// Box
         /// </summary>
diff --git a/Line.Messaging/Messages/Flex/Style/Background.cs b/Line.Messaging/Messages/Flex/Style/Background.cs
new file mode 100644
index 000000000..370663da1
--- /dev/null
+++ b/Line.Messaging/Messages/Flex/Style/Background.cs
@@ -0,0 +1,18 @@
+namespace Line.Messaging;
+
+/// <summary>Flex Box 線性漸層的傳輸資料，無資源與全域狀態；各訊息擁有自己的模型，建構後不要跨請求共用可變內容。</summary>
+public sealed class Background
+{
+    /// <summary>LINE 的背景種類；目前只支援 linearGradient，不是 BlockStyle 的背景色。</summary>
+    public string Type => "linearGradient";
+    /// <summary>漸層方向，採 LINE 格式，例如 90deg。</summary>
+    public string Angle { get; set; }
+    /// <summary>起點十六進位色碼，例如 #FF0000。</summary>
+    public string StartColor { get; set; }
+    /// <summary>選用中間點色碼；未指定時由序列化設定省略。</summary>
+    public string CenterColor { get; set; }
+    /// <summary>終點十六進位色碼，例如 #0000FF。</summary>
+    public string EndColor { get; set; }
+    /// <summary>選用中間色位置百分比，例如 25%；此資料不改變 Box 內元件順序。</summary>
+    public string CenterPosition { get; set; }
+}
diff --git a/Line.Messaging/Messages/Sender/Sender.cs b/Line.Messaging/Messages/Sender/Sender.cs
new file mode 100644
index 000000000..035a9503e
--- /dev/null
+++ b/Line.Messaging/Messages/Sender/Sender.cs
@@ -0,0 +1,23 @@
+using System;
+
+namespace Line.Messaging;
+
+/// <summary>單則訊息的發送者品牌呈現，不代表 channel 身分或授權；只含不可變顯示資料，不保存憑證。</summary>
+public sealed class Sender
+{
+    /// <summary>取得顯示名稱；在建構時驗證一至二十個 UTF-16 單元，避免進入傳輸階段才失敗。</summary>
+    public string Name { get; }
+    /// <summary>取得絕對 HTTPS 圖示網址；SDK 不下載圖片，亦不持有網路資源。</summary>
+    public string IconUrl { get; }
+    /// <summary>建立可供訊息獨立引用的品牌資訊；呼叫端不得以此顯示資料取代伺服器端 channel 授權。</summary>
+    /// <param name="name">一至二十字的顯示名稱。</param><param name="iconUrl">絕對 HTTPS 圖示網址。</param>
+    public Sender(string name, string iconUrl)
+    {
+        if (string.IsNullOrEmpty(name) || name.Length > 20)
+            throw new ArgumentException("發送者名稱必須為一至二十字。", nameof(name));
+        if (!Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
+            throw new ArgumentException("發送者圖示必須為絕對 HTTPS 網址。", nameof(iconUrl));
+        Name = name;
+        IconUrl = iconUrl;
+    }
+}
diff --git a/Line.Messaging/Messages/TextMessage.cs b/Line.Messaging/Messages/TextMessage.cs
index 6e7c6de0f..5fe4c82f7 100644
--- a/Line.Messaging/Messages/TextMessage.cs
+++ b/Line.Messaging/Messages/TextMessage.cs
@@ -56,6 +56,9 @@ namespace Line.Messaging
     /// <seealso cref="QuickReply"/>
     public class TextMessage : ISendMessage
     {
+        /// <summary>單則訊息選用的品牌呈現；未設定時不輸出 sender，維持既有 JSON。不得用它決定授權 channel。</summary>
+        public Sender Sender { get; set; }
+
         /// <summary>
         /// 取得訊息類型，固定為 Text
         /// Gets the message type, always Text
diff --git a/Line.Messaging/Webhooks/DeliveryContext.cs b/Line.Messaging/Webhooks/DeliveryContext.cs
new file mode 100644
index 000000000..88c50faa5
--- /dev/null
+++ b/Line.Messaging/Webhooks/DeliveryContext.cs
@@ -0,0 +1,11 @@
+namespace Line.Messaging.Webhooks;
+
+/// <summary>單一 webhook 投遞的不可變資訊；重送不代表新業務事件，後續去重應使用相同 WebhookEventId。</summary>
+public sealed class DeliveryContext
+{
+    /// <summary>是否為平台重送；缺少欄位的舊事件預設 false，不推測外部請求的信任程度。</summary>
+    public bool IsRedelivery { get; }
+    /// <summary>建立事件專屬的投遞資訊，不持有 request、來源 JSON 或身分憑證。</summary>
+    /// <param name="isRedelivery">平台已驗簽內容中的重送旗標。</param>
+    public DeliveryContext(bool isRedelivery) => IsRedelivery = isRedelivery;
+}
diff --git a/Line.Messaging/Webhooks/WebhookEvent.cs b/Line.Messaging/Webhooks/WebhookEvent.cs
index 9b5f875ff..f865e2aa8 100644
--- a/Line.Messaging/Webhooks/WebhookEvent.cs
+++ b/Line.Messaging/Webhooks/WebhookEvent.cs
@@ -17,26 +17,34 @@ using System.Collections.Generic;
 namespace Line.Messaging.Webhooks
 {
     /// <summary>
-    /// The webhook event generated on the LINE Platform.
-    /// https://developers.line.me/en/docs/messaging-api/reference/#webhook-event-objects
+    /// LINE webhook 的單筆事件；保留原有型別、來源與時間建構契約，新投遞欄位僅由解析器設定。
+    /// 模型不驗證簽章也不授予租戶權限；呼叫端必須先驗原始 body 的簽章再消費事件。
     /// </summary>
     public abstract class WebhookEvent
     {
+        /// <summary>平台提供的穩定事件 ID；重送保持同值，缺少欄位的舊事件為 null；不取代後續業務資料庫 ID。</summary>
+        public string WebhookEventId { get; private set; }
+
+        /// <summary>本次投遞資訊；每個事件獨立持有，不保留 JSON、HTTP request、token 或跨事件的可變狀態。</summary>
+        public DeliveryContext DeliveryContext { get; private set; } = new DeliveryContext(false);
+
         /// <summary>
-        /// Identifier for the type of event
+        /// 事件類別，由已知 enum 判定；未知類別沿用舊行為略過，不猜測業務意圖。
         /// </summary>
         public WebhookEventType Type { get; }
 
         /// <summary>
-        /// JSON object which contains the source of the event
+        /// 此事件專屬的來源資料；只供路由提示，不能直接視為伺服器端已授權使用者。
         /// </summary>
         public WebhookEventSource Source { get; }
 
         /// <summary>
-        /// Time of the event in milliseconds
+        /// 平台事件時間的 Unix 毫秒值；重送時不以接收時間覆寫，供後續去重與排序。
         /// </summary>
         public long Timestamp { get; }
 
+        /// <summary>建立事件既有欄位；建構時沒有 HTTP、token 或共用可變狀態，投遞資訊預設為非重送。</summary>
+        /// <param name="type">已知事件類別。</param><param name="source">事件專屬來源。</param><param name="timestamp">平台事件 Unix 毫秒。</param>
         public WebhookEvent(WebhookEventType type, WebhookEventSource source, long timestamp)
         {
             Type = type;
@@ -44,7 +52,20 @@ namespace Line.Messaging.Webhooks
             Timestamp = timestamp;
         }
 
+        /// <summary>在既有各類事件建構後補入共同投遞欄位；所有類別共走此入口，避免遺漏某個 switch 分支。</summary>
+        /// <param name="dynamicObject">呼叫端已完成驗簽及 JSON 解析的單筆物件；不保存原 JSON。</param><returns>可辨識的 detached 事件，或沿用舊行為回 null。</returns>
         internal static WebhookEvent CreateFrom(dynamic dynamicObject)
+        {
+            WebhookEvent result = CreateEvent(dynamicObject);
+            if (result == null) return null;
+            result.WebhookEventId = (string)dynamicObject.webhookEventId;
+            result.DeliveryContext = new DeliveryContext((bool?)dynamicObject.deliveryContext?.isRedelivery ?? false);
+            return result;
+        }
+
+        /// <summary>保留既有類別映射，不把投遞 ID 當成訊息或資料庫記錄的 ID；整個候選完成後才交還呼叫端。</summary>
+        /// <param name="dynamicObject">單筆已解析事件。</param><returns>已知事件或 null。</returns>
+        private static WebhookEvent CreateEvent(dynamic dynamicObject)
         {
             if (dynamicObject == null) { throw new ArgumentNullException(nameof(dynamicObject)); }
 


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.