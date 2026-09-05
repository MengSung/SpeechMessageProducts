# 奉獻稽核當機修正與狀態隔離 (Dedication Audit Crash Fix) 程式碼審查報告

## 審查總結 (Executive Summary)

針對 HEAD 相對於變更內容的詳細審查結果顯示：本次變更已完整修復 `DedicationAuditController.DedicationFeeAuditViewWeb` 從 Layout 導覽進入時因 Request-scoped `DonationPaymentManager.m_Contact` 為 `null` 導致的 HTTP 500 / `ArgumentNullException` 當機問題；同時完善了 `DonationPaymentFormModel` 的 null-safe fallback 與身分資料隔離機制，且單元測試覆蓋與專案規範皆表現優良。

---

## 重點檢查要點逐項評估 (Detailed Checklist Review)

### 1. Layout 導覽入口當機防衛 (`DedicationFeeAuditViewWeb`)
- **結果**：**[PASS] 已完全修復並安全防護**
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`（第 74-87 行、第 134-156 行）
- **詳細說明**：
  - 原流程在 Layout 導覽點擊進入時，會直呼 `SetDedicationFeeList(m_Contact)`，當 `m_Contact` 為 `null` 時擲出 `ArgumentNullException`。
  - 新實作改呼叫 `BuildAuditWebFormModel()`，優先檢驗 `InMemoryContext.PersonalInfomationModel?.m_LoginContact`。當由 Layout 導覽進入且 `loginContact == null` 時，會安全避開 CRM 查詢與傳傳 `null` 的路徑，轉入 fallback 重置流程回傳安全空白表單。
  - `SetupAuditViewBag(false)`（第 115 行）亦採用 null 條件運算子 `?.`（`m_DonationPaymentFormModel?.IsAOfficeWorker == true`），消除 ViewBag 初始化期間的 `NullReferenceException` 疑慮。

---

### 2. `BuildAuditWebFormModel` 的 Null-Safe Fallback 與個資隔離
- **結果**：**[PASS] 欄位重置完整，符合 Session/Request 隔離**
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`（第 144-155 行）
- **詳細說明**：
  - Fallback 流程明確清理重置了以下 8 項敏感個資與計算欄位：
    - `FullName` -> `string.Empty`
    - `Mobile` -> `string.Empty`
    - `DedicationNumber` -> `string.Empty`
    - `NationId` -> `string.Empty`
    - `LastSixDigit` -> `string.Empty`
    - `DedicationFeeList` -> `.Clear()`
    - `SameNameList` -> `.Clear()`
    - `TotalAmount` -> `0`
  - 並將重置後的安全模型回存至 `manager.m_DonationPaymentFormModel`。因 `DonationPaymentManager` 為 Request-scoped，此設計徹底防止上一位使用者或前一次 Request 的個資與奉獻紀錄洩漏至目前 Request。

---

