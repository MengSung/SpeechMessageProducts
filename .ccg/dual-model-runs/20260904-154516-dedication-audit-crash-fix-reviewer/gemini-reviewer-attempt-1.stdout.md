# 審查報告 (Code & UI Review Report)

本次變更主要修正 `DedicationAuditController.DedicationFeeAuditViewWeb` 在從 Layout 導覽進入時，因 request-scoped 的 login contact 為 null 而觸發當機的問題，並補強 `DonationPaymentFormModel` 在沒帶 contact 狀態下的資料隔離與清空邏輯。

---

## 審查問題回應與評估

1. **`DedicationFeeAuditViewWeb` Layout 導覽 null 當機修復**：
   - **結果**：**已通過**。
   - **說明**：原先流程會直呼 `SetDedicationFeeList` 帶入未驗證之 contact，導致 `ArgumentNullException`。現已改由 `BuildAuditWebFormModel()` 檢查 `loginContact = InMemoryContext.PersonalInfomationModel?.m_LoginContact`。若為 `null` 則跳過 CRM 查詢並安全回傳初始化的空白 `DonationPaymentFormModel`，從 Layout 導覽進入不會再當機。

2. **`BuildAuditWebFormModel` 的 null-safe fallback 與個資隔離**：
   - **結果**：**已通過**。
   - **說明**：fallback 邏輯明確將 `FullName`、`Mobile`、`DedicationNumber`、`NationId`、`LastSixDigit` 重置為 `string.Empty`，並將 `DedicationFeeList` 與 `SameNameList` 執行 `Clear()`、`TotalAmount` 清零。防範了同一個 request-scoped manager 殘留上一次請求個資與明細的風險。

3. **AJAX / DataGrid 路徑對 null 表單模型的防禦力**：
   - **結果**：**需注意 (Warning)**。
   - **說明**：若使用者直接發起 AJAX 請求且 `m_DonationPaymentFormModel` 為 `null`，部分 DataGrid 路由仍會拋出 `NullReferenceException`（詳見下方 Issues 分級報告）。

4. **單元測試真實性與脆弱反射測試評估**：
   - **結果**：**需改進 (Warning)**。
   - **說明**：測試真實注入了舊使用者殘留資料 (`staleModel`) 並斷言 `BuildAuditWebFormModel` 的清空行為，**非 Tautological 無效測試**。但測試使用了 `GetMethod("BuildAuditWebFormModel", ...)` 反射呼叫控制器私有方法，存在方法重構即破壞測試的脆弱性。

5. **規範符合度（Session 隔離、資源生命週期、 UTF-8 無 BOM、CRLF、繁體中文）**：
   - **結果**：**符合**。
   - **說明**：新增的測試與 Controller 程式碼註釋完整使用繁體中文，檔案編碼為 UTF-8 without BOM，換行格式為 CRLF，且沒有產生未釋放的非受控資源。

---

## 評分報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 18/20 - 成功消除 Layout 導覽進頁時的 HTTP 500 當機
Visual Consistency: 18/20 - 確保未登入/無 contact 狀態時顯示安全且乾淨的空白表單
Accessibility: 16/20 - DataGrid AJAX 路由在模型為 null 時未防禦 NRE
Performance: 19/20 - 輕量記憶體操作，無額外 DB/CRM 查詢開銷
Browser Compatibility: 15/20 - 測試覆蓋目標情境，但反射 private 方法具脆弱性

TOTAL SCORE: 86/100

ISSUES FOUND:
- AJAX DataGrid 路由未對 manager.m_DonationPaymentFormModel 為 null 做 defensive coding
- 測試案例使用字串反射呼叫 private 方法 BuildAuditWebFormModel

RECOMMENDATION: PASS (Suggested Minor Improvements)
```

---

## 問題分級與修正建議 (Issues & Recommendations)

### ⚠️ Warning (中度風險)

#### Issue 1: DataGrid AJAX 路由在 `m_DonationPaymentFormModel` 為 `null` 時存取成員會觸發 `NullReferenceException`
- **檔案與行號**：
  `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 200, Line 230)
- **原因**：
  在 `LoadDedicationFeeList` 與 `LoadSameNameList` 中，若直接發起 DataGrid AJAX 載入且 `m_DonationPaymentFormModel` 尚未被 View 流程賦值（或被舊流程清為 `null`）：
  ```csharp
  // Line 200: m_DonationPaymentFormModel 為 null 時會爆 NullReferenceException
  var tasks = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.DedicationFeeList;

  // Line 230: SameNameList 的 ?? 救不到 m_DonationPaymentFormModel 為 null 的狀況
  var sameNameList = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.SameNameList
      ?? new System.Collections.Generic.List<SameNameElement>();
  ```
- **修正建議**：
  ```csharp
  // Line 200 建議修正：
  var tasks = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel?.DedicationFeeList
      ?? new System.Collections.Generic.List<DedicationFee>();

  // Line 230 建議修正：
  var sameNameList = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel?.SameNameList
      ?? new System.Collections.Generic.List<SameNameElement>();
  ```

---

#### Issue 2: 單元測試採用反射呼叫 Controller 私有方法，具備測試脆弱度 (Fragility)
- **檔案與行號**：
  `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs` (Line 240-244, Line 274)
- **原因**：
  測試使用 `GetMethod("BuildAuditWebFormModel", BindingFlags.Instance | BindingFlags.NonPublic)` 反射呼叫私有方法。一旦 Controller 將該私有方法重命名或重構，測試無法在編譯時期抓出，而會在執行期回傳 `NullReferenceException` 或 `MethodNotFound`。
- **修正建議**：
  將 `BuildAuditWebFormModel` 標註為 `internal` 並利用 `[assembly: InternalsVisibleTo("ChurchReport.MemberInfo.Tests")]` 曝露給測試專案，直接以強型別方式呼叫 `controller.BuildAuditWebFormModel()`。

---

### ℹ️ Info (資訊與優點)

1. **跨請求 Session 個資防護徹底** (Line 139-153)：
   `BuildAuditWebFormModel` 確實確保了所有的跨使用者敏感欄位 (如身分證字號 `NationId`、信用卡號後六碼 `LastSixDigit`、奉獻清單與同名清單) 都被主動 `Clear()` 與清空，沒有洩漏上一位使用者個資的風險。
2. **符合 UTF-8 without BOM 與 CRLF 規範**：
   變更檔案檔案標頭與斷行符合 Windows .NET 專案環境要求。
