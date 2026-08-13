# P7.4 Batch B 本機審查報告 (Package01 StorLesson read-only consumer cutover)

本報告針對 P7.4 Batch B 未提交的 Git 變更進行程式碼與設計審查。本次變更主要涉及 `StorLesson` 的唯讀消費端切換，將原本依賴 CRM SDK Entity 的同步查詢，重構為基於 DTO 投影的非同步、具備取消感知（cancellation-aware）的架構。

---

## 1. 審查摘要 (Summary)

本次提交的變更完整且精確地實現了 P7.4 Batch B 的所有授權與預期變更，並嚴格遵守了所有硬性限制。
* **架構一致性**：成功將 `MemberInfoController` 與 `EquipmentController` 的課程載入路徑重構為全程非同步，並將 `HttpContext.RequestAborted` 傳遞至底層，實現了請求取消的傳播。
* **資料邊界隔離**：移除了 `StorLessonQueryService` 中原本在 Package01 啟用時對 `RetrieveEntity` 的補查邏輯，改由 Data8 Connector 透過 `lesson` inner link 一併投影 `new_class_start_date` 與 `new_now_stage_name`，確保 CRM Entity 與 SDK 狀態不會逸出 Connector 邊界。
* **測試覆蓋率**：新增了多項高質量的單元測試，涵蓋了 A/B 隔離、cancellation 傳播、錯誤型別 fail-closed 以及 null 日期維持 `DateTime.MinValue` 的邊界條件。

---

## 2. 審查清單與合規性檢查

### 預期變更合規性
* **Data8 `lesson` inner link 投影**：已在 `Package01Data8ReadOperations.cs` 中加入 `AddDiscipleLessonLink`，並精確讀取 `new_class_start_date` 與 `new_now_stage_name`。 (合規)
* **非同步與取消感知**：兩個 Controller 的 Action 均已改為 `async Task<object>`，並傳遞 `HttpContext.RequestAborted`。 (合規)
* **無 SDK 補查與狀態殘留**：`StorLessonQueryService` 的 Package01 路徑已完全移除 `RetrieveEntity`，且無跨請求的可變狀態。 (合規)
* **Legacy 呼叫端保持不變**：`GetEntityCollectionByContact`、`GetEntityCollectionByDiscipleLesson` 與 `FindStorLessonId` 均維持 legacy-only，未被誤標示為已遷移。 (合規)
* **Null 日期處理**：當 `ClassStartDate` 為 null 時，投影結果維持 `DateTime.MinValue`，避免了時區偏移導致的無效日期顯示。 (合規)

### 硬性限制合規性
* **Feature Gate 保持 False**：未強制啟用任何 feature gate，預設值均為 false。 (合規)
* **無擴張變更**：未包含 ToolUtility 移除、雙寫或 generic CRM proxy。 (合規)
* **編碼與格式**：檔案變更符合專案規範。 (合規)

---

## 3. 審查發現 (Findings)

### Critical (嚴重問題)
* **無**。未發現任何違反硬性限制或導致系統不穩定的嚴重問題。

### Warning (警告事項)
* **無**。程式碼品質良好，邏輯清晰，且有完善的測試保護。

### Info (提示資訊)
#### 1. 測試檔案中的字元編碼與註解亂碼問題
* **檔案路徑**：
  * `ChurchReport.MemberInfo.Tests/Controllers/StorLessonControllerProductClientContractTests.cs`
  * `ChurchReport.MemberInfo.Tests/Services/StorLessonQueryServiceAsyncTests.cs`
* **原因說明**：這兩個新增的測試檔案在讀取時，部分中文註解呈現亂碼（例如 `瑼?嚗hurchReport...`）。這通常是因為檔案在儲存時使用了特定的本地編碼（如 Big5 或帶 BOM 的 UTF-8），而 Git 或其他工具以不同的編碼解析。
* **建議措施**：建議開發團隊在提交前，將這些檔案統一轉換為 **UTF-8 without BOM** 編碼，並確保換行符符合專案規範（LF 或 CRLF），以確保在 CI/CD 環境與不同作業系統的 IDE 中註解均能正確顯示。

---

## 4. 優秀實踐 (Positive Notes)

1. **防禦性設計與 Fail-Closed**：在 `ReadOptionalAliasedUtcDateTime` 中，針對 aliased value 的型別進行了嚴格的檢查，若型別不符則直接拋出 `InvalidOperationException`，防止了潛在的 schema 漂移被隱蔽。
2. **記憶體預算控制**：在 `TryAddRecordBytes` 中，將新增的 `StageName` 欄位納入了單頁 64 KiB 的 byte budget 計算中，有效防止了記憶體超限風險。
3. **高質量的 A/B 隔離測試**：`Package01_contact_reads_keep_interleaved_request_projections_isolated` 測試精確模擬了交錯請求的情境，確保了 request-local 資料的隔離性，避免了跨請求的資料洩漏。
