以下是針對 Dynamics AD FS 診斷安全切片的雙模型分析與 Recommended Design。

---

### 1. Analysis (分析)

#### 當前架構評估與根因確認
1. **明文 Token 持久化洩漏**：`LocalDevAdfsTokenStore.cs` 與其備份檔 `LocalDevAdfsTokenStore.cs.bak` 會將 access token 與 refresh token 明文序列化成 JSON 並寫入磁碟。`AdfsOAuthTokenProvider.cs` 會在 token 交換成功後再次寫回此明文檔案。這違反了「token 不得明文持久化」的安全規範。
2. **診斷資訊洩漏與未授權回顯**：`DiagnosticsController.cs` 會將敏感的 authority、resource、client ID、完整授權 URL、token store path、上游 body preview、WhoAmI body 與 exception message 寫入可持久化的診斷 JSON 檔案，且未對回應進行 Sanitization，亦未設定 `Cache-Control: no-store, private` 標頭。
3. **Session State 殘留**：`DiagnosticsController.cs` 在 `/diagnostics/adfs-authorize` 建立 Session OAuth state，但在 callback 的各種路徑（錯誤、不匹配、成功、異常）中均未確定性地移除該 state，存在 Session 殘留與重放攻擊風險。
4. **不安全的驗證機制**：`AdfsOAuthTokenProvider.cs` 允許使用不安全的 Resource Owner Password Credentials (ROPC) grant（即 password grant），這在 AD FS IFD 環境中不符合 non-password service-workload 的安全規範。

---

### 2. Architecture Decision (架構決策)

#### 關鍵設計選擇與原理
- **完全廢棄檔案型 Token Persistence**：完全移除 `LocalDevAdfsTokenStore` 與 `LocalDevTokenStorePath` 的實質影響。Token 快取僅保留在記憶體中（In-Memory Cache），其生命週期與 `AdfsOAuthTokenProvider` 實例一致，並在 `Dispose` 時進行確定性清理。
- **廢棄 Password Grant (ROPC)**：在 `AdfsOAuthTokenProvider` 中，若啟用 `AllowLocalDevPasswordGrant` 則直接拋出異常。AD FS IFD 必須使用 non-password service-workload contract（如 Client Credentials with Client Secret/Certificate），不依賴瀏覽器或使用者 Session。
- **一次性 OAuth State 清理**：在 `AdfsCallback` 開始時，讀取 state 後立即呼叫 `Session.Remove` 銷毀 state，確保其為一次性使用（One-Time Use），防止重放攻擊。
- **退役 AdfsTokenProbe，保留記憶體內一次性交換驗證**：退役依賴檔案讀取的 `AdfsTokenProbe`。在 `AdfsCallback` 中，收到 code 後立即在記憶體中交換 token 並執行一次 WhoAmI，隨即將 token 變數設為 null 丟棄，不進行任何持久化或回顯。
- **嚴格的 Sanitization 與 Cache-Control**：移除所有診斷 JSON 檔案寫入邏輯（移除 `WriteProbeResultAsync`）。對所有診斷回應進行 Sanitization，移除敏感的 URL、Client ID、Token、Body Preview 等，並明確加上 `Cache-Control: no-store, private` 標頭。

#### 被拒絕的替代方案
- *替代方案 A：使用加密的檔案型 Token Store*。被拒絕，因為在 Local Dev 環境中，加密金鑰的妥善管理與保護極為困難，且互動式 authorization-code 僅作為診斷證據，不應成為 Production 運行時的 token 來源。
- *替代方案 B：完全移除 DiagnosticsController*。被拒絕，因為在開發與測試階段，驗證 AD FS Relying Party Trust 與 Client ID 設定是否正確非常重要，完全移除會導致可驗證性極差。

#### 假設與潛在副作用
- *假設*：開發人員在 Local Dev 環境中，每次重啟或 token 過期後，需要重新透過 `/diagnostics/adfs-authorize` 進行一次性驗證。
- *副作用*：由於移除了檔案型 token 快取，開發人員在 Local Dev 測試時可能需要更頻繁地進行互動式登入驗證，但這換來了絕對的安全性。

---

### 3. Implementation Plan (實施計劃)

#### 步驟與虛擬碼
1. **廢棄 Token Store**：修改 `LocalDevAdfsTokenStore.cs` 與 `LocalDevAdfsTokenStore.cs.bak`，將其標記為 `[Obsolete]` 並在所有方法中拋出 `NotSupportedException`。
2. **修改 Token Provider**：修改 `AdfsOAuthTokenProvider.cs`，移除所有與 `LocalDevAdfsTokenStore` 相關的讀寫邏輯，並在啟用 `AllowLocalDevPasswordGrant` 時拋出異常。
3. **強化控制器安全性**：修改 `DiagnosticsController.cs`：
   - 在所有 Action 中加入 `Response.Headers.CacheControl = "no-store, private";`。
   - 在 `AdfsCallback` 中，讀取 state 後立即從 Session 中移除。
   - 移除 `WriteProbeResultAsync` 與 `GetTokenStorePath`。
   - 退役 `AdfsTokenProbe`，直接回傳退役訊息。
   - 對 `AdfsAuthorize` 與 `AdfsCallback` 的回應進行 Sanitization，移除敏感資訊，並在交換 token 後立即將 `accessToken` 設為 null。
