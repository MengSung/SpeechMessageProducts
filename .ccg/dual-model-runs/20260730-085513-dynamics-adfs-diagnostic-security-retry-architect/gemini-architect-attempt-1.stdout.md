# Dynamics AD FS 診斷安全切片分析與設計報告

## 1. Analysis (當前架構評估)

### 1.1 憑證持久化漏洞 (Token Persistence Vulnerability)
- **問題點**：`LocalDevAdfsTokenStore.cs` 與其備份檔 `LocalDevAdfsTokenStore.cs.bak` 將 ADFS 的 `access_token` 與 `refresh_token` 以明文 JSON 格式寫入磁碟（通常位於 `Logs/adfs-local-token.json`）。
- **影響**：任何具有伺服器檔案讀取權限的處理程序或惡意軟體皆可竊取此長期有效的 `refresh_token`，違反憑證安全儲存規範。

### 1.2 密碼授權流程風險 (Password Grant Risk)
- **問題點**：`AdfsOAuthTokenProvider` 允許 `AllowLocalDevPasswordGrant` 流程，使用明文帳號密碼進行 `grant_type=password` 請求。
- **影響**：此流程不符合現代無密碼服務工作負載 (Non-password service-workload) 的安全標準，且容易導致開發環境憑證洩漏。

### 1.3 診斷控制器資訊洩漏與 Session 殘留 (Diagnostics Information Disclosure & Session Leakage)
- **問題點**：`DiagnosticsController` 在進行 OAuth 流程時，將 `state` 存入 Session，但在 callback 發生錯誤、狀態不匹配或異常時，未確定性地清除該 `state`。此外，`WriteProbeResultAsync` 會將包含完整授權 URL、Client ID、Body Preview 等敏感資訊的 JSON 寫入磁碟，且 HTTP 回應未設定快取控制。

---

## 2. Architecture Decision (架構決策)

### 2.1 完全移除檔案型 Token 持久化
- **決策**：完全刪除 `LocalDevAdfsTokenStore.cs` 與 `LocalDevAdfsTokenStore.cs.bak`。所有 Token 僅保留於記憶體中（透過 `AdfsOAuthTokenProvider` 的記憶體快取），生命週期與 Process 綁定，並在 `Dispose` 時進行安全抹除。
- **替代方案評估**：曾考慮使用 Windows DPAPI 加密寫檔，但因跨平台（如未來部署至 Linux 容器）與維護成本高而被否決。

### 2.2 限制 Token 來源與流程
- **決策**：廢除 `grant_type=password` 流程。`AdfsOAuthTokenProvider` 僅允許：
  1. **Client Credentials Grant**：使用安全 Secret Resolver 取得的 `client_secret`。
  2. **Direct Token Reference**：直接從 KeyVault 等安全來源解析 Bearer Token。
- **診斷環境**：互動式 `authorization_code` 僅作為一次性連線驗證，不寫入任何持久化介質。

### 2.3 記憶體內一次性診斷探針 (Memory-Only Probe)
- **決策**：保留 `DiagnosticsController` 的 `adfs-callback` 作為 DEBUG-only 探針，但改為**記憶體內即時交換**：
  - 收到 `code` 後，立即向 ADFS 交換 Token。
  - 立即使用該 Token 呼叫 `WhoAmI` API。
  - 呼叫完成後立即丟棄 Token，不進行任何存檔。
  - 廢除 `/diagnostics/adfs-token-probe` 端點（改為直接回傳 404 或 Fail-Closed 提示），因為已無本機檔案可供讀取。

### 2.4 嚴格的 Session 狀態管理與回應淨化
- **決策**：
  - 進入 `adfs-callback` 後，**第一步**必須立即從 Session 中讀取並 `Remove` 該 `state`，確保其為一次性使用 (One-time consumption)。
  - 移除所有診斷 JSON 檔案輸出。
  - 淨化 HTTP 回應：遮罩 `client_id`，移除 `SessionId`，不回顯任何 Token。
  - 強制加上 `Cache-Control: no-store, no-cache, must-revalidate, private` 標頭。

---

## 3. Implementation Plan (實作步驟與 Unified Diff)

### 3.1 實作步驟
1. **刪除檔案**：
   - 刪除 `SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs`
   - 刪除 `SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak`
2. **修改配置定義**：
   - 移除 `DynamicsWebApiOptions` 與 `ProductDynamicsOptions` 中的 `LocalDevTokenStorePath` 與 `AllowLocalDevPasswordGrant` 屬性。
