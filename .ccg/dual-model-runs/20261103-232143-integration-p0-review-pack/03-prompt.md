# 階段0 第3批：LineMessagingProcessor.Core

你是唯讀外部審查者。本輪為「單模型（使用者指定 ClaudeOnly）」，不是雙模型或配額降級。只審查，不改檔、不提交、不執行產品／工具／測試，不建立任何網路或 D365／LINE 連線。
審查重點：跨使用者／跨教會隔離、資源釋放、機密外洩、例外責任（設計10.5／10.9）、net48/net10行為一致、守門假綠燈。所有真實連線驗收仍由使用者負責。
機密欄位已遮罩；遮罩值不可用來推斷實際設定正確性。不得讀取 appsettings*、User Secrets、環境變數、Logs、金鑰／授權檔，或以 git show 取未遮罩版本。只以所附安全差異審查；如需上下文，可只查明確的非機密程式來源。不要開啟 .ccg/dual-model-runs 的其他歷史輸出或 research 原始紀錄。
輸出只列可驗證的 Critical／High／Medium／Low，包含檔案、行號、觸發條件、具體影響及最小修法；測試不足須說明可重現的缺口，不把純猜測當發現。沒有問題請明寫。不得聲稱實跑測試。
已核定例外：嚴格 .Tests 測試專案僅 CA1707 放寬；未實作 API 的 CA1716 可改語意近名；Core 不通知，Plugin 僅追蹤，最終告警在 net10 重試用盡／階段5；正式金鑰只程序環境且不讀內容；本階段不存在業務 Rejected。小組頁面由使用者確認恢復，圖表未逐項驗證，之前審查兩次為逾時。

範圍：73b6e6cf409daa7559fd3c11d482e3efc542c0d7..ae29cb40aa5c395f83c169068d7ca40e7094c509
安全差異 SHA256：b78bb09d737f1031680c421c2c260e851ce1ca3acc8ef2c85326863e6a3bab56
同批其他分份須一併審查後才算完成，本份不可代表全批。
專項：每次呼叫獨立 credential/request/result；回應與取消資源釋放；caller 取消只在收到回應前生效，收到回應後依 HTTP 分類；非 caller 取消 Retryable 且保留原始例外；不通知、不寫檔、不把 Token/body/userId 放進自產輸出。

```diff
diff --git a/.trellis/spec/backend/error-handling.md b/.trellis/spec/backend/error-handling.md
index df059409..8d506b78 100644
--- a/.trellis/spec/backend/error-handling.md
+++ b/.trellis/spec/backend/error-handling.md
@@ -95,3 +95,13 @@ catch (Exception ex)
     throw;
 }
 ```
+
+## 雙目標程式庫與 net48 Plugin
+
+依使用者 2026-11-03 核定，出處設計 v3.3 10.5／10.9、計畫 04 §5.4：
+- LineMessagingProcessor.Core 不是最終失敗邊界，不參考 ToolUtility 或 notifier，不新增未接入的 callback／no-op。無 HTTP 回應的內部逾時／HttpRequestException 以 Retryable 結果保留原始 TransportException；caller 取消只在收到回應前生效，收到回應後一律回傳分類結果；非 caller 造成的取消一律視為傳輸失敗（Retryable，保留原始例外）。依使用者 2026-11-03 核定補修。其他非預期例外往外拋。有 HTTP 回應不製造 TransportException。
+- 結果與例外只屬於這次操作，不存 static／快取，不持有 request／response／credential；回應先 Dispose。Core 自產訊息與 ToString 不含 Token、內文或 userId，禁止把原始 Exception 直接序列化或寫進診斷輸出。
+- net48 Plugin 沙箱不寫 Exception.log、不呼叫 notifier、不另外發管理者 LINE。Retryable 先以 ITracingService 追蹤安全欄位，再拋 InvalidPluginExecutionException；有原始傳輸例外時保留為 InnerException。Permanent 只追蹤；非預期例外追蹤型別後 `throw;`。
+- D365 端最終通知由階段 5 LineWebhook 每五分鐘彙總、依門檻交給共用入口；階段 0～4 只能從系統作業與 Plugin 追蹤查看，屬排定的過渡期。
+- net10 主機呼叫 Core，重試用盡後才經 ExceptionReporting 上報一次，屬階段 2 以後；正常 caller 取消與成功恢復的重試不上報，通知管道自身失敗不遞迴通知。
+- 日後遇到階段段落省略責任時，先查設計 Word 第十章及計畫 02～04；已有答案就按出處執行並在 WP 回報註明，不能只因本段未重複敘述而判定缺少架構。

diff --git a/LineMessagingProcessor.Core/ILinePushClient.cs b/LineMessagingProcessor.Core/ILinePushClient.cs
new file mode 100644
index 00000000..28d5a1f6
--- /dev/null
+++ b/LineMessagingProcessor.Core/ILinePushClient.cs
@@ -0,0 +1,21 @@
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace LineMessagingProcessor.Core;
+
+/// <summary>
+/// 雙目標最小單人推播能力；不含群播、廣播、業務授權或管理者告警。
+/// 呼叫端擁有輸入與結果的操作生命週期，以及最終失敗的回報責任。
+/// </summary>
+public interface ILinePushClient
+{
+    /// <summary>
+    /// 送出一次固定端點請求；收到回應前的 caller 取消直接拋出，收到回應後保留狀態碼分類結果。
+    /// 非 caller 取消與其他已核定傳輸失敗回傳 Retryable，保留原始例外交最終失敗邊界處理。
+    /// </summary>
+    /// <param name="credential">受信任伺服器邊界選定的憑證，不保存在共用標頭。</param>
+    /// <param name="request">已驗證並完成快照的單人請求。</param>
+    /// <param name="ct">只在取得回應前生效的呼叫端取消訊號，不得混同內部十秒或 HttpClient 自身逾時。</param>
+    /// <returns>已釋放 HTTP 資源的操作結果。</returns>
+    Task<LinePushResult> PushAsync(LineChannelCredential credential, LinePushRequest request, CancellationToken ct);
+}

diff --git a/LineMessagingProcessor.Core/LineChannelCredential.cs b/LineMessagingProcessor.Core/LineChannelCredential.cs
new file mode 100644
index 00000000..ad10b355
--- /dev/null
+++ b/LineMessagingProcessor.Core/LineChannelCredential.cs
@@ -0,0 +1,29 @@
+using System;
+
+namespace LineMessagingProcessor.Core;
+
+/// <summary>
+/// 單次送訊由呼叫端提供的不可變憑證值；不自行解析教會或保存到共用客戶端。
+/// 呼叫端決定最長生命週期，字串無法保證清零，因此禁止快取、序列化或記錄此值。
+/// </summary>
+public sealed class LineChannelCredential
+{
+    /// <summary>驗證標頭所需的非空 ASCII token；錯誤只提供固定原因，絕不回顯輸入。</summary>
+    /// <param name="token">由受信任邊界選定的頻道 token，不能由收件者指定。</param>
+    public LineChannelCredential(string token)
+    {
+        if (string.IsNullOrEmpty(token))
+            throw new ArgumentException("A nonempty header-safe token is required.", nameof(token));
+        foreach (var character in token)
+            if (character <= 32 || character >= 127)
+                throw new ArgumentException("The token must contain only visible ASCII characters.", nameof(token));
+        AccessToken = token;
+    }
+
+    /// <summary>只供當次組合 Authorization 使用，不向結果、全域標頭或診斷傳遞。</summary>
+    internal string AccessToken { get; }
+
+    /// <summary>提供固定遮罩文字，使一般插值診斷不會輸出憑證內容。</summary>
+    /// <returns>固定的 LineChannelCredential(***)。</returns>
+    public override string ToString() => "LineChannelCredential(***)";
+}

