# P7.4 Batch B 本機審查報告 (Package01 StorLesson read-only consumer cutover)

本報告針對 P7.4 Batch B 的未提交變更進行程式碼品質、效能、資源管理與 UI 數據呈現一致性的審查。

---

## 1. 總體評估 (Summary)

本次變更完整且嚴格地實現了 P7.4 Batch B 的設計目標。主要亮點包括：
* **無 SDK 邊界硬化**：成功將 `lesson` 的關聯欄位（`new_name`、`new_class_start_date`、`new_now_stage_name`）投影至純值 wire record，並透過 `StorLessonRecordDto` 傳遞，完全避免了在產品端（ChurchReport）重新呼叫 CRM SDK `RetrieveEntity` 進行補查。
* **非同步與取消傳播**：`MemberInfoController` 與 `EquipmentController` 的相關 Action 已成功改為非同步實作，並正確傳遞 `HttpContext.RequestAborted` 作為 cancellation token，確保資源能被確定性釋放。
* **隔離性與安全性**：嚴格區分了僅需畫面投影的非同步路徑與仍需 `EntityCollection` 的 legacy 同步路徑，避免了 sync-over-async 與跨請求的可變狀態污染。

---

## 2. 審查發現與分類 (Review Findings)

### 🔴 Critical Issues (嚴重問題)
* **無**：本次變更未發現會導致系統崩潰、安全性漏洞或違反硬性限制（如啟用 feature gate、CE 寫入等）的嚴重問題。

---

### ⚠️ Warning Issues (警告與潛在風險)

#### 1. 潛在的時區轉換溢出風險 (DateTime Offset Underflow Risk)
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/StorLessonQueryService.cs` (第 357 行)
* **程式碼**：
  ```csharp
  DiscipleLessonsDateTime = row.ClassStartDate?.LocalDateTime ?? DateTime.MinValue,
  ```
* **原因說明**：
  如果 CRM 傳回的 `ClassStartDate` 包含極端的 UTC 最小值（例如接近 `0001-01-01T00:00:00Z`），且執行伺服器的本機時區位於西半球（例如 UTC-5），呼叫 `.LocalDateTime` 會因為減去時區偏移量而導致時間低於 `DateTime.MinValue`，進而拋出 `ArgumentOutOfRangeException` 異常。
* **建議改善方案**：
  在轉換為 `LocalDateTime` 之前，應進行防禦性範圍檢查，或使用安全轉換 Helper。例如：
  ```csharp
  DiscipleLessonsDateTime = row.ClassStartDate.HasValue
      ? (row.ClassStartDate.Value.UtcDateTime < DateTime.MinValue.AddDays(1) 
          ? DateTime.MinValue 
          : row.ClassStartDate.Value.LocalDateTime)
      : DateTime.MinValue
  ```

---

### ℹ️ Info / Suggestions (建議與設計決策說明)

#### 1. 同步 API 刻意維持 Legacy-only 設計
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/StorLessonQueryService.cs`
* **說明**：
  `GetByContact`、`GetByDiscipleLesson` 與 `FindStorLessonId` 等同步 API 刻意不走 Package01 typed I/O，而是維持既有的 ToolUtility 查詢。這符合「SDK Entity / EntityCollection caller 必須保持 legacy-only，不能被誤標示為已遷移」的設計原則，有效防止了 sync-over-async 的發生。未來若有其他 caller 需要遷移，必須整體改為非同步路徑。

#### 2. 異常處理與請求取消 (Cancellation Handling)
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs` (第 405-410 行)
  * `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs` (第 582-587 行)
* **說明**：
  在 Controller 中捕獲 `OperationCanceledException` 並在 `HttpContext.RequestAborted.IsCancellationRequested` 為真時重新擲出（`throw;`），這是一個非常優秀的實作。它確保了當瀏覽器中斷連線時，ASP.NET Core 能夠立即終止請求並釋放 ProductClient 的連線與資源，避免不必要的運算與記憶體殘留。

---

## 3. 優秀實作點 (Positive Notes)

1. **Fail-Closed 錯誤處理**：在 `Package01Data8ReadOperations.cs` 的 `ReadOptionalAliasedUtcDateTime` 中，若 aliased 屬性存在但型別不符，會立即拋出 `InvalidOperationException` 進行 fail-closed，防止錯誤的資料格式污染上游。
2. **Byte Budget 計算精確**：在 `Package01Data8ReadOperations.cs` 中，將新引入的 `StageName` 納入 `TryAddStringBytes` 的單頁與累積 byte 預算計算中，確保大數據量時不會規避 64 KiB 的限制。
3. **單元測試覆蓋完整**：在 `OnPremiseData8ConnectorClientFactoryTests.cs` 與 `Package01FeeReadClientTests.cs` 中，新增了針對 `lesson` link 投影、錯誤型別拒絕、byte budget 限制以及 DTO 映射隔離性的測試，且測試替身（Fake/Mock）設計良好，無狀態且不殘留 session。
