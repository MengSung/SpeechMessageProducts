# Dynamics AD FS 診斷安全切片：靜態分析報告

> 分析基準：實際讀取 `LocalDevAdfsTokenStore.cs`（Abstractions 版）、`AdfsOAuthTokenProvider.cs`、`DiagnosticsController.cs`、`LocalDevAdfsTokenStore.cs.bak`、`DynamicsWebApiOptions.cs`、`ProductDynamicsOptions.cs`、`DynamicsProfileDefinition.cs`、兩份 `appsettings.json`、`AdfsOAuthTokenProviderTests.cs`，以及 SPEC（`dynamics-gateway-hosting-version-routing.md`）與 `07-23-dynamics-connection-compatibility` 的 `design.md` / `phase3-tier-a-*.md`。所有敏感值（ClientId、Authority/Resource host、redirect URI、SID）在本報告中一律以佔位符表示。

---

## Recommended design

**核心判斷：SPEC 已經明文禁止「預設明文 Token 持久化」與「瀏覽器／使用者 Session 依賴」，但目前的三個檔案（`LocalDevAdfsTokenStore` × 2 份、`AdfsOAuthTokenProvider`、`DiagnosticsController`）系統性違反這條紅線。這不是「要不要收緊」的問題，而是「現行程式碼已偏離已核准 SPEC」的問題。** 佐證：

- `design.md:703`：「no plaintext token persistence by default」
- `design.md:560`：AdfsOAuth 必須是「target-specific cold-start proof of a non-password service/workload flow…and no browser/user-session persistence」
- `design.md:701`：診斷產出的驗證紀錄「must not persist credentials, tokens, passwords, Session identifiers, client identifiers, callback values, private VM addresses, complete AD FS/CRM endpoints, or secret-reference values」

而且 codebase 裡已經有一個**同語意的先例**可以直接複用：`AdfsOAuthTokenProviderTests.cs:290-307` 的 `Legacy_adfs_token_probe_is_retired_without_password_or_result_output_paths` 測試 GREEN 驗證了 `docs/scripts/Invoke-AdfsTokenProbe.ps1` 已經被退役成「fail-closed，不接受帳密參數、不寫檔、不呼叫 WhoAmI、只給出指向 `/diagnostics/adfs-authorize` 的固定 guidance」。**這個切片的正確做法，就是把同一套「fail-closed 退役」模式，套用到 `DiagnosticsController` 目前仍在做明文寫檔／回顯的路徑上，並移除底層的檔案型 Token Store。**

### 回答 Q1：是否完全移除 `LocalDevAdfsTokenStore` / `LocalDevTokenStorePath` / 檔案型 persistence？

**是，完全移除，不留替代的檔案型 persistence。** 理由：

1. SPEC 的措辭是「no plaintext token persistence **by default**」——這代表允許的例外必須有明確 trust boundary、owner、生命週期與 deterministic cleanup 佐證；而目前的實作完全沒有這些佐證：
   - Trust boundary：`Save()`/`TryLoad()` 是 `public static class`，沒有存取控制、沒有 ACL 設定、沒有加密，任何能讀 `Logs/` 目錄的程序或人都能取得 refresh token。
   - Owner：`AdfsOAuthTokenProvider.TryPersistTokens` 與 `DiagnosticsController.AdfsCallback/AdfsTokenProbe` 都會寫同一個檔案——**至少兩個獨立元件宣稱擁有同一份 Token 檔**，違反 SPEC 多處強調的「single owner」原則（例如 `design.md:134` profile generation 不共用 client/credential/token）。
   - 生命週期：`UpdatedAtUtc` 只是記錄時間戳，沒有任何 TTL、輪替或強制刪除機制；檔案會無限期存在於磁碟。
   - Deterministic cleanup：完全不存在。沒有 dispose、沒有 finalizer、沒有 host-shutdown hook 會刪除這個檔案。
2. `DynamicsWebApiOptions.LocalDevTokenStorePath` / `ProductDynamicsOptions.EmbeddedModeOptions.LocalDevTokenStorePath` 的存在本身，讓「正式 Profile 是否誤用 local-dev 檔案」變成一個執行期組態問題而非型別層保證——這與 SPEC `design.md:436`「Unknown alias 必須在 admission/factory/token 之前 fail closed」的精神相反：目前反而是「有設定就會用」。
3. `LocalDevAdfsTokenStore.cs.bak` 是 **tracked source**，即使 `.cs` 版本被移除，`.bak` 仍會留在 repo 裡形成第二套幾乎相同的明文 store 實作（見下方 Root-cause #5）——這本身就是外洩面，必須連同刪除。

**結論：不保留任何檔案型 token persistence 的「替代機制」。** 短期本機診斷改採「記憶體內、單次要求、離開作用域即被 GC」的模式（見 Q3），不引入新的持久化層。

### 回答 Q2：`AdfsOAuthTokenProvider` 應允許的 token source

依 SPEC `design.md:560` 與現有程式碼分三段：