3. **重構 Token Provider**：
   - 移除 `AdfsOAuthTokenProvider` 中所有與 `LocalDevAdfsTokenStore` 相關的載入/儲存邏輯。
   - 移除密碼授權流程代碼。
4. **重構診斷控制器**：
   - 修改 `DiagnosticsController`，移除檔案寫入與 `adfs-token-probe` 端點。
   - 確保 Session State 進入 callback 即被清除。
   - 加入回應淨化與快取控制標頭。

### 3.2 Unified Diff Patch

```diff
diff --git a/SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs b/SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs
--- a/SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs
+++ b/SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs
@@ -162,19 +162,5 @@
-    /// <summary>
-    /// 僅供 local-dev-manifest 使用：允許 ADFS username/password grant。
-    /// </summary>
-    public bool AllowLocalDevPasswordGrant { get; set; }
-
-    /// <summary>
-    /// 用於儲存 refresh token 憑證名稱。
-    /// </summary>
-    public string? RefreshTokenSecretName { get; set; }
-
-    /// <summary>
-    /// local-dev 專用 token store 路徑。
-    /// </summary>
-    public string? LocalDevTokenStorePath { get; set; }
-
     /// <summary>
     /// local-dev authorization_code 流程使用的 OAuth redirect URI。
     /// </summary>
diff --git a/SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs b/SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs
--- a/SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs
+++ b/SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs
@@ -78,15 +78,5 @@
-    /// <summary>
-    /// 於 local-dev 允許使用 ADFS username/password grant。
-    /// </summary>
-    public bool AllowLocalDevPasswordGrant { get; set; }
-
-    /// <summary>
-    /// 用於儲存 refresh-token 憑證名稱。
-    /// </summary>
-    public string? RefreshTokenSecretName { get; set; }
-
-    /// <summary>
-    /// 於 local-dev 使用的 JSON token store 路徑。
-    /// </summary>
-    public string? LocalDevTokenStorePath { get; set; }
-
     /// <summary>
diff --git a/SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs b/SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs
--- a/SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs
+++ b/SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs
@@ -108,13 +108,4 @@
-            // 1.5) local-dev token store 優先於 access_token 快取載入
-            var storePath = ResolveTokenStorePath();
-            if (LocalDevAdfsTokenStore.TryLoad(storePath, out var stored) &&
-                !string.IsNullOrWhiteSpace(stored?.AccessToken) &&
-                stored!.AccessTokenExpiresAtUtc is not null &&
-                DateTimeOffset.UtcNow < stored.AccessTokenExpiresAtUtc.Value.AddSeconds(-60))
-            {
-                _cachedToken = stored.AccessToken;
-                _expiresAt = stored.AccessTokenExpiresAtUtc.Value;
-                return _cachedToken!;
-            }
-
             // 2) 記憶體快取檢查
@@ -167,4 +158,3 @@
             var token = await ReadBoundedTokenResponseAsync(response.Content, cancellationToken).ConfigureAwait(false);
-            TryPersistTokens(token.AccessToken, token.ExpiresInSeconds, token.RefreshToken);
             return new TokenResponse(token.AccessToken, token.ExpiresInSeconds);
         }
@@ -197,14 +187,4 @@
-            // 嘗試使用 refresh_token
-            if (TryResolveRefreshToken(out var refreshToken) && !string.IsNullOrWhiteSpace(refreshToken))
-            {
-                form.Add(new("grant_type", "refresh_token"));
-                form.Add(new("refresh_token", refreshToken!));
-                return form;
-            }
-
-            // 移除 AllowLocalDevPasswordGrant 支援，強制走安全憑證流程
-            throw new InvalidOperationException(
-                "AdfsOAuth has no usable token source. Client Credentials flow is required in production.");
+            form.Add(new("grant_type", "client_credentials"));
+            return form;
         }
 
-        private bool TryResolveRefreshToken(out string? refreshToken)
-        {
-            refreshToken = null;
-            return false;
-        }
-
-        private string? ResolveTokenStorePath() => null;
-
-        private void TryPersistTokens(string accessToken, int expiresInSeconds, string? refreshToken)
-        {
-            // 已移除檔案持久化邏輯
-        }
diff --git a/SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs b/SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs
--- a/SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs
@@ -45,4 +45,5 @@
         [HttpGet("")]
         public IActionResult Index()
         {
+            ApplySecureHeaders();
             return Json(new
             {
                 ServerTime = DateTime.Now,
                 Environment = "DEBUG",
                 User = User.Identity?.Name ?? "Anonymous",
                 IsAuthenticated = User.Identity?.IsAuthenticated ?? false,
                 AvailableEndpoints = new[]
                 {
                     new { Endpoint = "/diagnostics/adfs-authorize", Description = "ADFS 互動式授權" },
                     new { Endpoint = "/diagnostics/adfs-authorize?go=1", Description = "重新導向至 ADFS" }
                 },
-                Note = "jesus ADFS rejects password grant (unsupported_grant_type). Use adfs-authorize first."
+                Note = "Token persistence is disabled. Diagnostics are memory-only."
             });
         }
 
@@ -77,4 +78,5 @@
         [HttpGet("adfs-authorize")]
         public async Task<IActionResult> AdfsAuthorize(string? go = null)
         {
+            ApplySecureHeaders();
             var authority = GetAuthority();
             var resource = GetResource();
-            var clientId = GetClientId();
+            var clientId = MaskClientId(GetClientId());
             var redirectUri = GetRedirectUri();
             var state = Guid.NewGuid().ToString("N");
             HttpContext.Session.SetString(AdfsOAuthStateSessionKey, state);
 
             var authorizeUrl =
                 authority.TrimEnd('/') + "/oauth2/authorize" +
-                "?response_type=code" +
-                "&client_id=" + Uri.EscapeDataString(clientId) +
+                "?response_type=code" +
+                "&client_id=" + Uri.EscapeDataString(GetClientId()) +
                 "&resource=" + Uri.EscapeDataString(resource) +
                 "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
                 "&response_mode=query" +
                 "&state=" + Uri.EscapeDataString(state);
 
             var preview = new Dictionary<string, object?>
             {
                 ["ok"] = false,
                 ["stage"] = "authorize-preview",
                 ["serverTime"] = DateTime.Now.ToString("o"),
                 ["authority"] = authority,
                 ["resource"] = resource,
-                ["clientId"] = clientId,
+                ["clientId"] = clientId,
                 ["redirectUri"] = redirectUri,
-                ["authorizeUrl"] = authorizeUrl,
                 ["nextStep"] = "After ADFS client is registered, open /diagnostics/adfs-authorize?go=1"
             };
 
-            await WriteProbeResultAsync(preview).ConfigureAwait(false);
             var shouldGo =
                 string.Equals(go, "1", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(go, "true", StringComparison.OrdinalIgnoreCase);
 
             if (!shouldGo)
             {
                 return Json(preview);
             }
 
-            Trace.WriteLine("[ADFS-AUTH] redirect to authorize.");
             return Redirect(authorizeUrl);
         }
 
@@ -146,4 +148,6 @@
         [HttpGet("adfs-callback")]
         public async Task<IActionResult> AdfsCallback(string? code, string? state, string? error, string? error_description)
         {
+            ApplySecureHeaders();
+            var expectedState = HttpContext.Session.GetString(AdfsOAuthStateSessionKey);
+            HttpContext.Session.Remove(AdfsOAuthStateSessionKey); // 立即移除，確保一次性使用
+
             var result = new Dictionary<string, object?>
             {
                 ["ok"] = false,
                 ["stage"] = "callback",
                 ["serverTime"] = DateTime.Now.ToString("o")
             };
 
             if (!string.IsNullOrWhiteSpace(error))
             {
                 result["error"] = error;
                 result["errorDescription"] = error_description;
-                await WriteProbeResultAsync(result).ConfigureAwait(false);
                 return Json(result);
             }
 
-            var expectedState = HttpContext.Session.GetString(AdfsOAuthStateSessionKey);
             if (string.IsNullOrWhiteSpace(state) ||
                 string.IsNullOrWhiteSpace(expectedState) ||
                 !string.Equals(state, expectedState, StringComparison.Ordinal))
             {
                 result["error"] = "Invalid or missing OAuth state.";
-                await WriteProbeResultAsync(result).ConfigureAwait(false);
                 return Json(result);
             }
 
             if (string.IsNullOrWhiteSpace(code))
             {
                 result["error"] = "Missing authorization code.";
-                await WriteProbeResultAsync(result).ConfigureAwait(false);
                 return Json(result);
             }
 
             var authority = GetAuthority();
             var resource = GetResource();
             var clientId = GetClientId();
             var redirectUri = GetRedirectUri();
             var tokenUrl = authority.TrimEnd('/') + "/oauth2/token";
 
             try
             {
                 using var http = CreateHttpClient();
                 using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                 {
                     ["client_id"] = clientId,
                     ["grant_type"] = "authorization_code",
                     ["code"] = code,
                     ["redirect_uri"] = redirectUri,
                     ["resource"] = resource
                 });
 
                 using var response = await http.PostAsync(tokenUrl, content).ConfigureAwait(false);
                 var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                 result["tokenHttpStatus"] = (int)response.StatusCode;
 
                 if (!response.IsSuccessStatusCode)
                 {
                     result["error"] = "authorization_code exchange failed HTTP " + (int)response.StatusCode;
-                    await WriteProbeResultAsync(result).ConfigureAwait(false);
                     return Json(result);
                 }
 
                 using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                 var root = doc.RootElement;
                 if (!root.TryGetProperty("access_token", out var accessNode) ||
                     accessNode.ValueKind != JsonValueKind.String ||
                     string.IsNullOrWhiteSpace(accessNode.GetString()))
                 {
                     result["error"] = "Token response missing access_token.";
-                    await WriteProbeResultAsync(result).ConfigureAwait(false);
                     return Json(result);
                 }
 
                 var accessToken = accessNode.GetString()!;
-                // 不儲存至 LocalDevAdfsTokenStore，直接於記憶體中執行 WhoAmI 驗證
                 var who = await CallWhoAmIAsync(http, accessToken).ConfigureAwait(false);
                 result["whoAmIHttpStatus"] = who.StatusCode;
                 result["whoAmIOk"] = who.Ok;
                 if (who.Ok)
                 {
                     result["ok"] = true;
                     result["stage"] = "whoami-success";
                 }
 
-                await WriteProbeResultAsync(result).ConfigureAwait(false);
                 return Json(result);
             }
             catch (Exception ex)
             {
                 result["stage"] = "exception";
                 result["error"] = ex.GetType().Name;
-                await WriteProbeResultAsync(result).ConfigureAwait(false);
                 return Json(result);
             }
         }
 
-        [HttpGet("adfs-token-probe")]
-        public async Task<IActionResult> AdfsTokenProbe()
-        {
-            // 已廢除此端點，因為不再支援本機 Token 檔案讀取
-            return NotFound("Token probe endpoint has been retired for security hardening.");
-        }
-
         [HttpGet("session")]
         public IActionResult GetSessionInfo()
         {
+            ApplySecureHeaders();
             return Json(new
             {
-                SessionId = HttpContext.Session.Id, // 移除 SessionId 回顯
                 IsAvailable = HttpContext.Session.IsAvailable,
                 User = User.Identity?.Name ?? "Anonymous",
                 IsAuthenticated = User.Identity?.IsAuthenticated ?? false
             });
         }
 
+        private void ApplySecureHeaders()
+        {
+            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, private";
+            Response.Headers["Pragma"] = "no-cache";
+        }
+
+        private static string MaskClientId(string clientId)
+        {
+            if (string.IsNullOrWhiteSpace(clientId) || clientId.Length < 8) return "****";
+            return clientId.Substring(0, 4) + "..." + clientId.Substring(clientId.Length - 4);
+        }
+
-        private static async Task WriteProbeResultAsync(IDictionary<string, object?> result)
-        {
-            // 已移除檔案寫入邏輯
-        }
     }
 #endif
 }
```

