已完成對工作樹未提交變更（與 HEAD 相較）的程式碼審查。

---

## 審查總覽與驗證結果 (Summary)

整體變更品質良好，核心修復完整解決了 Layout 導覽登入狀態遺失時的 NullReferenceException / ArgumentNullException 問題，並且具備良好的資料隔離與 null-safe 防護。

| 檢查項目 | 結果 | 說明 |
| :--- | :---: | :--- |
| **1. Layout 導覽 Null 當機防護** | **通過 (PASS)** | `BuildAuditWebFormModel` 依據 `InMemoryContext.PersonalInfomationModel?.m_LoginContact` 判斷，若為 null 則不執行 `SetDedicationFeeList`（避免 `ArgumentNullException`），`SetupAuditViewBag` 使用 null 條件運算子 `m_DonationPaymentFormModel?.IsAOfficeWorker`，避免當機。 |
| **2. Null-safe Fallback & 資料隔離** | **通過 (PASS)** | Fallback 顯式清理 `FullName`, `Mobile`, `DedicationNumber`, `NationId`, `LastSixDigit`, `TotalAmount` 為空或 0，並對 `DedicationFeeList` 與 `SameNameList` 呼叫 `.Clear()`，重新賦值給 `manager.m_DonationPaymentFormModel`，防止上一 request 殘留資料跨 request 洩漏。 |
| **3. AJAX/Grid 路徑防護** | **通過 (PASS)** | 新增 `EnsureAuditFormModel` 輔助方法，確保 Grid 與 AJAX 端點 (`LoadDedicationFeeList`, `LoadSameNameList`, `GetFeesByContactId`) 在表單模型或內部 List 為 null 時自動補齊為空模型/空列表，避免 NullReferenceException。 |
| **4. 單元測試有效性** | **通過 (PASS)** | 測試採用 `InternalsVisibleTo` 直接呼叫 `BuildAuditWebFormModel()`，無脆弱字串反射呼叫方法。測試前預先注入骯髒 model 並斷言清理結果，具備真實行為保護力，非 tautological。 |
| **5. Session 隔離/記憶體/規範/編碼** | **通過 (PASS)** | 採用 Request-scoped 狀態重置、無靜態殘留、繁體中文 XML/Inline 註解、檔尾 CRLF 且全為 UTF-8 無 BOM。 |
| **6. `QueryStartDate` 年份動態預設** | **通過 (PASS)** | `DonationPaymentFormModel.QueryStartDate` 使用 `new DateTime(DateTime.Now.Year, 1, 1)` 動態計算，無固定 2026 硬編碼，且單元測試能有效防止防止倒退回 `DateTime.Now`（當天）。 |

---

## 詳細審查發現 (Detailed Findings)

### 🔴 Critical (嚴重問題)
- 無發現 Critical 問題。

---

### 🟡 Warning (警告與強固化建議)

#### 1. Grid 測試之斷言強度可進一步提升
- **檔案/行號**: `SpeechMessageProducts.ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs` (Line 297, 312)
- **問題描述**: 
  在 `Dedication_audit_fee_grid_returns_empty_data_when_form_model_is_missing` 與 `Dedication_audit_same_name_grid_returns_empty_data_when_form_model_is_missing` 兩個測試中，目前僅使用 `action.Should().NotThrow()`。雖然驗證了不會當機，但未能直接驗證傳回的 `ContentResult`/JSON 資料長度是否為 0。
- **改善建議**:
  建議可加強斷言傳回的 JSON 結果：
  ```csharp
  var result = controller.LoadDedicationFeeList(string.Empty, new DataSourceLoadOptions()) as ContentResult;
  result.Should().NotBeNull();
  result!.Content.Should().Contain("\"data\":[]");
  ```

---

### 🔵 Info (資訊與說明)

#### 1. 遺留註解標頭亂碼（歷史遺留問題）
- **檔案/行號**: 
  - `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 2-12)
  - `SpeechMessageProducts.ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs` (Line 2-12)
- **說明**: 檔案頂部的歷史註解標頭包含舊式 Big5/ANSI 轉換殘留字元（例如 `//   H :`），不影響程式功能或編譯。建議在未來的重構維護中予以整理。

#### 2. `DonationPaymentFormModel.QueryStartDate` 預設值與測試保護
- **檔案/行號**: 
  - `SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs` (Line 187)
  - `SpeechMessageProducts.ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs` (Line 53-60)
- **說明**: 
  1. `QueryStartDate` 正確設定為 `new DateTime(DateTime.Now.Year, 1, 1)`，成功依據執行當下年份動態計算。
  2. 測試 `New_donation_form_defaults_query_start_date_to_current_year_first_day` 正確比較 `model.QueryStartDate` 與 `new DateTime(DateTime.Now.Year, 1, 1)`。若未來被誤改回 `DateTime.Now`（如當天日期 9 月 4 日），該測試會立即 Fail，發揮防護作用。

---

## 審查結論 (Conclusion)

本次修改完全符合功能需求與開發規範：
- 成功解決 Layout 導覽入口下的 Null 崩潰問題。
- 完成徹底的跨 Request 資料清理與防洩漏。
- 提供完整且強固的單元測試涵蓋。

**整體評估: PASS (通過)**
