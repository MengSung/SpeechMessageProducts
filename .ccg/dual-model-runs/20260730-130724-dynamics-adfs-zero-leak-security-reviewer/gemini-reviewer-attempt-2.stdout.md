以下是針對 commit 範圍 `6301b4f29..2eee597cb` 進行的 Dynamics ADFS / OAuth 零洩漏安全審查報告。

---

# 零洩漏安全審查報告 (Dynamics ADFS / OAuth Zero-Leak Security Review)

## 1. 總體評估 (Summary)
本次變更成功移除了本機明文 ADFS token 持久化機制（刪除了 `LocalDevAdfsTokenStore.cs`），並對 `AdfsOAuthTokenProvider` 的生命週期、single-flight 併發控制、有界回應讀取與敏感記憶體清零（`ZeroMemory`）進行了嚴格的硬化。同時，ADFS 與 LINE OAuth 的 state 機制已實現單次消費（exactly-once read-and-remove）與固定時間比較（`FixedTimeEquals`）。

然而，審查中發現了 **Operator 授權繞過** 與 **併發用戶 Profile 洩漏** 的殘留風險，需進行修復。

---

## 2. 具體發現 (Findings)

### 🔴 Critical: DiagnosticsController 授權過於寬鬆 (Operator-Authorization Bypass)
* **檔案與行號**：`SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs` 第 29-31 行
* **具體失效模式**：
  `DiagnosticsController` 僅標記了 `[Authorize]` 屬性：
  ```csharp
  [Authorize]
  [Route("diagnostics")]
  public sealed class DiagnosticsController : Controller
  ```
  這意味著任何已登入的普通使用者（例如通過 LINE 登入的一般會友），只要擁有合法的 Session，皆可直接存取 `/diagnostics/adfs-authorize?go=1`。這會允許非操作員（ordinary users）觸發對外真實 ADFS/CRM 流量，探測部署設定，違反了 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 中要求診斷端點必須是 **operator-only** 或 **fail-closed** 的安全邊界。雖然此控制器被 `#if DEBUG` 包裹，但在 DEBUG 模式部署的測試/預發布環境中，這仍是一個嚴重的授權繞過漏洞。
* **最小安全修復**：
  將 `[Authorize]` 改為限制特定角色或原則，例如：
  ```csharp
  [Authorize(Roles = "Operator")]
  ```
* **必須失敗的回歸測試**：
  在 `AdfsDiagnosticSecurityTests.cs` 中新增一個測試，模擬非 Operator 角色登入的使用者存取 `DiagnosticsController` 的 Action，預期應回傳 `ChallengeResult` 或 `ForbidResult`（或 HTTP 403/401）。

---

### ⚠️ Warning: InMemoryContext 靜態狀態共享導致 Profile 洩漏風險 (Profile Leakage)
* **檔案與行號**：`SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs` 第 198-199 行、第 757 行、第 808 行
* **具體失效模式**：
  在 LINE 登入成功後，程式碼將 `userId` 寫入全域靜態的 `InMemoryContext`：
  ```csharp
  InMemoryContext.LineBindingViewModel.LineUserId = userProfile.userId;
  InMemoryContext.LineBindingViewModel.DisplayId = userProfile.userId;
  ```
  由於 `InMemoryContext` 是一個全域靜態單例（Static/Singleton state），在多用戶併發存取時，用戶 A 的 LINE ID 會直接覆蓋用戶 B 的狀態，導致嚴重的 **Profile Leakage** 與越權風險。
* **最小安全修復**：
  將 LINE 登入狀態與綁定資訊改為存放在當前請求的 `HttpContext.Items`、`TempData` 或 scoped 服務中，避免使用全域靜態的 `InMemoryContext`。
* **必須失敗的回歸測試**：
  編寫一個併發測試，模擬兩個不同的執行緒同時執行 `LineCallback`，傳入不同的 LINE `userId`，驗證兩者的狀態不會互相覆蓋或混淆。

---

### ℹ️ Info: Rollout Gate 與相依套件確認
* **檔案與行號**：`SpeechMessageProducts.ChurchReport/appsettings.json` 與 `appsettings.Development.json`
* **說明**：
  * 確認 `Package01FeeReadsEnabled` 依然維持 `false`，確保在所有 live 驗證閘門通過前，不會有實際的產品流量切換至新路由。
  * `Embedded` 模式、`Data8` 與 `PowerPlatform.Dataverse.Client` 專案均安全保留，符合 SPEC 的 rollout gate 要求。
  * 本機明文 token 檔案持久化已完全移除，且錯誤與診斷輸出中已遮蔽所有敏感資訊。

---

## 3. 安全防線殘留狀態確認 (Security Boundary Status)

* **Session Leakage**：**無殘留**。OAuth state 在 callback 的所有 terminal path 上均已實現 exactly-once read-and-remove。
* **Profile Leakage**：**有殘留風險**。`InMemoryContext.LineBindingViewModel` 的靜態狀態共享可能導致併發用戶間的 Profile 洩漏。
* **Memory/Resource Leakage**：**無殘留**。`AdfsOAuthTokenProvider` 實現了 single-flight 且在 Dispose 時會取消並清理所有 HttpClient、CTS 與快取；所有敏感 byte 陣列均有 `ZeroMemory` 清理。
* **Credential Leakage**：**無殘留**。本機明文 token 檔案持久化已完全移除，且錯誤與診斷輸出中已遮蔽所有敏感資訊。
* **Operator-Authorization Bypass**：**有殘留風險**。`DiagnosticsController` 僅使用 `[Authorize]`，未限制 operator-only 角色。