---

## 4. RED Test Matrix (測試矩陣)

為確保安全修正有效，應建立以下測試案例（RED 階段應失敗，修正後應通過）：

| 測試案例名稱 | 測試目的 | 預期行為 (修正後) |
| :--- | :--- | :--- |
| `Test_TokenProvider_NoFileWrite` | 驗證 Token 取得與更新過程中，絕無任何檔案寫入磁碟。 | 監控 I/O，確認無 `adfs-local-token.json` 產生。 |
| `Test_Callback_State_OneTimeUse` | 驗證 OAuth `state` 在進入 callback 後立即被銷毀，防止重放攻擊。 | 第一次呼叫 callback 後，Session 中的 state 應為 null。第二次呼叫應回傳 `Invalid state`。 |
| `Test_Diagnostics_NoSensitiveEcho` | 驗證診斷端點回傳的 JSON 中不含 `access_token`、`refresh_token`、`SessionId` 與完整 `client_id`。 | 斷言 JSON 回應中無上述敏感欄位。 |
| `Test_Diagnostics_CacheControl` | 驗證所有診斷回應皆包含防止快取的 HTTP 標頭。 | 檢查 Response Headers 含有 `Cache-Control: no-store...`。 |
| `Test_PasswordGrant_Disabled` | 驗證密碼授權流程已被完全移除或停用。 | 呼叫 Token 取得時若無 Client Secret 應直接拋出 `InvalidOperationException`。 |