diff --git a/LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj b/LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj
new file mode 100644
index 00000000..24aed000
--- /dev/null
+++ b/LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj
@@ -0,0 +1,17 @@
+<Project Sdk="Microsoft.NET.Sdk">
+  <PropertyGroup>
+    <TargetFrameworks>net48;net10.0</TargetFrameworks>
+    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
+    <!-- SDK 在 targets 之前決定 XML 文件輸出；嚴格開關與文件開關必須同在專案本體。 -->
+    <GenerateDocumentationFile>true</GenerateDocumentationFile>
+  </PropertyGroup>
+  <ItemGroup>
+    <PackageReference Include="Newtonsoft.Json" />
+    <InternalsVisibleTo Include="LineMessagingProcessor.Tests" />
+    <InternalsVisibleTo Include="SpeechMessage.Dynamics.Plugins.Tests" />
+  </ItemGroup>
+  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
+    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
+    <Reference Include="System.Net.Http" />
+  </ItemGroup>
+</Project>

diff --git a/LineMessagingProcessor.Core/LinePushClient.cs b/LineMessagingProcessor.Core/LinePushClient.cs
new file mode 100644
index 00000000..8b454a4f
--- /dev/null
+++ b/LineMessagingProcessor.Core/LinePushClient.cs
@@ -0,0 +1,129 @@
+using System;
+using System.Globalization;
+using System.Linq;
+using System.Net.Http;
+using System.Net.Http.Headers;
+using System.Text;
+using System.Threading;
+using System.Threading.Tasks;
+using Newtonsoft.Json;
+
+namespace LineMessagingProcessor.Core;
+
+/// <summary>
+/// 固定 LINE push 端點的單次傳輸實作，沒有重試佇列、背景工作或管理者 notifier。
+/// net48 的程序級共用 HttpClient 不含使用者狀態，net10 的 HttpClient 由主機擁有；
+/// 每個操作獨佔 HTTP 訊息、十秒逾時與取消連結，結束前按 using 次序釋放。
+/// </summary>
+public sealed class LinePushClient : ILinePushClient
+{
+    /// <summary>外部或沙箱程序擁有的無狀態連線池，不保存任何當次憑證或結果。</summary>
+    private readonly HttpClient _httpClient;
+    /// <summary>建立操作專屬逾時來源；委派本身不捕獲使用者、教會或 request 狀態。</summary>
+    private readonly Func<TimeSpan, CancellationTokenSource> _createTimeout;
+    /// <summary>固定十秒的 Core 內部期限，不讓未驗證輸入延長資源生命週期。</summary>
+    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
+
+#if NET48
+    /// <summary>
+    /// 沙箱 AppDomain／程序是此唯一客戶端與連線池的 owner，卸載時一併回收。
+    /// 它不使用 Cookie、預設認證或自動轉址，沒有 per-user 狀態；每個傳輸取消與內容另有 using owner。
+    /// </summary>
+    private static readonly HttpClient SharedClient = CreateSharedClient();
+
+    /// <summary>使用沙箱程序生命週期內的無狀態客戶端，不為每次 Plugin 執行建立連線池。</summary>
+    public LinePushClient() : this(SharedClient) { }
+#endif
+
+    /// <summary>連線由呼叫端擁有；必須使用無 Cookie、無預設憑證且不自動轉址的管線。</summary>
+    /// <param name="httpClient">可重用且無教會／使用者狀態的客戶端；Core 不 Dispose 它。</param>
+#if NET48
+    internal LinePushClient(HttpClient httpClient) : this(httpClient, StartTimeout) { }
+#else
+    public LinePushClient(HttpClient httpClient) : this(httpClient, StartTimeout) { }
+#endif
+
+    /// <summary>統一雙目標的計時來源所有權；來源必須逐次建立，回傳後由單一操作 Dispose。</summary>
+    /// <param name="httpClient">外部擁有的無狀態客戶端。</param>
+    /// <param name="createTimeout">依給定期限建立獨立來源的排程器，不得回傳共用或快取來源。</param>
+    internal LinePushClient(HttpClient httpClient, Func<TimeSpan, CancellationTokenSource> createTimeout)
+    {
+        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
+        _createTimeout = createTimeout ?? throw new ArgumentNullException(nameof(createTimeout));
+    }
+
+    /// <summary>以逐請求標頭與快照內容推播；只轉換已核定的傳輸失敗，其他例外往外拋。</summary>
+    /// <param name="credential">當次頻道憑證。</param>
+    /// <param name="request">當次不可變請求。</param>
+    /// <param name="ct">明確 caller 取消；只在尚未收到 HTTP 回應時生效，回應抵達後以狀態碼分類結果。</param>
+    /// <returns>不持有 request／response／credential 的結果。</returns>
+    public async Task<LinePushResult> PushAsync(LineChannelCredential credential, LinePushRequest request, CancellationToken ct)
+    {
+#if NET48
+        if (credential is null) throw new ArgumentNullException(nameof(credential));
+        if (request is null) throw new ArgumentNullException(nameof(request));
+#else
+        ArgumentNullException.ThrowIfNull(credential);
+        ArgumentNullException.ThrowIfNull(request);
+#endif
+        ct.ThrowIfCancellationRequested();
+
+        using var timeout = _createTimeout(RequestTimeout);
+        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
+        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push");
+        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
+        message.Headers.Add("X-Line-Retry-Key", request.RetryKey.ToString("D", CultureInfo.InvariantCulture));
+        var body = JsonConvert.SerializeObject(new
+        {
+            to = request.UserId,
+            messages = request.Messages.Select(text => new { type = "text", text }).ToArray()
+        });
+        message.Content = new StringContent(body, Encoding.UTF8, "application/json");
+
+        try
+        {
+            // 只需要標頭；不緩衝、不讀取也不保留錯誤本文，回傳前釋放回應及內容串流。
+            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token)
+                .ConfigureAwait(false);
+            var status = (int)response.StatusCode;
+            var outcome = status switch
+            {
+                >= 200 and <= 299 => LinePushOutcome.Success,
+                409 => LinePushOutcome.AlreadyAccepted,
+                429 or (>= 500 and <= 599) => LinePushOutcome.Retryable,
+                _ => LinePushOutcome.Permanent
+            };
+            var requestId = response.Headers.TryGetValues("x-line-request-id", out var values) ? values.FirstOrDefault() : null;
+            return new LinePushResult(outcome, response.StatusCode, requestId, null);
+        }
+        catch (OperationCanceledException exception)
+        {
+            // caller 取消只在沒有收到 HTTP 回應時中止操作；其他取消來源都是無回應的傳輸失敗。
+            // 一旦 SendAsync 已回傳 response，狀態碼結果優先，避免丟失服務端已知的接受／拒絕資訊。
+            ct.ThrowIfCancellationRequested();
+            return new LinePushResult(LinePushOutcome.Retryable, null, null, exception);
+        }
+        catch (HttpRequestException exception)
+        {
+            // 本層沒有收到 HTTP 回應，原始實例交給最終邊界；不能在可恢復重試階段重複告警。
+            // net48 不會像 net10 自動把同時發生的 caller 取消正規化，因此在此明確優先辨識。
+            ct.ThrowIfCancellationRequested();
+            return new LinePushResult(LinePushOutcome.Retryable, null, null, exception);
+        }
+    }
+
+    /// <summary>跨框架共用的正式排程器，建立具固定期限的獨立來源，由呼叫的 using 確定性釋放。</summary>
+    /// <param name="duration">Core 固定的十秒期限。</param>
+    /// <returns>尚未與其他操作共用的新來源。</returns>
+    private static CancellationTokenSource StartTimeout(TimeSpan duration) => new(duration);
+
+#if NET48
+    /// <summary>只在 AppDomain 建立一次無狀態池；TLS 依 OS／Framework 4.8 預設，不改程序全域 TLS。</summary>
+    /// <returns>由程序擁有、沒有 Cookie 或使用者憑證的客戶端。</returns>
+    private static HttpClient CreateSharedClient()
+    {
+        var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, UseDefaultCredentials = false };
+        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
+    }
+#endif
+}

