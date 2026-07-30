# Gateway JSON-only Content-Type 邊界分析報告

本分析報告針對 `SpeechMessage.Dynamics.Gateway` 的 operation endpoint 進行 JSON-only Content-Type 邊界分析，並提出最小且安全的修正方案。

---

## 1. 建議的媒體型別與 charset 契約

### 媒體型別 (Media Type) 契約
- **必須接受**:
  - 標準的 `application/json`。
  - 大小寫無關的媒體型別（例如 `Application/Json`、`APPLICATION/JSON`）。
  - 帶有參數的媒體型別，例如 `application/json; charset=utf-8`。
  - 符合 `application/*+json` 結構的媒體型別（例如 `application/problem+json`），以支援標準的 JSON 衍生格式。
- **必須拒絕**:
  - 缺少 `Content-Type` 標頭的請求。
  - 非 JSON 媒體型別（例如 `text/plain`、`application/xml`、`application/x-www-form-urlencoded`）。
  - 拒絕時應回傳 **HTTP 415 Unsupported Media Type**。

### 字元集 (Charset) 契約
- 根據 RFC 8259，JSON 的預設且唯一編碼為 UTF-8。
- 若 `Content-Type` 中指定了 `charset` 參數，其值必須為 `utf-8`（大小寫無關，例如 `utf-8`、`UTF-8`）。
- 若指定了其他不支援的字元集（例如 `utf-16`、`ascii`、`iso-8859-1`），應拒絕並回傳 **HTTP 415**。
- 若未指定 `charset` 參數，則預設為 UTF-8，應予以接受。

---

## 2. 最小實作位置與理由

### 實作位置
- 在 `SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationRequestBodyReader.cs` 的 `ReadAsync` 方法最開頭進行 Content-Type 驗證。
- 在 `GatewayOperationRequestBodyReadStatus` 中新增 `UnsupportedMediaType` 狀態。
- 在 `SpeechMessage.Dynamics.Gateway/Program.cs` 的 `app.MapPost` 路由處理器中，將 `UnsupportedMediaType` 狀態對應至 `Results.StatusCode(StatusCodes.Status415UnsupportedMediaType)`。

### 理由
1. **維持安全驗證順序 (Ordering)**:
   - `Program.cs` 中，`bodyReader.ReadAsync` 僅在 `operationAuthorizer.Authorize` 授權成功後才會被呼叫。
   - 將 Content-Type 檢查置於 `ReadAsync` 的最開頭，可確保未授權的 caller 在觸發媒體型別檢查或 body 讀取之前，就已被 401/403 拒絕。這完美維持了「Authentication 與 authorization 優先於 body read 與媒體型別解析」的契約，防止未授權 caller 探測 body 契約。
2. **資源保護與 Fail-Closed**:
   - 在讀取 request body stream 之前先驗證 Content-Type，可以避免為不合法的請求分配 `ArrayPool<byte>` 緩衝區或進行 stream I/O 讀取，從而保護伺服器資源。
3. **效能優化 (Fast-path)**:
   - 針對最常見的標準 `application/json` 進行快速路徑檢查（不區分大小寫且無參數時直接通過），避免每次請求都呼叫 `MediaTypeHeaderValue.TryParse` 進行解析，從而減少 CPU 與記憶體分配開銷。

---

## 3. 必須先建立的 RED 測試案例與 Assertion

我們需要在 `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs` 中新增以下測試案例：

1. **`Request_with_invalid_or_missing_content_type_is_rejected_with_415`**:
   - **情境**: 傳送缺少 `Content-Type`、使用 `text/plain`、`application/xml` 或不支援的字元集（如 `charset=utf-16`）的請求。
   - **Assertion**: 驗證回傳 `415 Unsupported Media Type`，且 `body.ReadCount` 必須為 0（代表完全沒有讀取 request stream），且 `executor.CallCount` 為 0。
2. **`Request_with_valid_json_content_type_is_accepted`**:
   - **情境**: 傳送 `application/json`、`APPLICATION/JSON`、`application/json; charset=utf-8` 或 `application/problem+json`。
   - **Assertion**: 驗證回傳 `200 OK`，且 `body.ReadCount` 大於 0，且 `executor.CallCount` 為 1。
3. **`Unauthorized_request_with_invalid_content_type_returns_403_before_415`**:
   - **情境**: 傳送未授權的請求，且 `Content-Type` 為錯誤的 `text/plain`。
   - **Assertion**: 驗證回傳 `403 Forbidden`，而非 `415`。這確保了授權驗證優先於媒體型別檢查。

---

## 4. 風險檢查 (Risk Checklist)

- **Authentication/Authorization Ordering**:
  - 檢查已置於 `bodyReader.ReadAsync` 內部，該方法在 `Program.cs` 中位於 `Authorize` 之後。401/403 順序不會退化。
- **資源 Owner**:
  - Content-Type 檢查為純 CPU 邏輯，不持有或新增任何未界定的 stream、buffer、timer、subscription、cache 或 background work。
  - 檢查失敗時直接回傳，不進行 `ArrayPool<byte>.Rent`，無緩衝區洩漏風險。