4. **更新測試案例**：修改 `AdfsOAuthTokenProviderTests.cs`，將原本測試 password grant 與 refresh token grant 的測試修改為驗證其拋出異常與退役行為，並新增測試驗證 `LocalDevAdfsTokenStore` 拋出 `NotSupportedException`。

---

### 4. Considerations (考量事項)

- **效能**：移除檔案 I/O 減少了磁碟讀寫開銷；記憶體快取與單一 flight 機制保證了 token 重新整理時的效能。
- **安全性**：完全消除了 token 明文持久化、Session 殘留與敏感資訊回顯的風險，符合安全合規規範。
- **可維護性**：程式碼結構更加清晰，退役了複雜且不安全的檔案型快取邏輯，降低了維護成本。

---

### 5. Findings Classification

- **Critical** (嚴重): `LocalDevAdfsTokenStore.cs` 將 access token 與 refresh token 明文寫入磁碟，且 `DiagnosticsController.cs` 未清理 Session OAuth state 並將敏感資訊寫入診斷 JSON 檔案。
- **Warning** (警告): `AdfsOAuthTokenProvider.cs` 允許使用已廢棄的 password grant (ROPC) 方式取得 token。
- **Info** (提示): `LocalDevAdfsTokenStore.cs.bak` 作為 tracked source 保留了不安全的實作，應予以清空或廢棄。

---

### 6. Unified Diff Patch

