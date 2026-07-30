我已讀完全部指定檔案，並額外核對了同一 repo 中今天稍早已完成的 gap audit（`.ccg/tasks/dynamics-connection-compatibility/research/phase456-current-gap-audit-20260730.md`）與已存在的 CCG 雙模型執行紀錄（`.ccg/dual-model-runs/...dynamics-adfs-diagnostic-security*`）。以下分析與該份 gap audit 的結論一致且互補，不重複跑一次相同稽核。

---

## Recommended design

**核心決策：完全移除檔案型 token persistence，Provider 只保留「秘密提供者參考」與「Process-local 快取」兩種來源；DiagnosticsController 的互動式探測改為「不落地、單次記憶體內交換、立即丟棄、輸出僅剩 sanitized boolean/category」。**

逐題回答：

**Q1 — 是否完全移除 `LocalDevAdfsTokenStore` / `LocalDevTokenStorePath` / 檔案型 persistence？**
是，全部移除，不做「加密後仍落地」的折衷。理由：
- **Trust boundary 不成立**：目前同一個檔案路徑被兩個獨立呼叫端寫入——`AdfsOAuthTokenProvider.TryPersistTokens`（WebApi 專案）與 `DiagnosticsController.AdfsCallback` / `AdfsTokenProbe`（ChurchReport 專案）。SPEC 要求「唯一 owner」，但這裡是雙寫，任一方都可能在另一方讀取期間覆寫，不存在單一 owner。
- **最長生命週期無界**：`Save()` 只有 `File.WriteAllText`，沒有 expiry、沒有 revoke-triggered 刪除、沒有 process shutdown 時的 deterministic cleanup；就算加密，過期的明文 claims（AuthorityUri/ResourceUri/ClientId）仍會無界殘留在磁碟。
- 這與 PRD「session/profile/token/credential/cache leakage as a zero-tolerance release blocker」與「Memory, timers ... must have deterministic lifecycle ownership and disposal」直接衝突。
- `AdfsOAuthTokenProvider` 本身已經有一個合格的短生命週期快取（`_cachedToken` / `_expiresAt`，Generation-owned、Dispose 時清空），這才是唯一該保留的「持有 token」的地方。

**Q2 — `AdfsOAuthTokenProvider` 應允許的 token source（區分診斷／Local Gateway／未來 Central Gateway）**

| 場景 | 允許來源 | 理由 |
| --- | --- | --- |
| 短期人工診斷（DEBUG probe） | 僅限 request-scoped 記憶體變數，探測完立即捨棄；**不**回饋進 `AdfsOAuthTokenProvider` 的任何快取或秘密來源 | 避免診斷路徑變成隱性 production token 供給管道 |
| Local Gateway（目前） | `CredentialReferenceName`（預先核發 bearer）或 `RefreshTokenSecretName`（由秘密提供者/User Secrets/環境變數解析，值來自人工一次性把診斷結果放入秘密提供者） | 這是現有兩個「秘密參考」路徑，本來就符合「Token 不得寫入 JSON、記錄或例外」；差別只在於「不再有第三個檔案來源」 |
| 未來 Central Gateway | 僅 `CredentialReferenceName`，且必須先通過 design.md/prd.md 定義的「target-specific non-password service-workload proof」（例如憑證式 client credentials、gMSA/Windows、或已驗證的服務端 refresh 流程） | Central 是多產品正式路徑，refresh_token 若源自互動式使用者登入不滿足「non-password service-workload」要求；在該證明完成前，IFD profile 必須維持 blocked（PRD 原文即如此規定） |

`AllowLocalDevPasswordGrant` 維持現狀不動——它本來就是 local-dev-only、預設 false，且 jesus ADFS 已證實拒絕 password grant，不在本次切片範圍。

**Q3 — DiagnosticsController 是否保留 in-memory-only probe？**

