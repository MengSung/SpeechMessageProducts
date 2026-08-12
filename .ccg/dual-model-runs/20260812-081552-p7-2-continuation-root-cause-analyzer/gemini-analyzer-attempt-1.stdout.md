# CCG 根因與跨使用者隔離分析報告

本報告針對 `DownloadListManager` 與 `ListManager` 的連線洩漏風險、例外處理堆疊重設、受控診斷欄位設計，以及本機測試與 CE evidence gate 的分離策略進行深入分析。

---

## 1. UX Analysis (使用者影響評估)

### 跨 Request／Profile 洩漏對使用者體驗與隱私的影響
*   **Critical (極高風險)**: 由於 `DownloadListManager` 將短生命週期的 `IOrganizationService` 寫入全域單例 (Singleton) 的 `ToolUtilityClass` 欄位，這會導致**跨使用者的資料洩漏 (Cross-user data leakage)**。
    *   **使用者旅程影響**: 使用者 A 登入並查詢其個人小組列表後，其連線憑證被寫入單例欄位。隨後使用者 B 登入時，系統會直接重用使用者 A 的連線，導致使用者 B 看到使用者 A 的私密小組資料、主日報表或個人通訊錄。這嚴重違反了隱私保護與合規性。
    *   **行動端與桌面端體驗**: 在高併發的行動端使用場景下，多個使用者同時發送 request，會導致連線在多個執行緒間被反覆覆寫，造成畫面資料錯亂、查詢失敗或權限提升錯誤。

---

## 2. Design Evaluation (設計系統與模式評估)

### Singleton 模式的誤用與狀態管理
*   **Critical (架構缺陷)**: `ToolUtilityClass` 被設計為 Singleton（透過 `ToolUtilityFactory.GetInstance()` 取得），但它內部卻維護了具備狀態的 `m_Crm2011OrganizationService` 與 `m_OrganizationService` 欄位。
    *   **生命週期不一致**: 傳入的 `IOrganizationService` 是從 `ICrmConnectionPool` 借出的短生命週期租約 (Lease)，應該在單次 Request 結束時被歸還或釋放。將短生命週期的租約寫入長生命週期的 Singleton 物件，破壞了物件的生命週期管理，導致資源無法被正確回收。
    *   **一致性缺失**: 現有模式違反了 **Operation-local Ownership** 原則。誰建立/借用連線，誰就應該在同一個 bounded flow 中負責釋放，而不應將其共享或交由全域單例保管。

---

## 3. Technical Considerations (技術考量與架構影響)

### 多執行緒併發與例外堆疊重設
*   **Warning (併發與除錯風險)**: 
    *   **Race Condition**: `IOrganizationService` 實例（特別是 `OrganizationServiceProxy`）並非 Thread-safe。多個執行緒同時呼叫 `RetrieveMultiple` 或 `Execute` 會導致連線中斷或內部狀態損毀。
    *   **Exception Stack Trace 遺失**: `DownloadListManager` 與 `ListManager` 內部的 `catch (Exception e) { ... throw e; }` 會重設例外的呼叫堆疊 (Stack Trace)，使除錯時無法定位到真正的錯誤源頭（例如 `FindLoginUser` 內部的 CRM 查詢失敗），增加了維護與診斷的難度。

---

## 4. Options (替代方案與權衡)

### 方案 A：重構 `ToolUtilityClass` 方法以接受參數 (最推薦)
*   **做法**: 修改 `ToolUtilityClass` 的查詢方法，使其接受一個可選的 `IOrganizationService` 參數。如果傳入，則使用傳入的 service；否則使用內建的連線。
*   **優點**: 徹底消除 Singleton 的狀態維護，實現無狀態 (Stateless) 的 Helper 類別。
*   **缺點**: 修改範圍較大，可能影響其他依賴 `ToolUtilityClass` 的模組。

### 方案 B：將 `DownloadListManager` 改為使用 Transient/Scoped 實例
*   **做法**: 移除 `DownloadListManager` 對 Singleton `ToolUtilityClass` 的依賴。在 `DownloadListManager` 內部，每次呼叫時建立一個新的 `ToolUtilityClass` 實例（非 Singleton），或者在 `DownloadListManager` 內部維護一個 operation-local 的 `ToolUtilityClass` 實例。
*   **優點**: 修改侷限在 `DownloadListManager` 內部，風險極低，且能完全隔離不同 Request 的連線。
*   **缺點**: 每次建立實例可能會有些微的效能開銷（但可透過快取連線服務緩解）。