| 情境 | 允許的 token source | 理由 |
|---|---|---|
| **短期診斷（DEBUG-only，本機開發者手動觸發）** | 僅記憶體內 authorization_code → access_token 交換，交換後立即使用、立即捨棄；不寫入 `RefreshTokenSecretName` 以外的任何儲存 | 用來證明「ADFS client 是否已註冊」「WhoAmI 是否可行」，是一次性人工證據，不是 runtime token source |
| **Local Gateway（`crm82`/`crm91` 開發環境）** | `CredentialReferenceName`（預先核發 bearer，經 Secret Resolver）或 `RefreshTokenSecretName`（經 Secret Resolver，值來自 User Secrets / 環境變數，非檔案） | 這兩個路徑已經存在於 `AdfsOAuthTokenProvider.RequestNewTokenAsync`/`TryResolveRefreshToken`，且都是「秘密參考名稱」而非明文——只是目前第二優先序被檔案型 store 插隊（見 Root-cause #2） |
| **未來 Central Gateway（正式多產品）** | 僅 `CredentialReferenceName`（confidential client + certificate 或 secret store 核發的 bearer），`AllowLocalDevPasswordGrant` 必須恆為 `false`；不允許任何 `LocalDevTokenStorePath` 或 refresh-token-from-file 路徑 | 對齊 `design.md:163`「Preferred replacement A is direct Web API v8.2 after ADFS OAuth client registration…」與非密碼服務流程要求 |

也就是說：`AdfsOAuthTokenProvider` 應該**完全移除**「local-dev token store 讀寫」這個 branch（目前程式碼第 109-119 行與第 251-260 行、271-298 行），只留 `CredentialReferenceName` → `RefreshTokenSecretName`（走 Secret Resolver）→ `AllowLocalDevPasswordGrant`（維持現狀，僅 local-dev 顯式旗標）三層。

### 回答 Q3：`DiagnosticsController` 是否保留記憶體內 probe？

**保留一個最小化、DEBUG-only、記憶體內、單次、無檔案輸出的 probe，取代目前的「callback+持久化+多欄位回顯」版本。** 比較：

| 面向 | 目前實作（callback 存檔＋token-probe 讀檔重試） | 建議實作（記憶體內單次探測） |
|---|---|---|
| 安全性 | 差：明文 token 落地、authority/resource/clientId/tokenUrl/bodyPreview/whoAmIBody 全部進 JSON 回應與檔案 | 好：access token 只存在於一次 HTTP 呼叫的區域變數中，方法返回後失去所有參考 |
| 可驗證性 | 高但代價過大：可重複探測、可離線檢視歷史結果檔 | 略低但仍足夠：每次都要重新走一次 authorization_code；但這正是「不留 runtime token source」的設計目標，不是缺點 |
| 維護成本 | 高：要同時維護檔案 schema、清理策略、跨請求狀態同步 | 低：沒有跨請求狀態，callback 處理完就結束 |

具體建議：把 `/diagnostics/adfs-authorize`（僅 preview + 手動 go=1 導向）保留，`/diagnostics/adfs-callback` 改為「code 換 token → 立即呼叫一次 WhoAmI → 組出**布林／狀態碼層級**的結果（例如 `ok`, `whoAmIHttpStatus`, `tokenAcquired`）→ 回應後不留 token 變數的任何強參考」，**刪除 `/diagnostics/adfs-token-probe`**（因為它的存在理由——「重用已存的 refresh token 而不必重新登入」——本質上就是在鼓勵 runtime 依賴這份診斷用 token，這正是 SPEC 要禁止的）。

### 回答 Q4：OAuth state 生命週期

State 必須是 **bounded、one-time、伺服器端** 的值，Session 仍適合承載，但目前實作只做了「建立」與「一次比對」，沒有「無論結果如何都移除」：

| 事件 | 目前行為 | 應有行為 |
|---|---|---|
| preview（`adfs-authorize` 不帶 `go=1`） | 已建立並寫入 Session（第 84-85 行），即使使用者只是在看 JSON 預覽也會建立 state | **不應在 preview 階段建立 state**；只有 `go=1` 真的要 redirect 時才建立，避免同一 Session 被多次 preview 覆寫合法的既有 state（TOCTOU：使用者開兩個分頁 preview，第二次 preview 會覆蓋第一次已經送出 redirect 的 state） |
| redirect（`go=1`） | 建立 state → 寫 Session → redirect | 建立 state → 寫 Session（含短 TTL 標記，如建立時間）→ redirect |
| callback error（`error` 參數存在） | 直接回應，**不移除 Session state**（第 157-163 行） | 移除 state 後才回應 |
| state mismatch | 直接回應，**不移除 Session state**（第 166-173 行） | 移除 state 後才回應（避免同一個殘留 state 被後續請求重放比對） |
| missing code | 直接回應，**不移除 Session state**（第 175-180 行） | 移除 state 後才回應 |
| success | 交換 token 成功後**不移除 Session state**（整個成功路徑都沒有 `Session.Remove`） | 一旦 state 比對通過（無論後續交換是否成功），立即移除，語意上 state 只驗證「這次 callback 屬於我剛才發起的 authorize」，不該在 token 交換失敗時仍保留給下一次 callback 重放 |
| exception | catch block（第 282-292 行）**不移除 Session state** | 移除 state 後才回應 |
| timeout / abandonment（使用者從未回到 callback） | 無機制；state 永遠留在 Session 直到 Session 過期 | 這是 Session 本身 TTL 該負責的部分；state key 本身不需要額外 TTL，只要**任何一次 callback 到達（無論成功/失敗/mismatch）就必須移除**，這樣就不會有「永久遺留」——真正的殘留只受 ASP.NET Session 過期時間限制，而 Session 過期是既有基礎設施保證，不是這個切片要重新發明的東西 |