保留，但收斂成「純記憶體、單次、立即丟棄」的最小 probe，而非完全比照 `Invoke-AdfsTokenProbe.ps1` 全面退役。原因：
- design.md/prd.md 明確保留「operator-only validation/discovery workflow」與「AD FS IFD 的 target-specific 冷啟動 non-password-workload proof」——在真正的服務端非密碼流程存在之前，互動式 authorization_code + 一次 WhoAmI 是目前唯一能產生「此 ADFS/ClientId/Resource 組合可行」證據的手段。
- `Invoke-AdfsTokenProbe.ps1` 的退役理由是它接受帳密（ROPC 風險），而目前 Controller 走的是 authorization_code（無帳密），風險層級不同，不需要同等力度的全面關閉。
- 但目前實作把交換結果**寫檔＋回顯敏感欄位**，這是可以修的缺陷，不是必須整個功能報廢的理由。

維護成本比較：
- **保留精簡 probe**：多維護 ~150 行程式碼與對應 RED tests；換來的是本機開發者仍可自行驗證 ADFS 可行性，不必每次改走 WinRM/DC 手動流程。
- **全面退役**：維護成本最低（一個 throw），但會把「本機互動式可行性驗證」這個能力完全移出程式，之後任何人要驗證 ADFS 只能手動用瀏覽器/Postman 組 URL，且更容易誤觸真正的密碼流程或把結果貼到不受控的地方——安全性未必更高，只是把責任轉嫁給人工操作。

結論：保留，但整個 slice 的心臟是把它改成「fail-closed by default、除非明確 sanitize 通過」。

**Q4 — OAuth state 生命週期**

現況：`AdfsAuthorize` 不論 `go` 參數為何都會先寫入 Session state；`AdfsCallback` 的 error / state mismatch / missing code / success / exception 五條路徑都只「讀」不「刪」。

建議：
1. **建立時機**：只在 `go=1`（真正要 redirect）時才建立並寫入 state；preview（`go` 未帶）不建立，避免每次好奇心點開 preview 就佔用一個 Session slot。
2. **讀取即刪除（read-then-delete）**：`AdfsCallback` 進入時第一件事就是 `Session.GetString` + 立刻 `Session.Remove`，比對放在刪除之後，確保 state 只能被消費一次（防止 callback 被重放/併發呼叫兩次都判定為合法）。
3. **五條路徑統一 cleanup**：用 `try/finally` 或提前的 read-then-delete 保證 error、state mismatch、missing code、success、exception 都已經移除 Session 值，不需要在每個分支各自呼叫一次。
4. **bounded 存活期**：state 值旁邊多存一個建立時間戳；即使 Session cookie 本身還活著，超過例如 5～10 分鐘的 state 一律視為過期拒絕，涵蓋 timeout/abandonment 情境。
5. Session 作為 bounded one-time state owner 是合適的——前提是上述四點都落實；Session 本身已有 `[Authorize]` 保護且是每使用者獨立的，不需要換成別的 store。

**Q5 — 應移除／sanitize 的欄位**

必須整批移除或改成固定 sanitized category（不得依請求內容變動）的欄位：
`authority`、`resource`、`clientId`、`redirectUri`、`authorizeUrl`、`tokenUrl`、`tokenStorePath`、`bodyPreview`、`whoAmIBody`、`processUser`（`Environment.UserName`）、`error`/`innerError` 的原始 exception message、`resultFile` 路徑、以及 `GetSessionInfo()` 回傳的 `SessionId`。`Trace.WriteLine` 中含 `authorizeUrl`/`clientId`/`redirectUri` 的兩行也必須移除。
允許保留的欄位：`ok`（bool）、`stage`（固定列舉：`preview` / `redirected` / `callback-error` / `state-invalid` / `code-missing` / `token-exchange-failed` / `whoami-failed` / `success` / `exception`）、`httpStatusCategory`（例如 `"2xx"`/`"4xx"`/`"5xx"`，不回傳實際 status code 以外的細節時可放整數 status code，因為它不是敏感值）、`serverTime`。
所有 `/diagnostics/*` 回應都應明確加上 `Cache-Control: private, no-store`（而不是只依賴全域的 `no-store, no-cache, must-revalidate, max-age=0`）——repo 內已有 `ChurchReport/Filters/StrictNoCacheFilter.cs` 並全域註冊（`no-store` 已含在其中），可以直接沿用而不必新增機制；若要精確符合題目要求的 `private, no-store` 字面值，可在 Controller 層另加一個輕量 `[ServiceFilter]` 或在 action 內顯式覆寫 header，兩者擇一即可，不需要重造一套 cache-control 系統。

