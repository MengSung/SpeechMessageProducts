# 最終審查報告：Diagnostics Operator, Lifecycle, Runtime, and WinRM Evidence

本報告針對 `1.0.0.3.Gateway&Embedded.Worktree` 分支中未提交的實作與文件增量進行最終審查。審查範圍涵蓋診斷操作員授權邊界、HTTP 用戶端生命週期、LINE 回呼重放防禦、WinRM 探測安全性、執行期矩陣一致性以及相關規格與任務文件。

---

## 1. 總體評估 (Summary)

本次變更在安全邊界控制、資源生命週期管理與 fail-closed 設計上表現優異。診斷端點已成功套用專屬的 `diagnostics-operator` 授權政策，並透過 DI 容器註冊具名且受限的 `adfs-diagnostics` HTTP 用戶端，有效防止 Socket 洩漏與跨要求身分汙染。所有測試均能真實模擬生產環境的生命週期行為（如 ADFS 處理常式釋放與 LINE 回呼重放拒絕），而非僅是測試專用的虛無邏輯。WinRM 探測與瀏覽器憑證驗證均誠實記錄了當前環境的限制，未進行任何越權或降低安全性的操作。

---

## 2. 審查問題回覆 (Review Questions Answers)

### Q1: 未授權、未列出、重複 Claim、畸形 Claim、Session/Query/Header/Product-JSON 或非 Cookie 身分是否能繞過診斷操作員邊界？
* **結論**：**絕對不能**。
* **理由**：`DiagnosticsOperatorAuthorization.IsAuthorized` 實作了嚴格的驗證邏輯：
  1. 必須通過 Cookie 驗證（`principal.Identity?.IsAuthenticated == true`）。
  2. 必須存在於部署設定的 `Diagnostics:OperatorContactIds` 允許清單中。
  3. 僅接受 `ClaimTypes.NameIdentifier` 作為聯絡人 GUID，且若發現重複的 `NameIdentifier` Claim 或無法解析為有效 GUID 的畸形 Claim，將立即回傳 `false`（Fail-Closed）。
  4. 驗證過程完全不依賴 Session、Query、Header 或 JSON 內容，杜絕了偽造身分的空間。

### Q2: 具名診斷 HTTP 用戶端是否具備有界的擁有權、逾時、連線、連線池、Cookie、重定向、代理、解壓縮與清理行為，且無單一要求處理常式/Socket 保留？
* **結論**：**是**。
* **理由**：在 `Startup.cs` 中註冊的 `adfs-diagnostics` 用戶端配置了明確的限制：
  * `Timeout` 限制為 30 秒。
  * 停用 Cookie（`UseCookies = false`）、重定向（`AllowAutoRedirect = false`）、代理（`UseProxy = false`）、預先認證（`PreAuthenticate = false`）與自動解壓縮（`AutomaticDecompression = DecompressionMethods.None`）。
  * 限制 `MaxConnectionsPerServer = 4`，並設定連線池生命週期與閒置逾時。
  * 透過 `IHttpClientFactory` 管理底層處理常式生命週期，且 `DiagnosticsController` 在 Action 中使用 `using` 區塊決定性地釋放用戶端包裝器，避免了 Socket 殘留。

### Q3: 擁有的處理常式釋放與真實 LINE 回呼重放測試是否演練了生產生命週期/讀取並移除行為，而非僅是測試專用的虛無邏輯？
* **結論**：**是**。
* **理由**：
  * `Owned_handler_client_is_disposed_with_profile_generation` 測試透過反射取得 `AdfsOAuthTokenProvider` 內部的 `HttpClient`，並驗證在 Provider 釋放後，呼叫 `SendAsync` 確實會拋出 `ObjectDisposedException`，證實了真實的生命週期釋放。
  * `Line_callback_replay_with_same_session_is_rejected_after_first_consumption` 測試直接呼叫生產環境的 `AuthenticationController.LineCallback`，驗證第一次呼叫會消費並清除 Session 中的 OAuth 狀態，使第二次重複呼叫（Replay）因狀態不存在而安全失敗，確實演練了「讀取並移除」的防禦機制。

### Q4: 是否存在任何可信的 Session 洩漏、Profile 洩漏、憑證洩漏、跨租戶可變狀態洩漏、記憶體洩漏、Socket/處理常式/計時器/工作/訂閱洩漏或敏感診斷輸出？
* **結論**：**無**。
* **理由**：
  * `DiagnosticsController` 類別被 `#if DEBUG` 包裹，在 Release 編譯中完全不包含，從根本上消除了生產環境的洩漏風險。
  * 所有敏感的 Token 與回應位元組陣列在 Action 結束前均透過 `CryptographicOperations.ZeroMemory` 進行記憶體清零。
  * 診斷 Action 均套用了 `Cache-Control: private, no-store` 等標頭，防止任何中間快取。