**結論：Session 仍是合適的 bounded one-time state owner，但目前的移除邏輯完全缺失。** 修法很簡單：`AdfsCallback` 一進入方法就先 `TryGetAndRemove`（一次性讀取並清除），而不是現在的「Get 之後在多個 return 路徑各自忘記 Remove」。這也順帶解決「state mismatch 時攻擊者能否透過猜測重放」的疑慮——一次性消費後，同一個 state 不可能被第二次接受。

### 回答 Q5：需要移除／固定分類的欄位

見下方「Security／sanitization requirements」一節的完整表格。

### 回答 Q6：RED test matrix

見下方獨立章節。

### 回答 Q7：範圍邊界與 rollback

見下方「Rollback and scope limits」。

---

## Root-cause confirmation

逐項核對使用者提供的根因，全部在原始碼中確認屬實：

1. **`LocalDevAdfsTokenStore.Save`（Abstractions，`LocalDevAdfsTokenStore.cs:69-86`）**：`JsonSerializer.Serialize(record, …)` 直接把 `AccessToken`/`RefreshToken` 明文序列化寫入 `File.WriteAllText`，無加密、無 DPAPI、無 ACL。**確認。**
2. **`AdfsOAuthTokenProvider`**：
   - 讀：`TryResolveRefreshToken`（249-260 行）與 `GetAccessTokenAsync` 內的「1.5) local-dev token store」分支（109-119 行）都會讀取明文檔案。
   - 寫：`TryPersistTokens`（271-298 行）在 `RequestNewTokenAsync` 交換成功後（168 行 `TryPersistTokens(...)`）再次寫回明文檔案，即使這次交換用的是 `refresh_token` grant 而非 authorization_code。**確認，且比使用者描述更廣：不只 callback 路徑，連 provider 自己的 token refresh 循環也會持續覆寫這個檔案。**
3. **`DiagnosticsController`**：
   - `AdfsCallback`（147-293 行）：成功路徑寫入 `LocalDevAdfsTokenStore.Save`（251-259 行），並把 `authority`/`resource`/`clientId`/`tokenUrl`/`redirectUri`/`bodyPreview`（`TrimBody`，500 字元）/`whoAmIBody`/`ex.Message`/`ex.InnerException.Message` 全部塞進 `result` 字典，再由 `WriteProbeResultAsync` 寫入 `Logs/adfs-token-probe-latest.json`（545-587 行）並 `return Json(result)` 回給呼叫端。**確認，且 exception 路徑（282-292 行）同樣會把例外訊息寫入可持久化 JSON 與 HTTP 回應。**
   - `AdfsTokenProbe`（298-435 行）：同樣把 `authority`/`resource`/`clientId`/`whoAmI`（完整 WhoAmI URL）/`tokenStorePath`/`tokenUrl`/`bodyPreview`/`whoAmIBody` 寫入回應與檔案。**確認。**
4. **OAuth state 清理缺失**：`AdfsAuthorize`（77-141 行）在 preview 階段（未帶 `go=1`）就已呼叫 `HttpContext.Session.SetString(AdfsOAuthStateSessionKey, state)`（85 行）。`AdfsCallback` 的 error（157-163）、state mismatch（166-173）、missing code（175-180）、success（192-281，全程無 `Session.Remove`）、exception（282-292）五條路徑，逐一確認**沒有任何一條**呼叫 `HttpContext.Session.Remove(AdfsOAuthStateSessionKey)`。**確認，比描述更嚴重：連 preview（使用者可能根本不會真的登入）也會建立 state，擴大了殘留 state 的觸發面。**
5. **`LocalDevAdfsTokenStore.cs.bak`**：確認是 tracked 檔案（`.bak` 副檔名代表非編譯輸入，但 `git status` 顯示未被 gitignore/未被標記為刪除，且內容與 Abstractions 版幾乎逐行相同，額外多了 `ResolveDefaultPath()`），會被靜態掃描與 `git grep` 命中，形成第二處「教學文件+明文 token schema」的外洩面，即使它不會被編譯進二進位。**確認。**
6. **Release 行為**：`DiagnosticsController` 整個類別被 `#if DEBUG` 包住（32 行起、589 行 `#endif`），確認 Release 組建下 `/diagnostics/*` 路由不存在（404 符合預期）。**確認，且這代表本切片的攻擊面主要限於 DEBUG／本機開發環境，不是 Production 對外暴露面——但仍是 release blocker，因為 (a) 明文檔案一旦在本機或 CI agent 產生就可能被誤提交或被同機其他程序讀取，(b) `AdfsOAuthTokenProvider` 的檔案讀寫路徑不受 `#if DEBUG` 保護，只要 `LocalDevTokenStorePath` 被設定（appsettings 中 `ChurchReport/appsettings.json:590` 確實設定了 `"LocalDevTokenStorePath": "Logs/adfs-sunnyvalechback-local-token.json"`），Release 組建一樣會寫明文檔案。** 這是本次分析新發現、使用者原始描述未明確涵蓋的擴大點。
7. **SPEC 對照**：`design.md:701` 明文禁止診斷輸出持久化 credential/token/Session ID/client ID/callback 值/完整端點；`design.md:703` 明文禁止預設明文 token persistence；`design.md:560` 明文禁止 browser/user-session persistence 作為 runtime token source。**確認，現況與 SPEC 直接衝突。**