**Q6 — RED test matrix（見下方獨立章節）**

**Q7 — 範圍風險與 in-scope/out-of-scope**（見下方獨立章節）

---

## Root-cause confirmation

已核對原始碼，7 項根因證據全部屬實，無需修正：

1. `LocalDevAdfsTokenStore.Save`（Abstractions 版）：`JsonSerializer.Serialize` 後 `File.WriteAllText`，`AccessToken`/`RefreshToken` 明文。（`LocalDevAdfsTokenStore.cs:69-86`）
2. `AdfsOAuthTokenProvider.TryPersistTokens`：token 交換成功後呼叫 `LocalDevAdfsTokenStore.Save`，並且 `TryResolveRefreshToken`/`GetAccessTokenAsync` 也會讀同一檔案。（`AdfsOAuthTokenProvider.cs:271-298`, `239-268`, `109-119`）
3. `DiagnosticsController.AdfsCallback`：寫入 `LocalDevAdfsTokenStore.Save`，並把 `tokenUrl`/`redirectUri`/`clientId`/`resource`/`bodyPreview`/`whoAmIBody`/`tokenStorePath` 放進 `result` 字典，再由 `WriteProbeResultAsync` 寫成 `Logs/adfs-token-probe-latest.json`。（`DiagnosticsController.cs:182-293`, `545-587`）
4. `AdfsAuthorize` 對任何 `go` 值都先 `Session.SetString`；`AdfsCallback` 的 error（157-163）、state mismatch（166-173）、missing code（175-180）、成功（192-281）、exception（282-292）五條路徑都沒有呼叫 `Session.Remove`。
5. `SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak` 確實是 tracked 檔案，內容與 Abstractions 版本幾乎相同（多一個 `ResolveDefaultPath()`），本身註解就寫「必須 gitignore，不可提交」卻仍被追蹤。
6. 未驗證（超出可讀檔案範圍的執行時行為），但與 `#if DEBUG` 包裹一致，屬合理推論。
7. `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md:560` 與 `prd.md:131-136` 已明文要求 IFD 用 target-specific non-password service-workload proof，且今天的 gap audit（`phase456-current-gap-audit-20260730.md:28-31, 60, 92-95, 141`）已把此問題列為 Critical #1，並提出幾乎相同的修復方向。

---

## Exact files to modify／delete

**刪除：**
- `SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak`（tracked 危險備份，直接刪除，不留 `// removed` 註解）

**修改（移除檔案 persistence 相關成員，其餘不動）：**

```diff
--- a/SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs
+++ b/SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs
@@
-public static class LocalDevAdfsTokenStore
-{
-    ...TryLoad/Save 檔案 I/O 實作全部移除...
-}
+// 此型別與檔案已整體移除：AD FS access/refresh token 不得以明文檔案持久化。
+// 詳見 dynamics-adfs-diagnostic-security slice 根因分析。
```
（實務上整個檔案刪除，`LocalDevAdfsTokenRecord`/`LocalDevAdfsTokenStore` 型別一併移除；`DynamicsWebApiOptions.LocalDevTokenStorePath` 與 `ProductDynamicsOptions.EmbeddedModeOptions.LocalDevTokenStorePath` 欄位同步移除，appsettings 中對應的 `"LocalDevTokenStorePath"` 鍵值一併刪除。）