- **取消 (Cancellation)**:
  - 檢查為同步操作，不影響 `CancellationToken` 的傳遞與取消行為。
- **記憶體與效能**:
  - 引入快速路徑優化，標準 `application/json` 請求無額外解析開銷。慢速路徑使用 ASP.NET Core 內建的高效 `MediaTypeHeaderValue` 解析器，避免自訂分割字串帶來的效能與安全風險。
- **資訊洩漏**:
  - 415 錯誤僅回傳狀態碼，不回顯任何 request body、credential、principal、token 或 session 資料。

---

## 5. 結論與分級 (Findings & Classification)

### Critical
- **Content-Type 邊界缺失**: 目前 Gateway 接受 `text/plain` 等非 JSON 媒體型別，這違反了 JSON-only 的對外契約，且可能被惡意 caller 用於探測 body 契約。必須修正以落實安全硬邊界。
- **驗證順序**: 必須確保 Content-Type 檢查在授權驗證之後執行，否則未授權的 caller 可以透過 Content-Type 探測授權狀態，導致安全邊界洩漏。

### Warning
- **解析效能開銷**: 若對每個請求都進行完整的 `MediaTypeHeaderValue` 解析，在高併發下會產生不必要的字串分配與 GC 壓力。必須實作快速路徑 (Fast-path) 優化。

### Info
- **繁體中文註解**: 新增的程式碼與測試案例必須包含完整、深入的繁體中文 XML 註解，說明信任邊界、記憶體與效能取捨，以符合專案規範。

---

## 6. 實作 Unified Diff Patch

以下為建議的實作修改：

