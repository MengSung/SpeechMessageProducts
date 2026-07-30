以下是針對 Dynamics ADFS 診斷與操作員生命週期（dynamics-adfs-operator-lifecycle）安全變更的審查報告。

### 執行摘要 (Executive Summary)
本審查已驗證所有高風險的驗證、授權、OAuth Session 重放、HTTP 處理常式/通訊端所有權、權杖生命週期以及記憶體/資源釋放機制。
* **操作員授權繞過 (Operator-Authorization Bypass)**：**已解決，無殘留風險**。
* **Session 洩漏 (Session Leakage)**：**已解決，無殘留風險**。
* **Profile 洩漏 (Profile Leakage)**：**已解決，無殘留風險**。
* **憑證洩漏 (Credential Leakage)**：**已解決，無殘留風險**。
* **記憶體/資源洩漏 (Memory/Resource Leakage)**：**已解決，無殘留風險**。

---

### 詳細審查結果 (Detailed Findings)

#### 1. 診斷端點授權與操作員白名單 (DiagnosticsController Authorization & Allowlist)
* **分類**：`Info`
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs`
  * `SpeechMessageProducts.ChurchReport/Security/DiagnosticsOperatorAuthorization.cs`
  * `SpeechMessageProducts.ChurchReport/Startup.cs`
* **說明**：
  * `DiagnosticsController` 已套用 `[Authorize(Policy = DiagnosticsOperatorAuthorization.PolicyName)]` 屬性。
  * 授權原則使用伺服器簽發的 Cookie `NameIdentifier` 聲明，並與部署專屬的唯讀操作員白名單（`Diagnostics:OperatorContactIds`）進行比對。
  * `IsAuthorized` 實作中，若發現重複的 `NameIdentifier` 聲明、未驗證的使用者、空名單或格式錯誤的 GUID，皆會立即回傳 `false`（Fail-Closed 關閉設計），確保未授權身分無法觸發任何 Session、ADFS 或 CRM 工作。
* **回歸測試**：`ChurchReport.MemberInfo.Tests/Security/AdfsDiagnosticSecurityTests.cs` 中的行為測試已完整覆蓋此授權邊界。

#### 2. 診斷 ADFS HTTP 路徑與 HttpClient 治理 (Bounded HTTP Client & Resource Scoping)
* **分類**：`Info`
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Startup.cs` (第 196-214 行)
  * `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs`
* **說明**：
  * 診斷 ADFS HTTP 請求已改用具名的 `IHttpClientFactory`（`adfs-diagnostics`），並配置專屬的 `SocketsHttpHandler`。
  * 該處理常式已明確停用 Cookie（`UseCookies = false`）、自動重新導向（`AllowAutoRedirect = false`）、代理伺服器（`UseProxy = false`）、自動解壓縮（`AutomaticDecompression = DecompressionMethods.None`）及預先驗證（`PreAuthenticate = false`）。
  * 連線數（`MaxConnectionsPerServer = 4`）、連線生命週期（`PooledConnectionLifetime = 5 min`）、閒置逾時（`PooledConnectionIdleTimeout = 2 min`）與處理常式生命週期（`SetHandlerLifetime = 10 min`）皆已設定上限。
  * 請求與回應的 Stream、Content 及 Rented Buffer 皆在 `using` 與 `finally` 區塊中進行確定性釋放，且緩衝區在歸還前皆以 `CryptographicOperations.ZeroMemory` 清零。

#### 3. 權杖提供者生命週期與處置驗證 (Token Provider Lifecycle & Disposal)
* **分類**：`Info`
* **檔案路徑**：
  * `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`
  * `SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs` (第 278-296 行)
* **說明**：
  * `AdfsOAuthTokenProvider` 實作了 `IDisposable` 與 `IAsyncDisposable`。當 Generation 處置時，會取消所有進行中的權杖取得工作，並關閉其持有的 `HttpClient` 與 `SocketsHttpHandler`。
  * 測試專案中新增了 `Owned_handler_client_is_disposed_with_profile_generation` 單元測試，透過反射取得私有欄位 `_ownedHttpClient`，驗證在 Provider 處置後，該 Client 確實被關閉且後續呼叫會拋出 `ObjectDisposedException`。

#### 4. LINE Callback 重放防禦與測試 (LINE Callback Replay Protection)
* **分類**：`Info`
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs` (第 146-153 行)
  * `ChurchReport.MemberInfo.Tests/Security/SensitiveDiagnosticOutputSecurityTests.cs` (第 177-210 行)
* **說明**：
  * `LineCallback` 在進行任何早期回傳（如錯誤檢查、State 不匹配或 Code 缺失）之前，會優先讀取並從 Session 中移除 `_LineLoginState`、`_LineLoginStateIssuedAtUtcTicks`、`_LineLoginCallbackUrl` 及 `_LineLoginNonce`。
  * 新增了 `Line_callback_replay_with_same_session_is_rejected_after_first_consumption` 測試，模擬使用相同的 Session 與 State 連續呼叫 `LineCallback` 兩次，驗證第二次呼叫會因 State 已被消費移除而遭拒絕（重新導向至 Login 並帶有 State 驗證失敗訊息）。

#### 5. 敏感資料洩漏防護 (Sensitive Data Leakage Prevention)
* **分類**：`Info`
* **檔案路徑**：
  * `ChurchReport.MemberInfo.Tests/Security/SensitiveDiagnosticOutputSecurityTests.cs`
* **說明**：
  * 原始碼合約測試（Source Contract Tests）已鎖定 `DiagnosticsController`、`AuthenticationController` 與 `SmallGroupController`，確保程式碼中不包含將 Bearer Token、Session ID、LINE 用戶 ID、上游錯誤詳細內容或 CRM 更新 Payload 寫入 Log、Debug 輸出、Redirect 參數或快取的程式碼片段。

#### 6. 關鍵配置保留 (Configuration Preservation)
* **分類**：`Info`
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/appsettings.json` (第 565 行)
* **說明**：
  * 驗證 `"Package01FeeReadsEnabled": false` 保持不變，且 Embedded 模式、Data8 相關整合與 `Microsoft.PowerPlatform.Dataverse.Client` 均正常保留。
