# Dynamics AD FS 診斷安全切片：靜態架構與安全分析報告

本報告針對 Dynamics AD FS 診斷安全切片進行深入的靜態架構與安全分析，旨在消除明文 Token 持久化、Session 洩漏、記憶體殘留以及敏感資訊回顯等 Release Blockers，並提出最小且完整的 Phase 4 安全修正切片設計。

---

## 1. Recommended Design (建議設計)

為符合安全規範並確保系統的 Fail-Closed 特性，建議採用以下設計方案：

### 1.1 完全移除檔案型 Token 持久化
- **廢棄磁碟寫入**：完全移除 `LocalDevAdfsTokenStore` 及其關聯的 `LocalDevTokenStorePath` 設定。所有 Token 的取得與快取必須限制在**進程記憶體（In-Memory）**中。
- **記憶體生命週期管理**：Token 快取由 `AdfsOAuthTokenProvider` 實例唯一擁有，其生命週期與該 Provider 的生命週期（或 DI 容器生命週期）綁定。當 Token 到期或 Provider 被釋放（Dispose）時，快取必須被確定性清除。

### 1.2 診斷控制器退役與 Fail-Closed 引導
- **完全移除 `DiagnosticsController`**：不保留任何在執行期交換 `authorization_code` 的互動式診斷端點。
- **靜態 Fail-Closed 指引**：將互動式診斷路徑退役，改為靜態的錯誤引導說明。若 ADFS 驗證失敗，系統僅回傳經過淨化的錯誤代碼（如 `AdfsTokenExchangeFailed`），並引導開發人員檢查 ADFS 用戶端註冊與憑證設定，不提供任何互動式的 Token 交換或回顯路徑。

### 1.3 嚴格的 Token 來源限制 (Non-Password Service-Workload Contract)
- **短期診斷**：僅允許在單元/整合測試中透過 Mock 進行記憶體內的一次性驗證，Production 環境不允許任何診斷性 Token 交換。
- **Local Gateway**：僅允許使用 **Client Credentials Grant**（透過 `ISecretResolver` 解析安全儲存的 Client Secret 或 Client Certificate），禁止使用 `password` grant 與檔案型 `refresh_token`。
- **Central Gateway**：強制使用 **Client Credentials Grant**，由 Central Gateway 統一向 ADFS 申請 Token 並於記憶體中管理，嚴格禁止任何互動式或密碼式登入。

---

## 2. Root-Cause Confirmation (根因確認)

經過對原始碼的靜態分析，確認以下安全漏洞與根因：

1. **明文 Token 寫入磁碟**：
   - `LocalDevAdfsTokenStore.cs` 中的 `Save` 方法使用 `File.WriteAllText` 將包含 `AccessToken` 與 `RefreshToken` 的 `LocalDevAdfsTokenRecord` 明文序列化為 JSON 檔案並寫入磁碟。
2. **Token 讀取與寫回**：
   - `AdfsOAuthTokenProvider.cs` 在 `GetAccessTokenAsync` 中會呼叫 `LocalDevAdfsTokenStore.TryLoad` 讀取明文 Token，並在 `RequestNewTokenAsync` 成功後呼叫 `TryPersistTokens` 再次將新取得的明文 Token 寫回磁碟。
3. **診斷控制器敏感資訊洩漏**：
   - `DiagnosticsController.cs` 的 `WriteProbeResultAsync` 方法會將包含 `authority`、`resource`、`clientId`、`redirectUri`、`authorizeUrl`、`tokenStorePath`、`bodyPreview`、`whoAmIBody` 以及例外訊息的完整字典寫入實體 JSON 檔案（`adfs-token-probe-latest.json`），造成嚴重的敏感資訊持久化洩漏。
