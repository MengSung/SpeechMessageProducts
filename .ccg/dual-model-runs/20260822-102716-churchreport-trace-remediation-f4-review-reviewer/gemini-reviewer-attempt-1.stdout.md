# UI Reviewer 審查報告：churchreport-trace-remediation-f4-review

本報告針對 F4 背景追蹤邊界（Background Trace Boundary）的變更進行程式碼審查。審查範圍包含 `DataverseTrace.cs`、`DataverseTraceTests.cs` 以及 `SmallGroupController.Save.cs`。

---

## 1. 摘要 (Summary)
本次變更完整且正確地實現了 F4 的所有技術需求。透過 `AsyncLocal<RequestContext>` 的 copy-on-write 特性，成功實現了背景作業（Background Operation）與父請求（Parent Request）之間的統計數據隔離，防止了背景 CRM 工作污染父請求的 `request.end` 指標。同時，巢狀與平行背景作業的隔離性也得到了單元測試的驗證。整體程式碼品質優良，異常處理完善，符合專案的設計規範。

---

## 2. 無障礙性問題 (Accessibility Issues)
* **評估**：本次變更完全屬於後端追蹤與控制器邏輯，不涉及任何前端 UI 元素、HTML 結構或 ARIA 屬性。
* **結論**：無相關 Accessibility 問題。

---

## 3. 設計一致性 (Design Consistency)
* **命名規範**：背景作業的事件命名（`bg.begin` / `bg.end`）與現有的 `request.begin` / `request.end` 保持高度一致。
* **欄位對應**：`bg.end` 輸出的 JSONL 欄位完整包含了所有 request 聚合欄位（如 `durationMs`、`crmCount`、`crmMs` 等），並額外附加了 `parentTraceId` 與 `op`，符合設計 schema。
* **主機中立性**：`ToolUtility` 內部的追蹤邏輯未引入任何與特定 Web 主機（如 HttpContext 或 Session）耦合的依賴，保持了良好的 Host-neutral 特性。

---

## 4. 建議與改進 (Suggestions)
* **欄位借用註解**：在 `TraceEntry` 類別中，`ClientId` 欄位在 `BackgroundEnd` 事件中被借用來傳遞 `topEntity` 名稱。雖然這在扁平化結構中是合理的設計，但建議在 `TraceEntry.ClientId` 欄位上加上註解說明，以避免後續維護人員的誤解。
* **編碼一致性**：確保所有新增或修改的原始碼檔案均以 **UTF-8 without BOM** 格式儲存，以防止在不同作業系統或 IDE 下出現中文註解亂碼的問題。

---

## 5. 優點 (Positive Notes)
* **防禦性編程**：`BeginBackgroundOperation` 對 `operationName` 進行了 null 檢查並提供 fallback，且在 `Task.Run` 內部對異常進行了妥善的捕獲與記錄，避免了背景執行緒異常導致的潛在問題。
* **生命週期管理**：`SaveIntegrate` 在啟動背景 DI Scope 之前即開啟了 `traceScope`，確保了背景作業的追蹤範圍能完整覆蓋整個背景生命週期。
* **測試覆蓋率**：單元測試 `Parallel_and_nested_background_scopes_keep_independent_contexts` 與 `Background_scope_owns_crm_statistics_without_polluting_parent_request` 真實且嚴格地驗證了隔離性合約。

---

## 6. 詳細發現 (Detailed Findings)

### 【Info】檔案中文註解編碼顯示問題
* **檔案路徑**：
  - `ToolUtility/Dataverse/DataverseTrace.cs`
  - `ToolUtility.Dataverse.Tests/DataverseTraceTests.cs`
  - `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs`
* **具體程式碼**：檔案中所有的繁體中文註解。
* **理由**：在某些檢視工具或終端環境下，檔案中的繁體中文註解可能會因為編碼識別問題顯示為亂碼（例如 `撽?`）。建議團隊在提交前確認檔案編碼是否確實為 UTF-8 without BOM，並確保開發環境的編碼設定一致。

### 【Info】`TraceEntry` 欄位借用缺乏說明
* **檔案路徑**：`ToolUtility/Dataverse/DataverseTrace.cs` (第 134 行)
* **具體程式碼**：
  ```csharp
  internal string ClientId;
  ```
* **理由**：在 `BackgroundScope.Dispose` 中，`top.Item1`（即 `topEntity`）被賦值給了 `ClientId`，並在 `WriteEventFields` 中以 `topEntity` 鍵值輸出。這屬於欄位借用，建議在 `ClientId` 宣告處加上註解，說明其在 `BackgroundEnd` 事件中代表 `topEntity`。
