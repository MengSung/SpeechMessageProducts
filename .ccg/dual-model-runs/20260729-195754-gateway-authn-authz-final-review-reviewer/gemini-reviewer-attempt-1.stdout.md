VALIDATION REPORT
=================
User Experience: 20/20 - API 授權失敗時統一回傳 403 (Forbid)，不向 caller 洩漏內部 mapping 狀態（避免 principal mapping oracle），同時在內部保留了 failure code 供診斷，設計非常安全且符合最小權限原則。
Visual Consistency: 20/20 - 嚴格遵守設計契約，所有 options/binding/operation 集合在 startup freeze，且在 Development/Testing/Production 環境下有清晰且一致的行為。
Accessibility: 20/20 - 實現了嚴格的 fail-closed 授權機制。未 mapping 的 principal 在進入 executor 之前即被攔截並回傳 403。SID 優先且不允許模糊比對。
Performance: 20/20 - 使用了 `FrozenDictionary` 進行 O(1) 無鎖查詢，且不建立跨 request 的 cache 或 mutable state，避免了記憶體洩漏與並行安全問題。
Browser Compatibility: 20/20 - 透過 `OperatingSystem.IsWindows()` 判斷，在 Windows 上使用真實 WindowsIdentity，在非 Windows 測試環境下 fallback 到 SID claim，保證了跨平台測試的相容性。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無（未發現任何 Critical 或 Warning 級別的問題）

RECOMMENDATION: PASS
=================

# Gateway AuthN/AuthZ 最終安全審查報告

本報告針對 `git diff 9719182d2..b68e6a9a4` 進行 Dynamics Local Gateway 的 Windows Negotiate、principal mapping、workload/alias/operation 授權與 operation catalog 邊界的最終安全審查。

---

## 一、 審查摘要 (Summary)

本次變更完整且嚴格地實現了 Dynamics Local Gateway 的安全邊界防禦。程式碼在初始化階段（Startup）即對所有授權綁定（Bindings）與操作目錄（Operation Catalog）進行了凍結（Freeze），並在 Request Fast Path 中使用無鎖的 `FrozenDictionary` 進行 O(1) 查詢。安全機制採取了嚴格的 **Fail-Closed** 設計，未授權或未對應的 Windows Principal 將在接觸任何執行器（Executor）、准入控制（Admission）或傳輸層（Transport）之前被攔截並回傳 403。

---

## 二、 契約驗證與數據流追蹤 (Contract Verification & Data Flow)

### 1. Development 環境安全邊界 (契約 1 & 2)
- **實作驗證**：在 `Program.cs` 中，當環境為 `Development` 時，程式碼固定註冊真實的 Kestrel Negotiate 驗證方案：
  ```csharp
  if (builder.Environment.IsDevelopment())
  {
      builder.Services
          .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
          .AddNegotiate();
  }
  ```
  即使在 `appsettings.Development.json` 中配置了測試用的 Fake Scheme，也會被 `Program.cs` 忽略，確保開發期必須通過真實的 Windows 憑證協商。
- **開發身分綁定**：`appsettings.Development.json` 中精確綁定了 `LENOVO-LEGION\Administrator` 與 SID `S-1-5-21-3356955407-2337739315-1638624769-500`，且僅授予 `crm82` 與 `runtime.health.whoami` 權限，無任何 Wildcard 或跨 Alias 權限。

### 2. 未對應 Principal 的攔截 (契約 3 & 5)
- **實作驗證**：`/v1/operations` 路由加上了 `.RequireAuthorization()`，匿名請求直接回傳 401。
- **授權過濾**：已驗證但未對應的帳號會經由 `AuthorizeOperationCatalog` 判定為 `unmapped-principal`，並在 Endpoint 中直接回傳 `Results.Forbid()` (403)，不會觸發後續的 Executor 或 Transport。
- **目錄最小化暴露**：已對應的帳號僅能取得其 Binding 中明列的 `CapabilityOperationIds` 子集合，過濾邏輯如下：
  ```csharp
  var operations = Package01OperationRegistry.All
      .Where(definition => authorization.CapabilityOperationIds.Contains(
          definition.CapabilityOperationId,
          StringComparer.OrdinalIgnoreCase))
  ```
  這確保了 Catalog 不會向所有已驗證帳號暴露完整的 Registry。