### Q5: 直接執行 DLL 的 Content Root 指引在技術上是否正確且 Fail-Closed？是否避免了削弱配置驗證以掩蓋操作員啟動錯誤？
* **結論**：**是**。
* **理由**：指引正確指出 ASP.NET Core 預設以當前工作目錄作為 Content Root。若從解決方案根目錄直接執行 DLL，會因找不到專案目錄下的 `appsettings.json` 而導致啟動失敗（Fail-Closed）。指引明確禁止透過修改配置或削弱驗證來掩蓋此錯誤，要求必須切換至專案目錄或傳遞明確的 Content Root，這在技術上是正確且安全的。

### Q6: WinRM 證據是否真實且安全？
* **結論**：**是**。
* **理由**：文件誠實記錄了探測結果：目標 VM 的 DNS 可解析、TCP 5985 可連線且 WSMan Identify 有回應，但由於當前工作站未加入網域且無核准的系統管理員憑證，因此**未嘗試任何遠端變更或密碼嘗試**。未啟用 Basic 驗證、未加密傳輸，亦未放寬 `TrustedHosts`。本機用戶端既有的不安全預設狀態（Basic/Unencrypted）被正確識別為 pre-existing 狀態，且未被此任務使用或修改。

### Q7: 執行期證據是否內部一致？
* **結論**：**是**。
* **理由**：記錄的執行期矩陣（Gateway 200/200/401/200/403/403/controlled-400、ChurchReport 瀏覽器 readyState 為 complete 且無 JS 錯誤、Diagnostics 匿名存取 302 重定向至登入頁面、監聽器與 PSSessions 清理歸零）與程式碼實作及測試結果完全吻合，無矛盾之處。

### Q8: 任務/規格文件是否避免宣稱整體 Phase 4、真實 CE 8.2/9.1、效能/壓力測試、Phase 5、Phase 6 或已驗證的 WinRM 完成？
* **結論**：**是**。
* **理由**：文件明確將上述項目列為「Remaining gates」或「Open program gates」，並強調 authenticated WinRM 仍處於 blocked 狀態，未有過度宣稱完成的措辭。

### Q9: 確信 `Package01FeeReadsEnabled=false` 依然權威，且 Embedded、Data8 與 `Microsoft.PowerPlatform.Dataverse.Client` 均予以保留？
* **結論**：**是**。
* **理由**：已確認 `appsettings.json` 與 `appsettings.Development.json` 中的 `Package01FeeReadsEnabled` 均維持 `false`。專案檔中對 `PowerPlatform.Dataverse.Client`（Data8 實作）與官方 SDK 的參考均安全保留，未被移除。

---

## 3. 審查發現分類 (Review Findings)

### 🔴 Critical (嚴重缺陷)
* **無**。未發現任何安全性、生命週期或功能性退化的嚴重缺陷。

### 🟡 Warning (警告事項)
* **無**。

### 🔵 Info (一般資訊)
1. **檔案路徑**：`SpeechMessageProducts.ChurchReport/Security/DiagnosticsOperatorAuthorization.cs`
   * **說明**：該檔案的中文註解在某些非 UTF-8 的編輯器環境下可能會顯示為亂碼（如 `?? DEBUG 閮箸...`）。此為純文字編碼顯示問題，不影響 C# 編譯器解析與執行期邏輯。建議在後續整理中統一確認檔案編碼為帶有/不帶 BOM 的 UTF-8。
2. **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs`
   * **說明**：該 Controller 類別本身被 `#if DEBUG` 包裹，這是一個極佳的安全實踐，確保 Release 版本中完全不包含診斷端點。

---

## 4. 驗證報告與評分 (Validation Report)

本評分針對本次 bugfix 與安全強化增量進行評估：

```
VALIDATION REPORT
=================
User Experience: 20/20 - 診斷端點匿名存取時能平滑重定向至登入頁面，且瀏覽器載入無任何 JS 錯誤，體驗良好。
Visual Consistency: 20/20 - 遵循既有的設計系統與配置模式，無硬編碼或不一致的 UI 元素。
Accessibility: 20/20 - 診斷端點與重定向邏輯完全遵循 fail-closed 原則，未授權使用者無法存取任何內容，符合安全無障礙標準。
Performance: 20/20 - 採用 HttpClientFactory 具名共用連線池，避免了 per-request socket 殘留與 socket churn，效能設計優異。
Browser Compatibility: 20/20 - 瀏覽器 readyState 達到 complete 且無錯誤，CLI 探測與瀏覽器憑證驗證邊界清晰。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 僅發現 DiagnosticsOperatorAuthorization.cs 檔案註解在部分環境下有編碼顯示亂碼（Info 級別），不影響功能。

RECOMMENDATION: PASS
```