4. **Session State 未確定性清理**：
   - `DiagnosticsController.cs` 在 `/diagnostics/adfs-authorize` 路徑中建立了 Session OAuth state，但在 `adfs-callback` 的錯誤路徑（如 `error` 不為空、`state` 不匹配、`code` 缺失）以及成功與異常路徑中，皆**沒有**呼叫 `Session.Remove(AdfsOAuthStateSessionKey)` 來確定性移除該 state，導致 Session 狀態殘留與潛在的 Session 固化攻擊風險。
5. **殘留的 Tracked 備份檔案**：
   - `LocalDevAdfsTokenStore.cs.bak` 作為被 Git 追蹤的檔案，保留了完全相同的明文 Token 寫入實作，構成程式碼庫的安全隱患。

---

## 3. Exact Files to Modify / Delete (待修改與刪除檔案清單)

為實現 Phase 4 安全修正切片，必須對以下檔案進行修改或刪除：

### 3.1 刪除的檔案 (Delete)
1. `SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs`
   - *原因*：完全移除檔案型 Token 持久化實作。
2. `SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak`
   - *原因*：移除殘留的備份原始碼，避免安全隱患。
3. `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs`
   - *原因*：完全移除互動式診斷控制器，消除 Session 洩漏與敏感資訊回顯。

### 3.2 修改的檔案 (Modify)
1. `SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`
   - *修改內容*：
     - 移除所有對 `LocalDevAdfsTokenStore` 的呼叫。
     - 移除 `ResolveTokenStorePath`、`TryPersistTokens` 與 `TryResolveRefreshToken` 中讀取/寫入檔案的邏輯。
     - 將 `AllowLocalDevPasswordGrant` 預設設為 `false`，並在偵測到啟用時拋出異常，強制符合 non-password 規範。
     - 在 `DisposeCoreAsync` 中，確保使用 `CryptographicOperations.ZeroMemory` 清除快取的 Token 緩衝區。
2. `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs`
   - *修改內容*：移除 `LocalDevTokenStorePath` 屬性。
3. `SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs`
   - *修改內容*：移除 `EmbeddedModeOptions` 中的 `LocalDevTokenStorePath` 屬性。
4. `SpeechMessageProducts.ChurchReport/appsettings.json`
   - *修改內容*：移除 `DynamicsAccess:Embedded:LocalDevTokenStorePath` 設定項。
5. `SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs`
   - *修改內容*：
     - 移除所有依賴實體檔案寫入的測試案例（如 `Refresh_token_grant_posts_expected_form` 中使用實體檔案的部分，改用 Mock 記憶體快取驗證）。
     - 新增安全性 RED 測試（詳見第 4 節）。

---

## 4. RED Test Matrix (RED 測試矩陣)

應在 `AdfsOAuthTokenProviderTests.cs` 中新增以下測試案例，以證明缺陷並確保修正後的安全性：

| 測試案例名稱 | 驗證目的 (Assertion) | 觸發條件 / 輸入 | 預期結果 |
| --- | --- | --- | --- |
| `Token_Acquisition_Does_Not_Write_To_Disk` | 確保 Token 取得流程中絕無任何磁碟寫入行為。 | 呼叫 `GetAccessTokenAsync` 取得新 Token。 | 監控檔案系統，確認無任何 JSON 或暫存檔案被建立或修改。 |
| `Token_Request_Failure_Does_Not_Echo_Sensitive_Data` | 確保 ADFS 請求失敗時，異常訊息與 Log 中不包含敏感資訊。 | 模擬 ADFS 回傳 400 Bad Request，且 Body 包含敏感錯誤訊息。 | 拋出的 `InvalidOperationException` 訊息中不包含原始 Body、Client ID 或 Endpoint。 |
| `OAuth_State_Is_Consumed_One_Time_Only` | 確保 OAuth State 在比對後立即被清除，無法重複使用。 | 傳入 State 進行驗證，隨後立即使用相同 State 再次驗證。 | 第一次驗證成功/失敗，第二次驗證必須因 State 不存在而失敗。 |
| `Error_Paths_Perform_Deterministic_Session_Cleanup` | 確保所有異常與錯誤路徑皆會清除 Session 中的 State。 | 模擬 State 不匹配、Code 缺失或 ADFS 回傳錯誤。 | 驗證 Session 中的 `Diagnostics.AdfsOAuth.State` 鍵值已被確定性移除。 |
| `Request_Cancellation_Disposes_Http_Resources` | 確保請求取消時，所有 HTTP 資源與 Stream 皆被正確釋放。 | 傳入已取消的 `CancellationToken` 呼叫 `GetAccessTokenAsync`。 | 拋出 `OperationCanceledException`，且關聯的 `HttpResponseMessage` 與 Stream 被 Dispose。 |
| `Provider_Disposal_Zeroes_Memory_And_Releases_Sockets` | 確保 Provider 釋放時，記憶體中的 Token 被抹除且無背景資源殘留。 | 呼叫 `DisposeAsync`。 | 快取變數設為 `null`，敏感 Byte 陣列被 `ZeroMemory` 覆寫，且 `HttpClient` 與 `SemaphoreSlim` 被釋放。 |