diff --git a/LineMessagingProcessor.Core/LinePushOutcome.cs b/LineMessagingProcessor.Core/LinePushOutcome.cs
new file mode 100644
index 00000000..596da2de
--- /dev/null
+++ b/LineMessagingProcessor.Core/LinePushOutcome.cs
@@ -0,0 +1,16 @@
+namespace LineMessagingProcessor.Core;
+
+/// <summary>HTTP 與傳輸層結果分類；此階段只分類，不執行重試、業務拒絕或管理者通知。</summary>
+public enum LinePushOutcome
+{
+    /// <summary>HTTP 2xx 表示平台接受本次請求。</summary>
+    Success,
+    /// <summary>HTTP 409 表示相同 Retry Key 已接受，呼叫端不得換鍵重送。</summary>
+    AlreadyAccepted,
+    /// <summary>429、5xx 或未取得回應的傳輸失敗，是否重試由外層決定。</summary>
+    Retryable,
+    /// <summary>不應盲目重試的 HTTP 錯誤，先修正憑證或輸入。</summary>
+    Permanent,
+    /// <summary>保留供階段五的業務拒絕；目前 Core 沒有會產生此值的路徑。</summary>
+    Rejected
+}

diff --git a/LineMessagingProcessor.Core/LinePushRequest.cs b/LineMessagingProcessor.Core/LinePushRequest.cs
new file mode 100644
index 00000000..f3e215cc
--- /dev/null
+++ b/LineMessagingProcessor.Core/LinePushRequest.cs
@@ -0,0 +1,63 @@
+using System;
+using System.Collections.Generic;
+
+namespace LineMessagingProcessor.Core;
+
+/// <summary>
+/// 僅容許一名收件者與最多五則 text 的操作快照，不能表示群播或廣播。
+/// 建構時複製並驗證全部訊息再發布，來源集合後續修改不得影響已建立請求。
+/// </summary>
+public sealed class LinePushRequest
+{
+    /// <summary>在任何 HTTP 操作之前完成單人 ID、訊息界限與事件重試鍵驗證。</summary>
+    /// <param name="userId">U 加三十二個小寫十六進位字元，須完整符合、不能附加換行。</param>
+    /// <param name="messages">一到五則非空文字，每則最多五千個 UTF-16 碼元。</param>
+    /// <param name="retryKey">同一業務事件重試必須沿用的非空識別碼。</param>
+    public LinePushRequest(string userId, IReadOnlyList<string> messages, Guid retryKey)
+    {
+        if (userId is null || userId.Length != 33 || userId[0] != 'U')
+            throw new ArgumentException("A valid single-recipient identifier is required.", nameof(userId));
+        for (var index = 1; index < userId.Length; index++)
+        {
+            var character = userId[index];
+            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
+                throw new ArgumentException("A valid single-recipient identifier is required.", nameof(userId));
+        }
+#if NET48
+        if (messages is null) throw new ArgumentNullException(nameof(messages));
+#else
+        ArgumentNullException.ThrowIfNull(messages);
+#endif
+        var count = messages.Count;
+        if (count is < 1 or > 5)
+            throw new ArgumentException("One through five text messages are required.", nameof(messages));
+        if (retryKey == Guid.Empty)
+            throw new ArgumentException("A nonempty event retry key is required.", nameof(retryKey));
+
+        // 先完成有界候選再發布；不得保存 caller 清單，或逐步暴露尚未驗證完的內容。
+        var snapshot = new string[count];
+        for (var index = 0; index < count; index++)
+        {
+            var text = messages[index];
+            if (string.IsNullOrEmpty(text) || text.Length > 5000)
+                throw new ArgumentException("Each text must contain one through 5000 UTF-16 code units.", nameof(messages));
+            snapshot[index] = text;
+        }
+        UserId = userId;
+        RetryKey = retryKey;
+        Messages = Array.AsReadOnly(snapshot);
+    }
+
+    /// <summary>伺服器已驗證的單一收件 ID，僅用於當次 HTTP 內容，不可當成授權依據。</summary>
+    public string UserId { get; }
+
+    /// <summary>呼叫專屬且不可變的完整訊息集合，不把外部可變清單直接發布。</summary>
+    public IReadOnlyList<string> Messages { get; }
+
+    /// <summary>由業務事件衍生的重試鍵；Core 不重新產生或在失敗時替換。</summary>
+    public Guid RetryKey { get; }
+
+    /// <summary>診斷只能顯示型別及遮罩，不帶 userId 或訊息內容。</summary>
+    /// <returns>固定遮罩。</returns>
+    public override string ToString() => "LinePushRequest(***)";
+}

