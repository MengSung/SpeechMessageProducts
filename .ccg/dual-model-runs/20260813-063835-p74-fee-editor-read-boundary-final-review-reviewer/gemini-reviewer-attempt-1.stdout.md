# P7.4 Fee-Editor Read-Boundary 最終審查報告

本報告針對 P7.4 `fee-editor` 唯讀邊界（read-boundary）的變更進行程式碼審查與驗證。

---

## 1. 總體評估 (Summary)
本次變更成功實作了 P7.4 合約所要求的唯讀邊界保護。新路由 `/FeeManagement/Api/FeeEditorRows/{discipleLessonsId?}` 嚴格遵循了雙重部署閘門（dual deployment gates）的短路保護、伺服器端授權驗證、不可變 DTO 投影，以及取消語意（`OperationCanceledException`）的透傳。所有測試案例均已就緒，且本機設定檔中的閘門均保持關閉（`false`），符合僅限本機驗證的規範。

---

## 2. 審查清單與合約符合度驗證

### 2.1 雙重部署閘門與短路保護 (Contract 1 & 2)
* **驗證結果**：**符合**。
* **細節**：在 `FeeManagementController.GetFeeEditorRows` 中，第一行程式碼即為 `if (!DonationDynamicsAccessBootstrap.IsPackage01FeeEditorReadEnabled(_configuration))`。此檢查在解析 browser 傳入的 `discipleLessonsId`、讀取 Session/FeeList、建立 Package01 client 或進行任何 I/O 之前執行。
* **閘門邏輯**：`IsPackage01FeeEditorReadEnabled` 同時要求 `IsPackage01Enabled`（`Package01FeeReadsEnabled`）與 `Package01FeeEditorReadEnabled` 皆為 `true`。

### 2.2 伺服器端授權與定位器解析 (Contract 2)
* **驗證結果**：**符合**。
* **細節**：
  * 系統先透過 `feeList.EnsureLoginScope(account, password)` 重新 scope 既有 session cache。
  * 接著使用 `FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds` 根據伺服器端已載入的課程快照建立授權的 `Guid` 白名單。
  * 最後才解析 browser 傳入的 `discipleLessonsId` 並透過 `IsAuthorizedTarget` 進行比對。
  * 過程中**無**任何 CRM 掃描、遺留載入器（如 `EnsureLessonListLoaded`）或回退重試機制。

### 2.3 固定操作與伺服器擁有屬性 (Contract 3)
* **驗證結果**：**符合**。
* **細節**：
  * 呼叫 `_package01Client.RetrieveFeeEditorRowsByDiscipleLessonAsync` 時，固定傳入 `profileAlias`（來自伺服器配置的 `ProductDynamicsOptions`）與 `WorkloadSubjectId`（固定為 `"church-report-service"`）。
  * 該方法在 `Package01FeeReadClient` 中被固定映射至 `OperationIds.FeesEditorLoadByDiscipleLesson`（即 `fees.editor.load.by.disciplelesson`）。

### 2.4 不可變白名單純量 DTO (Contract 4)
* **驗證結果**：**符合**。
* **細節**：
  * 回傳型別為 `FeeEditorReadResult`，其建構子對傳入的 rows 進行了防禦性複製（defensive copy），並將其包裝為 `ReadOnlyCollection<FeeEditorReadRow>`。
  * `FeeEditorReadRow` 僅包含純量屬性（如 `Guid?`、`DateTimeOffset?`、`decimal?`、`string`），不持有任何 CRM Entity 或可編輯的 `Fee` 物件。
  * 該路徑與 `UpdateFeeData`、`SaveBatch` 等可編輯路徑完全隔離。

### 2.5 課程相符性與取消語意 (Contract 5)
* **驗證結果**：**符合**。
* **細節**：
  * `FeeEditorReadService.RetrieveAsync` 遍歷結果時，若發現任何 row 的 `DiscipleLessonId` 與請求的 `discipleLessonId` 不符或為 `null`，會立即拋出 `InvalidOperationException`，防止發布部分或錯誤的資料。
  * 控制器的 catch 區塊使用 `catch (Exception ex) when (ex is not OperationCanceledException)`，確保 `OperationCanceledException` 能原封不動地逃脫，保留 ASP.NET Core 原有的取消語意。

### 2.6 本機驗證限制 (Contract 6)
* **驗證結果**：**符合**。
* **細節**：
  * `appsettings.json` 與 `appsettings.Development.json` 中的 `Package01FeeReadsEnabled` 與 `Package01FeeEditorReadEnabled` 均明確設為 `false`。
  * 本報告僅作為本機驗證證據，**不聲稱** CE、Dedicated、切流（cutover）、P7.5 或 P8 的完成。

---

## 3. 發現與建議 (Findings & Suggestions)

### 【Info】檔案註解存在亂碼
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Services/FeeEditorLessonAccessResolver.cs`
  * `SpeechMessageProducts.ChurchReport/Services/FeeEditorReadService.cs`
  * `SpeechMessageProducts.ChurchReport/Models/FeeEditorReadResult.cs`
* **原因**：上述檔案的標頭與方法註解中存在非 ASCII 字元的亂碼（可能是 UTF-8 與 Big5 轉換問題）。
* **影響**：不影響程式碼編譯與執行，但會降低程式碼的可讀性與維護性。
* **建議處置**：將檔案編碼統一轉換為帶有 BOM 的 UTF-8（UTF-8 with BOM），並修復註解中的中文說明。

---

## 4. 優秀實作亮點 (Positive Notes)
1. **極度嚴格的 Fail-Closed 設計**：`FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds` 在遇到任何未載入、`null` 課程、無效 GUID 或重複 GUID 時，皆會立即回傳 `false` 並清空輸出，確保授權白名單的絕對安全。
2. **高品質的合約測試**：`FeeManagementControllerFeeEditorReadContractTests` 採用了讀取原始碼並進行結構分析的靜態合約測試，能有效防止日後開發人員在修改控制器時，意外將 legacy 載入器或可編輯路徑引入該唯讀端點。
3. **無狀態與生命週期隔離**：`FeeEditorReadService` 採 request-local 設計，不持有任何 HTTP 上下文或快照狀態，且其依賴的 `IPackage01FeeReadClient` 透過單例的 Process Host 進行管理，避免了連線池與 socket 資源洩漏的風險。