---

## Exact files to modify／delete

| 檔案 | 動作 | 說明 |
|---|---|---|
| `SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak` | **刪除** | tracked 但未使用的重複明文 store 實作；無編譯依賴，移除無風險 |
| `SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs` | **刪除** | 連同 `LocalDevAdfsTokenRecord` 型別一併移除 |
| `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs` | **修改** | 移除第 109-119 行（load-from-store 快取分支）、249-260 行（`TryResolveRefreshToken` 的 store fallback）、262-268 行（`ResolveTokenStorePath`）、271-298 行（`TryPersistTokens` 全數）、168 行對 `TryPersistTokens` 的呼叫；`RequestNewTokenAsync` 回傳值不再攜帶 `RefreshToken`（因為沒有地方要存它） |
| `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs` | **修改** | 移除 `LocalDevTokenStorePath` 屬性（89-92 行） |
| `SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs` | **修改** | 移除 `EmbeddedModeOptions.LocalDevTokenStorePath` 屬性（174-177 行）；同段落 XML doc 已經自我矛盾（176 行寫「正式環境不得使用」卻仍存在於型別上，本身就是需要移除的訊號） |
| `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileDefinition.cs` | **修改** | `CloneOptions` 中移除 `LocalDevTokenStorePath = source.LocalDevTokenStorePath` 這一行（125 行），與 Options 型別變更同步 |
| `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs` | **修改** | 移除 `LocalDevAdfsTokenStore.Save`／`TryLoad` 呼叫、`GetTokenStorePath()`；`AdfsCallback` 交換成功後只用區域變數呼叫一次 WhoAmI、不落地；**整個 `AdfsTokenProbe`（298-435 行）與 `GetTokenStorePath`（491-510 行）建議直接刪除**（其存在理由本身違反 SPEC）；`WriteProbeResultAsync`（545-587 行）改為只寫入 sanitized 固定分類欄位或直接移除檔案輸出、只保留 HTTP 回應；state 清理邏輯依 Q4 修正 |
| `SpeechMessageProducts.ChurchReport/appsettings.json` | **修改** | 移除 `DynamicsAccess:Embedded:LocalDevTokenStorePath`（590 行）；`RedirectUri`（589 行）與 `ClientId`（587 行）維持，但確認不再有任何程式路徑會把它們寫入檔案 |
| `SpeechMessage.Dynamics.Gateway/appsettings.json` | **不需修改** | 未使用 `LocalDevTokenStorePath`；`AuthMode: AdfsOAuth` 走的是 `SecretReference`/`*SecretName`，本來就符合 SPEC |
| `SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs` | **修改** | `Refresh_token_grant_posts_expected_form`（233-280 行）目前依賴 `LocalDevAdfsTokenStore.Save`/`LocalDevTokenStorePath` 建立測試前置狀態，型別移除後必須改用 `RefreshTokenSecretName` + `DictionarySecretResolver` 建立同等測試前置；新增 RED tests（見下節） |

**不動、確認保留的部分**（避免範圍蔓延）：`ISecretResolver`、`CredentialReferenceName`/`RefreshTokenSecretName`/`AllowLocalDevPasswordGrant` 三個既有欄位、`AdfsAuthorize` 的 preview JSON 端點本體、`Package01FeeReadsEnabled=false`、Embedded/Data8/`PowerPlatform.Dataverse.Client`、Phase 4-6 既有其他程式。

---

## RED test matrix

以下測試應先寫成 **RED**（在移除明文 store 與加上 sanitization 前失敗），證明缺陷存在，再實作修正使其轉 GREEN。全部歸屬 `SpeechMessage.Dynamics.Tests` 或 ChurchReport 測試專案（若無對應 controller 測試專案，需新增最小 integration test host）。