---

## 5. Lifecycle / Session / Memory-Leak Analysis

### 5.1 記憶體洩漏與資源釋放 (Memory Leakage & Resource Disposal)
- **HttpClient 租約管理**：`AdfsOAuthTokenProvider` 實作了 `IDisposable` 與 `IAsyncDisposable`。當 `_httpClientFactory` 為 null 時，Provider 會自行建立並管理 `_ownedHttpClient` 與 `_ownedHttpHandler`。在 `DisposeCoreAsync` 中，`_ownedHttpClient` 會被確定性釋放，且 `SemaphoreSlim` (`_gate`) 與 `CancellationTokenSource` (`_disposeCts`) 也會被妥善釋放，防止背景 Socket 與同步資源殘留。
- **Token 抹除**：在 `DisposeCoreAsync` 中，`_cachedToken` 被設為 `null`。由於不再寫入磁碟，當 Provider 被釋放時，記憶體中的 Token 將隨垃圾回收 (GC) 釋放，無殘留風險。

### 5.2 Session 生命週期 (Session Lifecycle)
- **狀態綁定**：OAuth `state` 存放在 ASP.NET Core Session 中。藉由在 `AdfsCallback` 進入點立即執行 `Session.Remove(AdfsOAuthStateSessionKey)`，確保了該狀態的生命週期為「單次使用即丟棄」，避免了 Session 劫持與狀態殘留。