```diff
--- a/SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs
+++ b/SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs
@@ GetAccessTokenAsync
-            // 1.5) local-dev token store 仍有效的 access_token 可直接用；...
-            var storePath = ResolveTokenStorePath();
-            if (LocalDevAdfsTokenStore.TryLoad(storePath, out var stored) && ...)
-            { ... }
-
             // 2) 快取未過期直接重用。
@@ TryResolveRefreshToken
-        var storePath = ResolveTokenStorePath();
-        if (LocalDevAdfsTokenStore.TryLoad(storePath, out var record) && ...)
-        { refreshToken = record!.RefreshToken; return true; }
-
         return false;
@@ RequestNewTokenAsync
-            TryPersistTokens(token.AccessToken, token.ExpiresInSeconds, token.RefreshToken);
             return new TokenResponse(token.AccessToken, token.ExpiresInSeconds);
@@
-    private string? ResolveTokenStorePath() { ... }
-    private void TryPersistTokens(...) { ... }
```

```diff
--- a/SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs
@@ AdfsAuthorize
-            var state = Guid.NewGuid().ToString("N");
-            HttpContext.Session.SetString(AdfsOAuthStateSessionKey, state);
+            var shouldGo = ...;
+            string? state = null;
+            if (shouldGo)
+            {
+                state = Guid.NewGuid().ToString("N");
+                HttpContext.Session.SetString(AdfsOAuthStateSessionKey, state);
+                HttpContext.Session.SetString(AdfsOAuthStateCreatedAtKey, DateTimeOffset.UtcNow.ToString("o"));
+            }
@@
-            var preview = new Dictionary<string, object?> { ...authority/resource/clientId/redirectUri/authorizeUrl... };
+            var preview = new Dictionary<string, object?>
+            {
+                ["ok"] = false,
+                ["stage"] = "authorize-preview",
+                ["serverTime"] = DateTime.Now.ToString("o")
+            };
@@
-            Trace.WriteLine("[ADFS-AUTH] preview authorizeUrl=" + authorizeUrl);
@@
-            Trace.WriteLine("[ADFS-AUTH] redirect to authorize. redirectUri=" + redirectUri + " clientId=" + clientId);
+            // 不再記錄 URL/ClientId；只保留是否導向的布林事件（如需要）。
@@ AdfsCallback
-            var expectedState = HttpContext.Session.GetString(AdfsOAuthStateSessionKey);
+            var expectedState = HttpContext.Session.GetString(AdfsOAuthStateSessionKey);
+            var expectedStateCreatedAt = HttpContext.Session.GetString(AdfsOAuthStateCreatedAtKey);
+            HttpContext.Session.Remove(AdfsOAuthStateSessionKey);      // read-then-delete：涵蓋所有後續分支
+            HttpContext.Session.Remove(AdfsOAuthStateCreatedAtKey);
+            // 加入 bounded 存活期檢查（例如 10 分鐘），過期一律視為 invalid state。
@@
-                result["tokenUrl"] = tokenUrl; result["redirectUri"] = redirectUri;
-                result["clientId"] = clientId; result["resource"] = resource;
+                // 不回顯任何端點/ClientId/Resource。
@@
-                result["bodyPreview"] = TrimBody(body);
+                // 不回顯上游 body。
@@
-                LocalDevAdfsTokenStore.Save(storePath, new LocalDevAdfsTokenRecord { ... });
-                result["tokenStorePath"] = storePath;
+                // 不落地；accessToken 只存在本方法的區域變數，方法結束即由 GC 回收。
@@
-                result["whoAmIBody"] = who.BodyPreview;
+                // 只保留 result["whoAmIOk"]。
@@ GetSessionInfo
-                SessionId = HttpContext.Session.Id,
+                // SessionId 移除，不回顯。
```

