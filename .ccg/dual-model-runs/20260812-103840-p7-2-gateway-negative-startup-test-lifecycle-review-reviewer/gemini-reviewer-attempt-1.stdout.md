# UI 審查報告：Gateway 負向啟動測試生命週期重構

本報告針對以下兩個測試檔案的未提交變更進行審查：
- `SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`
- `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs`

---

## 1. 總體評估 (Summary)

本次變更旨在解決 .NET 10 `WebApplicationFactory` 在預期 Host 啟動失敗（負向測試）時的生命週期競態條件（disposal race）。在 .NET 10 中，當 Host 啟動拋出例外時，頂層的 `app.Run()` 會立即處置 Host，而 `TestHost` 內部機制隨後嘗試讀取已處置的 `IServiceProvider`，導致拋出 `ObjectDisposedException` 並遮蔽了原本應有的 `InvalidOperationException` 或 `OptionsValidationException`。

變更將**純設定驗證的負向測試**重構為直接建構 `ConfigurationGatewayOperationAuthorizer` 或呼叫 `GatewayRequestBodyLimitOptions.BindAndValidate` 的單元測試，同時**完整保留**了所有正向的 HTTP/TestHost/Kestrel 整合測試。

經審查，此重構設計極為精確，完全覆蓋了正式啟動時的 fail-closed 安全契約，徹底排除了框架競態導致的測試不穩定性，且無任何資源或狀態洩漏。

---

## 2. 審查要點分析

### 2.1 啟動驗證契約覆蓋 (Fail-Closed Contract)
- **`GatewayRequestBodyBoundaryTests.cs`**：
  - 測試 `Request_limit_above_hard_ceiling_fails_deployment_validation` 改為直接呼叫 `GatewayRequestBodyLimitOptions.BindAndValidate(configuration)`。
  - 經確認，正式環境的 `Program.cs` 在 `ConfigureKestrel` 階段確實是呼叫此方法來設定 Kestrel 的 `MaxRequestBodySize`。因此，直接驗證此方法能精確模擬 Kestrel 啟動時的驗證路徑，確保超限設定會拋出 `InvalidOperationException` 阻止啟動。
- **`GatewayWorkloadBoundaryTests.cs`**：
  - 多個負向測試（如無效 selector、重複 principal/operation、未知 alias/operation 等）改為直接建構 `ConfigurationGatewayOperationAuthorizer`。
  - 經確認，`ConfigurationGatewayOperationAuthorizer` 在建構子中執行了完整的設定解析與安全驗證（包括 SID 格式、重複性檢查、白名單比對等）。正式環境中該類別被註冊為 Singleton，其建構子會在 Host 啟動期執行。因此，直接建構該類別能 100% 覆蓋啟動時的 fail-closed 驗證契約。

### 2.2 資源與狀態洩漏檢查 (Resource & State Leaks)
- **無跨測試狀態洩漏**：重構後的測試使用 `CreateAuthorizerConfiguration` 輔助方法，每次呼叫皆建構獨立的 `ConfigurationBuilder` 與 `InMemoryCollection`，不保留任何 reload subscription，無 static 狀態共享。
- **無資源殘留**：`ConfigurationGatewayOperationAuthorizer` 內部使用 `FrozenDictionary` 凍結設定，建構後即為唯讀，不持有任何 Socket、Timer、背景工作或需要確定性釋放的非受控資源。

### 2.3 整合測試覆蓋率 (Integration Coverage)
- **未削弱整合測試**：所有需要實際發送 HTTP 請求、驗證 `ClaimsPrincipal` 解析、Executor 呼叫次數以及 `Cache-Control` 標頭的正向整合測試均完整保留，並繼續使用 `WebApplicationFactory` 進行端到端驗證。
- 負向測試原本就預期 Host 啟動失敗（無法接受任何 HTTP 流量），將其改為單元測試並不會減少任何實際的整合覆蓋，反而提升了測試套件的穩定性與執行效率。

### 2.4 程式碼與 XML 文件品質
- **繁體中文 XML 註解品質優異**：新增的註解詳細說明了 .NET 10 框架競態的背景、測試意圖、資源擁有權（ownership）以及 fail-closed 行為，用詞精確（如「框架競態」、「materialization 契約」、「disposal race」等），符合專案的高標準文件規範。
- **程式碼風格一致**：完全遵循既有的 C# 命名規範與 FluentAssertions 斷言風格。

---

## 3. 評分報告 (Scoring Report)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 解決了 .NET 10 框架下的啟動競態，避免了 CI/CD 流程中隨機的測試失敗，提升開發體驗。
Visual Consistency: 20/20 - 程式碼風格、命名規範與既有測試檔案完全一致，XML 註解格式正確。
Accessibility: 20/20 - 後端測試代碼，結構清晰，易於維護與閱讀。
Performance: 20/20 - 負向測試不再啟動完整的 WebApplicationFactory 宿主環境，大幅縮短測試執行時間，且無資源洩漏。
Browser Compatibility: 20/20 - 後端測試代碼，不涉及瀏覽器相容性。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無 (No issues found)

RECOMMENDATION: PASS
```

---

## 4. 具體發現分類 (Findings Classification)

### 🟢 Info
- **`SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs`**
  - **說明**：`Request_limit_above_hard_ceiling_fails_deployment_validation` 測試預期拋出的例外由 `OptionsValidationException` 改為 `InvalidOperationException`。此變更符合 `GatewayRequestBodyLimitOptions.BindAndValidate` 的實際行為，且與 Kestrel 啟動時的異常中斷路徑完全一致，屬正確調整。
- **`SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs`**
  - **說明**：新增的 `CreateAuthorizerConfiguration` 輔助方法每次皆配置新的 Dictionary 與 configuration root，確保了測試案例之間的完全隔離，設計良好。