| # | 測試名稱（意圖） | 驗證內容 | 對應根因 |
|---|---|---|---|
| 1 | `AdfsOAuthTokenProvider_never_writes_any_file_during_token_acquisition` | 用 `FileSystemWatcher` 或臨時目錄快照，斷言整個 `GetAccessTokenAsync` 生命週期（含 refresh_token grant 成功）不建立任何檔案 | 根因 #1、#2 |
| 2 | `AdfsOAuthTokenProvider_type_has_no_local_token_store_dependency` | 反射／編譯期斷言：`DynamicsWebApiOptions` 不再有 `LocalDevTokenStorePath` 屬性；`AdfsOAuthTokenProvider` 程式集不再參考 `LocalDevAdfsTokenStore` 型別 | 根因 #1、#2 |
| 3 | `DiagnosticsController_callback_success_does_not_persist_token_to_disk` | Integration test：模擬合法 code+state callback，斷言 `Logs/` 目錄前後檔案數不變 | 根因 #3 |
| 4 | `DiagnosticsController_response_never_echoes_access_or_refresh_token` | 對 `AdfsCallback`/若保留的探測端點回應序列化結果做遞迴掃描，斷言不含任何符合 JWT 格式或等於已知假 token 值的字串 | 根因 #3 |
| 5 | `DiagnosticsController_response_never_echoes_authority_resource_clientid_tokenurl` | 同上，斷言回應 JSON 不含 `authority`/`resource`/`clientId`/`tokenUrl`/`redirectUri`/`whoAmI` 完整值等 key，或其值均已替換為固定分類字串（如 `"redacted"`） | 根因 #3、SPEC `design.md:701` |
| 6 | `DiagnosticsController_exception_message_is_sanitized_not_raw` | 強制底層 HttpClient 丟出含假敏感字串的例外，斷言回應與（若保留）記錄中不含該字串，只含固定分類（如 `"upstream-error"`） | 根因 #3 |
| 7 | `AdfsCallback_error_path_removes_oauth_state_from_session` | 帶 `error` 參數呼叫 callback，斷言之後 `Session.GetString(stateKey)` 為 null | 根因 #4 |
| 8 | `AdfsCallback_state_mismatch_removes_oauth_state_from_session` | 帶不符 `state` 呼叫，斷言之後 Session 已無該 key | 根因 #4 |
| 9 | `AdfsCallback_missing_code_removes_oauth_state_from_session` | 不帶 `code` 呼叫，斷言 Session 已清除 | 根因 #4 |
| 10 | `AdfsCallback_success_removes_oauth_state_from_session` | 合法交換成功後，斷言 Session 已清除（目前完全沒有 Remove，此測試會直接 RED） | 根因 #4 |
| 11 | `AdfsCallback_exception_path_removes_oauth_state_from_session` | Token 端點回傳例外時，斷言 Session 已清除 | 根因 #4 |
| 12 | `AdfsOAuthState_is_one_time_consumable` | 用同一個合法 state 呼叫 callback 兩次，第二次必須因 state 已被消費而回傳「Invalid or missing OAuth state」，而不是重新比對到殘留值 | 根因 #4 |
| 13 | `AdfsAuthorize_preview_without_go_does_not_create_session_state` | 呼叫 `adfs-authorize`（不帶 `go=1`），斷言 Session 未被寫入 state（依 Q4 建議修正後的行為） | 根因 #4（擴大發現） |
| 14 | `DiagnosticsController_all_diagnostic_responses_set_cache_control_private_no_store` | 對 `Index`/`AdfsAuthorize`/`AdfsCallback`/`session`/`performance` 逐一斷言 `Cache-Control: private, no-store` | Q5 |
| 15 | `AdfsCallback_respects_request_cancellation` | 傳入已取消的 `CancellationToken`（或請求中斷模擬），斷言不會繼續呼叫 WhoAmI、不會寫入任何狀態，且底層 `HttpClient`/`HttpResponseMessage`/`Stream` 都被正確 dispose（無殘留） | Q6：request cancellation／stream disposal |
| 16 | `AdfsCallback_disposes_http_response_and_content_stream_on_every_path`（error/mismatch/missing-code/success/exception 五路徑各一組或參數化） | 用可觀察 dispose 狀態的 fake `HttpMessageHandler`／`HttpContent`，斷言 `using` 涵蓋所有 early-return 路徑，不留未 dispose 的 `HttpResponseMessage` | Q6：HTTP response/stream disposal |
| 17 | `DiagnosticsController_leaves_no_background_resource_after_request_completes` | 完成一次 callback 請求後，斷言沒有殘留 timer、沒有 fire-and-forget `Task`、目前進程的 handle/thread 數量回到基準值附近（避免 flaky，用寬鬆門檻） | Q6：background resource residue |
| 18 | `LocalDevAdfsTokenStore_type_removed_from_solution`（編譯期／反射斷言，取代原本存在測試） | 斷言 `SpeechMessage.Dynamics.Abstractions` 與 `SpeechMessage.Dynamics.WebApi` 程式集都不再輸出 `LocalDevAdfsTokenStore`/`LocalDevAdfsTokenRecord` 型別 | 根因 #1、#5 |
| 19 | `Repository_does_not_contain_LocalDevAdfsTokenStore_bak_file` | 檔案系統斷言 `.bak` 檔已不存在於 repo | 根因 #5 |
| 20 | `Refresh_token_grant_uses_secret_resolver_not_file_store`（既有 `Refresh_token_grant_posts_expected_form` 改寫） | 用 `RefreshTokenSecretName` + `DictionarySecretResolver` 取代原本用 `LocalDevAdfsTokenStore.Save` 建立前置狀態的做法 | 型別移除後的相容性回歸測試 |