（`AdfsTokenProbe`、`GetTokenStorePath`、`WriteProbeResultAsync` 三個方法在移除檔案落地與敏感欄位後，若已無其他用途則整段刪除，改由 `AdfsAuthorize`/`AdfsCallback` 內完成單次 in-memory WhoAmI 驗證；不再需要獨立的「用 store 裡的 refresh_token 重新整理」路徑，因為已無 store。）

**appsettings（僅刪除欄位，不變更其餘結構）：**
- `SpeechMessageProducts.ChurchReport/appsettings.json` 的 `DynamicsAccess:Embedded:LocalDevTokenStorePath` 整行移除。
- `SpeechMessageProducts.ChurchReport/appsettings.Development.json` 若有對應鍵，同步移除（今日 gap audit 已指出 Development override 仍保留 inactive Embedded metadata，屬同一個清理範圍，但完整搬遷到 deployment-owned 邊界是 Phase 5 的既定工作，本切片只移除 token-store 路徑本身，不做整段 Embedded 區塊搬遷）。

**測試：**
- `SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs` 中 `Refresh_token_grant_posts_expected_form` 目前依賴 `LocalDevAdfsTokenStore.Save`/`LocalDevTokenStorePath` 建立測試前置資料——型別移除後必須改寫成直接注入 `RefreshTokenSecretName` 對應的 secret（走 `DictionarySecretResolver`），驗證同一份「送出 refresh_token 表單」行為，但不再經過檔案。

---

## RED test matrix

| # | 測試 | 涵蓋缺陷 | 預期斷言 |
| --- | --- | --- | --- |
| 1 | `LocalDevAdfsTokenStore` 型別已不存在於任一組件 | 檔案落地本體 | 編譯層級證明：反射掃描 `SpeechMessage.Dynamics.Abstractions` + `SpeechMessage.Dynamics.WebApi` 組件，`GetTypes()` 中不存在名稱包含 `LocalDevAdfsTokenStore` 的型別 |
| 2 | `AdfsOAuthTokenProvider` 成功交換 token 後，指定的臨時目錄下沒有任何新檔案 | Provider 端寫檔 | 對臨時目錄做 `Directory.GetFiles` 前後快照比對，交換前後檔案數不變 |
| 3 | `AdfsCallback` 成功路徑回應 JSON 不含 `authority`/`resource`/`clientId`/`redirectUri`/`tokenUrl`/`bodyPreview`/`whoAmIBody`/`tokenStorePath` 任一 key | Controller 回顯 | 對回應 JSON 做 key allowlist 斷言（只允許 `ok`/`stage`/`serverTime`/`whoAmIOk`） |
| 4 | `AdfsCallback` 成功後，ChurchReport 的 `Logs` 目錄下沒有新的 `adfs-token-probe-latest.json`（或該檔案已整體不存在） | 診斷結果落地 | 呼叫前後對 `Logs` 目錄做快照比對 |
| 5 | `GetSessionInfo` 回應不含 `SessionId` | Session 回顯 | key allowlist 斷言 |
| 6 | `AdfsAuthorize(go=null)`（preview）呼叫後，`HttpContext.Session` 中不存在 OAuth state key | state 建立時機 | mock `ISession`，preview 呼叫後對 `SetString` 呼叫次數斷言為 0 |
| 7 | `AdfsCallback` 對 state mismatch / missing code / error / exception 四種路徑呼叫後，Session state key 均已被移除 | state one-time cleanup | 每個分支各自建立一個假 state，呼叫 callback，斷言 `Session.GetString(key)` 之後為 null |
| 8 | 同一個合法 state 被 callback 呼叫兩次（模擬重放/併發），第二次必須回傳 invalid-state，不得再次執行 token 交換 | state 單次消費 | 第二次呼叫時斷言沒有新的 HTTP token 請求被送出（計數器） |
| 9 | state 建立超過設定的 bounded 存活期（例如注入一個 11 分鐘前的時間戳）後 callback 判定 invalid | timeout/abandonment | 直接操控 session 中的建立時間戳，斷言回應 `stage="state-invalid"` |
| 10 | `AdfsCallback`/`AdfsAuthorize` 傳入已取消的 `CancellationToken`（若簽章支援）或以極短逾時模擬取消，斷言不遺留背景 Task、HttpClient 未 Dispose 計數為 0 | request cancellation/timeout | 用 tracking `HttpMessageHandler`/`HttpClient` 斷言呼叫結束後 `Dispose` 已被呼叫 |
| 11 | 例外路徑（token endpoint 丟出非預期例外）回應中 `error` 欄位不含原始 `ex.Message` 內容，只含固定 category 字串 | 例外訊息回顯 | 讓 stub handler 丟出含有 marker 字串的例外，斷言回應 JSON 不包含該 marker |
| 12 | 每個 `/diagnostics/*` action 回應皆含 `Cache-Control` header 且值包含 `no-store` 與 `private` | sanitization header 契約 | 對每個 action 個別呼叫並檢查 `Response.Headers["Cache-Control"]` |
| 13（既有，需保留/更新）| `AdfsOAuthTokenProviderTests.Legacy_adfs_token_probe_is_retired_...` | 舊 script 退役邊界 | 維持不動，作為既有 fail-closed 慣例的對照組 |
| 14 | `Refresh_token_grant_posts_expected_form`（改寫版） | Provider 仍可用 secret-based refresh token | 改為透過 `RefreshTokenSecretName` + `DictionarySecretResolver` 提供 refresh token，不再建立任何檔案，其餘斷言（表單內容）不變 |
| 15 | Provider dispose 後，殘留於任何欄位/靜態變數中都不含 access/refresh token 字串（用反射掃描實例欄位） | memory-leak / token 殘留 | Dispose 後用反射列舉私有欄位，斷言字串型欄位皆為 null 或不含先前注入的 marker token |

