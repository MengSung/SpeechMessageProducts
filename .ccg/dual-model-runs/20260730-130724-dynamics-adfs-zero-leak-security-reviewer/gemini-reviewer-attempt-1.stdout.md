以下是針對 Dynamics ADFS / OAuth zero-leak 安全性變更（Commit 範圍 `6301b4f29..2eee597cb`）的審查報告。

---

# Dynamics ADFS / OAuth Zero-Leak 安全審查報告

## 1. 總體評估 (Summary)
本次變更成功移除了 Phase 4 的明文 ADFS token 本地持久化機制（`LocalDevAdfsTokenStore.cs` 已完全刪除），並對 `AdfsOAuthTokenProvider` 進行了嚴格的生命週期隔離與 single-flight 併發保護。LINE OAuth 流程也補上了隨機 state、時效性檢查、時間常數比較（`FixedTimeEquals`）以及單次消費（exactly-once）機制，有效防範了 CSRF 與重播攻擊。

然而，審查中發現 **DiagnosticsController 的授權機制存在繞過風險**，且診斷端點的 `HttpClient` 建立方式可能在高併發下導致 Socket 資源抖動。

---

## 2. 具體發現 (Findings)

### 🔴 Critical (嚴重)

#### 1. 診斷端點操作員授權繞過 (Operator-Authorization Bypass)
* **檔案與行號**：`SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs` 第 29-31 行
* **具體 Failure Mode**：
  `DiagnosticsController` 僅標記了 `[Authorize]` 屬性。這意味著任何已登入的普通會友或終端使用者（只要通過 LINE 登入或一般帳密登入取得 Session），皆可直接存取 `/diagnostics`、`/diagnostics/adfs-authorize`、`/diagnostics/adfs-callback` 等高風險診斷端點。這違反了 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 中要求診斷端點必須是 **operator-only**（僅限操作員）或 **fail-closed** 的安全邊界，可能導致一般使用者惡意觸發 ADFS 授權重定向或讀取系統效能指標。
* **最小安全修復**：
  將 `DiagnosticsController` 的授權屬性限制為特定角色或原則，例如：
  ```csharp
  [Authorize(Roles = "Operator")] // 或使用自訂的 OperatorOnly Policy
  [Route("diagnostics")]
  public sealed class DiagnosticsController : Controller
  ```
* **回歸測試 (Regression Test)**：
  在 `AdfsDiagnosticSecurityTests.cs` 中新增以下測試，驗證非操作員身分存取時必須被拒絕：
  ```csharp
  [Fact]
  public void Diagnostics_endpoint_rejects_non_operator_users()
  {
      var controller = CreateController(new RecordingSession("session-id"));
      // 模擬非 operator 的普通登入使用者
      controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
          new[] { new Claim(ClaimTypes.Name, "regular-user"), new Claim(ClaimTypes.Role, "User") },
          "synthetic-authentication"));

      // 預期應拋出授權失敗或回傳 ForbidResult/ChallengeResult
      // 此測試在未修復前應失敗（目前會回傳 JsonResult 200）
  }
  ```
* **洩漏/繞過狀態**：**Operator-authorization bypass 仍然存在**。

---

### 🟡 Warning (警告)

#### 2. 診斷端點 HttpClient 頻繁建立導致 Socket 耗盡風險 (Potential Resource Churn)
* **檔案與行號**：`SpeechMessageProducts.ChurchReport/Controllers/DiagnosticsController.cs` 第 337-350 行
* **具體 Failure Mode**：
  `CreateHttpClient` 方法在每次處理 ADFS callback 時，都會手動 `new HttpClient(new SocketsHttpHandler { ... }, disposeHandler: true)`。雖然在 `AdfsCallback` 中有使用 `using` 進行釋放，但在高併發的診斷或測試情境下，頻繁建立與銷毀 `SocketsHttpHandler` 會導致底層 TCP 連線無法重用，進而引發 Socket 耗盡（Socket Exhaustion）與連線池抖動（Connection Pool Churn）。
* **最小安全修復**：
  改由 DI 注入 `IHttpClientFactory`，並透過具名用戶端來建立 `HttpClient`，以重用 Host 託管的連線池：
  ```csharp
  // 於 Constructor 注入 IHttpClientFactory
  private readonly IHttpClientFactory _httpClientFactory;

  private HttpClient CreateHttpClient()
      => _httpClientFactory.CreateClient("diagnostics-client");
  ```
* **回歸測試 (Regression Test)**：
  新增測試連續呼叫 `CreateHttpClient` 100 次，並反射檢查其底層的 `HttpMessageHandler` 是否為同一個連線池實例，或驗證 `DiagnosticsController` 是否依賴 `IHttpClientFactory`。
* **洩漏/繞過狀態**：**無立即的 Memory Leakage，但存在潛在的 Socket/Resource Churn 風險**。

---

### ℹ️ Info (資訊)

#### 3. `InMemoryContext` 生命週期與跨會話隔離確認
* **檔案與行號**：`SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs` 第 198-199, 808-809 行
* **具體 Failure Mode**：
  程式碼中直接將 LINE 登入成功後的 `userId` 寫入 `InMemoryContext.LineBindingViewModel.LineUserId`。若 `InMemoryContext` 的生命週期在 DI 容器中被誤設定為 Singleton，將會導致嚴重的跨使用者會話滲漏（Session Bleeding）。
* **安全確認**：
  經核對專案審計文件（`Session_Bleeding_Fix_TODO.md`），`InMemoryContext` 已被確認註冊為 **Scoped** 生命週期，且內部資料基於 Session ID 進行隔離，因此**目前無 Session Leakage 或 Profile Leakage 漏洞**。但後續維護時須確保此生命週期配置不被修改。

---

## 3. 關鍵不變式核對 (Rollout Gates Verification)

1. **`Package01FeeReadsEnabled=false`**：
   * **核對結果**：**通過**。在 `appsettings.json`、`appsettings.Development.json` 以及 `DonationDynamicsAccessBootstrap.cs` 中，該 rollout gate 依然保持為 `false`，未被修改。
2. **Embedded、Data8 與 `PowerPlatform.Dataverse.Client` 保留**：
   * **核對結果**：**通過**。相關專案引用與相容性程式碼均完整保留，符合 SPEC 要求。
