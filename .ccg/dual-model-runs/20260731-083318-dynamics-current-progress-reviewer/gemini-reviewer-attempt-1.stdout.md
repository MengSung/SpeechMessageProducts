以下是針對 Dynamics Gateway 當前進度的審查報告：

# Dynamics Gateway 當前進度審查報告

## 審查發現分類 (Findings)

### 🔴 Critical (嚴重)

1. **缺乏強型別 OData 投影與 OData 註解洩漏風險**
   * **路徑**：`SpeechMessage.Dynamics.Abstractions/Operations/OperationExecutionResult.cs` (第 16-29 行) 與 `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs` (第 340-390 行)
   * **原因**：`OperationExecutionResult.Data` 仍為 `object?`，且 `DynamicsWebApiClient` 直接將上游的 `JsonElement` 傳回，未過濾絕對路徑的 `@odata.context` 或 `@odata.nextLink`。這可能導致內部 CRM 終端節點與元數據洩漏給客戶端，且未實現伺服器端分頁循環，是 Phase 5 遷移的重大阻礙。

2. **缺乏真實 Dynamics CE 8.2 / 9.1 環境驗證**
   * **路徑**：`SpeechMessage.Dynamics.SmokeTests/LiveDynamicsWebApiSmokeTests.cs` (第 34-135 行)
   * **原因**：目前的冒煙測試預設關閉，且在啟用時使用 placeholder 組織識別與 `RequireDurableHostCoordinator=false`。所有測試均在本地模擬環境中進行，缺乏真實 CRM 伺服器的連線、驗證、分頁與容錯證明。

3. **多進程容量協調與故障隔離未經真實多進程驗證**
   * **路徑**：`SpeechMessage.Dynamics.Tests/SqlRuntimeHostSlotCoordinatorTests.cs` (第 210-383 行)
   * **原因**：`SqlRuntimeHostSlotCoordinatorTests` 的併發測試是在單一測試進程（test process）的多個 Task 中執行，無法代表真實多進程（multi-process）環境下的鎖定、隔離、Epoch 隔離與 Fencing 行為。

4. **產品配置中殘留未啟用的 Embedded 路由元數據**
   * **路徑**：`SpeechMessageProducts.ChurchReport/appsettings.json` (第 547-590 行) 與 `appsettings.Development.json` (第 4-18 行)
   * **原因**：雖然 `Package01FeeReadsEnabled` 已設為 `false`，但基礎配置中仍包含未啟用的 Embedded 模式 CRM/OAuth/token-store 路由元數據，這違反了產品邊界規範，應在 Phase 5 遷移時將其徹底移至 deployment-owned 的 Gateway/secret 邊界。

---

### 🟡 Warning (警告)

1. **診斷與操作介面缺乏快取控制**
   * **路徑**：`SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs` (第 340-390 行)
   * **原因**：操作回應（operation response）未強制加上 `Cache-Control: no-store, private` 標頭，可能導致敏感的 CRM 數據被代理伺服器或瀏覽器快取。

2. **容量管理缺乏 per-workload 公平調度**
   * **路徑**：`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs` (第 210-320 行)
   * **原因**：`OrganizationAdmissionManager` 僅實現了信號量（semaphore）與總量限制，缺乏 per-workload 的公平調度（fair/deficit scheduling）與飢餓邊界（starvation bound），可能導致單一工作負載佔滿所有容量。

3. **浸泡測試與關機基準僅基於本地模擬**
   * **路徑**：`SpeechMessage.Dynamics.Tests/Phase4IsolationSoakTests.cs` (第 31-238 行) 與 `DynamicsHttpTransportSocketSoakTests.cs` (第 78-214 行)
   * **原因**：Soak 測試使用 fake/runtime-local handler 與 loopback TCP socket，無法反映真實 CRM 連線池、Token 刷新、多進程重啟或協調器中斷時的資源與線程洩漏基準。

4. **靜態單例生命週期管理缺失**
   * **路徑**：`ToolUtility/Factory/ToolUtilityFactory.cs` (第 25-116 行)
   * **原因**：`ToolUtilityFactory` 作為進程級別的靜態單例，未納入生產環境 Host 的關機清理流程，可能導致關機時資源未釋放或 Dispose 後繼續使用的風險，這是 Phase 6 移除 SDK 的生命週期阻礙。

---

### 🔵 Info (提示)

1. **會話資源釋放機制已實現**
   * **路徑**：`SpeechMessageProducts.ChurchReport/Services/Caching/SessionScopedResourceDisposalCoordinator.cs` (第 24-176, 405-500, 648-864 行)
   * **原因**：Donation Session 範圍的資源租約、排空與重試機制已正確實現，且未殘留 Session ID 或 Token 等敏感憑證。

2. **明文 Token 持久化已成功移除**
   * **路徑**：`SpeechMessage.Dynamics.Abstractions/Configuration/LocalDevAdfsTokenStore.cs` 與 `DiagnosticsController.cs`
   * **原因**：根據最近的提交與 `AdfsDiagnosticSecurityTests.cs` 的合約測試，`LocalDevAdfsTokenStore` 及其備份檔已完全移除，且 `DiagnosticsController` 已通過無敏感輸出的合約檢查。

---

## 階段狀態表 (Phase-by-Phase Status Table)

| 階段 | 狀態 | 說明 |
| :--- | :--- | :--- |
| **Phase 0: 準備與評估** | **已完成 (Verified)** | 已完成架構規格制定與基礎依賴盤點。 |
| **Phase 1: 基礎連線驗證** | **本地驗證 (Local Verified)** | 已實現 Web API 客戶端，但僅在本地模擬與 placeholder 識別下通過測試。 |
| **Phase 2: 容量與准入控制** | **本地驗證 (Local Verified)** | 准入管理器與本地協調器已實現，但缺乏真實多進程與公平調度驗證。 |
| **Phase 3: 穩定性與 Rollback** | **部分實現 (Partially Wired)** | 已實現 Session 資源釋放與 Rollback 旗標，但缺乏真實環境的 Rollback 演練。 |
| **Phase 4: 隔離與安全硬化** | **本地驗證 (Local Verified)** | 已移除明文 Token 落地，並在本地模擬下通過多 Profile 隔離與浸泡測試。 |
| **Phase 5: 消費者遷移** | **未開始 (Not Started)** | 尚未開始將 ChurchReport 工作流遷移至 Gateway，`Package01FeeReadsEnabled` 仍為 `false`。 |
| **Phase 6: SDK 移除與強制執行** | **未開始 (Not Started)** | 尚未移除 Data8、WCF 與舊版 CRM SDK 依賴。 |

---

## 下一個最重要的 Gate (Single Most Important Next Gate)

**建立真實的 Dynamics CE 8.2 / 9.1 整合測試環境，並通過真實 ADFS OAuth 憑證與 OData 強型別投影的端到端 (E2E) 驗證。**
在未取得真實 CRM 環境的驗證證據前，無法確保 Gateway 在生產環境的相容性與容量限制能正常運作。

---

## 任務完成狀態聲明

**本任務目前尚未完成 (NOT COMPLETE)。**
雖然 Phase 4 的安全硬化與明文 Token 移除已在本地模擬測試中通過，但由於缺乏真實 CRM 環境驗證、強型別 OData 投影尚未實現，且 Phase 5 遷移與 Phase 6 SDK 移除尚未開始，因此整體計畫仍處於中期階段。
