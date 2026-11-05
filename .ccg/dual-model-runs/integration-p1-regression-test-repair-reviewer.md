# CCG reviewer Task: integration-p1-regression-test-repair

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
Active task: .trellis/tasks/11-05-integration-p1

P1 局部測試修補審查；僅審查此提示的合成測試與產品邊界摘錄，不讀 repo、不呼叫工具、不修改檔案。輸出繁體中文 Critical/Warning/Info，明確區分測試契約修正和不當降低驗收標準。
背景：先前SDK修正把所有HTTP失敗的遠端本文排除於例外與公開DTO，安全訊息為「LINE API request failed. HTTP <code>.」並保留HTTP狀態。舊3案期待直接回傳遠端message，違反永久隔離規則；本次保留ProviderRejected分類／標準例外，精確驗證安全訊息，注入合成私密標記並檢查不洩漏。另一案反射_client已失效；改讀_transport.client，仍真正斷言自有HttpClient已Disposed（不只公開SDK停送），再加SDK停送與替換實例檢查。產品程式本次未改。
P1-04測試helper用安全XML reader，禁DTD、外部resolver、上限1MiB，忽略縮排但保留元素順序／屬性／欄位值精確比較；Fee特性測試在原實作跑綠，日期局部culture以finally還原。無真實連線／機密，所有ID為合成資料。
已執行：初次全方案1399/1403、4失敗；修補後其中三案通過，反射接錯對象的測試筆誤已修正且1/1通過。最新完整回歸仍待收斂。此review不代表完整SDK審查或P1驗收。

## Tracked diff
diff --git a/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs b/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs
index b2efc3c15..514981523 100644
--- a/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs
+++ b/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs
@@ -39,7 +39,7 @@ public sealed class LineUtilityClassWorkflowTests
     /// </summary>
     /// <remarks>
     /// 故障注入使用向後相容的字串建構式，使第一個 client 明確由工具類別擁有；接著呼叫公開 token
-    /// 切換 API。決定性斷言是舊 client 內部 HttpClient 的下一次操作立即拋出
+    /// 切換 API。決定性斷言是舊 client 及其 transport 擁有的 HttpClient 下一次操作立即拋出
     /// <see cref="ObjectDisposedException"/>，而非繼續持有 socket handler。這個測試不發送真實 LINE
     /// 請求，且 using 會在測試結束釋放替換後的 client。
     /// </remarks>
@@ -54,17 +54,27 @@ public sealed class LineUtilityClassWorkflowTests
         clientField.Should().NotBeNull();
         var oldClient = (LineMessagingClient)clientField!.GetValue(utility)!;
 
-        var httpClientField = typeof(LineMessagingClient).GetField(
-            "_client",
+        // Transport 已集中擁有連線；只探查資源所有權，不讀取任何 credential。
+        // 同時檢查公開 API 與真正的 HttpClient，避免只清 token 卻洩漏自有 handler 也被誤判通過。
+        var transportField = typeof(LineMessagingClient).GetField(
+            "_transport",
+            BindingFlags.Instance | BindingFlags.NonPublic);
+        transportField.Should().NotBeNull();
+        var transport = transportField!.GetValue(oldClient)!;
+        var httpClientField = transport.GetType().GetField(
+            "client",
             BindingFlags.Instance | BindingFlags.NonPublic);
         httpClientField.Should().NotBeNull();
-        var oldHttpClient = (HttpClient)httpClientField!.GetValue(oldClient)!;
+        var oldHttpClient = (HttpClient)httpClientField!.GetValue(transport)!;
 
         IOrganizationService crmService = null!;
         utility.SetupChannelAccessToken(ref crmService);
 
         Func<Task> operation = () => oldHttpClient.GetAsync("https://127.0.0.1:1/");
         await operation.Should().ThrowAsync<ObjectDisposedException>();
+        Func<Task> sdkOperation = () => oldClient.GetBotInfoAsync();
+        await sdkOperation.Should().ThrowAsync<ObjectDisposedException>();
+        clientField.GetValue(utility).Should().NotBeSameAs(oldClient);
     }
 
     [Fact]
diff --git a/ChurchReport.MemberInfo.Tests/Payments/PushUtilityTests.cs b/ChurchReport.MemberInfo.Tests/Payments/PushUtilityTests.cs
index b56d82f5c..26eac134e 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/PushUtilityTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/PushUtilityTests.cs
@@ -40,12 +40,17 @@ public sealed class PushUtilityTests
         handler.RequestBody.Should().Contain("\"text\":\"ATM instructions\"");
     }
 
