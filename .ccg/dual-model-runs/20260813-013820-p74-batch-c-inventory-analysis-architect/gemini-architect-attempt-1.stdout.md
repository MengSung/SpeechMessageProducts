# P7.4 Batch C Caller-Shape Inventory 唯讀分析報告

本報告針對目前 Repository 中三個 `not-migrated` 的能力進行分析，評估其在既有 P7.4 active task 中作為本機唯讀 sub-batch 遷移的可行性。

---

## 逐一能力分析

### 1. ORG-CALL-00005 / `fee.dedication.retrieve.by.contact`

*   **實際 ChurchReport Caller 檔案／方法及其 Response Shape**：
    *   **Caller**：`SpeechMessageProducts.ChurchReport\Controllers\DedicationController.cs`（或相關的奉獻查詢服務），呼叫 `ToolUtilityClass.RetrieveDedicationFee(contactName, contactId)`。
    *   **Response Shape**：回傳 `EntityCollection`，包含 `new_fee` 實體列表（包含 `new_feeid`, `new_name`, `createdon`, `new_pay_date`, `new_fee_shoud_pay`, `new_fee_really_paid`, `new_pay_way`, `new_category`, `new_others` 等欄位）。
*   **屬性分析**：
    *   **是否唯讀**：是。
    *   **是否相鄰 Write**：否，此為獨立的歷史奉獻紀錄查詢。
    *   **是否依賴 SDK Entity/EntityCollection**：是，既有 caller 依賴 `EntityCollection` 進行後續的 Map 處理。
*   **P7.4 遷移可行性**：
    *   **可行**。此能力與已遷移的 `fee.dedication.retrieve.by.contact.date.range` (ORG-CALL-00006) 結構高度相似，且 `IPackage01FeeReadClient` 已具備對應的 `RetrieveDedicationFeesByContactAsync` 方法，回傳 `IReadOnlyList<FeeRecordDto>`。
*   **最小檔案與測試邊界**：
    *   **最小檔案**：
        *   `SpeechMessageProducts.ChurchReport\Services\DonationFeeQueryService.cs`（引入或擴充此服務以支援不帶日期區間的查詢）。
        *   `SpeechMessageProducts.ChurchReport\Controllers\DedicationController.cs`。
    *   **測試邊界**：
        *   於 `ChurchReport.MemberInfo.Tests` 中建立單元測試，驗證當 `Package01FeeReadsEnabled` 為 `false` 時，走 legacy `ToolUtility` 路線；為 `true` 時，走 `IPackage01FeeReadClient` 路線。
        *   驗證 `FeeRecordDto` 到 `DedicationFee` 畫面 model 的 mapping 正確性，並確保 `CancellationToken` 傳遞與異常隔離（fault isolation）。

---

### 2. ORG-CALL-00064 / `fees.retrieve.by.dedication.period`

*   **實際 ChurchReport Caller 檔案／方法及其 Response Shape**：
    *   **Caller**：`SpeechMessageProducts.ChurchReport\Controllers\DedicationController.cs` 或金流對帳模組，呼叫 `ToolUtilityClass.RetrieveFee(dedicationBookingName, dedicationBookingId, paidPeriod)`。
    *   **Response Shape**：回傳 `EntityCollection`，包含 `new_fee` 實體。
*   **屬性分析**：
    *   **是否唯讀**：是。
    *   **是否相鄰 Write**：是，此查詢通常用於金流對帳或奉獻期程管理，讀取特定期程的費用後，常伴隨金流狀態更新或扣款寫入（Package02 write）。
    *   **是否依賴 SDK Entity/EntityCollection**：是。
*   **P7.4 遷移可行性**：
    *   **不可行 (維持 Temporary-Legacy)**。
*   **精確 Blocker**：
    *   此查詢與金流寫入流程（Package02 write）高度交錯。若在 P7.4 單獨將讀取端切換為 DTO，會導致讀寫模型不一致，且缺乏 transaction 邊界保護。必須維持 temporary-legacy，待 P7.5/P8 寫入端遷移時一併處理。

---

### 3. ORG-CALL-00066 / `fees.editor.load.by.disciplelesson`

*   **實際 ChurchReport Caller 檔案／方法及其 Response Shape**：
    *   **Caller**：`SpeechMessageProducts.ChurchReport\Controllers\FeeManagementController.cs`，呼叫 `ToolUtilityClass.RetrieveStorLessonsByDiscipleLessonsFetchXml(lessonName, lessonId)`。
    *   **Response Shape**：回傳 `EntityCollection`，包含 `new_stor_lesson` 實體。
*   **屬性分析**：
    *   **是否唯讀**：是。
    *   **是否相鄰 Write**：是，此能力專門用於 "Fee Editor"（費用編輯器），載入資料是為了讓使用者在介面上編輯費用並進行儲存（write）。
    *   **是否依賴 SDK Entity/EntityCollection**：是，且需要從中提取 FormattedValues 或進行關聯實體（EntityReference）的 rehydration。
*   **P7.4 遷移可行性**：
    *   **不可行 (維持 Temporary-Legacy)**。
*   **精確 Blocker**：
    *   與費用編輯器的寫入流程（Write Action）交錯，且依賴 SDK EntityCollection 的 rehydration 以進行後續的關聯更新。此外，`fees.editor.load.by.disciplelesson` 的 projection 需要補齊為 lesson inner link 的名稱、開課 UTC 等複雜欄位，強行切換會破壞編輯器的讀寫一致性。

---

## 評審結論與建議分類

### [Info] ORG-CALL-00005 可作為 P7.4 下一個安全實作的本機唯讀 sub-batch
*   **判定**：`fee.dedication.retrieve.by.contact` 具備完整的 DTO 契約與對應的 Client 實作，且無相鄰寫入依賴，適合在 `Package01FeeReadsEnabled` 保持 disabled 的前提下，進行 DTO-only 的本機代碼重構與測試覆蓋。

### [Warning] ORG-CALL-00064 與 ORG-CALL-00066 應維持 Temporary-Legacy
*   **判定**：這兩個能力與金流對帳及費用編輯器的寫入流程高度耦合，且依賴 SDK Entity 的狀態重建。在寫入端尚未遷移前，強行切換讀取端會引入嚴重的讀寫不一致風險，應列為 P7.5/P8 的 blocker，不應在 P7.4 進行硬切。