### 3. SID 優先與精確比對 (契約 4)
- **實作驗證**：在 `ResolveAuthenticatedBinding` 中，優先嘗試解析 Windows SID 並進行字典查找；若未命中，才以 `principal.Identity.Name` 進行 Fallback。
- **比對嚴格度**：`_bindingsByWindowsSid` 與 `_bindingsByPrincipalName` 皆使用 `StringComparer.OrdinalIgnoreCase` 的 `FrozenDictionary`，僅允許大小寫無關的**精確比對**，且在建構時透過 `RejectWildcard` 拒絕了任何包含 `*` 或 `?` 的模糊設定。

### 4. 已知但未授權的 Profile 拒絕 (契約 6)
- **實作驗證**：測試案例 `Unmapped_profile_alias_is_forbidden_even_if_principal_is_mapped` 驗證了當 `crm91` 為已知 Profile（存在於 Configuration 中）但未包含在該 Principal 的 Binding 內時，請求會在 Executor 執行前被攔截並回傳 403，排除了因 "Unknown Profile" 導致的偶然通過。

### 5. 環境 Authentication Scheme 控制 (契約 7)
- **實作驗證**：
  - **Testing**：允許延遲從 `IConfiguration` 讀取 `DynamicsGateway:AuthenticationScheme`，以支援 `WebApplicationFactory` 的測試 Scheme 注入。
  - **Production**：固定使用 `IISDefaults.AuthenticationScheme`，任何來自 Configuration 的 Scheme 覆寫皆會被忽略，防止惡意配置繞過 IIS 安全邊界。

### 6. 無狀態與記憶體安全 (契約 8 & 9)
- **實作驗證**：`ConfigurationGatewayOperationAuthorizer` 作為 Singleton，其內部欄位皆為唯讀的 `FrozenDictionary`。`Authorize` 與 `AuthorizeOperationCatalog` 方法皆為同步、無 I/O 且無狀態的純運算，不保留 `ClaimsPrincipal`、`HttpContext` 或任何可變狀態，避免了 Session 洩漏與記憶體殘留。

---

## 三、 安全與設計評級發現 (Findings)

### 【Critical】
- **無**。未發現任何 Critical 級別的安全漏洞或契約違反。

### 【Warning】
- **無**。未發現任何 Warning 級別的潛在風險。

### 【Info】
1. **ReDoS 防禦實踐**
   - **檔案**：`SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs`
   - **行為**：在 `TryGetAuthenticatedWindowsSid` 中，使用 `Regex.IsMatch` 驗證 SID 格式時，明確指定了 `TimeSpan.FromMilliseconds(100)` 的超時時間，有效防禦了正則表達式拒絕服務（ReDoS）攻擊。
2. **跨平台測試相容性**
   - **檔案**：`SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs`
   - **行為**：`TryGetAuthenticatedWindowsSid` 優先使用 `OperatingSystem.IsWindows()` 讀取真實的 `WindowsIdentity`，並在非 Windows 環境下 fallback 至 `ClaimTypes.PrimarySid` 讀取，這使得在 Linux 容器或 CI/CD 環境中進行單元與整合測試成為可能，設計兼顧了安全性與可測試性。
3. **Startup 階段防禦性驗證**
   - **檔案**：`SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs`
   - **行為**：`GatewayOperationAuthorizationStartupValidator` 作為 `IHostedService`，在應用程式啟動時即觸發 Authorizer 的建構子，對所有配置進行重複性、長度、Wildcard 與 Canonical 存在性檢查，若有不合規配置將直接拋出 `InvalidOperationException` 阻止 Host 啟動，落實了 **Fail-Fast** 原則。
4. **編碼與格式規範**
   - **行為**：所有新增與修改的程式碼皆包含詳盡的繁體中文 XML 與實作註解，且檔案格式皆遵守 UTF-8 without BOM 與 CRLF 換行規範。

---

## 四、 審查結論 (Conclusion)

本次提交的程式碼在安全性、效能、並行安全與資源管理上皆表現優異，完全符合 Dynamics Local Gateway 的安全契約要求。建議予以 **PASS** 並合併。