---

## 6. Security / Sanitization Requirements (安全與淨化規範)

1. **傳輸安全**：所有 ADFS 與 CRM 端點必須強制使用 HTTPS。
2. **日誌淨化**：`Trace.WriteLine` 與 `ILogger` 嚴禁輸出包含 `code`、`state`、`access_token` 或 `client_secret` 的完整 URL 或 Body。
3. **快取控制**：所有診斷端點必須回傳：
   ```http
   Cache-Control: no-store, no-cache, must-revalidate, private
   Pragma: no-cache
   ```
4. **遮罩處理**：Client ID 在診斷預覽畫面中必須進行遮罩（例如 `2ad8...f9bc`），Client Secret 則完全不得顯示。

---

## 7. Rollback and Scope Limits (回滾與範疇限制)

### 7.1 範疇限制 (In-Scope vs Out-of-Scope)
- **In-Scope**：
  - 移除 `LocalDevAdfsTokenStore` 及其備份檔。
  - 修改 `AdfsOAuthTokenProvider` 與 `DiagnosticsController` 以符合記憶體內一次性驗證規範。
  - 淨化診斷回應與加入快取控制。
- **Out-of-Scope**：
  - **不得**刪除 Embedded 模式、Data8 或 `PowerPlatform.Dataverse.Client` 依賴。
  - **不得**啟用 `Package01FeeReadsEnabled`（必須維持 `false`）。
  - **不得**修改 Production 環境的路由與非 ADFS 認證邏輯。

### 7.2 回滾計畫 (Rollback Plan)
- 若此安全切片導致 Local Gateway 無法取得 Token，可透過 Git 還原此切片之修改。由於此修正不涉及資料庫 Schema 變更，代碼還原後即可立即恢復舊有行為。

---

## 8. Reviewer Findings (審查發現分類)

### Critical (危險)
- **檔案型明文憑證儲存**：`LocalDevAdfsTokenStore` 將 `access_token` 與 `refresh_token` 明文寫入磁碟，屬重大安全漏洞，必須立即移除。
- **Session State 未確定性清除**：`DiagnosticsController` 在錯誤路徑上未清除 Session 中的 OAuth state，易受重放攻擊影響。

### Warning (警告)
- **密碼授權流程殘留**：`AllowLocalDevPasswordGrant` 允許在開發環境使用帳密流程，不符合無密碼服務工作負載規範。
- **診斷資訊過度揭露**：診斷端點回傳完整 Client ID 與 ADFS 授權網址，且未設定快取控制標頭。

### Info (提示)
- **DEBUG 條件編譯**：`DiagnosticsController` 目前已使用 `#if DEBUG` 包裹，Release 版本不會暴露此控制器，但仍應在代碼層面進行徹底硬化以防萬一。