---

## Lifecycle／Session／memory-leak analysis

- **Session**：`AdfsOAuthStateSessionKey` 目前是唯一狀態，改為「建立時機延後到真正 redirect + read-then-delete + bounded TTL」後，Session 內不會再有無界存活的 OAuth 相關資料；`[Authorize]` 保證只有已登入使用者的 Session 能建立/消費該 key，符合「bounded one-time state owner」定位。移除 `SessionId` 回顯後，`GetSessionInfo` 端點也不再洩漏可用於 session fixation/hijacking 佐證的識別碼。
- **記憶體**：`AdfsOAuthTokenProvider` 既有的 Dispose 契約（`_gate` 排空 → 清空 `_cachedToken`/`_expiresAt` → Dispose HttpClient/Handler/CTS）已經是這次唯一該保留、且已被 `Disposed_provider_rejects_new_token_work_and_releases_owned_http_resources` 測試覆蓋的正確模式，本次修改不動它，只是拿掉檔案 I/O 分支，反而減少了一個「Dispose 後檔案裡仍留著舊 token」的殘留面。`DiagnosticsController` 的 `accessToken`/`refreshToken` 一律是方法內區域變數，方法返回後由 GC 回收，不需要額外顯式清除（C# 沒有可靠的記憶體清零機制，這裡對齊 `AdfsOAuthTokenProvider.ReadBoundedTokenResponseAsync` 已有的 `CryptographicOperations.ZeroMemory` 只用於位元組緩衝區，字串本身不強求清零，屬於既有專案慣例，不在本切片擴大範圍）。
- **背景資源**：`DiagnosticsController.CreateHttpClient()` 每次呼叫都新建 `HttpClient`/`SocketsHttpHandler`，目前用 `using` 包裹，Dispose 契約本身沒問題；移除欄位回顯不影響這部分。唯一要新增的驗證是 RED test #10，確認取消/逾時情境下這個 `using` 仍然正確觸發。
- **無新背景工作**：本切片不新增 timer、定時任務或長生命週期背景服務；`AdfsAuthorize`/`AdfsCallback` 仍是純同步-in-request 生命週期。

---

## Security／sanitization requirements