---

## 5. Lifecycle / Session / Memory-Leak Analysis (生命週期、Session 與記憶體洩漏分析)

### 5.1 Session 狀態生命週期與洩漏分析
- **現狀問題**：`DiagnosticsController` 將 `state` 寫入 Session，但在多個回呼分支（包括錯誤回傳、State 不匹配、例外發生）中皆未呼叫 `Session.Remove`。這會導致 Session 狀態在伺服器端長期殘留，佔用記憶體，且在 Session 未過期前，該 State 仍可能被惡意利用。
- **修正方案**：完全移除該控制器。若在其他模組中需要使用 State 驗證，必須採用 **Read-and-Delete (一次性讀取即刪除)** 模式：
  ```csharp
  var expectedState = httpContext.Session.GetString(StateKey);
  if (expectedState is not null)
  {
      httpContext.Session.Remove(StateKey); // 讀取後立即移除，不論後續比對是否成功
  }
  ```

### 5.2 記憶體洩漏與未清理背景資源分析
- **`HttpClient` 與 `SocketsHttpHandler` 的生命週期**：
  - `AdfsOAuthTokenProvider` 在建構子中，若 `IHttpClientFactory` 為空，會自行建立 `_ownedHttpHandler` 與 `_ownedHttpClient`。
  - 在 `DisposeCoreAsync` 中，必須確保 `_ownedHttpClient` 被確定性 Dispose。由於 `SocketsHttpHandler` 被設為 `disposeHandler: true`，釋放 Client 將一併釋放 Handler，避免 Socket 連結殘留。
- **`SemaphoreSlim` 與 `CancellationTokenSource`**：
  - `_gate` (SemaphoreSlim) 與 `_disposeCts` (CancellationTokenSource) 在 `DisposeCoreAsync` 中必須被確定性 Dispose。
  - 必須在 `Dispose` 流程中先呼叫 `_disposeCts.Cancel()`，以通知所有在 `_gate.WaitAsync` 中等待的非同步工作立即中斷，避免執行緒被永久掛起（Thread Hang）。

---

## 6. Security / Sanitization Requirements (安全與淨化要求)

為防止敏感資訊外洩，系統必須嚴格執行以下淨化與快取控制要求：

1. **禁止回顯敏感欄位**：
   - 任何 API 回應、例外訊息或系統 Log 中，嚴禁包含 `access_token`、`refresh_token`、`client_secret`、`password` 等憑證內容。
   - 嚴禁回顯完整的 ADFS 授權 URL、Client ID、Authority URI 或 Resource URI。
2. **例外訊息淨化**：
   - 當 ADFS 伺服器回傳錯誤時，不得將上游的 `bodyPreview` 或原始 JSON 錯誤直接包裝進例外訊息中回傳給前端。應將其轉換為標準的、不含敏感資訊的錯誤分類（例如：`AdfsTokenExchangeFailed`）。