diff --git a/LineMessagingProcessor.Core/LinePushResult.cs b/LineMessagingProcessor.Core/LinePushResult.cs
new file mode 100644
index 00000000..96b79935
--- /dev/null
+++ b/LineMessagingProcessor.Core/LinePushResult.cs
@@ -0,0 +1,48 @@
+using System;
+using System.Globalization;
+using System.Net;
+using Newtonsoft.Json;
+
+namespace LineMessagingProcessor.Core;
+
+/// <summary>
+/// 單次呼叫的不可變結果，只保存分類與必要診斷值，不持有 HTTP 訊息或憑證。
+/// 傳輸例外保留原始實例供最終邊界上報；不得直接序列化或輸出其 Message／ToString。
+/// 呼叫端消費後應放棄結果參考，Core 不在 static 或快取保存它。
+/// </summary>
+public sealed class LinePushResult
+{
+    /// <summary>只允許傳輸實作建立一致結果；有 HTTP 狀態與無回應故障為互斥路徑。</summary>
+    /// <param name="outcome">已分類結果。</param>
+    /// <param name="statusCode">有回應才提供的 HTTP 狀態。</param>
+    /// <param name="requestId">有回應才提供的平台診斷 ID。</param>
+    /// <param name="transportException">只在未收到回應的故障傳入原始實例。</param>
+    internal LinePushResult(LinePushOutcome outcome, HttpStatusCode? statusCode, string? requestId, Exception? transportException)
+    {
+        Outcome = outcome;
+        StatusCode = statusCode;
+        RequestId = requestId;
+        TransportException = transportException;
+    }
+
+    /// <summary>取得 HTTP／傳輸分類，不把可恢復失敗當作成功。</summary>
+    public LinePushOutcome Outcome { get; }
+
+    /// <summary>只有平台接受或同鍵已接受才成功，Rejected 不在本階段產生。</summary>
+    public bool Success => Outcome is LinePushOutcome.Success or LinePushOutcome.AlreadyAccepted;
+
+    /// <summary>收到回應時的狀態碼；傳輸失敗沒有 HTTP 狀態碼。</summary>
+    public HttpStatusCode? StatusCode { get; }
+
+    /// <summary>平台 x-line-request-id；不讀取或保存可能含敏感資訊的錯誤回應本文。</summary>
+    public string? RequestId { get; }
+
+    /// <summary>僅無回應的傳輸失敗保留原始例外；有 HTTP 狀態的結果必須為 null。</summary>
+    [JsonIgnore]
+    public Exception? TransportException { get; }
+
+    /// <summary>只輸出固定格式的分類與數字狀態碼，不包含 ID、例外本文或任何請求內容。</summary>
+    /// <returns>可安全用於基本診斷的摘要。</returns>
+    public override string ToString() => "LinePushResult(" + Outcome + ", " +
+        (StatusCode.HasValue ? ((int)StatusCode.Value).ToString(CultureInfo.InvariantCulture) : "no-response") + ")";
+}

diff --git a/LineMessagingProcessor.Tests/Golden/push-text.json b/LineMessagingProcessor.Tests/Golden/push-text.json
new file mode 100644
index 000000000..ceaebe530
--- /dev/null
+++ b/LineMessagingProcessor.Tests/Golden/push-text.json
@@ -0,0 +1,7 @@
+{
+  "to": "[REDACTED_LINE_ID]",
+  "messages": [
+    { "type": "text", "text": "hello" },
+    { "type": "text", "text": "quoted \"text\"" }
+  ]
+}

diff --git a/LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj b/LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj
index 8937f3ead..cc20f6cb5 100644
--- a/LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj
+++ b/LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj
@@ -1,26 +1,40 @@
 <Project Sdk="Microsoft.NET.Sdk">
   <PropertyGroup>
-    <TargetFramework>net10.0</TargetFramework>
+    <TargetFrameworks>net48;net10.0</TargetFrameworks>
+    <LangVersion>latest</LangVersion>
     <ImplicitUsings>enable</ImplicitUsings>
     <Nullable>enable</Nullable>
     <IsPackable>false</IsPackable>
     <IsTestProject>true</IsTestProject>
-    <NoWarn>NU1605</NoWarn>
+    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
+    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
   </PropertyGroup>
 
   <ItemGroup>
-    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
-    <PackageReference Include="xunit" Version="2.6.6" />
-    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    <PackageReference Include="FluentAssertions" Version="6.12.0" />
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
+    <PackageReference Include="FluentAssertions" />
+    <PackageReference Include="Newtonsoft.Json" />
+    <PackageReference Include="coverlet.collector" PrivateAssets="all" />
   </ItemGroup>
 
-  <ItemGroup>
+  <!-- 舊 SDK 與處理器在階段一才支援 net48；保留既有四類共 33 案例在 net10 執行，
+       不讓 net48 參考 net10 專案，也不修改、刪除或重命名任何既有測試。 -->
+  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
     <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
     <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />
   </ItemGroup>
+  <ItemGroup>
+    <ProjectReference Include="../LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj" />
+    <None Update="Golden/push-text.json" CopyToOutputDirectory="PreserveNewest" />
+  </ItemGroup>
+  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
+    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
+    <Reference Include="System.Net.Http" />
+    <Compile Remove="LineMessagingProcessorSendMessageTests.cs;LineMessagingProcessorReliableNotificationTests.cs;LineMessagingProcessorIdentityProfileTests.cs;LineMessagingProcessorGroupRoomProfileTests.cs" />
+  </ItemGroup>
 </Project>

