# P7.2 Weekly-Report Precondition Analysis Report

本報告針對 P7.2 Slice C `FreshPreflightProbe` 中關於週報（Weekly Report）前置條件的查詢邏輯、脫敏結果證明力、診斷方法及實作缺陷進行深度分析。

---

## 1. 核心問題解答

### (1) 查詢的解讀 (Interpretation of the Query)
根據 `P72FreshSliceCFixturePreflightProbe.cs` 中的 `HasExactlyOneActiveWeeklyReport` 實作：
* **查詢範圍**：該查詢**僅要求**針對特定的 `descriptor-bound transfer target list`（即 `targetListId`）與指定的 `UTC Sunday` 存在恰好一個 active 週報。
* **組織級影響**：查詢條件中明確包含了 `query.Criteria.AddCondition("new_list_group_present_weekly_report", ConditionOperator.Equal, targetListId)`。因此，這**不是**組織級的全局唯一性檢查。其他 group 針對同一個 Sunday 所建立的週報（其關聯的 list ID 不同）**不會**影響此查詢的結果。

### (2) 脫敏結果所能證明與不能證明的事項 (What is and is not proven)
* **已證明 (Proven)**：
  * 該特定 `targetListId` 在該 `UTC Sunday` 下，處於 Active 狀態（`statecode = 0`）的週報數量**不等於 1**（即數量為 0，或大於等於 2）。
* **未證明 (Not Proven)**：
  * **無法區分基數（Cardinality）**：無法證明目前是「**零個匹配（Zero Matches）**」還是「**重複匹配（Duplicate Matches）**」。
  * **無法排除異常**：無法證明是否因 SDK 回傳 null 集合而導致失敗（雖然連線與權限異常會被外層 `catch` 捕獲並投影為 `probe-unavailable`）。

### (3) 安全的下一步診斷問題 (Safe Next Diagnostic Question)
在不修改 CRM 資料且不暴露敏感資訊的前提下，可向管理員或透過唯讀工具提出以下診斷問題：
> 「針對目標清單 `targetListId` 與指定的 UTC 週日日期，CRM 中實際存在的 active `new_group_present_weekly_report` 記錄數量為何？是 0 筆（尚未建立）還是 2 筆以上（重複建立）？」

### (4) 實作與字詞缺陷 (Defects)
* **實作缺陷 (Warning)**：
  * `HasExactlyOneActiveWeeklyReport` 回傳 `false` 時，統一將 `weeklyReport` 狀態投影為 `"not-exactly-one-active"`。這將 `rows.Entities.Count == 0` 與 `rows.Entities.Count >= 2`（或 `rows.MoreRecords == true`）兩種本質不同的環境狀態混為一談，失去了精確診斷的價值。
* **字詞缺陷 (Info)**：
  * `"not-exactly-one-active"` 對於終端使用者或部署人員而言不夠直觀，無法指引其應去「補建週報」還是「清理重複週報」。

---

## 2. UX Analysis (使用者體驗分析)

* **使用者影響評估**：
  當探測失敗並顯示 `not-exactly-one-active` 時，運維人員無法得知確切的錯誤原因。這會導致排查流程變長：運維人員必須手動登入 CRM 系統，針對該特定 List 進行複雜的條件檢索，才能決定是要執行建立還是刪除操作。
* **使用者旅程影響**：
  在 Preflight 唯讀探測階段，目標是「快速定位環境阻礙」。模糊的錯誤分類破壞了此階段的自動化診斷價值，增加了人工介入的成本。
* **無障礙與易用性考量**：
  明確的錯誤分類（如區分「缺失」與「重複」）符合 WCAG 2.1 錯誤識別與指引原則，能顯著提升運維工具的易用性。

---

## 3. Design Evaluation (設計系統評估)

* **一致性與既有模式**：
  探針的其他欄位（如 `OwnerState` 區分 `"inactive-or-missing"`，`OwnerKind` 區分 `"other-or-missing"`）皆試圖在脫敏的前提下提供最精確的狀態分類。週報欄位採用單一的 `"not-exactly-one-active"` 與此設計細粒度不一致。
* **合約與 Token 限制**：
  若要細分狀態值，必須修改 `P72FreshSliceCFixturePreflightProbeResult` 的 allowlist 值。由於 PowerShell 端設有嚴格的強型別 Schema 驗證（`Get-StrictFreshPreflightProbeEvidenceFile`），任何 C# 端的狀態值變更都必須同步更新 PowerShell 的驗證 allowlist，否則會觸發 `evidence-result-unavailable` 並導致 fail-closed。

---

## 4. Technical Considerations (技術架構考量)

* **效能與 Bundle Size**：
  現有查詢已設定 `TopCount = 2`，這在效能上是最佳實踐（避免了因重複資料過多而拉取大量實體）。區分 0 與 2+ 不需要增加額外的 CRM 查詢，僅需在記憶體中對 `rows.Entities.Count` 進行分支判斷。
* **狀態管理與測試**：
  重構此邏輯時，需在 `P72Data8ListManagementFreshFixtureProvisionerTests.cs` 中新增對應的單元測試案例，確保在 `Count == 0` 時回傳 `"zero-active"`，在 `Count >= 2` 或 `MoreRecords == true` 時回傳 `"duplicate-active"`。

---

## 5. Options & Recommendation (方案與建議)

### 替代方案評估

| 方案 | 優點 | 缺點 |
| :--- | :--- | :--- |
| **方案 A：保持現狀** | 無需修改程式碼與 PowerShell 驗證合約，無相容性風險。 | 運維排查成本高，無法區分無資料與重複資料。 |
| **方案 B：細分狀態值 (推薦)** | 提供精確診斷（`"zero-active"` / `"duplicate-active"`），運維能立即採取對應行動。 | 需要同步修改 C# 實作、單元測試及 PowerShell 驗證 allowlist。 |
| **方案 C：增加額外 Count 欄位** | 保留原狀態，提供額外輔助資訊。 | 增加 JSON Schema 複雜度，且有極微小的資料洩露風險。 |

### 建議 (Recommendation)
**採用方案 B**。
* **理由**：在唯讀探測的限制下，區分「無週報」與「重複週報」能提供最直接的修復指引，且完全不違反「不暴露敏感 ID 與資料」的脫敏安全限制。此修改僅涉及狀態值的細分，對系統架構影響極小，但能顯著提升運維效率。