1. 所有 `/diagnostics/*` JSON 回應改採白名單序列化（明確列出允許欄位的 DTO 或固定 key 集合），禁止把整個內部 `Dictionary<string, object?> result` 直接序列化——這是目前最大的「新增一個欄位就自動外洩」風險來源。
2. 所有回應顯式設定 `Cache-Control: private, no-store`（沿用或搭配既有全域 `StrictNoCacheFilter`）。
3. 移除所有 `Trace.WriteLine`/`Debug.WriteLine` 中含 URL、ClientId、RedirectUri、Body 的呼叫；如需除錯事件，只記錄固定 category 字串。
4. 不得以任何形式（檔案、Session、Cache、靜態欄位、日誌）持久化 access_token / refresh_token；秘密只能透過 `ISecretResolver` 以參考名稱解析。
5. `AdfsOAuthStateSessionKey` 的值只是隨機 GUID，不含語意，可以留在 Session，但必須做到本文件 Q4 的四點（建立時機延後、read-then-delete、五分支 cleanup、bounded TTL）。
6. `[Authorize]` 目前只確認「已登入」，未確認「是否為 operator」；今日的 gap audit 已把這點列為既有已知缺口之一（"operator-only" 尚未落實）。建議至少加上角色/policy 檢查（例如既有 `ConfigurationGatewayOperationAuthorizer` 或站內既有的管理員角色機制），但**此項是否在本切片範圍內完成，取決於團隊是否已有現成的 operator policy 可直接掛上**；若沒有現成機制，本切片可以先做「sanitize 輸出」這一半，operator policy 收斂列為 Warning 而非本切片的阻斷項，避免臨時發明一套新的授權機制擴大範圍。

---

## Rollback and scope limits

**In-scope：**
- 刪除 `LocalDevAdfsTokenStore.cs.bak`
- 移除 `LocalDevAdfsTokenStore` 型別與其在 Abstractions/WebApi 的所有呼叫點
- 移除 `DynamicsWebApiOptions.LocalDevTokenStorePath`、`ProductDynamicsOptions.EmbeddedModeOptions.LocalDevTokenStorePath` 欄位與 appsettings 對應鍵
- `DiagnosticsController` 的欄位白名單化、Trace 清理、Session state 生命週期修正、`GetSessionInfo` 欄位收斂
- 對應的新增/改寫 RED→GREEN 測試

**Out-of-scope（明確不動）：**
- `Embedded`、Data8、`PowerPlatform.Dataverse.Client` 的任何刪除或行為變更
- `Package01FeeReadsEnabled`（維持 `false`）
- Phase 4～6 既定 Gate 與其驗收清單
- CE 8.2/9.1 routing、Admission/Capacity、Coordinator、Idempotency Ledger 等既有 Phase 4 未完成項目（這些是今日 gap audit 列出的其他 Critical 項，不屬於這次「ADFS 診斷安全」切片）
- 新增 operator policy 機制本身（若團隊沒有現成可掛的 policy，僅做 Warning 記錄，不在本切片內臨時發明）
- `AllowLocalDevPasswordGrant` 行為本身
- 把診斷結果自動寫回 secret provider——任何「診斷探測結果要拿來給 Local Gateway 長期使用」都必須是人工、帶外（out-of-band）動作，程式本身不得自動串接這條路徑

**Rollback：**
- 這是純刪除/收斂型變更，沒有新的資料結構或遷移，`git revert` 該 commit 即可完整還原；由於 `Package01FeeReadsEnabled` 全程維持 `false`，此切片本身在 revert 前後都不影響任何 production 流量路徑。
- 若 sanitize 後的 probe 破壞既有本機開發流程（例如某開發者仍依賴檔案型 refresh token 自動續期），正確做法是操作者手動把新取得的 refresh token 放進 User Secrets／環境變數對應的 `RefreshTokenSecretName`，而不是回退檔案落地機制。

---

## Critical／Warning／Info findings