diff --git a/LineMessagingProcessor.Tests/LinePushClientTests.cs b/LineMessagingProcessor.Tests/LinePushClientTests.cs
new file mode 100644
index 000000000..35c743375
--- /dev/null
+++ b/LineMessagingProcessor.Tests/LinePushClientTests.cs
@@ -0,0 +1,517 @@
+using System.Collections.Concurrent;
+using System.Globalization;
+using System.Net;
+using System.Net.Http;
+using System.Reflection;
+using System.Text;
+using LineMessagingProcessor.Core;
+using Newtonsoft.Json;
+using Newtonsoft.Json.Linq;
+using Xunit;
+
+namespace LineMessagingProcessor.Tests;
+
+/// <summary>
+/// 以完全離線的 HTTP 替身保護雙目標單人推播契約；故障只在記憶體注入。
+/// 所有資料均為合成值；測試擁有 client、handler、取消來源與閘門，結束必須排空並 Dispose。
+/// 原列十一個方法完整保留，額外驗證原始例外、caller 取消、不可變快照及資源釋放。
+/// </summary>
+public sealed class LinePushClientTests
+{
+    /// <summary>固定合成收件 ID，不代表任何真實會友，也不作為授權來源。</summary>
+    private const string UserId = "[REDACTED_LINE_ID]";
+    /// <summary>只用於離線標頭比對的合成值，不是有效頻道憑證。</summary>
+    private const string Token = "synthetic-channel-secret";
+    /// <summary>固定業務事件鍵，保證黃金 JSON／標頭不依賴亂數或時間。</summary>
+    private static readonly Guid RetryKey = new("11111111-2222-3333-4444-555555555555");
+
+    /// <summary>成功回應注入固定 ID；斷言 URL、方法、兩個逐請求標頭及共用標頭沒有被污染。</summary>
+    [Fact(DisplayName = "成功推播使用當次 Bearer 與 Retry Key")]
+    public async Task Push_Success_SendsBearerAndRetryKeyHeaders()
+    {
+        using var fixture = new Fixture();
+        var result = await fixture.Client.PushAsync(new(Token), Request(), default);
+        Assert.True(result.Success);
+        Assert.Equal(LinePushOutcome.Success, result.Outcome);
+        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
+        Assert.Equal("synthetic-request-id", result.RequestId);
+        Assert.Equal("POST", fixture.Handler.Method);
+        Assert.Equal("https://api.line.me/v2/bot/message/push", fixture.Handler.Url);
+        Assert.Equal("Bearer " + Token, fixture.Handler.Authorization);
+        Assert.Equal(RetryKey.ToString("D"), fixture.Handler.RetryHeader);
+        Assert.Null(fixture.Http.DefaultRequestHeaders.Authorization);
+        Assert.False(fixture.Http.DefaultRequestHeaders.Contains("X-Line-Retry-Key"));
+    }
+
+    /// <summary>注入同鍵已接受回應；必須成功且明確分類，不能要求換鍵再送。</summary>
+    [Fact(DisplayName = "409 視為相同事件已接受")]
+    public async Task Push_409_TreatedAsAlreadyAccepted()
+    {
+        using var fixture = new Fixture(409);
+        var result = await fixture.Client.PushAsync(new(Token), Request(), default);
+        Assert.True(result.Success);
+        Assert.Equal(LinePushOutcome.AlreadyAccepted, result.Outcome);
+        Assert.Equal((HttpStatusCode)409, result.StatusCode);
+    }
+
+    /// <summary>以節流狀態注入可恢復失敗；必須只回傳可重試結果，不在 Core 重送。</summary>
+    [Fact(DisplayName = "429 可重試且一次只送一次")]
+    public async Task Push_429_IsRetryable() => await AssertStatus(429, LinePushOutcome.Retryable);
+
+    /// <summary>伺服器故障以狀態碼分類，不製造不存在的傳輸例外。</summary>
+    [Fact(DisplayName = "500 分類為可重試")]
+    public async Task Push_500_IsRetryable() => await AssertStatus(500, LinePushOutcome.Retryable);
+
+    /// <summary>
+    /// 手動取消 Core 所有的逾時來源並拋出原始 TaskCanceledException；
+    /// 斷言期限為十秒、保留 TaskCanceledException 型別、無狀態碼，回傳前來源與連結 token 均已釋放。
+    /// Framework 的取消 Task 會在 HttpClient 邊界正規化例外，不能把 handler 內部實例誤當 Core 收到的實例。
+    /// </summary>
+    [Fact(DisplayName = "內部十秒逾時保留例外並釋放取消來源")]
+    public async Task Push_Timeout_IsRetryableAndDisposesCts()
+    {
+        using var timeout = new CancellationTokenSource();
+        var original = new TaskCanceledException("Synthetic timeout.");
+        using var fixture = new Fixture();
+        fixture.Handler.Reply = (_, _) => { timeout.Cancel(); throw original; };
+        TimeSpan? observed = null;
+        var client = new LinePushClient(fixture.Http, duration => { observed = duration; return timeout; });
+        var result = await client.PushAsync(new(Token), Request(), default);
+        Assert.Equal(TimeSpan.FromSeconds(10), observed);
+        Assert.Equal(LinePushOutcome.Retryable, result.Outcome);
+        Assert.IsType<TaskCanceledException>(result.TransportException);
+        Assert.Null(result.StatusCode);
+        Assert.Null(result.RequestId);
+        Assert.Throws<ObjectDisposedException>(() => timeout.Cancel());
+        Assert.Throws<ObjectDisposedException>(() => _ = fixture.Handler.SentToken.WaitHandle);
+    }
+
+    /// <summary>錯誤憑證收到 401，必須永久失敗且 Core 不盲目重試。</summary>
+    [Fact(DisplayName = "401 屬永久失敗")]
+    public async Task Push_401_IsPermanent() => await AssertStatus(401, LinePushOutcome.Permanent);
+
+    /// <summary>注入格式錯誤、大小寫錯誤與尾端換行；必須在送 HTTP 之前拒絕且訊息不回顯輸入。</summary>
+    [Theory(DisplayName = "非法單人 ID 在 HTTP 前拒絕")]
+    [InlineData(null)]
+    [InlineData("")]
+    [InlineData("Ushort")]
+    [InlineData("G00000000000000000000000000000000")]
+    [InlineData("[REDACTED_LINE_ID]")]
+    [InlineData("[REDACTED_LINE_ID]\n")]
+    [InlineData("[REDACTED_LINE_ID] ")]
+    public void Push_InvalidUserId_ThrowsBeforeHttp(string? invalid)
+    {
+        using var fixture = new Fixture();
+        var error = Assert.Throws<ArgumentException>(() => new LinePushRequest(invalid!, new[] { "hello" }, RetryKey));
+        Assert.Equal("userId", error.ParamName);
+        Assert.DoesNotContain("Ushort", error.Message, StringComparison.Ordinal);
+        Assert.Equal(0, fixture.Handler.Calls);
+    }
+
+    /// <summary>以真實請求快照與反射驗證公開面只有單人 ID，沒有多播／廣播方法或多人收件型別。</summary>
+    [Fact(DisplayName = "公開能力無法表示多人推播")]
+    public void Push_MoreThanOneRecipientNotPossible()
+    {
+        var request = Request();
+        Assert.Equal(UserId, request.UserId);
+        Assert.Equal(typeof(string), typeof(LinePushRequest).GetProperty(nameof(LinePushRequest.UserId))!.PropertyType);
+        Assert.Single(typeof(ILinePushClient).GetMethods());
+        Assert.Equal("PushAsync", typeof(ILinePushClient).GetMethods()[0].Name);
+        Assert.DoesNotContain(typeof(LinePushClient).GetMethods(), method =>
+            method.Name.IndexOf("multicast", StringComparison.OrdinalIgnoreCase) >= 0 ||
+            method.Name.IndexOf("broadcast", StringComparison.OrdinalIgnoreCase) >= 0);
+    }
+
+    /// <summary>插值及一般診斷只顯示固定遮罩；精確斷言避免把 token 藏在摘要尾端。</summary>
+    [Fact(DisplayName = "憑證字串永遠只回傳固定遮罩")]
+    public void Credential_ToString_NeverContainsToken()
+        => Assert.Equal("LineChannelCredential(***)", new LineChannelCredential(Token).ToString());
+
+    /// <summary>以檔案黃金資料比較實際傳輸 JSON；只允許單一 to 與 text 訊息，不含重試鍵或 token。</summary>
+    [Fact(DisplayName = "單人文字 JSON 與黃金檔完全一致")]
+    public async Task Push_RequestBody_MatchesGoldenJson()
+    {
+        using var fixture = new Fixture();
+        await fixture.Client.PushAsync(new(Token), Request(), default);
+        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "push-text.json"));
+        Assert.True(JToken.DeepEquals(JToken.Parse(golden), JToken.Parse(fixture.Handler.Body!)));
+        Assert.DoesNotContain(Token, fixture.Handler.Body!, StringComparison.Ordinal);
+        Assert.Equal("application/json; charset=utf-8", fixture.Handler.ContentType);
+    }
+
+    /// <summary>
+    /// 同一 client 同時送五十個不同頻道／事件；閘門確保全部在途，逐請求交叉核對，
+    /// 等待所有工作完成才釋放 owner，斷言活躍數回零、沒有共用標頭或結果混用。
+    /// </summary>
+    [Fact(DisplayName = "五十個並行憑證的標頭及結果不互串")]
+    public async Task Push_ConcurrentDifferentTokens_HeadersNotMixed()
+    {
+        const int count = 50;
+        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
+        var seen = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
+        int entered = 0;
+        using var fixture = new Fixture();
+        fixture.Handler.Reply = async (request, ct) =>
+        {
+            try
+            {
+                var body = JObject.Parse(await request.Content!.ReadAsStringAsync());
+                var message = body["messages"]![0]!["text"]!.Value<string>()!;
+                Assert.Equal("synthetic-" + message, request.Headers.Authorization!.Parameter);
+                Assert.Equal(Guid.Parse(message).ToString("D"), request.Headers.GetValues("X-Line-Retry-Key").Single());
+                Assert.True(seen.TryAdd(message, true));
+                if (Interlocked.Increment(ref entered) == count) gate.TrySetResult(true);
+                await gate.Task;
+                ct.ThrowIfCancellationRequested();
+                return new HttpResponseMessage(HttpStatusCode.OK);
+            }
+            catch (Exception exception)
+            {
+                // 任何斷言失敗也要打開故障閘門，讓其他請求結束，不能留下永遠等待的測試工作。
+                gate.TrySetException(exception);
+                throw;
+            }
+        };
+        var client = fixture.Client;
+        var tasks = Enumerable.Range(1, count).Select(index =>
+        {
+            var key = Guid.Parse(index.ToString("x8", CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000");
+            var text = key.ToString("D");
+            return client.PushAsync(new("synthetic-" + text), new(UserId, new[] { text }, key), default);
+        }).ToArray();
+        try
+        {
+            var results = await Task.WhenAll(tasks);
+            Assert.All(results, result => Assert.True(result.Success));
+            Assert.Equal(count, seen.Count);
+            Assert.Equal(count, fixture.Handler.PeakActive);
+            Assert.Equal(0, fixture.Handler.Active);
+            Assert.Null(fixture.Http.DefaultRequestHeaders.Authorization);
+        }
+        finally { gate.TrySetResult(true); await Task.WhenAll(tasks); }
+    }
+
+    /// <summary>注入已取消或在途 caller token；必須拋取消而非回結果，並釋放操作資源。</summary>
+    [Theory(DisplayName = "caller 取消不誤判可重試故障")]
+    [InlineData(false)]
+    [InlineData(true)]
+    public async Task Push_CallerCanceled_ThrowsOperationCanceled_NotRetryable(bool inFlight)
+    {
+        using var caller = new CancellationTokenSource();
+        using var fixture = new Fixture();
+        if (!inFlight) caller.Cancel();
+        fixture.Handler.Reply = (_, ct) => { caller.Cancel(); throw new OperationCanceledException("Synthetic caller cancellation.", ct); };
+        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Client.PushAsync(new(Token), Request(), caller.Token));
+        Assert.Equal(inFlight ? 1 : 0, fixture.Handler.Calls);
+        Assert.Equal(0, fixture.Handler.Active);
+    }
+
+    /// <summary>caller 與網路故障競爭時，已觀察的 caller 取消仍須以取消退出，不把網路故障當結果或洩漏。</summary>
+    [Fact(DisplayName = "caller 取消優先於同時發生的網路故障")]
+    public async Task Push_CallerCanceledDuringNetworkFailure_ThrowsCancellation()
+    {
+        using var caller = new CancellationTokenSource();
+        using var fixture = new Fixture();
+        fixture.Handler.Reply = (_, _) => { caller.Cancel(); throw new HttpRequestException("Synthetic connection fault."); };
+        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Client.PushAsync(new(Token), Request(), caller.Token));
+    }
+
+    /// <summary>
+    /// 假傳輸先建立完整 HTTP 回應，再於交回回應前取消 caller；驗證已知狀態仍依 2xx／429 分類，
+    /// 並確認 request、response、連結取消來源與 handler 在途計數均已確定性釋放。
+    /// </summary>
+    [Theory(DisplayName = "回應抵達後 caller 取消仍回傳狀態分類")]
+    [InlineData(200, LinePushOutcome.Success)]
+    [InlineData(429, LinePushOutcome.Retryable)]
+    public async Task Push_CallerCanceledAfterResponse_ReturnsClassifiedResult(int status, LinePushOutcome expected)
+    {
+        using var caller = new CancellationTokenSource();
+        using var fixture = new Fixture(status);
+        using var timeout = new CancellationTokenSource();
+        var createResponse = fixture.Handler.Reply;
+        fixture.Handler.Reply = async (request, transportToken) =>
+        {
+            var response = await createResponse(request, transportToken).ConfigureAwait(false);
+            caller.Cancel();
+            return response;
+        };
+        var client = new LinePushClient(fixture.Http, _ => timeout);
+
+        var result = await client.PushAsync(new(Token), Request(), caller.Token);
+
+        Assert.True(caller.IsCancellationRequested);
+        Assert.Equal(expected, result.Outcome);
+        Assert.Equal((HttpStatusCode)status, result.StatusCode);
+        Assert.Null(result.TransportException);
+        Assert.True(fixture.Handler.ResponseContent!.Disposed);
+        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Handler.RequestContent!.ReadAsStringAsync());
+        Assert.Equal(0, fixture.Handler.Active);
+        Assert.Throws<ObjectDisposedException>(() => timeout.Cancel());
+        Assert.Throws<ObjectDisposedException>(() => _ = fixture.Handler.SentToken.WaitHandle);
+    }
+
+    /// <summary>
+    /// 注入 caller 與 Core 逾時來源都未取消的 TaskCanceledException；驗證這不是正常 caller 取消，
+    /// 必須成為 Retryable，TransportException 保留傳輸邊界實際收到的取消例外型別，且 handler 排空。
+    /// </summary>
+    [Fact(DisplayName = "非 caller 取消視為可重試傳輸失敗")]
+    public async Task Push_NonCallerCancellation_IsRetryableAndKeepsOriginalException()
+    {
+        var original = new TaskCanceledException("Synthetic transport cancellation.");
+        using var fixture = new Fixture();
+        using var caller = new CancellationTokenSource();
+        using var timeout = new CancellationTokenSource();
+        fixture.Handler.Reply = (_, _) => throw original;
+        var client = new LinePushClient(fixture.Http, _ => timeout);
+
+        var result = await client.PushAsync(new(Token), Request(), caller.Token);
+
+        Assert.False(caller.IsCancellationRequested);
+        Assert.False(timeout.IsCancellationRequested);
+        Assert.Equal(LinePushOutcome.Retryable, result.Outcome);
+        Assert.NotNull(result.TransportException);
+        Assert.IsType<TaskCanceledException>(result.TransportException);
+        Assert.Null(result.StatusCode);
+        Assert.Null(result.RequestId);
+        Assert.Equal(0, fixture.Handler.Active);
+        Assert.Throws<ObjectDisposedException>(() => timeout.Cancel());
+    }
+
+    /// <summary>模擬 DNS／連線類故障；傳回原始例外同一實例，不能轉文字、包裝或虛構 HTTP 狀態。</summary>
+    [Fact(DisplayName = "網路失敗保留原始傳輸例外")]
+    public async Task Push_NetworkFailure_IsRetryableAndKeepsOriginalException()
+    {
+        var original = new HttpRequestException("Synthetic network unavailable.");
+        using var fixture = new Fixture();
+        fixture.Handler.Reply = (_, _) => throw original;
+        var result = await fixture.Client.PushAsync(new(Token), Request(), default);
+        Assert.Equal(LinePushOutcome.Retryable, result.Outcome);
+        Assert.False(result.Success);
+        Assert.Same(original, result.TransportException);
+        Assert.Null(result.StatusCode);
+        Assert.Null(result.RequestId);
+    }
+
+    /// <summary>已有 HTTP 回應的各分類都不產生傳輸例外；2xx 邊界、4xx 與 5xx 使用相同規則。</summary>
+    [Theory(DisplayName = "HTTP 結果不攜帶傳輸例外")]
+    [InlineData(201, LinePushOutcome.Success)]
+    [InlineData(204, LinePushOutcome.Success)]
+    [InlineData(299, LinePushOutcome.Success)]
+    [InlineData(400, LinePushOutcome.Permanent)]
+    [InlineData(401, LinePushOutcome.Permanent)]
+    [InlineData(403, LinePushOutcome.Permanent)]
+    [InlineData(404, LinePushOutcome.Permanent)]
+    [InlineData(409, LinePushOutcome.AlreadyAccepted)]
+    [InlineData(429, LinePushOutcome.Retryable)]
+    [InlineData(502, LinePushOutcome.Retryable)]
+    [InlineData(599, LinePushOutcome.Retryable)]
+    public async Task Push_HttpStatusFailure_HasNoTransportException(int status, LinePushOutcome expected)
+        => await AssertStatus(status, expected);
+
+    /// <summary>
+    /// 注入安全固定訊息的原始傳輸例外，檢查 Core 不把當次敏感輸入加入結果／例外／摘要。
+    /// 原始例外不得改寫；這項測試保護 Core 自產診斷，不宣稱能清洗外部自訂例外的內容。
+    /// </summary>
+    [Fact(DisplayName = "傳輸故障診斷不加入憑證內文或收件 ID")]
+    public async Task Push_FailureDetails_NeverContainTokenOrBody()
+    {
+        const string body = "synthetic-private-body";
+        using var fixture = new Fixture();
+        var original = new HttpRequestException("Synthetic transport fault.");
+        fixture.Handler.Reply = (_, _) => throw original;
+        var result = await fixture.Client.PushAsync(new(Token), new(UserId, new[] { body }, RetryKey), default);
+        Assert.Same(original, result.TransportException);
+        var diagnostic = result + " " + result.TransportException!.Message + " " + result.TransportException;
+        foreach (var forbidden in new[] { Token, body, UserId })
+            Assert.DoesNotContain(forbidden, diagnostic + JsonConvert.SerializeObject(result), StringComparison.Ordinal);
+    }
+
+    /// <summary>程式錯誤不被 catch-all 轉成可重試；例外同一實例直接往最終邊界傳遞。</summary>
+    [Fact(DisplayName = "非預期例外直接往外拋")]
+    public async Task Push_UnexpectedException_IsNotConverted()
+    {
+        var original = new InvalidOperationException("Synthetic programming fault.");
+        using var fixture = new Fixture();
+        fixture.Handler.Reply = (_, _) => throw original;
+        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Client.PushAsync(new(Token), Request(), default)));
+        Assert.Equal(0, fixture.Handler.Active);
+    }
+
+    /// <summary>成功與失敗回應在結果發布前釋放；request 內容也已關閉，caller 的 HttpClient 仍可重用。</summary>
+    [Theory(DisplayName = "回傳前釋放 HTTP 訊息且不接管外部客戶端")]
+    [InlineData(200)]
+    [InlineData(500)]
+    public async Task Push_ResultDoesNotRetainHttpResources(int status)
+    {
+        using var fixture = new Fixture(status);
+        var result = await fixture.Client.PushAsync(new(Token), Request(), default);
+        Assert.True(fixture.Handler.ResponseContent!.Disposed);
+        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Handler.RequestContent!.ReadAsStringAsync());
+        var fields = typeof(LinePushResult).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
+        Assert.DoesNotContain(fields, field => field.FieldType == typeof(HttpRequestMessage) ||
+            field.FieldType == typeof(HttpResponseMessage) || field.FieldType == typeof(LineChannelCredential));
+        await fixture.Client.PushAsync(new(Token), Request(), default);
+        Assert.Equal(2, fixture.Handler.Calls);
+        Assert.NotEqual(LinePushOutcome.Rejected, result.Outcome);
+    }
+
+    /// <summary>外部清單修改不能改變快照；每則五千與五則上限都可送出，任何 setter 必須拒絕。</summary>
+    [Fact(DisplayName = "訊息集合完整複製並唯讀發布")]
+    public async Task Request_MessagesAreDetachedAndBounded()
+    {
+        var source = new[] { new string('x', 5000), "b", "c", "d", "e" };
+        var request = new LinePushRequest(UserId, source, RetryKey);
+        source[0] = "mutated";
+        Assert.Equal(5000, request.Messages[0].Length);
+        Assert.Throws<NotSupportedException>(() => ((IList<string>)request.Messages)[0] = "mutated");
+        using var fixture = new Fixture();
+        Assert.True((await fixture.Client.PushAsync(new(Token), request, default)).Success);
+    }
+
+    /// <summary>零則、六則、空文字、過長文字、空事件鍵與 null 都在 HTTP 前拒絕，不輸出輸入值。</summary>
+    [Fact(DisplayName = "不合法文字數量長度或事件鍵在傳輸前拒絕")]
+    public void Request_InvalidMessagesOrRetryKey_ThrowsBeforeHttp()
+    {
+        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, Array.Empty<string>(), RetryKey));
+        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, new[] { "a", "b", "c", "d", "e", "f" }, RetryKey));
+        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, new[] { "" }, RetryKey));
+        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, new[] { new string('x', 5001) }, RetryKey));
+        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, new string[] { null! }, RetryKey));
+        Assert.Throws<ArgumentNullException>(() => new LinePushRequest(UserId, null!, RetryKey));
+        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, new[] { "a" }, Guid.Empty));
+    }
+
+    /// <summary>標頭注入或空憑證在建構時拒絕；所有錯誤不應附帶 token 本身。</summary>
+    [Theory(DisplayName = "無效憑證不進入 HTTP 標頭解析器")]
+    [InlineData(null)]
+    [InlineData("")]
+    [InlineData("synthetic secret")]
+    [InlineData("synthetic\r\nsecret")]
+    public void Credential_InvalidToken_RejectsWithoutEcho(string? invalid)
+    {
+        var error = Assert.Throws<ArgumentException>(() => new LineChannelCredential(invalid!));
+        Assert.DoesNotContain("synthetic", error.Message, StringComparison.Ordinal);
+    }
+
+    /// <summary>建立固定可重現的單人雙訊息快照，黃金檔與送訊斷言共用同一資料。</summary>
+    private static LinePushRequest Request() => new(UserId, new[] { "hello", "quoted \"text\"" }, RetryKey);
+
+    /// <summary>對實際 HTTP 分類結果斷言，不能只驗證替身自己設定的狀態。</summary>
+    private static async Task AssertStatus(int status, LinePushOutcome expected)
+    {
+        using var fixture = new Fixture(status);
+        var result = await fixture.Client.PushAsync(new(Token), Request(), default);
+        Assert.Equal(expected, result.Outcome);
+        Assert.Equal((HttpStatusCode)status, result.StatusCode);
+        Assert.Null(result.TransportException);
+        Assert.Equal(expected is LinePushOutcome.Success or LinePushOutcome.AlreadyAccepted, result.Success);
+        Assert.Equal(1, fixture.Handler.Calls);
+    }
+
+    /// <summary>案例專屬 owner；只建立離線 handler，禁止落到預設網路管線，結束確認所有傳輸排空。</summary>
+    private sealed class Fixture : IDisposable
+    {
+        /// <summary>獨佔的離線傳輸替身，不跨測試共用可變診斷。</summary>
+        public Handler Handler { get; }
+        /// <summary>案例擁有的客戶端；Core 只借用，不修改全域狀態或釋放此 owner。</summary>
+        public HttpClient Http { get; }
+        /// <summary>使用公開或友元建構路徑，兩目標的行為及測試主體完全相同。</summary>
+        public LinePushClient Client => new(Http);
+        /// <summary>配置固定回應狀態，不接觸系統時鐘、使用者設定或外部端點。</summary>
+        public Fixture(int status = 200) { Handler = new Handler(status); Http = new HttpClient(Handler) { Timeout = Timeout.InfiniteTimeSpan }; }
+        /// <summary>測試結束先驗證沒有在途操作，再由 client 釋放 handler，清除有限診斷參考。</summary>
+        public void Dispose() { try { Assert.Equal(0, Handler.Active); } finally { Http.Dispose(); } }
+    }
+
+    /// <summary>可注入單次故障的 handler；診斷只保留最近一次合成輸入，owner Dispose 時清空。</summary>
+    private sealed class Handler : HttpMessageHandler
+    {
+        /// <summary>原子在途數，驗證完成或 fault 都經 finally 排空。</summary>
+        private int _active;
+        /// <summary>歷史峰值，五十案例閘門驗證真正重疊而不是同步逐一執行。</summary>
+        private int _peak;
+        /// <summary>送出次數供拒絕輸入／無內部重試的決定性斷言。</summary>
+        public int Calls;
+        /// <summary>目前在途數，不應於 owner 釋放時殘留。</summary>
+        public int Active => Volatile.Read(ref _active);
+        /// <summary>最大在途數，只是合成測試計數。</summary>
+        public int PeakActive => Volatile.Read(ref _peak);
+        /// <summary>當次故障注入，回傳回應由 Core 擁有且負責釋放。</summary>
+        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply { get; set; }
+        /// <summary>最近傳輸的連結 token，僅用於操作結束後的 Dispose 斷言。</summary>
+        public CancellationToken SentToken { get; private set; }
+        /// <summary>最近的合成 JSON 快照，不持有完整 request。</summary>
+        public string? Body { get; private set; }
+        /// <summary>最近合成標頭，並行驗證另直接讀每個獨立 request。</summary>
+        public string? Authorization { get; private set; }
+        /// <summary>最近重試鍵，沒有真實業務資料。</summary>
+        public string? RetryHeader { get; private set; }
+        /// <summary>最近固定 URL，用於防止端點被輸入控制。</summary>
+        public string? Url { get; private set; }
+        /// <summary>最近 HTTP 方法。</summary>
+        public string? Method { get; private set; }
+        /// <summary>最近 JSON content type，確認傳輸編碼。</summary>
+        public string? ContentType { get; private set; }
+        /// <summary>只為斷言釋放而暫留的 request content，Dispose 後清除參考。</summary>
+        public HttpContent? RequestContent { get; private set; }
+        /// <summary>只為斷言釋放而暫留的 response content，Dispose 後清除參考。</summary>
+        public TrackedContent? ResponseContent { get; private set; }
+        /// <summary>建立固定回應，不做任何外部連線或額外背景工作。</summary>
+        public Handler(int status)
+        {
+            Reply = (_, _) =>
+            {
+                ResponseContent = new TrackedContent();
+                var response = new HttpResponseMessage((HttpStatusCode)status) { Content = ResponseContent };
+                response.Headers.Add("x-line-request-id", "synthetic-request-id");
+                return Task.FromResult(response);
+            };
+        }
+        /// <summary>擷取傳輸資料後執行注入行為；任何取消／例外都在 finally 歸還活躍計數。</summary>
+        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
+        {
+            Interlocked.Increment(ref Calls);
+            var active = Interlocked.Increment(ref _active);
+            int previous;
+            do { previous = Volatile.Read(ref _peak); if (previous >= active) break; }
+            while (Interlocked.CompareExchange(ref _peak, active, previous) != previous);
+            try
+            {
+                SentToken = cancellationToken;
+                RequestContent = request.Content;
+                Body = await request.Content!.ReadAsStringAsync();
+                Authorization = request.Headers.Authorization?.ToString();
+                RetryHeader = request.Headers.GetValues("X-Line-Retry-Key").Single();
+                Url = request.RequestUri!.AbsoluteUri;
+                Method = request.Method.Method;
+                ContentType = request.Content.Headers.ContentType?.ToString();
+                return await Reply(request, cancellationToken);
+            }
+            finally { Interlocked.Decrement(ref _active); }
+        }
+        /// <summary>外部 owner 清理時丟棄所有合成輸入參考，避免 fixture 長期留住憑證。</summary>
+        protected override void Dispose(bool disposing)
+        {
+            if (disposing)
+            {
+                RequestContent?.Dispose();
+                ResponseContent?.Dispose();
+                Body = Authorization = RetryHeader = Url = Method = ContentType = null;
+                RequestContent = null;
+                ResponseContent = null;
+            }
+            base.Dispose(disposing);
+        }
+    }
+
+    /// <summary>以記憶體內容追蹤回應 Dispose；若 Core 忘記釋放，即使 GC 最後回收也會測試失敗。</summary>
+    private sealed class TrackedContent : StringContent
+    {
+        /// <summary>明確 Dispose 的決定性證據，不依賴 GC 或 Finalizer。</summary>
+        public bool Disposed { get; private set; }
+        /// <summary>內容故意含合成敏感文字，Core 不應讀取或把它加入錯誤結果。</summary>
+        public TrackedContent() : base("synthetic-response-body", Encoding.UTF8, "application/json") { }
+        /// <summary>先記錄明確釋放，再交由基底釋放記憶體串流。</summary>
+        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
+    }
+}
```
