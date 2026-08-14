# Architecture Analysis Report: P7 Runtime Health WhoAmI ProductClient

本報告針對新增的 `ORG-CALL-00003 runtime.health.whoami` 無狀態具型化 ProductClient 介面、實作及相依性注入（DI）註冊進行架構正確性、隔離性、生命週期、失敗行為、相容性評估，並提供最小測試計畫。

---

## 1. Correctness (正確性)

### **[Critical] 嚴格的 GUID 驗證與 Fail-Closed 契約**
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient\Diagnostics\RuntimeHealthWhoAmIClient.cs`
* **判定依據**：WhoAmI 運作用於驗證執行期健康狀態與識別。若回傳的 GUID 包含空值（`Guid.Empty`）或 `null`，代表底層連線或識別已處於不確定狀態。
* **具體要求**：實作中必須對 `WhoAmIResponseData` 的 `UserId`、`BusinessUnitId` 與 `OrganizationId` 進行非空與非 `Guid.Empty` 驗證。若驗證失敗，必須立即拋出 `InvalidOperationException`，絕不可返回部分初始化的 DTO。

### **[Warning] 嚴格的響應分支與版本校對**
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient\Diagnostics\RuntimeHealthWhoAmIClient.cs`
* **判定依據**：為防止響應混淆或未預期的資料投影，必須確保 Executor 回傳的數據完全符合預期。
* **具體要求**：必須驗證 `OperationExecutionResult.Data` 的 `OperationId` 是否完全等於 `OperationIds.RuntimeHealthWhoAmI`、`CeVersion` 是否為 `"9.1"`，且 `ResponseKind` 必須為 `OperationResponseKind.WhoAmI`。

### **[Info] 介面與 DTO 定義**
* **檔案路徑**：
  * `SpeechMessage.Dynamics.ProductClient\Diagnostics\IRuntimeHealthWhoAmIClient.cs`
  * `SpeechMessage.Dynamics.ProductClient\Diagnostics\RuntimeHealthWhoAmIResult.cs`
* **具體要求**：定義唯讀且不可變（Immutable）的 DTO，僅暴露 `UserId`、`BusinessUnitId` 與 `OrganizationId` 三個 `Guid` 屬性。

---

## 2. Isolation (隔離性)

### **[Critical] 無狀態（Stateless）設計與安全邊界**
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient\Diagnostics\RuntimeHealthWhoAmIClient.cs`
* **判定依據**：ProductClient 僅作為無狀態的傳輸外殼，不應保留任何與特定請求、憑證或連線相關的狀態，以避免多執行緒併發時的狀態交叉污染。
* **具體要求**：
  * 類別成員欄位僅允許持有 DI 注入的 `IDynamicsOperationExecutor` 與 `ILogger`，嚴禁宣告任何用於快取 Request、Response、ProfileAlias、WorkloadSubjectId 或 GUID 的成員變數。
  * 所有參數與執行結果必須完全存活於方法呼叫的 Stack 範圍內。

### **[Critical] 敏感資訊防洩漏邊界**
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient\Diagnostics\RuntimeHealthWhoAmIClient.cs`
* **判定依據**：根據安全邊界規範，產品邊界外嚴禁暴露底層 SDK 物件、HTTP 狀態碼、Endpoint 網址、認證資訊（Credentials）或原始錯誤訊息。
* **具體要求**：若 Executor 執行失敗，僅能記錄去識別化的 Warning 日誌，並拋出不含敏感資訊的 `InvalidOperationException`。

---

## 3. Lifecycle (生命週期)

### **[Warning] 資源清理與處置權限歸屬**
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient\Diagnostics\RuntimeHealthWhoAmIClient.cs`
* **判定依據**：ProductClient 不應越權管理 I/O 連線或租約生命週期。
* **具體要求**：Client 類別不可實作 `IDisposable`。底層連線、HTTP Client、Lease 或 Permit 的生命週期與清理責任應完全由注入的 `IDynamicsOperationExecutor` 負責。

### **[Info] DI 註冊生命週期**
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient\DependencyInjection\ProductClientServiceCollectionExtensions.cs`
* **具體要求**：由於 Client 為完全無狀態，應使用 `TryAddSingleton` 將其註冊為 Singleton，以減少不必要的物件實例化開銷。

---

## 4. Failure Behavior (失敗行為)

### **[Warning] 失敗傳播與日誌記錄**
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient\Diagnostics\RuntimeHealthWhoAmIClient.cs`
* **判定依據**：當健康檢查失敗時，必須確保呼叫端能明確捕獲異常，同時日誌中不得包含敏感的連線字串或認證 Token。
* **具體要求**：當 `OperationExecutionResult.Succeeded` 為 `false` 時，應使用 `ILogger` 記錄警告日誌（僅包含錯誤代碼如 `ErrorCode`），隨後拋出 `InvalidOperationException`。

---

## 5. Compatibility (相容性)

### **[Info] 增量式 DI 註冊**
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient\DependencyInjection\ProductClientServiceCollectionExtensions.cs`
* **具體要求**：
  * 新增 `AddSpeechMessageDynamicsRuntimeHealthWhoAmI` 擴充方法，或在現有的 `AddSpeechMessageDynamicsGatewayProductClient` 中增量註冊 `IRuntimeHealthWhoAmIClient`。
  * 此變更為純增量設計，不影響現有的 ToolUtility 舊有程式碼，亦不涉及任何 Feature Gate 的變更，具備完全的向下相容性。

---

## 6. Minimal Test Plan (最小測試計畫)

### **[Info] 測試實作規劃**
* **測試檔案路徑**：`SpeechMessage.Dynamics.Tests\RuntimeHealthWhoAmIProductClientTests.cs`
* **核心測試案例**：
  1. **Success_Path_Mapping**：模擬 Executor 回傳成功的 `OperationResponseData.ForWhoAmI`（包含三個有效 GUID），驗證 Client 能正確解析並回傳對應的唯讀 DTO。
  2. **Executor_Failure_Throws**：模擬 Executor 回傳 `Succeeded = false`，驗證 Client 拋出 `InvalidOperationException`。
  3. **Empty_Or_Null_Guid_Throws**：模擬回傳的 `UserId`、`BusinessUnitId` 或 `OrganizationId` 為 `Guid.Empty` 或 `null`，驗證 Client 觸發 Fail-Closed 並拋出異常。
  4. **Mismatched_Operation_Or_Version_Throws**：模擬回傳不匹配的 `OperationId`、`CeVersion` 或 `ResponseKind`，驗證 Client 拒絕該回應並拋出異常。
  5. **DI_Registration_Verification**：驗證透過 `IServiceCollection` 註冊後，能正確解析出 `IRuntimeHealthWhoAmIClient` 的 Singleton 實例。