+    /// <summary>
+    /// 保護付款推播失敗仍拋標準例外且不洩漏遠端本文。注入含合成私密標記的 HTTP 400；
+    /// 嚴格檢查失敗分類、安全訊息及例外文字，不能以回復原始 provider message 換取相容。
+    /// HTTP 替身及 transport 由本案 using 擁有並在 await 排空後確定性釋放。
+    /// </summary>
     [Fact]
     public async Task SendMessageOrThrowAsync_throws_when_line_rejects_push_message()
     {
         var handler = new RecordingLineHandler(
             HttpStatusCode.BadRequest,
-            "{\"message\":\"invalid user id\",\"details\":[]}");
+            "{\"message\":\"private-synthetic-payment-detail\",\"details\":[]}");
         using var httpClient = new HttpClient(handler);
         var pushUtility = new PushUtility(new LineMessagingClient(
             httpClient,
@@ -54,8 +59,11 @@ public sealed class PushUtilityTests
 
         var act = () => pushUtility.SendMessageOrThrowAsync("bad-user", "ATM instructions");
 
-        await act.Should().ThrowAsync<LineNotificationException>()
-            .WithMessage("invalid user id");
+        var exception = await act.Should().ThrowAsync<LineNotificationException>()
+            .WithMessage("LINE API request failed. HTTP 400.");
+        exception.Which.Result.Status.Should().Be(LineNotificationStatus.ProviderRejected);
+        exception.Which.Result.ErrorMessage.Should().Be("LINE API request failed. HTTP 400.");
+        exception.Which.ToString().Should().NotContain("private-synthetic-payment-detail");
     }
 
     [Fact]
diff --git a/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs b/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs
index 0dd02fee5..4f7df50a2 100644
--- a/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs
@@ -110,12 +110,13 @@ public sealed class LineRichMenuWorkflowTests
 
     /// <summary>
     /// LINE provider 明確拒絕 request 時，OrThrow 變體應丟出標準 RichMenu 例外並保留 provider-rejected 狀態。
+    /// 注入含合成私密標記的 HTTP 400；精確檢查錯誤代碼及安全訊息，例外不得洩漏遠端本文。
     /// </summary>
     [Fact]
     public async Task CreateUploadAndLinkOrThrowAsync_throws_standard_exception_when_provider_rejects_request()
     {
         var handler = new SequencedHttpMessageHandler();
-        handler.Enqueue(HttpStatusCode.BadRequest, """{"message":"invalid rich menu","details":[]}""");
+        handler.Enqueue(HttpStatusCode.BadRequest, """{"message":"private-synthetic-richmenu-detail","details":[]}""");
         var workflow = CreateWorkflow(handler);
 
         var action = () => workflow.CreateUploadAndLinkOrThrowAsync(new LineRichMenuCreateUploadAndLinkRequest
@@ -128,7 +129,9 @@ public sealed class LineRichMenuWorkflowTests
         var exception = await action.Should().ThrowAsync<LineRichMenuException>();
         exception.Which.Result.Status.Should().Be(LineRichMenuStatus.ProviderRejected);
         exception.Which.Result.ErrorCode.Should().Be("line-richmenu-provider-rejected");
-        exception.Which.Result.ErrorMessage.Should().Be("invalid rich menu");
+        exception.Which.Result.ErrorMessage.Should().Be("LINE API request failed. HTTP 400.");
+        exception.Which.Message.Should().Be("LINE API request failed. HTTP 400.");
+        exception.Which.ToString().Should().NotContain("private-synthetic-richmenu-detail");
     }
 
     /// <summary>
diff --git a/LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs b/LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs
index d4d503418..5c81a5569 100644
--- a/LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs
+++ b/LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs
@@ -517,10 +517,14 @@ public sealed class LineNotificationWorkflowTests
         handler.Requests.Should().BeEmpty();
     }
 
+    /// <summary>
+    /// 保護 provider 拒絕仍轉成標準失敗例外，且不回顯可能帶個資的遠端本文。
+    /// 注入含合成私密標記的 HTTP 400，決定性斷言為狀態不變、安全訊息精確相等及例外不含標記。
+    /// </summary>
     [Fact]
     public async Task SendOrThrowAsync_throws_standard_exception_when_send_fails()
     {
-        var handler = new CapturingHttpMessageHandler(HttpStatusCode.BadRequest, """{"message":"invalid user id","details":[]}""");
+        var handler = new CapturingHttpMessageHandler(HttpStatusCode.BadRequest, """{"message":"private-synthetic-notification-detail","details":[]}""");
         var workflow = CreateWorkflow(handler);
 
         var action = () => workflow.SendOrThrowAsync(new LineNotificationRequest
@@ -531,7 +535,9 @@ public sealed class LineNotificationWorkflowTests
 
         var exception = await action.Should().ThrowAsync<LineNotificationException>();
         exception.Which.Result.Status.Should().Be(LineNotificationStatus.ProviderRejected);
-        exception.Which.Result.ErrorMessage.Should().Be("invalid user id");
+        exception.Which.Result.ErrorMessage.Should().Be("LINE API request failed. HTTP 400.");
+        exception.Which.Message.Should().Be("LINE API request failed. HTTP 400.");
+        exception.Which.ToString().Should().NotContain("private-synthetic-notification-detail");
     }
 
     private static LineNotificationWorkflow CreateWorkflow(CapturingHttpMessageHandler handler)
diff --git a/ToolUtility.Tests/ToolUtility.Tests.csproj b/ToolUtility.Tests/ToolUtility.Tests.csproj
index 309c7461a..2e32e1160 100644
--- a/ToolUtility.Tests/ToolUtility.Tests.csproj
+++ b/ToolUtility.Tests/ToolUtility.Tests.csproj
@@ -45,4 +45,9 @@
     <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
   </ItemGroup>
 
+  <ItemGroup>
+    <!-- 黃金查詢是合成資料，內嵌後兩個測試宿主均不依賴執行目錄或網路。 -->
+    <EmbeddedResource Include="Golden\ToolUtility\*.xml" />
+  </ItemGroup>
+
 </Project>

## Line.Messaging/HttpResponseMessageExtensions.cs
```csharp
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

## Line.Messaging/ChannelHttpTransport.cs
```csharp
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

## ToolUtility.Tests/TestHelpers/GoldenQueryAssertions.cs
```csharp
using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// 比對內嵌的合成查詢黃金資料，鎖定跨目標搬移前後的 XML 條件樹。
    /// 不比較無語義的引號與縮排；元素、順序、屬性及欄位值必須完全相同。
    /// </summary>
    internal static class GoldenQueryAssertions
    {
        /// <summary>
        /// 以固定資源名稱讀取預期查詢，拒絕 DTD 與過大 XML 後比較正規化內容。
        /// 每次讀取獨立擁有資源串流及 reader，成功或斷言失敗皆確定釋放，不保存查詢資料。
        /// </summary>
        /// <param name="fileName">測試作者指定的資源檔名，不接受使用者輸入。</param>
        /// <param name="actualXml">受測服務實際送到 SDK 替身的 XML。</param>
        internal static void Equal(string fileName, string actualXml)
        {
            using (var stream = typeof(GoldenQueryAssertions).Assembly.GetManifestResourceStream(
                "ToolUtility.Tests.Golden.ToolUtility." + fileName))
            {
                Assert.NotNull(stream);
                using (var reader = XmlReader.Create(stream!, CreateSettings()))
                {
                    var expected = XDocument.Load(reader).ToString(SaveOptions.DisableFormatting);
                    using (var text = new StringReader(actualXml))
                    using (var actual = XmlReader.Create(text, CreateSettings()))
                    {
                        Assert.Equal(expected, XDocument.Load(actual).ToString(SaveOptions.DisableFormatting));
                    }
                }
            }
        }

        /// <summary>
        /// 建立單次 reader 的安全設定；固定上限足以容納測試查詢，禁止外部解析。
        /// </summary>
        private static XmlReaderSettings CreateSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = true,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            };
        }
    }
}

```

## ToolUtility.Tests/TestHelpers/GoldenQueryAssertionsTests.cs
```csharp
using System.Xml;
using Xunit;
using Xunit.Sdk;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// 驗證搬移守門自身確實拒絕語義變更，不會只因 XML 能解析就放行。
    /// 所有內容為固定合成字串；測試不使用檔案系統、網路、真實識別碼或共用可變狀態。
    /// </summary>
    public sealed class GoldenQueryAssertionsTests
    {
        /// <summary>
        /// 只改引號及縮排應通過；若 helper 又開始比較無語義空白，此案例會失敗。
        /// </summary>
        [Fact]
        public void Equal_OnlyWhitespaceAndQuoteStyleDiffer_Passes()
        {
            GoldenQueryAssertions.Equal("comparison-contract.xml",
                "<fetch><entity name='synthetic'><attribute name='syntheticid'/><order attribute='syntheticid' descending='false'/></entity></fetch>");
        }

        /// <summary>
        /// 注入欄位改名、遺漏排序或變更元素順序，均須斷言失敗，不能降低查詢黃金檔強度。
        /// </summary>
        /// <param name="actualXml">只有一個明確契約差異的合成查詢。</param>
        [Theory]
        [InlineData("<fetch><entity name='other'><attribute name='syntheticid'/><order attribute='syntheticid' descending='false'/></entity></fetch>")]
        [InlineData("<fetch><entity name='synthetic'><attribute name='syntheticid'/></entity></fetch>")]
        [InlineData("<fetch><entity name='synthetic'><order attribute='syntheticid' descending='false'/><attribute name='syntheticid'/></entity></fetch>")]
        public void Equal_QueryContractChanges_Rejects(string actualXml)
        {
            Assert.ThrowsAny<XunitException>(() => GoldenQueryAssertions.Equal("comparison-contract.xml", actualXml));
        }

        /// <summary>
        /// 注入 DTD，即使實體是內嵌文字也拒絕解析；防止日後無意啟用外部實體解析。
        /// </summary>
        [Fact]
        public void Equal_Dtd_RejectsBeforeComparison()
        {
            Assert.Throws<XmlException>(() => GoldenQueryAssertions.Equal("comparison-contract.xml",
                "<!DOCTYPE fetch [<!ENTITY value 'synthetic'>]><fetch><entity name='&value;'/></fetch>"));
        }

        /// <summary>
        /// 注入超過 1 MiB 的短期合成 XML；reader 必須以容量錯誤拒絕，不能保留無界輸入。
        /// </summary>
        [Fact]
        public void Equal_OversizedDocument_RejectsBeforeComparison()
        {
            var oversized = "<fetch>" + new string('x', 1024 * 1024) + "</fetch>";
            Assert.Throws<XmlException>(() => GoldenQueryAssertions.Equal("comparison-contract.xml", oversized));
        }
    }
}

```

## ToolUtility.Tests/FeeOperations/FeeCharacterizationTests.cs
```csharp
using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.FeeOperations;
using Xunit;

namespace ToolUtility.Tests.FeeOperations
{
    /// <summary>
    /// 在 FeeService 尚未搬移前鎖定現行有效查詢與結果傳遞。
    /// 全部使用合成教會資料與獨立 SDK 替身；不連 D365，不將空實作視為正確業務。
    /// </summary>
    public sealed class FeeCharacterizationTests
    {
        private const string ContactId = "11111111-1111-4111-8111-111111111111";

        /// <summary>
        /// 保護奉獻查詢的欄位、排序、聯絡人與分類條件；注入同名但不同 ID 的資料，
        /// 決定性斷言為實際 FetchXML 符合黃金檔且兩筆合法記錄均完整保留。
        /// </summary>
        [Fact]
        public void RetrieveDedicationFee_ExistingImplementation_MatchesGoldenAndPreservesSameNameRows()
        {
            var expected = CreateSameNameRows();
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => GoldenQueryAssertions.Equal(
                    "fee-dedication.xml", Assert.IsType<FetchExpression>(query).Query))
                .Returns(expected);
            var sut = new FeeService(new object(), client.Object);

            var result = sut.RetrieveDedicationFee("Synthetic Contact", ContactId);

            Assert.Same(expected, result);
            Assert.Equal(2, result.Entities.Count);
            Assert.NotEqual(result.Entities[0].Id, result.Entities[1].Id);
            Assert.Equal(result.Entities[0]["new_name"], result.Entities[1]["new_name"]);
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 保護日期區間查詢的既有付款狀態白名單與含端點日期條件；注入固定日期和空結果，
        /// 斷言完整 XML 而非只比對片段，且空集合來自 SDK 而非服務自行製造。
        /// </summary>
        [Fact]
        public void RetrieveDedicationFeeByDateRange_ExistingImplementation_MatchesGolden()
        {
            var expected = new EntityCollection();
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => GoldenQueryAssertions.Equal(
                    "fee-dedication-date-range.xml", Assert.IsType<FetchExpression>(query).Query))
                .Returns(expected);
            var sut = new FeeService(new object(), client.Object);

            // 原產品目前依 ambient culture 格式化；此特性基準固定西曆，避免測試機預設曆法改變 golden。
            // 僅設定當次執行脈絡，finally 還原；不可改 DefaultThreadCurrentCulture 污染並行案例。
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var result = sut.RetrieveDedicationFeeByDateRange(
                    "Synthetic Contact", ContactId, new DateTime(2027, 1, 2), new DateTime(2027, 3, 4));
                Assert.Same(expected, result);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 保護 SDK 失敗不被改成空集合或成功；注入同一例外實例，
        /// 斷言原始例外與呼叫次數，讓最終 host 能以同一事件交給共用通知入口。
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RetrieveDedicationFee_SdkFails_PropagatesOriginalException(bool useDateRange)
        {
            var failure = new InvalidOperationException("Synthetic SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(failure);
            var sut = new FeeService(new object(), client.Object);

            var actual = Assert.Throws<InvalidOperationException>(() =>
            {
                if (useDateRange)
                    sut.RetrieveDedicationFeeByDateRange(
                        "Synthetic Contact", ContactId, new DateTime(2027, 1, 2), new DateTime(2027, 3, 4));
                else
                    sut.RetrieveDedicationFee("Synthetic Contact", ContactId);
            });

            Assert.Same(failure, actual);
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 建立無機密的兩筆合法同名資料；每案重新配置集合，防止測試間共享可變 Entity。
        /// </summary>
        private static EntityCollection CreateSameNameRows()
        {
            return new EntityCollection(new[]
            {
                new Entity("new_fee", new Guid("22222222-2222-4222-8222-222222222222"))
                {
                    ["new_name"] = "Synthetic Fee"
                },
                new Entity("new_fee", new Guid("33333333-3333-4333-8333-333333333333"))
                {
                    ["new_name"] = "Synthetic Fee"
                }
            });
        }
    }
}

```

## ToolUtility.Tests/Golden/ToolUtility/comparison-contract.xml
```xml
<fetch>
  <entity name="synthetic">
    <attribute name="syntheticid" />
    <order attribute="syntheticid" descending="false" />
  </entity>
</fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/fee-dedication-date-range.xml
```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="new_fee">
    <attribute name="new_feeid" />
    <attribute name="new_name" />
    <attribute name="createdon" />
    <attribute name="new_pay_date" />
    <attribute name="new_fee_shoud_pay" />
    <attribute name="new_fee_really_paid" />
    <attribute name="new_pay_way" />
    <attribute name="new_category" />
    <attribute name="new_others" />
    <attribute name="new_paid_period" />
    <order attribute="new_name" descending="false" />
    <filter type="and">
      <condition attribute="new_contact_new_fee" operator="eq" uiname="Synthetic Contact" uitype="contact" value="{11111111-1111-4111-8111-111111111111}" />
      <condition attribute="new_category" operator="not-null" />
      <condition attribute="new_pay_status" operator="in">
        <value>100000001</value>
        <value>100000002</value>
        <value>100000003</value>
        <value>100000004</value>
        <value>100000006</value>
      </condition>
      <condition attribute="new_pay_date" operator="on-or-after" value="2027-1-2" />
      <condition attribute="new_pay_date" operator="on-or-before" value="2027-3-4" />
    </filter>
  </entity>
</fetch>

```

## ToolUtility.Tests/Golden/ToolUtility/fee-dedication.xml
```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="new_fee">
    <attribute name="new_feeid" />
    <attribute name="new_name" />
    <attribute name="createdon" />
    <attribute name="new_pay_date" />
    <attribute name="new_fee_shoud_pay" />
    <attribute name="new_fee_really_paid" />
    <attribute name="new_pay_way" />
    <attribute name="new_category" />
    <attribute name="new_others" />
    <order attribute="new_name" descending="false" />
    <filter type="and">
      <condition attribute="new_contact_new_fee" operator="eq" uiname="Synthetic Contact" uitype="contact" value="{11111111-1111-4111-8111-111111111111}" />
      <condition attribute="new_category" operator="not-null" />
    </filter>
  </entity>
</fetch>

```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.