**Critical**
1. `LocalDevAdfsTokenStore`（含 `.bak` 追蹤檔）以明文 `File.WriteAllText` 保存 access/refresh token，且無 owner-only ACL、無 expiry、無 deterministic delete，違反 PRD 的 zero-tolerance token leakage 要求。（`LocalDevAdfsTokenStore.cs:69-86`；`LocalDevAdfsTokenStore.cs.bak` 全檔）
2. `AdfsOAuthTokenProvider` 與 `DiagnosticsController` 對同一個 token store 檔案雙寫，不存在單一 trust boundary owner，違反 SPEC「唯一 owner」要求。（`AdfsOAuthTokenProvider.cs:271-298`；`DiagnosticsController.cs:250-259, 379-387`）
3. `DiagnosticsController` 的診斷回應與 `Logs/adfs-token-probe-latest.json` 會回顯/落地 authority、resource、clientId、完整 authorizeUrl/tokenUrl、上游 body preview、WhoAmI body、process identity；`Trace.WriteLine` 額外把 authorizeUrl/clientId/redirectUri 寫進系統 Trace。這些都是可持久化的憑證級 metadata 外洩面。（`DiagnosticsController.cs:96-140, 182-293, 545-587`）
4. `AdfsAuthorize`/`AdfsCallback` 的 OAuth state 在 error/state-mismatch/missing-code/success/exception 五條路徑都沒有確定性移除，且 preview 路徑也會無條件建立 state，形成可被重放或無界累積的 Session 佔用。（`DiagnosticsController.cs:84-141, 146-293`）

**Warning**
1. `GetSessionInfo` 端點回顯 `HttpContext.Session.Id`，雖然是 DEBUG-only 且需登入，仍屬不必要的識別碼外洩面，建議一併收斂。（`DiagnosticsController.cs:437-447`）
2. `[Authorize]` 目前只驗證「已登入」而非「operator」，operator-only 的定位尚未在授權層落實；今日 gap audit 已將此列為既有缺口，建議另立追蹤項而非塞進本切片臨時發明機制。
3. `AdfsCallback` 成功路徑仍會把新的 refresh_token 定期改寫進 store（若保留 refresh 邏輯但拿掉檔案，需要明確定義「這次交換出來的 refresh_token 之後去哪裡」——建議只回傳給操作者於終端顯示一次性 sanitized 成功訊息，不自動寫入任何 secret 來源，避免又形成一條隱性持久化路徑）。
4. `SpeechMessageProducts.ChurchReport/appsettings.json` 的 base `DynamicsAccess:Embedded` 區塊即使 `ExecutionMode=Embedded` 目前生效、`Gateway` 未啟用，仍同時攜帶 CRM OAuth/token-store metadata；這與 gap audit Critical #2 是同一個更大範圍問題，本切片只處理其中的 `LocalDevTokenStorePath` 欄位，其餘欄位搬遷留給 Phase 5 既定工作項。

**Info**
1. 既有 `docs/scripts/Invoke-AdfsTokenProbe.ps1` 退役慣例（fail-closed、固定 ASCII 訊息、無任何參數/檔案/HTTP）與既有 `AdfsOAuthTokenProviderTests.Legacy_adfs_token_probe_is_retired_...` 測試可直接作為本切片 sanitization 力度的參照基準，不需要另外發明一套標準。
2. `ChurchReport/Filters/StrictNoCacheFilter.cs` 已全域註冊 `no-store, no-cache, must-revalidate, max-age=0`，可直接沿用滿足「no-store」要求；若需要字面上的 `private` 值，只需在此基礎上小幅擴充，不必新建快取控制機制。
3. `AdfsOAuthTokenProvider` 既有的 Generation-owned Dispose/single-flight/HTTP lease 生命週期設計本身沒有缺陷，本切片不改動其結構，只移除檔案 I/O 分支，風險面因此縮小而非擴大。

---
SESSION_ID: f96693a3-51ff-4e31-978b-0a9518b2f232