---

## Lifecycle／Session／memory-leak analysis

**Token Provider（`AdfsOAuthTokenProvider`）本身的生命週期管理是本次讀到的程式碼中做得最好的部分**——`Dispose`/`DisposeAsync` 有 single-flight gate、`CancellationTokenSource` 串接、`ObjectDisposedException` fail-fast、`ArrayPool` 歸還前 `CryptographicOperations.ZeroMemory`。這部分**不需要修改**，本切片唯一要動的是移除它「順手」讀寫檔案的兩個分支——這兩個分支本身沒有生命週期保護（沒有鎖住檔案的 handle 生命週期、沒有隨 Provider Dispose 而清除檔案）。

**問題集中在 `DiagnosticsController` 這種「每請求一個 controller 實例」的模型上：**

1. **Session Leakage**：`AdfsOAuthStateSessionKey` 沒有確定性移除路徑（Root-cause #4），這是最直接的 Session 資源洩漏——不是「記憶體洩漏」而是「敏感狀態超過其應有生命週期地留在 Session store 裡」，若 Session provider 是分散式（如 SQL/Redis Session），代表這個殘留值會離開單一 process 邊界，擴大暴露面。
2. **檔案系統即「持久化記憶體」**：`LocalDevAdfsTokenStore` 把本應是短生命週期的 token 變成無限期存活的檔案內容，等同於把「process 記憶體洩漏」問題轉移成「更嚴重的磁碟洩漏」——因為即使 process 重啟、Provider 被 Dispose，檔案內容仍然有效可用。這是本分析認為**優先權高於單純的記憶體衛生**的理由：目前實作沒有 memory leak（`AdfsOAuthTokenProvider` 的 dispose 邏輯正確清空 `_cachedToken`），但有更嚴重的「across-restart persistence」問題。
3. **HttpClient/Stream 生命週期**：`DiagnosticsController.CreateHttpClient()`（512-521 行）每次呼叫都新建一個 `HttpClient`（含新的 `SocketsHttpHandler`），雖然有 `using`（194 行 `using var http = CreateHttpClient()`），但**沒有共用 `IHttpClientFactory`**，在高頻診斷呼叫下會有 socket 耗盡風險（Diagnostics 是 DEBUG-only、低頻，風險可接受，但若保留 probe 端點，建議至少評估改用 `IHttpClientFactory` 對齊 `AdfsOAuthTokenProvider` 的做法）。`response`/`whoResponse` 都有 `using`（204、350、533、527 行），這部分**沒有洩漏**，Q6 中列的 dispose 測試主要是防止未來重構破壞這個既有正確性，而不是修正現有 bug。
4. **背景資源**：`Trace.WriteLine`（128、139、581 行）會把 authorizeUrl／clientId 等寫入 Trace listener——若正式環境有掛 Trace 收集器（如 ETW/檔案），這些欄位一樣會外洩到診斷基礎設施之外，必須與 HTTP 回應欄位一併 sanitize 或直接移除。這不是「resource leak」而是「sanitization leak」，但成因相同：診斷程式碼把敏感值當作可自由輸出的偵錯資訊處理。
5. **Session 作為 bounded one-time state owner 的既有良好先例**：SPEC `design.md:526-601` 的 `SessionScopedResourceLease`／`AcquireForSessionRequest` 模式（256-bit scope、bounded stripe、logout 時 deterministic drain）證明這個 codebase 已經知道怎麼做「Session 綁定但生命週期明確」的資源——OAuth state 的修正應該參考同一個哲學（建立時機延後到真正需要、任何終止路徑都清除），但**不需要引入同等複雜度**（OAuth state 只是單一 GUID 字串比對，不需要 stripe/lease 基礎設施）。

---

## Security／sanitization requirements

### 必須移除或改為固定 sanitized 分類的欄位

