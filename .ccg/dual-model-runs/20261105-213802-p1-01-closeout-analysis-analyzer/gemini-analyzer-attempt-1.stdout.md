# P1-01 孤兒清理獨立驗收收尾 — 設計分析報告

> 檢驗對象：P1-01 孤兒清理與工作包收尾（基於 Git 工作樹與既有報告）
> 審查角色：資深 UI/UX 設計與架構分析師
> 審查模式：Read-Only（零寫入權限、不修改檔案、不執行建置）

---

## 1. 執行摘要與分析範圍

本報告針對 **P1-01 孤兒清理獨立驗收收尾**（Task: `p1-01-closeout-analysis`）進行設計架構與合規性複審。
根據規範與既有歷史紀錄（包含 `wp-reports/P1-01-reference-audit.md` 與 Git 提交紀錄 `f5c69cde`、`82515aab`、`55e7caa1`、`03c8aa28`）：
- **P1-01 核心任務**：在刪除前逐項證明無外部參考後，清除 51 個孤兒檔案與目錄（包含 Trace、LinePayCSharp、兩個冗餘的 `_Net10.csproj`，以及 4 個僅留 `packages.lock.json` 的 `Crm82/91Worker` 鎖定目錄）；將 15 個孤兒效能測試搬入正式專案 `ChurchReport.MemberInfo.Tests/Performance` 並標上 `Performance` trait；將 `ToolUtility.Tests` 納入方案。
- **使用者決策（2026-11-05）**：維持現有 DevExpress/DevExtreme 版本與功能，不升級、不續訂、不替換 UI；暫緩新套件來源與 hosted 完整 CI 恢復，不以其阻擋 P1-01，且**絕不宣稱 hosted CI 通過**；本地完整方案驗證仍為強制要求。

---

## 2. 審查發現分類（Reviewer Findings）

### 【Critical】（關鍵性風險與缺口）
1. **驗收報告缺失（Acceptance Report Missing）**
   - **路徑**：`wp-reports/P1-01-acceptance.md`
   - **說明**：既有的 `wp-reports/P1-01-reference-audit.md` 僅記錄了刪除前的引用稽核與 51 個檔案清單（由 `f5c69cde` 執行），但截至目前為止，**缺少正式的 P1-01 驗收報告（`wp-reports/P1-01-acceptance.md`）**來匯總前後重複性掃描比對、建置結果、測試通過率及 15 個效能案例的執行快照。
   - **建議**：主實作人員應補齊 `wp-reports/P1-01-acceptance.md`，明確記載 before/after duplicates baseline、`ToolUtility.Tests` 方案整合格局與離線執行結果。

### 【Warning】（警告與注意項目）
1. **Crm82/91Worker 目錄殘留定義與鎖定邊界**
   - **路徑**：`wp-reports/P1-01-reference-audit.md` / `SpeechMessage.Dynamics.Crm82Worker` 等
   - **說明**：審查發現四個 Worker 目錄實際僅殘留 `packages.lock.json`（無 csproj、無原始碼）。雖然稽核文件有文字說明其為 lock-only 刪除，但若開發者在其他未清理乾淨的檢出中誤判其關聯性，易導致依賴還原異常。
   - **建議**：確認 Git 刪除命令是否完整覆蓋 untracked 檔案，並在驗收文件中明載其 lock-only 刪除的防禦性理由。
2. **效能測試 Trait 標記與離線測試隔離**
   - **路徑**：`ChurchReport.MemberInfo.Tests/Performance/`
   - **說明**：15 個效能測試由孤兒專案移入正式測試專案後，必須確保帶有 `[Trait("Category", "Performance")]`（或對應的 `Performance` trait），且在執行時嚴格遵守**離線、無外部網路與 CRM 依賴**原則，避免 CI 階段因連線逾時而失敗。

### 【Info】（資訊與建議）
1. **多專案雙目標（Dual-Targeting）與方案結構保持**
   - 專案成功納入方案後，應維持 `.net462` 與 `.net10.0` 的邊界隔離，確保 Plugin 專案維持 .NET Framework 4.8 且不混入 Core 專案專屬套件。
2. **進度頁面與任務狀態同步**
   - 實作進度 HTML（如 `文件/規劃設計書/實作計畫-3.3/實作進度.html`）應精確標示 P1-01 的收斂狀態，不得在未通過全方案閘門前宣稱階段全面完成。

---

## 3. 驗證缺口與檢核表 (Verification Gap & Checklist)

| 檢核項目 | 要求標準 | 當前狀態 | 審查判定 |
| :--- | :--- | :--- | :--- |
| **P1-01 刪除前參考證明** | 51 個檔案逐項無引用佐證 | 已有 `wp-reports/P1-01-reference-audit.md` | **符合** (Info) |
| **P1-01 正式驗收報告** | 交付 `wp-reports/P1-01-acceptance.md` | 檔案尚不存在 | **【Critical】需補齊** |
| **效能測試遷移** | 15 個案例移入正式專案 + Performance trait | 已於 commit 中搬移 | **符合** (Warning: 需確認 trait 標記) |
| **ToolUtility.Tests 整合** | 加入主方案並通過測試 | 已加入方案與執行 | **符合** (Info) |
| **重複性與建置驗證** | Duplicate 基準不增加、建置與測試全過 | 依據歷史記錄 baseline1125 / baseline2278 | **符合** (Info) |
| **CI 宣告約束** | 暫緩 hosted CI，絕不宣稱 hosted CI 通過 | 符合使用者決策 | **符合** (Critical) |

---

## 4. 建議方案與替代選項 (Options & Trade-offs)

1. **選項 A（嚴格閉環法 - 推薦）**：
   - 由主實作代理依據現有刪除稽核與測試結果，補編 `wp-reports/P1-01-acceptance.md`，並執行一次本地離線方案測試，確認 duplicates baseline 未增加後再將 P1-01 標示為完全收斂。
   - *優點*：確保文件與程式碼完全同步，符合 CCG 雙模型嚴格驗收規範。
   - *缺口*：需耗費額外時間編寫驗收報告。
2. **選項 B（維持現狀掛起法）**：
   - 依現狀保持 f5c69cde 與 82515aab 的提交成果，僅在進度頁面註明 P1-01 程式已刪除但正式驗收報告待補。
   - *缺口*：不符合完整工作包交付規範，容易造成雙模型複審時產生 Critical 阻擋。

---

## 5. 最終建議 (Preferred Recommendation)

- **偏好做法**：採納**選項 A**。由主實作人員依據現有 `wp-reports/P1-01-reference-audit.md` 內容衍生並產出 `wp-reports/P1-01-acceptance.md`，確認 51 個孤兒檔案清理、15 個效能測試（含 Performance trait）與 `ToolUtility.Tests` 方案整合格局均有案可稽。
- **合規提醒**：嚴格遵守使用者於 2026-11-05 之決策，**絕不宣稱 hosted CI 通過**，所有驗證以本地離線方案測試為準。