### 方案 C：使用 `AsyncLocal<IOrganizationService>` 進行執行緒隔離
*   **做法**: 在 `ToolUtilityClass` 內部使用 `AsyncLocal` 來儲存 `IOrganizationService`，確保每個非同步呼叫鏈擁有獨立的連線副本。
*   **優點**: 不需要修改方法簽章，且能保證執行緒隔離。
*   **缺點**: 增加了程式碼的複雜度，且在非同步上下文切換不當時仍有洩漏風險。

---

## 5. Recommendation (推薦方案與具體實作建議)

### 審查點 1：跨 Request 洩漏風險與最低風險的 Operation-local 介面修正方式
*   **結論**: 確有極高洩漏風險。
*   **修正建議 (方案 B)**:
    1. 修改 `DownloadListManager` 的建構子，使其不再透過 `ToolUtilityFactory.GetInstance()` 取得單例，而是改為接受 `IConfiguration` 並直接 `new ToolUtilityClass(configuration)`，或者透過 DI 容器注入一個 `Transient` 或 `Scoped` 的 `ToolUtilityClass` 實例。
    2. 在 `GetListManager` 中，將傳入的 `organizationService` 賦值給該**局部實例**的 `m_Crm2011OrganizationService`。由於該實例的生命週期僅限於該次 Request，因此不會洩漏給其他 Request。
    3. 移除 `GetListManager` 中將 service 寫回全域單例的邏輯。

### 審查點 2：Child-to-Parent 受控診斷的固定分類欄位
*   **結論**: 為了說明 no-go 又不洩漏 CRM 細節，應定義一組標準化的診斷欄位。
*   **允許的固定分類欄位**:
    *   `ErrorCategory` (string): 例如 `ConnectionError`、`AuthError`、`Timeout`、`DataValidationError`。
    *   `OperationStage` (string): 例如 `RetrieveContact`、`QueryList`、`GetSmallGroupMemberNumber`。
    *   `DiagnosticCode` (string): 例如 `ERR-CRM-001` (連線逾時)、`ERR-CRM-002` (權限不足)。
    *   `IsRecoverable` (bool): 表示該錯誤是否可重試。
    *   `Timestamp` (DateTime): 錯誤發生的時間。
*   **禁止洩漏的欄位**: 原始 `Exception.Message`、`StackTrace`、任何 GUID、CRM ID、帳號密碼、Endpoint URL。

### 審查點 3：必須先寫的最小 TDD 測試與回歸測試
*   **測試 1：跨使用者隔離測試 (Isolation Test)**
    *   **步驟**: 建立兩個 Mock `IOrganizationService` 實例（`mockService1` 與 `mockService2`）。
    *   **驗證**: 依序或併發呼叫 `DownloadListManager.GetListManager`，驗證呼叫 1 結束後，呼叫 2 執行時，`DownloadListManager` 內部使用的 service 確實是 `mockService2`，且全域單例 `ToolUtilityFactory.GetInstance()` 的欄位中沒有殘留任何一個 mock 實例。
*   **測試 2：Exception Stack Trace 保留測試**
    *   **步驟**: 故意讓 `GetListManager` 內部的 CRM 呼叫拋出例外。
    *   **驗證**: 將 `throw e;` 改為 `throw;`，並驗證外部捕獲的例外其 Stack Trace 包含原始拋出點（例如 `FindLoginUser`），而非被 catch 區塊截斷。
*   **測試 3：Timeout 與 Dispose 測試**
    *   **步驟**: 模擬 CRM 呼叫逾時拋出 `TimeoutException`。
    *   **驗證**: 驗證 `DownloadListManager` 能正確釋放資源，且多次呼叫 `Dispose` 不會拋出 `ObjectDisposedException`（冪等性驗證）。

### 審查點 4：Slice D–H 本機 Capability 與 CE Evidence Gate 分離
*   **策略**:
    1. **Feature Toggle**: 在 `appsettings.json` 中加入 `CrmConnection:UseScopedUtility` 開關。在本地開發與測試（Slice D-H）時啟用新機制，而未通過 CE gate 的環境則保持舊機制。
    2. **Shadow Execution**: 在本地測試中並行執行新舊路徑，比對兩者產出的 `MultiGroupList` 資料是否完全一致，並將比對結果輸出為 evidence 檔。
    3. **介面抽象化**: 將 `DownloadListManager` 的資料存取行為抽象為介面（例如 `IListRepository`），舊的實作依賴 `ToolUtility`，新的實作依賴乾淨的、不具狀態的連線。透過 DI 容器在不同環境注入不同的實作，確保 `ToolUtility` 在 Slice D-H 期間安全共存，直到完全棄用。
