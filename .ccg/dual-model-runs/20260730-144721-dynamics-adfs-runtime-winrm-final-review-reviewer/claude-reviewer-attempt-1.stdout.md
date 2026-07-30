# 最終審查報告：Diagnostics Operator、Lifecycle、Runtime 與 WinRM 證據

審查範圍：`DiagnosticsOperatorAuthorization.cs`、`DiagnosticsController.cs`、`Startup.cs`、`appsettings.json`、三份異動測試檔案，以及 `.trellis`/`.ccg` 任務與規格文件。`.ccg/dual-model-runs/**` 視為產生的證據而非產品程式碼。已直接閱讀原始碼與 `git diff`，未依賴既有的 Gemini 審查結論。

## Critical 🔴
無。

## Warning 🟡
無。未發現任何會讓未授權、重複/畸形 claim、Session/Query/Header/JSON 或非 cookie 身分繞過 `diagnostics-operator` 邊界的路徑；`DiagnosticsOperatorAuthorization.IsAuthorized`（`DiagnosticsOperatorAuthorization.cs:53-81`）對空清單、未驗證身分、重複 `NameIdentifier`、畸形 GUID 與清單外聯絡人均在到達比對前 fail closed，且以 `FrozenSet` 持有不可變快照。

## Info 🔵

1. **`SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs:522-527`** — `GetRequiredRedirectUri()` 在未設定 `DynamicsAccess:Embedded:RedirectUri` 時，回退使用 `HttpContext.Request.Scheme`/`Request.Host` 組出 `redirect_uri`，且此值同時用於 authorize 導向（line 291）與 token exchange（line 311）。由於此端點已受 `diagnostics-operator` 政策與 fail-closed allowlist 保護，且 ADFS 端通常會驗證已註冊的 redirect URI，實際可被利用的機率很低；但既然這是 OAuth 安全敏感欄位，建議之後直接要求明確設定 `RedirectUri`，避免任何情境下信任 Host header 組值。此為強化建議，非本次增量引入的邊界缺陷。

2. Gemini 審查中提及 `DiagnosticsOperatorAuthorization.cs` 中文註解在部分編輯器可能顯示亂碼——經以 `file` 指令與位元檢查確認該檔案為 UTF-8（無 BOM）＋CRLF，符合專案編碼要求；此為誤報，非實際問題。

## 逐項確認

- **Q1（授權繞過）**：確認不可能。`IsAuthorized` 對「空清單→false」「未驗證身分→false」「重複 NameIdentifier→立即 false」「畸形/空 GUID→立即 false」「清單外→false」均逐一驗證，且只讀 cookie 簽發的 claim，程式碼中沒有讀取 Session/Query/Header/JSON 的路徑。
- **Q2（具名 HTTP client 生命週期）**：`Startup.cs:196-213` 註冊 `adfs-diagnostics` client，`Timeout=30s`、`UseCookies=false`、`AllowAutoRedirect=false`、`UseProxy=false`、`AutomaticDecompression=None`、`PreAuthenticate=false`、`MaxConnectionsPerServer=4`、`PooledConnectionLifetime=5min`、`PooledConnectionIdleTimeout=2min`、`SetHandlerLifetime=10min`；controller 僅以 `using var http = _httpClientFactory.CreateClient(...)` 取得短生命週期 wrapper（`DiagnosticsController.cs:175-176`），handler/socket pool 由 Host 唯一擁有。
- **Q3（生命週期測試真實性）**：`AdfsOAuthTokenProviderTests.cs` 新增測試以反射取得生產程式的私有欄位 `_ownedHttpClient`，呼叫 `DisposeAsync()` 後對同一 client 執行真實 `SendAsync`，斷言拋出 `ObjectDisposedException`；已對照 `AdfsOAuthTokenProvider.cs:459`（`_ownedHttpClient?.Dispose()`，`disposeHandler:true`）確認邏輯真實。LINE replay 測試使用 `RuntimeHelpers.GetUninitializedObject` 建立未經建構式的 `AuthenticationController`，直接呼叫生產 `LineCallback` action 兩次；已對照 `AuthenticationController.LineLoginOAuth.cs:152-160`（read-and-remove 在所有 early return 之前執行）與 `ExchangeCodeForToken`（`services` 為空時安全提前回傳 null，不觸網），確認第一次呼叫因缺設定安全失敗、第二次因 Session state 已被移除而回到「State 驗證失敗」，兩者行為皆為生產路徑，非測試專用虛無邏輯。
- **Q4（洩漏/敏感輸出）**：`DiagnosticsController` 整體以 `#if DEBUG` 包裹（Release 不含此型別）；token、responseBytes 均以 `CryptographicOperations.ZeroMemory` 在 `finally` 清零；所有 action 皆設定 `private, no-store`；`GetSessionInfo` 只回傳布林可用性，不回傳 Session ID。
- **Q5（Content Root 指引）**：規格文件正確描述 ASP.NET Core 以目前工作目錄作為 content root 的預設行為，並明確禁止透過修改設定或削弱驗證來掩蓋操作員從錯誤目錄啟動 DLL 的問題，技術描述正確且維持 fail-closed。
- **Q6（WinRM 證據）**：`review.md` 與 `phase4-local-central-boundary-verification.md` 一致記錄僅完成 DNS/TCP 5985/WSMan Identify 探測，因無核准的系統管理員身分而未嘗試任何遠端變更、密碼或降級驗證，且明確標註既有本機 Basic/unencrypted WinRM 用戶端狀態為 pre-existing、未被本次工作使用或修改。
- **Q7（Runtime 矩陣一致性）**：`review.md` 記錄的 200/200/401/200/403/403/controlled-400、ChurchReport `readyState=complete` 零 JS 錯誤、Diagnostics 匿名 302 導向 `/Login`、Gateway 瀏覽器受限於自簽憑證、監聽器與 PSSession 歸零，彼此之間與程式碼實作、測試結果沒有矛盾。
- **Q8（文件是否過度宣稱）**：`task.json`/`review.md`/spec 文件的 "Remaining gates"/"Open program gates" 段落明確將 Phase 4 整體、真實 CE 8.2/9.1、soak/performance、Phase 5、Phase 6、已驗證 WinRM 列為未完成，未見過度宣稱字樣。
- **Q9（設定保留）**：已直接確認 `appsettings.json`/`appsettings.Development.json` 中 `Package01FeeReadsEnabled` 均為 `false`；`PowerPlatform.Dataverse.Client.csproj` 與 ChurchReport 專案參考仍存在，未被移除。

## 總結

本次診斷操作員授權邊界、具名 HTTP client 生命週期、ADFS handler 釋放與 LINE callback 重播防護的實作與測試皆通過獨立原始碼審查，未發現 Critical 或 Warning 級缺陷；WinRM 與執行期文件誠實記錄限制與剩餘缺口，未見過度宣稱。建議：PASS，可合併本次安全強化增量。

---
SESSION_ID: <redacted>