```diff
--- a/SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs
+++ b/SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs
@@ -1,87 +1,35 @@
 // ============================================================================
 // 檔案：SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs
-// 說明：用於 local-dev 暫存 ADFS access/refresh token，以便 WebApi 可以重複使用。
-//
-// 維護合約與要點：
-// 1. jesus ADFS 不支援 password grant，只能使用 authorization_code / refresh_token。
-// 2. 開發人員手動瀏覽一次，取得 refresh_token 儲存後，之後自動 refresh。
-// 3. 不是 per-user CRM session pool。
-// 4. 檔案必須在 gitignore。
+// 說明：已退役。檔案型 token store 已經被廢棄以符合安全規範。
 // ============================================================================
 
-using System.Text.Json;
+using System;
 
 namespace SpeechMessage.Dynamics.Abstractions.Configuration;
 
 /// <summary>
-/// 暫存 ADFS token 檔案記錄格式。
+/// 已退役的 ADFS token 記錄。
 /// </summary>
+[Obsolete("File-based token store is retired for security compliance.", true)]
 public sealed class LocalDevAdfsTokenRecord
 {
     public string? AccessToken { get; set; }
     public string? RefreshToken { get; set; }
     public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
     public string? AuthorityUri { get; set; }
     public string? ResourceUri { get; set; }
     public string? ClientId { get; set; }
     public DateTimeOffset UpdatedAtUtc { get; set; }
 }
 
 /// <summary>
-/// 以 JSON 檔案持久化暫存 ADFS token。
+/// 已退役的 ADFS token 檔案儲存庫。
 /// </summary>
+[Obsolete("File-based token store is retired for security compliance.", true)]
 public static class LocalDevAdfsTokenStore
 {
-    private static readonly JsonSerializerOptions JsonOptions = new()
-    {
-        WriteIndented = true,
-        PropertyNameCaseInsensitive = true
-    };
-
-    private static readonly object Gate = new();
-
     public static bool TryLoad(string? path, out LocalDevAdfsTokenRecord? record)
     {
-        record = null;
-        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
-        {
-            return false;
-        }
-
-        try
-        {
-            lock (Gate)
-            {
-                var json = File.ReadAllText(path);
-                record = JsonSerializer.Deserialize<LocalDevAdfsTokenRecord>(json, JsonOptions);
-                return record is not null &&
-                       (!string.IsNullOrWhiteSpace(record.AccessToken) ||
-                        !string.IsNullOrWhiteSpace(record.RefreshToken));
-            }
-        }
-        catch
-        {
-            record = null;
-            return false;
-        }
+        throw new NotSupportedException("File-based token store is retired for security compliance.");
     }
 
     public static void Save(string path, LocalDevAdfsTokenRecord record)
     {
-        ArgumentException.ThrowIfNullOrWhiteSpace(path);
-        ArgumentNullException.ThrowIfNull(record);
-
-        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
-        var dir = Path.GetDirectoryName(path);
-        if (!string.IsNullOrWhiteSpace(dir))
-        {
-            Directory.CreateDirectory(dir);
-        }
-
-        var json = JsonSerializer.Serialize(record, JsonOptions);
-        lock (Gate)
-        {
-            File.WriteAllText(path, json);
-        }
+        throw new NotSupportedException("File-based token store is retired for security compliance.");
     }
 }
--- a/SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak
+++ b/SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak
@@ -1,113 +1,39 @@
 // ============================================================================
 // 檔案：SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs
-// 說明：用於 local-dev 暫存 ADFS access/refresh token，供 IFD Web API 使用。
-//
-// 維護合約與要點：
-// 1. jesus 的 ADFS 不支援 password grant，只能使用 authorization_code / refresh_token。
-// 2. 開發人員手動瀏覽一次 authorization_code，將 refresh_token 存於此處。
-// 3. 之後的 Embedded / Package1 用 refresh_token 拿 access_token，免去每次手動。
-// 4. 這不是 per-user CRM session pool，是「共用 token 檔」，僅限 local-dev。
-// 5. 檔案必須在 gitignore，不可送出。
+// 說明：已退役。檔案型 token store 已經被廢棄以符合安全規範。
 // ============================================================================
 
-using System.Text.Json;
+using System;
+using SpeechMessage.Dynamics.Abstractions.Configuration;
 
 namespace SpeechMessage.Dynamics.WebApi.Runtime;
 
 /// <summary>
-/// 暫存 ADFS token 檔案記錄格式。
-/// </summary>
-public sealed class LocalDevAdfsTokenRecord
-{
-    public string? AccessToken { get; set; }
-    public string? RefreshToken { get; set; }
-    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
-    public string? AuthorityUri { get; set; }
-    public string? ResourceUri { get; set; }
-    public string? ClientId { get; set; }
-    public DateTimeOffset UpdatedAtUtc { get; set; }
-}
-
-/// <summary>
-/// 以 JSON 檔案持久化暫存 ADFS token。
+/// 已退役的 ADFS token 檔案儲存庫備份。
 /// </summary>
+[Obsolete("File-based token store is retired for security compliance.", true)]
 public static class LocalDevAdfsTokenStore
 {
-    private static readonly JsonSerializerOptions JsonOptions = new()
-    {
-        WriteIndented = true,
-        PropertyNameCaseInsensitive = true
-    };
-
-    private static readonly object Gate = new();
-
     public static bool TryLoad(string? path, out LocalDevAdfsTokenRecord? record)
     {
-        record = null;
-        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
-        {
-            return false;
-        }
-
-        try
-        {
-            lock (Gate)
-            {
-                var json = File.ReadAllText(path);
-                record = JsonSerializer.Deserialize<LocalDevAdfsTokenRecord>(json, JsonOptions);
-                return record is not null &&
-                       (!string.IsNullOrWhiteSpace(record.AccessToken) ||
-                        !string.IsNullOrWhiteSpace(record.RefreshToken));
-            }
-        }
-        catch
-        {
-            record = null;
-            return false;
-        }
+        throw new NotSupportedException("File-based token store is retired for security compliance.");
     }
 
     public static void Save(string path, LocalDevAdfsTokenRecord record)
     {
-        ArgumentException.ThrowIfNullOrWhiteSpace(path);
-        ArgumentNullException.ThrowIfNull(record);
-
-        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
-        var dir = Path.GetDirectoryName(path);
-        if (!string.IsNullOrWhiteSpace(dir))
-        {
-            Directory.CreateDirectory(dir);
-        }
-
-        var json = JsonSerializer.Serialize(record, JsonOptions);
-        lock (Gate)
-        {
-            File.WriteAllText(path, json);
-        }
+        throw new NotSupportedException("File-based token store is retired for security compliance.");
     }
 
     public static string ResolveDefaultPath()
     {
-        var candidates = new[]
-        {
-            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "adfs-local-token.json")),
-            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "SpeechMessageProducts.ChurchReport", "Logs", "adfs-local-token.json")),
-            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Logs", "adfs-local-token.json")),
-            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Logs", "adfs-local-token.json"))
-        };
-
-        foreach (var candidate in candidates)
-        {
-            var dir = Path.GetDirectoryName(candidate);
-            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
-            {
-                return candidate;
-            }
-        }
-
-        return candidates[0];
+        throw new NotSupportedException("File-based token store is retired for security compliance.");
     }
 }
--- a/SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs
+++ b/SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs
@@ -108,19 +108,6 @@
-            // 1.5) local-dev token store 優先載入 access_token 立即使用，避免每次 multi-profile 啟動都重複向網路請求。
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
             // 2) 記憶體快取檢查。若有其他請求在 Gate 等待，Dispose 發生時此處會直接由下一個 owner 拋出異常。
             if (!string.IsNullOrWhiteSpace(_cachedToken) &&
                 DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
             {
                 return _cachedToken!;
             }
@@ -165,7 +152,7 @@
             }
 
             var token = await ReadBoundedTokenResponseAsync(response.Content, cancellationToken).ConfigureAwait(false);
-            TryPersistTokens(token.AccessToken, token.ExpiresInSeconds, token.RefreshToken);
+            // File-based token persistence is retired for security compliance.
             return new TokenResponse(token.AccessToken, token.ExpiresInSeconds);
         }
         finally
@@ -204,31 +191,10 @@
 
         // 選擇性：本機開發 password grant（注意：ADFS 可能回傳 unsupported_grant_type）
         if (_options.AllowLocalDevPasswordGrant)
         {
-            if (string.IsNullOrWhiteSpace(_options.UserNameSecretName) ||
-                string.IsNullOrWhiteSpace(_options.PasswordSecretName))
-            {
-                throw new InvalidOperationException(
-                    "AdfsOAuth local-dev password grant requires UserNameSecretName and PasswordSecretName.");
-            }
-
-            if (!_secretResolver.TryResolve(_options.UserNameSecretName, out var userName) ||
-                string.IsNullOrWhiteSpace(userName))
-            {
-                throw new InvalidOperationException("Failed to resolve ADFS username secret.");
-            }
-
-            if (!_secretResolver.TryResolve(_options.PasswordSecretName, out var password) ||
-                string.IsNullOrWhiteSpace(password))
-            {
-                throw new InvalidOperationException("Failed to resolve ADFS password secret.");
-            }
-
-            form.Add(new("grant_type", "password"));
-            form.Add(new("username", userName!));
-            form.Add(new("password", password!));
-            return form;
+            throw new InvalidOperationException("Password grant (ROPC) is retired for security compliance.");
         }
 
         throw new InvalidOperationException(
             "AdfsOAuth has no usable token source. " +
             "For jesus ADFS, open /diagnostics/adfs-authorize once to obtain refresh_token, " +
@@ -248,15 +214,6 @@
             refreshToken = fromSecret;
             return true;
         }
-
-        var storePath = ResolveTokenStorePath();
-        if (LocalDevAdfsTokenStore.TryLoad(storePath, out var record) &&
-            !string.IsNullOrWhiteSpace(record?.RefreshToken))
-        {
-            refreshToken = record!.RefreshToken;
-            return true;
-        }
-
         return false;
     }
 
@@ -269,32 +226,7 @@
 
     private void TryPersistTokens(string accessToken, int expiresInSeconds, string? refreshToken)
     {
-        var storePath = ResolveTokenStorePath();
-        if (string.IsNullOrWhiteSpace(storePath))
-        {
-            return;
-        }
-
-        try
-        {
-            LocalDevAdfsTokenStore.TryLoad(storePath, out var existing);
-            var record = existing ?? new LocalDevAdfsTokenRecord();
-            record.AccessToken = accessToken;
-            if (!string.IsNullOrWhiteSpace(refreshToken))
-            {
-                record.RefreshToken = refreshToken;
-            }
-            record.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresInSeconds));
-            record.AuthorityUri = ResolveAuthority();
-            record.ResourceUri = ResolveResource();
-            record.ClientId = ResolveClientId();
-            LocalDevAdfsTokenStore.Save(storePath, record);
-        }
-        catch (Exception ex)
-        {
-            _logger.LogWarning(ex, "Failed to persist local-dev ADFS token store at {Path}", storePath);
-        }
+        // Retired for security compliance.
     }
 
     private string ResolveAuthority()
--- a/SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs
@@ -78,18 +78,10 @@
         public async Task<IActionResult> AdfsAuthorize(string? go = null)
         {
+            Response.Headers.CacheControl = "no-store, private";
             var authority = GetAuthority();
             var resource = GetResource();
             var clientId = GetClientId();
             var redirectUri = GetRedirectUri();
             var state = Guid.NewGuid().ToString("N");
             HttpContext.Session.SetString(AdfsOAuthStateSessionKey, state);
 
             var authorizeUrl =
                 authority.TrimEnd('/') + "/oauth2/authorize" +
                 "?response_type=code" +
                 "&client_id=" + Uri.EscapeDataString(clientId) +
                 "&resource=" + Uri.EscapeDataString(resource) +
                 "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
                 "&response_mode=query" +
                 "&state=" + Uri.EscapeDataString(state);
 
             var preview = new Dictionary<string, object?>
             {
                 ["ok"] = false,
                 ["stage"] = "authorize-preview",
                 ["serverTime"] = DateTime.Now.ToString("o"),
-                ["authority"] = authority,
-                ["resource"] = resource,
-                ["clientId"] = clientId,
-                ["redirectUri"] = redirectUri,
-                ["authorizeUrl"] = authorizeUrl,
+                ["authority"] = "[REDACTED]",
+                ["resource"] = "[REDACTED]",
+                ["clientId"] = "[REDACTED]",
+                ["redirectUri"] = "[REDACTED]",
+                ["authorizeUrl"] = "[REDACTED]",
                 ["knownFacts"] = new[]
                 {
                     "Password grant is disabled on this ADFS (unsupported_grant_type).",
                     "Previous authorize attempt failed with Relying party = 'Dynamics 365 系統平台 IFD'.",
                     "That RP name is the CRM IFD trust, which usually means OAuth client is not registered/permitted.",
                     "Provisional ClientId 2ad88395-... is a Dynamics Online sample id and is likely NOT registered on this on-prem ADFS."
                 },
                 ["adfsAdminRequired"] = new
                 {
                     goal = "Register a public/native OAuth client on ADFS and permit it for CRM IFD resource",
                     samplePowerShell = new[]
                     {
                         "$clientId = [guid]::NewGuid().Guid",
                         "Add-AdfsClient -Name 'SpeechMessage-ChurchReport-LocalDev' -ClientId $clientId -RedirectUri 'http://localhost:43371/diagnostics/adfs-callback'",
                         "# Then put $clientId into DynamicsAccess:Embedded:ClientId",
                         "# If using Application Group model, also grant permission to CRM Web API / IFD relying party identifier."
                     }
                 },
                 ["nextStep"] = "After ADFS client is registered, open /diagnostics/adfs-authorize?go=1"
             };
 
-            await WriteProbeResultAsync(preview).ConfigureAwait(false);
-            Trace.WriteLine("[ADFS-AUTH] preview authorizeUrl=" + authorizeUrl);
-
             var shouldGo =
                 string.Equals(go, "1", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(go, "true", StringComparison.OrdinalIgnoreCase);
 
             if (!shouldGo)
             {
                 return Json(preview);
             }
 
-            Trace.WriteLine("[ADFS-AUTH] redirect to authorize. redirectUri=" + redirectUri + " clientId=" + clientId);
             return Redirect(authorizeUrl);
         }
 
@@ -146,148 +138,60 @@
         [HttpGet("adfs-callback")]
         public async Task<IActionResult> AdfsCallback(string? code, string? state, string? error, string? error_description)
         {
+            Response.Headers.CacheControl = "no-store, private";
             var result = new Dictionary<string, object?>
             {
                 ["ok"] = false,
                 ["stage"] = "callback",
                 ["serverTime"] = DateTime.Now.ToString("o"),
                 ["processUser"] = Environment.UserName
             };
 
+            var expectedState = HttpContext.Session.GetString(AdfsOAuthStateSessionKey);
+            HttpContext.Session.Remove(AdfsOAuthStateSessionKey); // Deterministic cleanup of state
+
             if (!string.IsNullOrWhiteSpace(error))
             {
-                result["error"] = error;
-                result["errorDescription"] = error_description;
-                await WriteProbeResultAsync(result).ConfigureAwait(false);
+                result["error"] = "Callback error received.";
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
-            result["tokenUrl"] = tokenUrl;
-            result["redirectUri"] = redirectUri;
-            result["clientId"] = clientId;
-            result["resource"] = resource;
 
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
-                    result["error"] = "authorization_code exchange failed HTTP " + (int)response.StatusCode;
-                    result["bodyPreview"] = TrimBody(body);
-                    await WriteProbeResultAsync(result).ConfigureAwait(false);
+                    result["error"] = "authorization_code exchange failed.";
                     return Json(result);
                 }
 
                 using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                 var root = doc.RootElement;
                 if (!root.TryGetProperty("access_token", out var accessNode) ||
                     accessNode.ValueKind != JsonValueKind.String ||
                     string.IsNullOrWhiteSpace(accessNode.GetString()))
                 {
                     result["error"] = "Token response missing access_token.";
-                    result["bodyPreview"] = TrimBody(body);
-                    await WriteProbeResultAsync(result).ConfigureAwait(false);
                     return Json(result);
                 }
 
                 var accessToken = accessNode.GetString()!;
-                string? refreshToken = null;
-                if (root.TryGetProperty("refresh_token", out var refreshNode) &&
-                    refreshNode.ValueKind == JsonValueKind.String)
-                {
-                    refreshToken = refreshNode.GetString();
-                }
-
-                var expiresIn = 3600;
-                if (root.TryGetProperty("expires_in", out var expNode))
-                {
-                    if (expNode.ValueKind == JsonValueKind.Number && expNode.TryGetInt32(out var n))
-                    {
-                        expiresIn = n;
-                    }
-                    else if (expNode.ValueKind == JsonValueKind.String &&
-                             int.TryParse(expNode.GetString(), out var s))
-                    {
-                        expiresIn = s;
-                    }
-                }
-
-                var storePath = GetTokenStorePath();
-                LocalDevAdfsTokenStore.Save(storePath, new LocalDevAdfsTokenRecord
-                {
-                    AccessToken = accessToken,
-                    RefreshToken = refreshToken,
-                    AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)),
-                    AuthorityUri = authority,
-                    ResourceUri = resource,
-                    ClientId = clientId
-                });
 
-                result["ok"] = !string.IsNullOrWhiteSpace(refreshToken) || !string.IsNullOrWhiteSpace(accessToken);
-                result["stage"] = "token-saved";
-                result["tokenStorePath"] = storePath;
-                result["hasRefreshToken"] = !string.IsNullOrWhiteSpace(refreshToken);
-                result["expiresIn"] = expiresIn;
-                result["nextStep"] = "Open /diagnostics/adfs-token-probe to verify WhoAmI";
+                result["ok"] = true;
+                result["stage"] = "token-exchanged";
 
                 // 立即 WhoAmI 驗證
-                var who = await CallWhoAmIAsync(http, accessToken).ConfigureAwait(false);
+                var who = await CallWhoAmIAsync(http, accessToken).ConfigureAwait(false);
+                accessToken = null; // Deterministic cleanup of sensitive token in memory
+
                 result["whoAmIHttpStatus"] = who.StatusCode;
                 result["whoAmIOk"] = who.Ok;
-                result["whoAmIBody"] = who.BodyPreview;
                 if (who.Ok)
                 {
                     result["ok"] = true;
                     result["stage"] = "whoami";
                 }
 
-                await WriteProbeResultAsync(result).ConfigureAwait(false);
                 return Json(result);
             }
             catch (Exception ex)
             {
                 result["stage"] = "exception";
-                result["error"] = ex.GetType().Name + ": " + ex.Message;
-                if (ex.InnerException is not null)
-                {
-                    result["innerError"] = ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
-                }
-                await WriteProbeResultAsync(result).ConfigureAwait(false);
+                result["error"] = "An error occurred during token exchange.";
                 return Json(result);
             }
         }
 
         /// <summary>
-        /// 測試：嘗試使用 local token store / refresh_token，並測試 WhoAmI。
+        /// 已退役。檔案型 token store 已經被廢棄以符合安全規範。
         /// </summary>
         [HttpGet("adfs-token-probe")]
         public async Task<IActionResult> AdfsTokenProbe()
         {
-            var authority = GetAuthority();
-            var resource = GetResource();
-            var clientId = GetClientId();
-            var whoAmI = GetWhoAmIUrl();
-            var storePath = GetTokenStorePath();
-
-            var result = new Dictionary<string, object?>
-            {
-                ["ok"] = false,
-                ["stage"] = "init",
-                ["serverTime"] = DateTime.Now.ToString("o"),
-                ["processUser"] = Environment.UserName,
-                ["authority"] = authority,
-                ["resource"] = resource,
-                ["clientId"] = clientId,
-                ["whoAmI"] = whoAmI,
-                ["tokenStorePath"] = storePath
-            };
-
-            try
-            {
-                using var http = CreateHttpClient();
-                string? accessToken = null;
-
-                if (LocalDevAdfsTokenStore.TryLoad(storePath, out var stored) && stored is not null)
-                {
-                    result["storeLoaded"] = true;
-                    result["storeHasRefreshToken"] = !string.IsNullOrWhiteSpace(stored.RefreshToken);
-                    result["storeHasAccessToken"] = !string.IsNullOrWhiteSpace(stored.AccessToken);
-                    result["storeExpiresAtUtc"] = stored.AccessTokenExpiresAtUtc?.ToString("o");
-
-                    if (!string.IsNullOrWhiteSpace(stored.AccessToken) &&
-                        stored.AccessTokenExpiresAtUtc is not null &&
-                        DateTimeOffset.UtcNow < stored.AccessTokenExpiresAtUtc.Value.AddSeconds(-60))
-                    {
-                        accessToken = stored.AccessToken;
-                        result["tokenSource"] = "local-store-access-token";
-                    }
-                    else if (!string.IsNullOrWhiteSpace(stored.RefreshToken))
-                    {
-                        var tokenUrl = authority.TrimEnd('/') + "/oauth2/token";
-                        result["tokenUrl"] = tokenUrl;
-                        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
-                        {
-                            ["client_id"] = clientId,
-                            ["grant_type"] = "refresh_token",
-                            ["refresh_token"] = stored.RefreshToken!,
-                            ["resource"] = resource
-                        });
-                        using var tokenResponse = await http.PostAsync(tokenUrl, content).ConfigureAwait(false);
-                        var tokenBody = await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
-                        result["tokenHttpStatus"] = (int)tokenResponse.StatusCode;
-                        if (!tokenResponse.IsSuccessStatusCode)
-                        {
-                            result["stage"] = "refresh";
-                            result["error"] = "refresh_token failed HTTP " + (int)tokenResponse.StatusCode;
-                            result["bodyPreview"] = TrimBody(tokenBody);
-                            result["hint"] = "Open /diagnostics/adfs-authorize to login again.";
-                            await WriteProbeResultAsync(result).ConfigureAwait(false);
-                            return Json(result);
-                        }
-
-                        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(tokenBody) ? "{}" : tokenBody);
-                        var root = doc.RootElement;
-                        accessToken = root.GetProperty("access_token").GetString();
-                        var expiresIn = 3600;
-                        if (root.TryGetProperty("expires_in", out var expNode) &&
-                            expNode.ValueKind == JsonValueKind.Number &&
-                            expNode.TryGetInt32(out var n))
-                        {
-                            expiresIn = n;
-                        }
-                        string? newRefresh = stored.RefreshToken;
-                        if (root.TryGetProperty("refresh_token", out var rn) && rn.ValueKind == JsonValueKind.String)
-                        {
-                            newRefresh = rn.GetString();
-                        }
-
-                        LocalDevAdfsTokenStore.Save(storePath, new LocalDevAdfsTokenRecord
-                        {
-                            AccessToken = accessToken,
-                            RefreshToken = newRefresh,
-                            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
-                            AuthorityUri = authority,
-                            ResourceUri = resource,
-                            ClientId = clientId
-                        });
-                        result["tokenSource"] = "refresh_token";
-                    }
-                }
-                else
-                {
-                    result["storeLoaded"] = false;
-                }
-
-                if (string.IsNullOrWhiteSpace(accessToken))
-                {
-                    // password grant 在 jesus 上會 unsupported_grant_type，這裡僅作提示，不主動走密碼流。
-                    result["stage"] = "need-authorize";
-                    result["error"] = "No local ADFS token. jesus ADFS only supports authorization_code/refresh_token.";
-                    result["nextStep"] = "Open /diagnostics/adfs-authorize while logged into ChurchReport.";
-                    await WriteProbeResultAsync(result).ConfigureAwait(false);
-                    return Json(result);
-                }
-
-                var who = await CallWhoAmIAsync(http, accessToken!);
-                result["stage"] = "whoami";
-                result["whoAmIHttpStatus"] = who.StatusCode;
-                result["whoAmIBody"] = who.BodyPreview;
-                result["ok"] = who.Ok;
-                if (!who.Ok)
-                {
-                    result["error"] = "WhoAmI failed HTTP " + who.StatusCode;
-                    result["location"] = who.Location;
-                }
-                else
-                {
-                    result["nextStep"] = "Set DynamicsAccess:Package01FeeReadsEnabled=true and retest fee list Returned=56";
-                }
-
-                await WriteProbeResultAsync(result).ConfigureAwait(false);
-                return Json(result);
-            }
-            catch (Exception ex)
-            {
-                result["stage"] = "exception";
-                result["error"] = ex.GetType().Name + ": " + ex.Message;
-                if (ex.InnerException is not null)
-                {
-                    result["innerError"] = ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
-                }
-                await WriteProbeResultAsync(result).ConfigureAwait(false);
-                return Json(result);
-            }
+            Response.Headers.CacheControl = "no-store, private";
+            return Json(new
+            {
+                ok = false,
+                error = "AdfsTokenProbe is retired. File-based token store is removed for security compliance. Use adfs-authorize for one-time memory-only exchange and verification."
+            });
         }
 
         [HttpGet("session")]
         public IActionResult GetSessionInfo()
         {
+            Response.Headers.CacheControl = "no-store, private";
             return Json(new
             {
                 SessionId = HttpContext.Session.Id,
@@ -449,6 +225,7 @@
         [HttpGet("performance")]
         public IActionResult GetPerformanceInfo()
         {
+            Response.Headers.CacheControl = "no-store, private";
             var process = Process.GetCurrentProcess();
             return Json(new
             {
@@ -490,72 +267,5 @@
-        private string GetTokenStorePath()
-        {
-            var configured = _configuration["DynamicsAccess:Embedded:LocalDevTokenStorePath"];
-            if (!string.IsNullOrWhiteSpace(configured))
-            {
-                return configured!;
-            }
-
-            // 預設寫入本機的 Logs，方便 Codex 閱讀
-            var projectLogs = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "adfs-local-token.json"));
-            try
-            {
-                Directory.CreateDirectory(Path.GetDirectoryName(projectLogs)!);
-                return projectLogs;
-            }
-            catch
-            {
-                return Path.Combine(Path.GetTempPath(), "adfs-local-token.json");
-            }
-        }
-
         private static HttpClient CreateHttpClient()
             => new HttpClient(new SocketsHttpHandler
             {
                 UseCookies = false,
                 AllowAutoRedirect = false,
                 UseProxy = false
             })
             {
                 Timeout = TimeSpan.FromSeconds(30)
             };
-
-        private static async Task WriteProbeResultAsync(IDictionary<string, object?> result)
-        {
-            try
-            {
-                var candidates = new List<string>
-                {
-                    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Logs")),
-                    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "SpeechMessageProducts.ChurchReport", "Logs")),
-                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Logs")),
-                    Path.Combine(AppContext.BaseDirectory, "Logs")
-                };
-
-                string? logsDir = null;
-                foreach (var candidate in candidates)
-                {
-                    try
-                    {
-                        Directory.CreateDirectory(candidate);
-                        logsDir = candidate;
-                        break;
-                    }
-                    catch
-                    {
-                    }
-                }
-
-                if (logsDir is null)
-                {
-                    result["resultFileError"] = "Unable to create Logs directory.";
-                    return;
-                }
-
-                var path = Path.Combine(logsDir, "adfs-token-probe-latest.json");
-                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
-                await System.IO.File.WriteAllTextAsync(path, json).ConfigureAwait(false);
-                result["resultFile"] = path;
-                Trace.WriteLine("[ADFS-PROBE] wrote " + path + " ok=" + result["ok"] + " stage=" + result["stage"]);
-            }
-            catch (Exception writeEx)
-            {
-                result["resultFileError"] = writeEx.Message;
-            }
-        }
     }
 #endif
 }
--- a/SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs
+++ b/SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs
@@ -55,99 +55,22 @@
-    [Fact]
-    public async Task Password_grant_posts_expected_form_and_caches_token()
-    {
-        var callCount = 0;
-        HttpRequestMessage? seen = null;
-        string? body = null;
-
-        var provider = CreateProvider(
-            options: new DynamicsWebApiOptions
-            {
-                AuthMode = DynamicsAuthMode.AdfsOAuth,
-                AuthorityUri = "https://sts.example.local/adfs",
-                ClientId = "client-xyz",
-                ResourceUri = "https://jesus.example.local/",
-                UserNameSecretName = "USER_SECRET",
-                PasswordSecretName = "PASS_SECRET",
-                AllowLocalDevPasswordGrant = true,
-                TimeoutSeconds = 10
-            },
-            secrets: new Dictionary<string, string>
-            {
-                ["USER_SECRET"] = @"SPEECHMESSAGE\Administrator",
-                ["PASS_SECRET"] = "not-a-real-password"
-            },
-            responder: request =>
-            {
-                callCount++;
-                seen = request;
-                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
-                return JsonResponse("""{"access_token":"adfs-token-001","expires_in":1200,"token_type":"bearer"}""");
-            });
-
-        var first = await provider.GetAccessTokenAsync();
-        var second = await provider.GetAccessTokenAsync();
-
-        first.Should().Be("adfs-token-001");
-        second.Should().Be("adfs-token-001");
-        callCount.Should().Be(1, "token must be cached until near expiry");
-
-        seen.Should().NotBeNull();
-        seen!.Method.Should().Be(HttpMethod.Post);
-        seen.RequestUri!.AbsoluteUri.Should().Be("https://sts.example.local/adfs/oauth2/token");
-        body.Should().NotBeNull();
-        body.Should().Contain("grant_type=password");
-        body.Should().Contain("client_id=client-xyz");
-        body.Should().Contain("resource=" + Uri.EscapeDataString("https://jesus.example.local/"));
-        body.Should().Contain("username=" + Uri.EscapeDataString(@"SPEECHMESSAGE\Administrator"));
-        body.Should().Contain("password=not-a-real-password");
-    }
+    [Fact]
+    public async Task Password_grant_is_retired_and_throws_invalid_operation()
+    {
+        var provider = CreateProvider(
+            options: new DynamicsWebApiOptions
+            {
+                AuthMode = DynamicsAuthMode.AdfsOAuth,
+                AuthorityUri = "https://sts.example.local/adfs",
+                ClientId = "client-xyz",
+                ResourceUri = "https://jesus.example.local/",
+                UserNameSecretName = "USER_SECRET",
+                PasswordSecretName = "PASS_SECRET",
+                AllowLocalDevPasswordGrant = true,
+                TimeoutSeconds = 10
+            },
+            secrets: new Dictionary<string, string>
+            {
+                ["USER_SECRET"] = @"SPEECHMESSAGE\Administrator",
+                ["PASS_SECRET"] = "not-a-real-password"
+            },
+            responder: _ => JsonResponse("{}"));
+
+        var act = async () => await provider.GetAccessTokenAsync();
+        await act.Should().ThrowAsync<InvalidOperationException>()
+            .WithMessage("*Password grant (ROPC) is retired*");
+    }
 
     [Fact]
     public async Task Password_grant_disabled_fails_closed()
     {
         var provider = CreateProvider(
             options: new DynamicsWebApiOptions
             {
                 AuthMode = DynamicsAuthMode.AdfsOAuth,
                 AuthorityUri = "https://sts.example.local/adfs",
                 ClientId = "client-xyz",
                 ResourceUri = "https://jesus.example.local/",
                 UserNameSecretName = "USER_SECRET",
                 PasswordSecretName = "PASS_SECRET",
                 AllowLocalDevPasswordGrant = false,
                 TimeoutSeconds = 10
             },
             secrets: new Dictionary<string, string>
             {
                 ["USER_SECRET"] = "u",
                 ["PASS_SECRET"] = "p"
             },
             responder: _ => JsonResponse("""{"access_token":"x","expires_in":60}"""));
 
         var act = async () => await provider.GetAccessTokenAsync();
         await act.Should().ThrowAsync<InvalidOperationException>()
             .WithMessage("*no usable token source*");
     }
@@ -231,50 +154,13 @@
-    [Fact]
-    public async Task Refresh_token_grant_posts_expected_form()
-    {
-        HttpRequestMessage? seen = null;
-        string? body = null;
-        var storePath = Path.Combine(Path.GetTempPath(), "adfs-local-token-test-" + Guid.NewGuid().ToString("N") + ".json");
-        try
-        {
-            LocalDevAdfsTokenStore.Save(storePath, new LocalDevAdfsTokenRecord
-            {
-                RefreshToken = "refresh-abc",
-                AccessToken = "old-access",
-                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
-            });
-
-            var provider = CreateProvider(
-                options: new DynamicsWebApiOptions
-                {
-                    AuthMode = DynamicsAuthMode.AdfsOAuth,
-                    AuthorityUri = "https://sts.example.local/adfs",
-                    ClientId = "client-xyz",
-                    ResourceUri = "https://jesus.example.local/",
-                    LocalDevTokenStorePath = storePath,
-                    AllowLocalDevPasswordGrant = false,
-                    TimeoutSeconds = 10
-                },
-                secrets: new Dictionary<string, string>(),
-                responder: request =>
-                {
-                    seen = request;
-                    body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
-                    return JsonResponse("""{"access_token":"refreshed-001","expires_in":900,"refresh_token":"refresh-abc"}""");
-                });
-
-            var token = await provider.GetAccessTokenAsync();
-            token.Should().Be("refreshed-001");
-            seen!.RequestUri!.AbsoluteUri.Should().Be("https://sts.example.local/adfs/oauth2/token");
-            body.Should().Contain("grant_type=refresh_token");
-            body.Should().Contain("refresh_token=refresh-abc");
-            body.Should().Contain("client_id=client-xyz");
-        }
-        finally
-        {
-            if (File.Exists(storePath))
-            {
-                File.Delete(storePath);
-            }
-        }
-    }
+    [Fact]
+    public async Task File_based_token_store_throws_not_supported()
+    {
+        var actLoad = () => LocalDevAdfsTokenStore.TryLoad("dummy", out _);
+        actLoad.Should().Throw<NotSupportedException>();
+
+        var actSave = () => LocalDevAdfsTokenStore.Save("dummy", new LocalDevAdfsTokenRecord());
+        actSave.Should().Throw<NotSupportedException>();
+    }
```
