# Dedication Audit Crash Fix - 程式碼審查報告

## 綜合評估 Summary

本次未提交變更成功修復了 `DedicationAuditController.DedicationFeeAuditViewWeb` 從 Layout 導覽進入時，因 request-scoped `DonationPaymentManager.m_Contact` 或 `m_LoginContact` 為 `null` 而導致當機的缺陷。修改內容正確實作了 null-safe fallback，清空上一位使用者的敏感個資與奉獻紀錄，並將安全模型回存至 manager，避免了後續 AJAX/Grid 請求解參考當機的風險。單元測試精準覆蓋了異常防衛與狀態隔離契約。

---

## 審查要點逐項分析

### 1. Layout 導覽入口當機防衛 (`DedicationFeeAuditViewWeb`)
- **結果**：**通過（已完全防禦）**
- **分析**：`DedicationFeeAuditViewWeb()` 呼叫私有建模方法 `BuildAuditWebFormModel()`。此方法改由 `InMemoryContext.PersonalInfomationModel?.m_LoginContact` 檢查登入 Contact；當由 Layout 導覽進入且 `loginContact == null` 時，會自動跳過呼叫拋出 `ArgumentNullException` 的 `manager.SetDedicationFeeList(loginContact)`，轉而進入 safe fallback 流程產生並回傳安全的空白表單模型，不再觸發當機。

### 2. `BuildAuditWebFormModel` 狀態隔離與資料抹除
- **結果**：**通過（正確隔離與清空）**
- **分析**：在 fallback 流程中，對 `DonationPaymentFormModel` 進行了全面的重置：
  - 識別個資：`FullName`、`Mobile`、`DedicationNumber`、`NationId`、`LastSixDigit` 皆強制重置為 `string.Empty`。
  - 奉獻金額與清單：`TotalAmount` 歸零 (0)，`DedicationFeeList` 與 `SameNameList` 補上 null 檢查 (`??=`) 後執行 `.Clear()` 清空。
  - 預設值維護：呼叫 `EnsureFormDefaults()` 確保 UI 下拉選單安全預設值存在。
  跨使用者（Cross-user / Cross-request）資料不會殘留或帶到目前 request。

### 3. Manager 表單模型 null 處置與 AJAX/Grid 路徑安全性
- **結果**：**通過（已回存模型，無後續當機風險）**
- **分析**：修改第 151 行加入 `manager.m_DonationPaymentFormModel = model;`。當舊流程或 Session 清空將 manager 內之表單模型設為 `null` 時，`BuildAuditWebFormModel()` 會將剛重置好的安全模型重新賦值回 `manager.m_DonationPaymentFormModel`。後續 DevExtreme DataGrid / AJAX 路徑（如 `LoadDedicationFeeList`、`LoadSameNameList`、`GetFeesByContactId`）讀取 `InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel` 時，皆可安全存取非 null 的清單物件。

### 4. 測試有效性與反射安全性
- **結果**：**通過（無重言式測試，斷言決定性強）**
- **分析**：
  - `Dedication_audit_web_form_without_login_contact_returns_isolated_blank_model`：預先注入包含前位使用者個資與 9999 奉獻金額的舊模型，驗證 `loginContact == null` 時不拋出例外且回傳模型之 `FullName`、`Mobile`、`NationId`、`DedicationFeeList` 等皆已清空歸零。若有工程師刪除清空動作，測試必失敗。
  - `Dedication_audit_web_form_reassigns_new_default_model_to_manager`：預先設定 `m_DonationPaymentFormModel = null!`，驗證執行後 manager 之表單模型被重新指派為 returned result。若第 151 行被移除，測試必失敗。
  - **反射測試評估**：測試使用反射呼叫控制器私有方法 `BuildAuditWebFormModel` 係為了隔離 MVC Web Pipeline（避免建置無關之 Session/ViewBag/HTTP Context 模擬器）。測試中均包含 `Should().NotBeNull()` 斷言，若未來方法重構重命名，測試會明確失敗而不會誤判通過。

### 5. 架構規範與維護性標準
- **結果**：**通過**
  - **Session Isolation**：全數使用 request-scoped `InMemoryContext` 狀態，清空重建機制消除了跨請求污染。
  - **Memory / Resource Lifecycle**：測試使用 `RuntimeHelpers.GetUninitializedObject`，無背景工作或外部 CRM/HTTP 資源洩漏。集合使用 `.Clear()` 避免不必要的物件配額耗用。
  - **繁體中文與文件規範**：新增之 XML doc comments 與註解均使用標準繁體中文。
  - **UTF-8 無 BOM & CRLF**：變更檔案編碼與換行符號符合專案規範。

---

## 審查發現與分類 Findings

### 🔴 Critical Issues
- *無 (None)*

---

### 🟡 Warning Issues

#### 1. `SetupAuditViewBag` 在 LINE 登入分支存取 `m_DonationPaymentFormModel` 未防禦 null
- **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 112)
- **問題說明**：
  在 `SetupAuditViewBag(bool isWebLogin)` 的 `else` 分支 (LINE 登入視圖 `DedicationFeeAuditViewLine`) 中：
  ```csharp
  ViewBag.IsAOfficeWorker = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.IsAOfficeWorker ? "是的" : "否";
  ```
  雖然 `DedicationFeeAuditViewWeb` (`isWebLogin == true`) 不會走此分支，但在 LINE 入口點時，`SetupAuditViewBag(false)` 比 `SetDedicationFeeList` 更早執行。若此時 `m_DonationPaymentFormModel` 為 `null`，將在第 112 行拋出 `NullReferenceException`。
- **改善建議**：
  改用 null Safe Navigation Operator (`?.`)：
  ```csharp
  ViewBag.IsAOfficeWorker = (InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel?.IsAOfficeWorker == true) ? "是的" : "否";
  ```

---

### 🔵 Info Issues

#### 1. `LoadDedicationFeeList` 可加入額外 null 條件防衛
- **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 200)
- **問題說明**：
  `LoadSameNameList` (Line 230) 採用了 null 合併運算子防衛 (`?? new List<SameNameElement>()`)，而 `LoadDedicationFeeList` (Line 200) 則直接存取 `.DedicationFeeList`：
  ```csharp
  var tasks = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.DedicationFeeList;
  ```
  雖然 `BuildAuditWebFormModel()` 已保證 Web 入口會掛載非 null 模型，但若前端未經 View 渲染即直接發送 AJAX 請求至 API 入口，第 200 行可更具韌性。
- **改善建議**：
  建議比照 Line 230 改寫為：
  ```csharp
  var tasks = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel?.DedicationFeeList 
      ?? new System.Collections.Generic.List<DedicationFee>();
  ```

---

## 總結結論 Decision

**審查結果：PASS (通過)**

本次當機修正方向正確、隔離完整且測試驗證嚴謹，建議可提交進入整合階段。標示為 Warning / Info 的建議可作為後續微調參考。
