# 診斷與生命週期安全審查報告 (Diagnostics & Lifecycle Security Review Report)

本報告針對 `dynamics-adfs-operator-lifecycle-retry` 變更進行了完整的安全與資源生命週期審查。審查重點在於 ADFS 診斷控制器的授權邊界、OAuth 狀態生命週期、HTTP 連線與通訊端擁有權、敏感資訊洩漏防護，以及記憶體與資源釋放機制。

---

## 核心安全驗證結論

根據對工作區變更與測試程式碼的詳細審查，以下核心安全指標的驗證結果如下：

1. **操作員授權繞過 (Operator-Authorization Bypass)**：**無 (None)**
   * **驗證結果**：`DiagnosticsController` 已套用 `[Authorize(Policy = DiagnosticsOperatorAuthorization.PolicyName)]` 屬性。該 Policy 僅在 `DEBUG` 模式下註冊，並透過 `DiagnosticsOperatorAuthorization.IsAuthorized` 進行嚴格驗證。驗證過程僅使用伺服器發行的 Cookie `NameIdentifier` 聲明，並與部署專屬的唯讀 `FrozenSet<string>` 允許清單進行比對。若發現多個 `NameIdentifier` 聲明或無效 GUID，將立即拒絕存取（Fail Closed）。
2. **Session 洩漏 (Session Leakage)**：**無 (None)**
   * **驗證結果**：`DiagnosticsController` 中的 OAuth 狀態（State 與發行時間戳記）在回呼（Callback）時會被立即讀取並從 Session 中移除（Read-and-Remove）。不論是成功、錯誤、狀態不匹配或例外狀況路徑，皆能確保狀態被清除且僅能被消費一次（Exactly-Once）。
3. **設定檔洩漏 (Profile Leakage)**：**無 (None)**
   * **驗證結果**：實作中沒有發明應用程式未發行的角色，未信任 Session/Query/Header/Product JSON，未跨請求保留 Principal，亦未建立跨 Session/Profile 的可變授權狀態。
4. **憑證與敏感資訊洩漏 (Credential Leakage)**：**無 (None)**
   * **驗證結果**：所有錯誤回應均回傳固定的錯誤類別（如 `"upstream-error"`），不包含任何上游回應主體、例外狀況詳細資料、權杖、Session ID 或 LINE 使用者 ID。測試已驗證敏感資訊不會被寫入記錄、回應或測試輸出中。
5. **記憶體與資源洩漏 (Memory/Resource Leakage)**：**無 (None)**
   * **驗證結果**：所有 `HttpClient`、`HttpRequestMessage`、`HttpResponseMessage`、`HttpContent`、`Stream`、`Process` 以及從 `ArrayPool` 租用的緩衝區皆有明確的 `using` 區塊或 `try-finally` 進行確定性的釋放與清理（包含使用 `CryptographicOperations.ZeroMemory` 清除敏感位元組陣列）。

---

## 審查發現分類 (Review Findings)

### 1. Critical 發現
* **無 (None)**：未發現任何阻礙發布的 Critical 安全漏洞或資源洩漏問題。

---

### 2. Warning 發現
* **無 (None)**：未發現顯著的潛在風險。

---

### 3. Info 發現

#### 發現 A：診斷控制器與授權 Policy 僅在 DEBUG 模式下啟用
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs` (第 34, 538 行)
  * `SpeechMessageProducts.ChurchReport/Startup.cs` (第 192, 214, 689, 709 行)
* **說明**：`DiagnosticsController` 類別與 `Startup.cs` 中的 `diagnostics-operator` 授權 Policy 註冊皆被包覆在 `#if DEBUG` 條件編譯指令中。這是一項極佳的安全實踐，確保診斷端點與相關的調試程式碼絕對不會被編譯進 Release 生產環境中，從源頭杜絕了生產環境的攻擊面。
* **回歸測試**：`AdfsDiagnosticSecurityTests.cs` 中的 `Controller_requires_diagnostics_operator_policy` 測試。

#### 發現 B：安全預設的允許清單配置
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/appsettings.json` (第 15-17 行)
* **說明**：`Diagnostics:OperatorContactIds` 在預設的 `appsettings.json` 中被配置為空陣列 `[]`。這意味著在預設情況下，沒有任何使用者能夠通過診斷 Policy 的驗證，必須由部署人員手動在環境設定中加入特定的 Contact ID GUID 才能啟用，符合最小權限原則。
* **回歸測試**：`AdfsDiagnosticSecurityTests.cs` 中的 `Diagnostics_operator_authorization_uses_server_issued_contact_claim_and_fails_closed` 測試。

#### 發現 C：具名 HttpClient 的嚴格傳輸層配置
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Startup.cs` (第 196-214 行)
* **說明**：為診斷 ADFS 註冊的具名 `adfs-diagnostics` 用戶端配置了極為嚴格且安全的 `SocketsHttpHandler`：
  * 停用 Cookie (`UseCookies = false`)
  * 停用自動重新導向 (`AllowAutoRedirect = false`)
  * 停用代理伺服器 (`UseProxy = false`)
  * 停用自動解壓縮 (`AutomaticDecompression = DecompressionMethods.None`)
  * 限制最大連線數為 4 (`MaxConnectionsPerServer = 4`)
  * 限制連線生命週期與閒置逾時。
  這能有效防止 SSRF（伺服器端請求偽造）與連線集區耗盡攻擊。
* **回歸測試**：`AdfsDiagnosticSecurityTests.cs` 中的 `Startup_registers_bounded_factory_owned_adfs_diagnostics_client` 測試。

#### 發現 D：Token Provider 的確定性生命週期處置
* **檔案路徑**：`SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs` (第 278-296 行)
* **說明**：單元測試 `Owned_handler_client_is_disposed_with_profile_generation` 驗證了 `AdfsOAuthTokenProvider` 的 `_ownedHttpClient` 在 `DisposeAsync` 時會被正確處置，且後續的任何 SendAsync 呼叫都會拋出 `ObjectDisposedException`。這證明了 Token Provider 擁有明確的資源擁有權與清理機制。
* **回歸測試**：`AdfsOAuthTokenProviderTests.cs` 中的 `Owned_handler_client_is_disposed_with_profile_generation` 測試。

#### 發現 E：LINE 回呼重放攻擊防禦
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/Security/SensitiveDiagnosticOutputSecurityTests.cs` (第 176-210 行)
* **說明**：單元測試 `Line_callback_replay_with_same_session_is_rejected_after_first_consumption` 驗證了 LINE 登入回呼在第一次消費後，Session 中的所有狀態、發行時間、回呼與 nonce 資料皆會被清除，第二次使用相同 Session 與狀態的重放請求將被明確拒絕。這有效防範了 Session 重放與 CSRF 攻擊。
* **回歸測試**：`SensitiveDiagnosticOutputSecurityTests.cs` 中的 `Line_callback_replay_with_same_session_is_rejected_after_first_consumption` 測試。

#### 發現 F：功能旗標與相依套件保持不變
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/appsettings.json` (第 565 行)
* **說明**：`Package01FeeReadsEnabled` 依然保持為 `false`，且 `Embedded` 模式、`Data8` 相關整合與 `Microsoft.PowerPlatform.Dataverse.Client` 均正常保留，未被意外修改或移除，符合當前架構規格要求。

---

## 總結與建議

本次變更在安全性與資源管理上表現優異，程式碼實作與測試覆蓋率皆高度對齊了安全規範。所有已知的洩漏風險（Session、Profile、Credential、Memory/Resource）均已得到妥善處理與驗證。

**建議結論**：**通過 (PASS)**。無須進行額外的安全修復。