| 欄位 | 目前出現位置 | 建議處置 |
|---|---|---|
| `access_token` / `refresh_token` 本體 | 從未直接放進 `result` dict，但透過 `TryPersistTokens`／`LocalDevAdfsTokenStore.Save` 落地 | 移除落地路徑後此問題消失；額外新增測試（RED #4）防止未來回歸 |
| `authority`、`resource`、`clientId`、`redirectUri`、`authorizeUrl`、`tokenUrl`、`whoAmI`（完整端點） | `AdfsAuthorize` 96-125 行；`AdfsCallback` 187-190 行；`AdfsTokenProbe` 313-317、342 行 | 全部改為布林／分類：例如 `"authorityConfigured": true`、`"clientIdConfigured": true`，不輸出實際值。若開發者需要實際值除錯，改讓其直接看伺服器端組態（appsettings/User Secrets），不透過 HTTP 回應 |
| `bodyPreview`（上游 token/WhoAmI body，最長 500 字元） | `TrimBody` 用於 211、223、357 行；`whoAmIBody` 於 272、409 行 | 移除，改成 `"upstreamBodyLength": <int>` 或固定字串 `"body-omitted"`；上游回應可能包含 claims、內部主機名稱等 |
| `ex.Message` / `ex.InnerException.Message` | `AdfsCallback` 284-289 行、`AdfsTokenProbe` 426-430 行 | 改為 `ex.GetType().Name` 加固定分類（如 `"stage": "exception", "category": "network"`），不回顯原始 `Message`（.NET HttpClient 例外訊息常含目標 URI） |
| `tokenStorePath` | `AdfsTokenProbe` 317 行、`AdfsCallback` 263 行 | 隨 `LocalDevAdfsTokenStore` 一併移除 |
| `processUser`（`Environment.UserName`） | `AdfsCallback` 154 行、`AdfsTokenProbe` 312 行 | 移除；這是本機帳號名稱，屬於不必要外洩的環境資訊 |
| `resultFile`（寫入檔案的完整路徑） | `WriteProbeResultAsync` 580 行 | 隨檔案輸出一併移除 |
| `SessionId`（`HttpContext.Session.Id`） | `GetSessionInfo` 442 行 | 若此端點保留供除錯用，`SessionId` 本身可作為 session-fixation/hijack 的偵察資訊，建議改為 `"sessionAvailable": bool` 而非完整 ID |
| ADFS RP 錯誤細節、Activity ID 等（見 `phase3-tier-a-ifd-auth-blocker.md`） | 目前只存在於文件與 Trace，不在程式輸出中 | 維持現狀（文件層級記錄可接受，不是 runtime 輸出） |

### `Cache-Control` 要求

`DiagnosticsController` 目前**完全沒有設定任何 `Cache-Control` header**（無論成功或失敗路徑）。所有動作方法（`Index`、`AdfsAuthorize`、`AdfsCallback`、`AdfsTokenProbe`、`GetSessionInfo`、`GetPerformanceInfo`）都應在回應寫出前設定 `Response.Headers["Cache-Control"] = "private, no-store"`（可用共用 `OnActionExecuting`/filter 一次套用到整個 controller，比逐一方法加更不容易漏），避免瀏覽器快取、代理快取或共享終端裝置留存包含敏感診斷資訊的回應。

---

## Rollback and scope limits

### In-scope（本切片應包含）

- 刪除 `LocalDevAdfsTokenStore.cs`（Abstractions）、`LocalDevAdfsTokenStore.cs.bak`（WebApi）
- 移除 `AdfsOAuthTokenProvider` 對檔案型 store 的讀寫分支
- 移除 `DynamicsWebApiOptions.LocalDevTokenStorePath`、`ProductDynamicsOptions.EmbeddedModeOptions.LocalDevTokenStorePath`，同步更新 `DynamicsProfileDefinition.CloneOptions`
- 移除 `ChurchReport/appsettings.json` 中的 `LocalDevTokenStorePath`
- 修正 `DiagnosticsController` 的 OAuth state 清理（五條路徑 + preview 延後建立）
- Sanitize `DiagnosticsController` 所有回應欄位、加 `Cache-Control: private, no-store`
- 刪除 `AdfsTokenProbe` 端點與 `WriteProbeResultAsync` 檔案輸出
- 新增本報告列出的 20 項 RED tests，並使其轉 GREEN
- 修正 `AdfsOAuthTokenProviderTests.cs` 中依賴舊型別的既有測試

### Out-of-scope（明確不做，避免範圍蔓延）

- **不變更** `Package01FeeReadsEnabled` 的值或其閘門邏輯本身（維持 `false`）
- **不刪除** Embedded 模式、`SpeechMessage.Dynamics.WebApi`、`Data8` 相關程式或 `PowerPlatform.Dataverse.Client` 依賴
- **不新增** Central Gateway 的任何實作（僅在分析中定性描述其 token source 應該長什麼樣）
- **不處理** `ChurchReport/appsettings.json` 中其他明文機密（LINE Channel Secret、金流 Key/IV 等）——這些是既有、獨立的問題，混入本切片會使 PR 難以審查且責任歸屬混淆；應另立 ticket
- **不重構** `AdfsOAuthTokenProvider` 既有已正確的 dispose/single-flight/bounded-read 邏輯
- **不改動** `SpeechMessage.Dynamics.Gateway/appsettings.json`（已符合 SPEC，無需變更）
- **不引入**新的加密型 token 快取或 DPAPI 包裝——SPEC 要求的是「不要預設持久化」，不是「用更安全的方式持久化」；引入加密儲存本身就是範圍蔓延且違反「no plaintext… by default」的精神（等於在製造一個新的「非預設」例外卻無法證明其 owner/生命週期）
- **不變更**現有 `phase3-tier-a-*.md` 操作文件的既有內容（僅做為證據保留，除非其中仍指示操作者依賴 `/diagnostics/adfs-token-probe`，那部分文字需要同步更新以免誤導）

### Rollback 方案

若切片導致本機 ADFS 手動驗證流程（`phase3-tier-a-browser-adfs-probe.md` 所描述的操作）無法完成：