### 3. AJAX / DataGrid 後續路徑 Null 防衛
- **結果**：**[PASS] 已防禦，全數路徑補齊懶載入保護**
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`（第 166-174 行、第 221 行、第 251 行、第 411 行）
- **詳細說明**：
  - 引入 `EnsureAuditFormModel(manager)` 靜態 Helper。
  - 在 `LoadDedicationFeeList` (Line 221)、`LoadSameNameList` (Line 251) 及 `GetFeesByContactId` (Line 411) 路由進入點，皆優先經由 `EnsureAuditFormModel` 確保 `m_DonationPaymentFormModel` 及其內的 `DedicationFeeList` / `SameNameList` 已被實例化（非 `null`）。
  - 若前端在 View 未渲染前發起 AJAX/Grid 載入請求，DataGrid 能正確接收空 List 並回傳標準 JSON 物件，不再觸發當機。

---

### 4. 單元測試真實度與品質
- **結果**：**[PASS] 測試真實有效，非 Tautological 重言式測試**
- **檔案與行號**：`ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`（第 229-316 行）
- **詳細說明**：
  - `Dedication_audit_web_form_without_login_contact_returns_isolated_blank_model`：注入含有舊個資與金額（TotalAmount=9999）的過期模型，驗證當 `m_LoginContact` 為 null 時執行 `BuildAuditWebFormModel()` 後 8 項個資欄位被決定性清空歸零。若刪除清空邏輯，測試必失敗。
  - `Dedication_audit_web_form_reassigns_new_default_model_to_manager`：驗證 null 模型被重新賦值與關聯至 manager。
  - **反射測試防禦**：`SpeechMessageProducts.ChurchReport.csproj` 已加入 `<InternalsVisibleTo Include="ChurchReport.MemberInfo.Tests" />`，測試係強型別直接呼叫 `internal` 之 `BuildAuditWebFormModel()` 與 `EnsureAuditFormModel()`，非脆弱的字串反射呼叫。

---

### 5. 架構規範與維護性標準
- **結果**：**[PASS] 符合所有 Session/記憶體/文件/編碼規範**
- **詳細說明**：
  - **Session Isolation**：完全使用 Request-scoped `InMemoryContext` 狀態，無跨請求 Static 洩漏。
  - **Memory Lifecycle**：使用 `.Clear()` 與 null coalescing 運算子，未配置多餘非受控資源。
  - **繁體中文文件**：新增 XML doc comments 及程式碼註解全數採用正體中文（繁體中文）。
  - **編碼與換行**：所有修改檔案符合 UTF-8 without BOM 與 CRLF 換行規範。
  - **效能要求**：`loginContact == null` 時完全避開外部 CRM / DB 查詢，輕量快速。

---

### 6. `DonationPaymentFormModel.QueryStartDate` 動態預設與測試防護
- **結果**：**[PASS] 年份動態計算且測試防護有效**
- **檔案與行號**：
  - `SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs`（第 187 行）
  - `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`（第 53-60 行）
- **詳細說明**：
  - `QueryStartDate` 屬性初始化語法為 `new DateTime(DateTime.Now.Year, 1, 1)`，會在每次建立新模型時根據當下系統年份動態指定為該年度 1 月 1 日，未硬編碼（Hardcode）為 2026。
  - 單元測試 `New_donation_form_defaults_query_start_date_to_current_year_first_day` 斷言 `model.QueryStartDate.Should().Be(new DateTime(DateTime.Now.Year, 1, 1))`。若程式碼倒退回 `DateTime.Now`（當天），測試將因日期不匹配而失敗，有效防禦了回歸風險。

---

## 審查發現與分類 (Findings)

### 🔴 Critical Issues
- *無 (None)*

---

### 🟡 Warning Issues

#### 1. Grid 載入單元測試斷言可進一步強化 (Test Assertion Strength)
- **檔案與行號**：`ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`（第 297 行、第 312 行）
- **原因說明**：
  - 測試 `Dedication_audit_fee_grid_returns_empty_data_when_form_model_is_missing` 與 `Dedication_audit_same_name_grid_returns_empty_data_when_form_model_is_missing` 目前僅使用 `action.Should().NotThrow()` 斷言無例外拋出。
- **改善建議**：
  - 可進一步捕捉 Action 回傳之 `IActionResult` / `LoadResult` 並斷言其 `data` 筆數為 0，以確保 Grid 不僅不當機，且回傳格式與資料量完全符合空集合預期：
    ```csharp
    var result = controller.LoadDedicationFeeList(string.Empty, new DevExtreme.AspNet.Mvc.DataSourceLoadOptions());
    result.Should().NotBeNull();
    ```

---

### 🔵 Info Issues

#### 1. 舊有檔案標頭亂碼清理建議 (Header Encoding Artifact)
- **檔案與行號**：
  - `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`（第 2-12 行）
  - `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`（第 2-12 行）
- **原因說明**：
  - 檔案頂部歷史 AI 註解標頭存在舊有 Big5/亂碼字元（如 `// AI-蝜?銝剜?瑼?閮餉圾`）。
  - 此為歷史遺留問題，不影響編譯與功能，但在後續重構任務中可統一修復為正體中文標頭。

---

## 評分與總結 (Validation Report)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 完全解決 Layout 導覽與未登入狀況下的 HTTP 500 當機
Visual Consistency: 20/20 - 未登入/無 Contact 狀態下提供安全乾淨的空白表單
Accessibility: 20/20 - AJAX/Grid 各路由防護到位，能正確輸出 JSON 回應
Performance: 20/20 - 無登入 contact 時直接避開 CRM 查詢，無效能與資源負擔
Browser Compatibility: 20/20 - 通過動態年份與身分隔離測試，強型別 InternalsVisibleTo 測試無脆弱度

TOTAL SCORE: 100/100

ISSUES FOUND:
- [Warning] DonationPaymentViewDefaultsTests.cs (Line 297, 312) 可加強 Grid 回傳 JSON 物件筆數斷言

RECOMMENDATION: PASS (通過，可直接提交與合併)
```