3. **快取控制標頭 (Cache-Control Headers)**：
   - 所有涉及驗證、診斷或 Token 取得的 HTTP 回應，必須明確設定以下標頭，防止瀏覽器或中間代理伺服器快取敏感內容：
     ```http
     Cache-Control: private, no-store, no-cache, must-revalidate
     Pragma: no-cache
     Expires: 0
     ```

---

## 7. Rollback and Scope Limits (回滾與範圍限制)

### 7.1 In-Scope (納入範圍)
- 刪除 `LocalDevAdfsTokenStore.cs`、`LocalDevAdfsTokenStore.cs.bak` 與 `DiagnosticsController.cs`。
- 修改 `AdfsOAuthTokenProvider.cs`，移除所有檔案讀寫與快取邏輯，並實作記憶體抹除與資源釋放。
- 移除設定檔（`appsettings.json`）與 Options 類別中的 `LocalDevTokenStorePath` 屬性。
- 更新單元測試，移除檔案依賴並補齊安全性 RED 測試。

### 7.2 Out-of-Scope (排除範圍)
- **保留 Embedded、Data8 與 `PowerPlatform.Dataverse.Client`**：此安全修正切片**不得**刪除或實質修改這些模組，以維持 Phase 4～6 的相容性與延後退役合約。
- **維持 `Package01FeeReadsEnabled=false`**：此設定必須持續維持，不得在此切片中啟用。
- **不修改 Central Gateway 核心**：不影響 Central Gateway 的部署與路由邏輯。

### 7.3 Rollback (回滾策略)
- 若此安全修正導致 Local Dev 環境因缺少 Token 持久化而無法正常進行 ADFS 驗證，**不得回滾至明文檔案儲存方案**。
- 應透過在開發環境的 `appsettings.Development.json` 中設定 `CredentialReferenceName`，直接指向由開發人員手動取得並安全儲存於環境變數中的短期 Bearer Token，或改用 Client Credentials 驗證模式。

---

## 8. Critical / Warning / Info Findings (審查發現分類)

### 8.1 Critical (嚴重缺陷) - 必須立即修正，Release Blocker
- **F-01: 明文 Token 持久化至磁碟**
  - *檔案路徑*：`SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs`
  - *原因*：將 `AccessToken` 與 `RefreshToken` 明文寫入 JSON 檔案，違反 Token 不得明文持久化的 SPEC 安全規定，極易導致憑證外洩。
- **F-02: 診斷控制器洩漏系統與憑證資訊**
  - *檔案路徑*：`SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs`
  - *原因*：將 `authority`、`resource`、`clientId`、`tokenStorePath`、`bodyPreview` 等敏感資訊寫入可持久化的診斷 JSON 檔案，且 Session State 在多個錯誤與異常路徑中未被清除，存在 Session 洩漏風險。
- **F-03: 追蹤的備份檔案殘留安全漏洞**
  - *檔案路徑*：`SpeechMessage.Dynamics.WebApi/Runtime/LocalDevAdfsTokenStore.cs.bak`
  - *原因*：此檔案被 Git 追蹤且包含完全相同的明文寫入實作，構成程式碼庫的安全隱患。

### 8.2 Warning (警告事項) - 建議於本切片中一併修正
- **W-01: 廢棄的設定屬性殘留**
  - *檔案路徑*：`SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs`、`SpeechMessage.Dynamics.Abstractions/Configuration/ProductDynamicsOptions.cs`
  - *原因*：仍保留 `LocalDevTokenStorePath` 屬性，容易誤導後續開發人員嘗試啟用檔案型 Token 儲存。

### 8.3 Info (參考資訊) - 需配合更新
- **I-01: 測試案例需同步重構**
  - *檔案路徑*：`SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs`
  - *原因*：需移除對實體檔案寫入的測試依賴，並新增針對記憶體安全、State 一次性消耗與異常路徑清理的驗證測試。