1. 這是**低風險 rollback**：全部變更都在 DEBUG-only 程式碼與型別的一個可選屬性上，`git revert` 該切片的 commit 即可完整還原，不涉及資料庫 schema、不涉及已發布的 Production 組態格式變更（`LocalDevTokenStorePath` 從未在任何「必填」欄位上出現，移除它不會讓既有 JSON 反序列化失敗——多餘屬性會被 `System.Text.Json`/`IConfiguration` 忽略）。
2. 若移除 `AdfsTokenProbe` 端點造成操作者流程斷點，暫時的緩解不是「還原明文檔案」，而是把 `AdfsCallback` 回應的 `whoAmIOk`/`whoAmIHttpStatus` 欄位視為單次驗證證據（本切片保留這兩個布林/狀態碼欄位，只移除高敏感的 body/URL 欄位）。
3. Rollback 觸發條件建議：若 RED test matrix 中任何一項無法在合理時間內轉 GREEN 且會阻塞 Phase 4 其他工作，應先以 `[Obsolete]`/功能旗標暫時保留檔案型 store 但預設關閉，而不是延後整個安全切片——但這只是「暫緩」，不是本切片的建議終態。

---

## Critical／Warning／Info findings

### Critical

1. **[Critical] 明文 Token 落地磁碟，且不受 `#if DEBUG` 保護**
   `AdfsOAuthTokenProvider.TryPersistTokens`／`LocalDevAdfsTokenStore.Save` 只要 `LocalDevTokenStorePath` 被設定（`ChurchReport/appsettings.json:590` 目前確實設定），Release 組建一樣會執行，寫入 access/refresh token 明文 JSON。這是 release blocker，且範圍比 `DiagnosticsController`（有 `#if DEBUG`）更大。
   *File:* `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs:271-298`

2. **[Critical] OAuth state 在成功、錯誤、mismatch、缺碼、例外五條路徑均未移除**
   Session 中的一次性 state 值從未被清除，使其可能被重放或長期殘留於（可能分散式的）Session store 中。
   *File:* `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs:85, 157-292`

3. **[Critical] 診斷端點把 authority/resource/clientId/tokenUrl/bodyPreview/whoAmIBody/exception message 同時回顯於 HTTP 回應與可持久化檔案**
   直接違反 SPEC `design.md:701` 的明文禁止清單；`bodyPreview`/`whoAmIBody` 可能夾帶上游 claims 或內部主機資訊。
   *File:* `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs:96-125, 187-190, 211-223, 269-272, 313-357, 406-414`

### Warning

4. **[Warning] `LocalDevAdfsTokenStore.cs.bak` 為 tracked source，重複了明文 token schema**
   非編譯輸入但仍是可被掃描到的原始碼與教學文字，屬不必要的第二外洩面，且與正式版本內容分歧（缺少最新的 gitignore 教學提醒），增加維護混淆。
   *File:* `SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak`

5. **[Warning] `AdfsAuthorize` 在 preview（未帶 `go=1`）階段就建立 OAuth state**
   使用者只是查看預覽 JSON 也會覆寫既有合法 state，可能導致「開兩個分頁」情境下前一個已發起的合法 authorize 流程 state 被意外置換，造成後續合法 callback 誤判為 mismatch。
   *File:* `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs:77-141`

6. **[Warning] 診斷回應完全沒有 `Cache-Control` header**
   所有 `DiagnosticsController` 動作方法均未設定 no-store，回應可能被瀏覽器或中介代理快取，即使切片完成 sanitization，殘留的布林/狀態欄位仍可能被快取洩漏「該環境是否已成功完成 ADFS 授權」等狀態資訊。
   *File:* `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs`（全檔案）

7. **[Warning] `AdfsTokenProbe` 端點的存在本身鼓勵把診斷用 token 當 runtime token source**
   其邏輯（優先用已存 access token → 用已存 refresh token 換新 token → 才提示重新登入）事實上讓開發者傾向長期依賴這份診斷憑證，而非走 SPEC 期望的 `CredentialReferenceName`/`RefreshTokenSecretName` 秘密參考路徑。
   *File:* `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs:298-435`

### Info

8. **[Info] `AdfsOAuthTokenProvider` 的 dispose／single-flight／bounded-read 生命週期設計正確，屬本次分析中唯一不需修改的核心邏輯**，本切片應避免在移除檔案型 store 時意外破壞這部分（例如誤刪 `_gate`/`_disposeCts` 相關程式碼）。
   *File:* `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs:87-140, 399-453`

9. **[Info] Codebase 已有兩個可直接複用的良好先例，建議切片直接對齊而非重新發明**：(a) `Invoke-AdfsTokenProbe.ps1` 的 fail-closed 退役模式（`AdfsOAuthTokenProviderTests.cs:282-307`），(b) `SessionScopedResourceLease` 的 bounded one-time Session 綁定模式（`design.md:526-601`）。

10. **[Info] `ChurchReport/appsettings.json` 中同檔案存在其他明文機密（LINE Channel Secret、金流金鑰/IV）**，屬於 out-of-scope 但建議另立 ticket 追蹤，避免與本切片的 ADFS 修正混淆審查範圍。

---
SESSION_ID: 173aedde-cf09-436e-9e8a-49feb4e51773