```diff
--- a/SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationRequestBodyReader.cs
+++ b/SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationRequestBodyReader.cs
@@ -4,6 +4,7 @@
 using System.Text.Json;
 using Microsoft.AspNetCore.Http;
 using Microsoft.Extensions.Options;
+using Microsoft.Net.Http.Headers;
 
 namespace SpeechMessage.Dynamics.Gateway.RequestLimits;
 
@@ -20,6 +21,9 @@
     PayloadTooLarge = 1,
 
     /// <summary>JSON ?⊥???瘛晞€oot shape ?航炊?nknown member ??duplicate property嚗ndpoint ???喳???400??/summary>
-    InvalidJson = 2
+    InvalidJson = 2,
+
+    /// <summary>Content-Type 缺失、非 JSON 媒體類型或不支援的字元集，endpoint 回傳 415</summary>
+    UnsupportedMediaType = 3
 }
 
 /// <summary>
@@ -51,6 +55,10 @@
     /// <summary>撱箇?銝 parser exception??雿??典? DTO ????400 蝯???/summary>
     public static GatewayOperationRequestBodyReadResult InvalidJson(int wireByteCount)
         => new(GatewayOperationRequestBodyReadStatus.InvalidJson, null, wireByteCount);
+
+    /// <summary>建立 Content-Type 不支援或缺失的 415 結果</summary>
+    public static GatewayOperationRequestBodyReadResult UnsupportedMediaType()
+        => new(GatewayOperationRequestBodyReadStatus.UnsupportedMediaType, null, 0);
 }
 
 /// <summary>
@@ -118,6 +126,38 @@
         ArgumentNullException.ThrowIfNull(request);
 
+        // 驗證 Content-Type 邊界契約 (JSON-only)
+        // 信任邊界：此檢查在授權驗證之後執行，防止未授權 caller 探測 body 契約。
+        // 效能取捨：使用快速路徑 (Fast-path) 檢查最常見的 "application/json"，避免解析開銷。
+        var contentType = request.ContentType;
+        if (string.IsNullOrWhiteSpace(contentType))
+        {
+            return GatewayOperationRequestBodyReadResult.UnsupportedMediaType();
+        }
+
+        var isApplicationJson = string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase);
+        if (!isApplicationJson)
+        {
+            if (!MediaTypeHeaderValue.TryParse(contentType, out var parsedMediaType))
+            {
+                return GatewayOperationRequestBodyReadResult.UnsupportedMediaType();
+            }
+
+            // 媒體型別必須為 application/json 或以 +json 結尾
+            var mediaType = parsedMediaType.MediaType.Value;
+            var isValidJsonMediaType = string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
+                                       (mediaType is not null && mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
+
+            if (!isValidJsonMediaType)
+            {
+                return GatewayOperationRequestBodyReadResult.UnsupportedMediaType();
+            }
+
+            // 字元集 (Charset) 契約：若有指定則必須為 utf-8
+            var charset = parsedMediaType.Charset.Value;
+            if (charset is not null && !string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase))
+            {
+                return GatewayOperationRequestBodyReadResult.UnsupportedMediaType();
+            }
+        }
+
         var declaredLength = request.ContentLength;
         if (declaredLength is < 0)
         {
--- a/SpeechMessage.Dynamics.Gateway/Program.cs
+++ b/SpeechMessage.Dynamics.Gateway/Program.cs
@@ -236,6 +236,11 @@
         if (bodyRead.Status == GatewayOperationRequestBodyReadStatus.PayloadTooLarge)
         {
             return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
         }
 
+        if (bodyRead.Status == GatewayOperationRequestBodyReadStatus.UnsupportedMediaType)
+        {
+            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
+        }
+
         if (bodyRead.Status != GatewayOperationRequestBodyReadStatus.Success ||
             bodyRead.Request is null)
         {
--- a/SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs
+++ b/SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs
@@ -309,6 +309,93 @@
         iisMaximum.Should().Be(configuredMaximum);
     }
 
+    /// <summary>
+    /// 驗證當 Content-Type 缺失、非 JSON 媒體類型或使用不支援的字元集時，
+    /// 應回傳 415 Unsupported Media Type，且不得讀取 request stream。
+    /// </summary>
+    [Theory]
+    [InlineData(null)]
+    [InlineData("")]
+    [InlineData("text/plain")]
+    [InlineData("application/xml")]
+    [InlineData("application/json; charset=utf-16")]
+    [InlineData("application/json; charset=ascii")]
+    public async Task Request_with_invalid_or_missing_content_type_is_rejected_with_415(string? contentType)
+    {
+        var executor = new RecordingExecutor();
+        await using var factory = CreateFactory(
+            executor,
+            maxRequestBodyBytes: 64,
+            mapped: true,
+            useKestrel: false);
+        var body = new TrackingReadStream(Encoding.UTF8.GetBytes("{\"parameters\":{}}"));
+
+        var response = await factory.Server.SendAsync(context =>
+        {
+            context.Request.Method = HttpMethod.Post.Method;
+            context.Request.Scheme = "https";
+            context.Request.Host = new HostString("localhost");
+            context.Request.Path = OperationPath;
+            context.Request.ContentType = contentType;
+            context.Request.ContentLength = body.Length;
+            context.Request.Body = body;
+        });
+
+        response.Response.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
+        body.ReadCount.Should().Be(0, "Request stream must not be read for unsupported media types");
+        executor.CallCount.Should().Be(0);
+        body.Dispose();
+    }
+
+    /// <summary>
+    /// 驗證當 Content-Type 為合法的 JSON 媒體類型（包含大小寫差異、參數或符合 +json 格式）時，
+    /// 應能成功處理並解析請求。
+    /// </summary>
+    [Theory]
+    [InlineData("application/json")]
+    [InlineData("APPLICATION/JSON")]
+    [InlineData("application/json; charset=utf-8")]
+    [InlineData("application/json; charset=UTF-8")]
+    [InlineData("application/problem+json")]
+    [InlineData("application/problem+json; charset=utf-8")]
+    public async Task Request_with_valid_json_content_type_is_accepted(string contentType)
+    {
+        var executor = new RecordingExecutor();
+        await using var factory = CreateFactory(
+            executor,
+            maxRequestBodyBytes: 128,
+            mapped: true,
+            useKestrel: false);
+        var body = new TrackingReadStream(Encoding.UTF8.GetBytes("{\"parameters\":{}}"));
+
+        var response = await factory.Server.SendAsync(context =>
+        {
+            context.Request.Method = HttpMethod.Post.Method;
+            context.Request.Scheme = "https";
+            context.Request.Host = new HostString("localhost");
+            context.Request.Path = OperationPath;
+            context.Request.ContentType = contentType;
+            context.Request.ContentLength = body.Length;
+            context.Request.Body = body;
+        });
+
+        response.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
+        body.ReadCount.Should().BeGreaterThan(0);
+        executor.CallCount.Should().Be(1);
+        body.Dispose();
+    }
+
+    /// <summary>
+    /// 驗證未授權的請求即使 Content-Type 錯誤或缺失，也必須先回傳 403 Forbidden，
+    /// 確保授權驗證優先於媒體型別檢查，防止未授權 caller 探測 body 契約。
+    /// </summary>
+    [Fact]
+    public async Task Unauthorized_request_with_invalid_content_type_returns_403_before_415()
+    {
+        var executor = new RecordingExecutor();
+        await using var factory = CreateFactory(
+            executor,
+            maxRequestBodyBytes: 64,
+            mapped: false, // 未授權
+            useKestrel: false);
+        var body = new TrackingReadStream(Encoding.UTF8.GetBytes("{\"parameters\":{}}"));
+
+        var response = await factory.Server.SendAsync(context =>
+        {
+            context.Request.Method = HttpMethod.Post.Method;
+            context.Request.Scheme = "https";
+            context.Request.Host = new HostString("localhost");
+            context.Request.Path = OperationPath;
+            context.Request.ContentType = "text/plain"; // 錯誤的 Content-Type
+            context.Request.ContentLength = body.Length;
+            context.Request.Body = body;
+        });
+
+        response.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
+        body.ReadCount.Should().Be(0);
+        executor.CallCount.Should().Be(0);
+        body.Dispose();
+    }
+
     /// <summary>
     /// 撱箇?? Testing Host?rogram 敹??貊 configured fake scheme嚗? handler ?芸? server-side configuration 撱箇? principal嚗?
     /// 摰敹賜 HTTP identity headers嚗xecutor ?箏銝€獢?閮擃?double嚗estrel 璅∪???Factory ?臭??? listener ??socket cleanup??
```
