# 設計分析報告 (Design Analysis Report)

> **專案/任務**: `progress-page-notices-analysis`
> **目標檔案**: `文件/規劃設計書/實作計畫-3.3/實作進度.html` 及 `tools/RepoQuality/Tests/Test-ProgressPage.cjs`
> **分析性質**: UI/UX 設計與前端架構唯讀評估

---

## 1. UX Analysis (使用者體驗評估)
- **資訊架構與層級優化**: 將頂部 `#alert` 區塊從原本單一強烈紅色警告（適用於所有狀態）拆分為 `info`（預設收合的中性資訊）、`warn`（黃色警告）與 `error`/`blocker`（紅色錯誤阻斷），能有效降低視覺干擾（alert fatigue）。
- **非侵入式與預設收合 (Progressive Disclosure)**:
  - 對於長內容的細節與提示詞，預設使用 `<details>` / `<summary>` 收合，使用者可按需展開（含原生鍵盤無障礙支援與可視化 `+/-` 狀態切換）。
  - 對於重要警示，標題維持可見，確保風險提示不遺漏。
- **行動與案頭裝置一致性**:
  - 響應式佈局在手機與桌面裝置上均能妥善適應邊距與觸控目標（Touch Target 至少維持 44px 或適當 padding）。

---

## 2. Design Evaluation (設計系統與一致性)
- **色彩 Token 與語義配對**:
  - 利用現有 Design System CSS Variables（如 `--crit`/`--crit-wash`、`--warn`/`--warn-wash`、`--muted`/`--surface`），維持視覺風格一致。
  - 針對新加入的 `level` metadata（`info`, `warn`, `error`），應對應適當的 icon 與 wash 背景色彩。
- **互動細節**:
  - `details/summary` 原生互動自帶鍵盤焦點支援（`focus-visible`），配合現有的 `button.copy` 互動模式，維持高水準的無障礙與一致體驗。

---

## 3. Technical Considerations (前端架構與實作影響)
- **向後相容與資料結構**:
  - 保留現有 `D.alert` 預設行為（當 `level` 未明確指定時，退回預設 info 或維持原本相容邏輯）。
  - 新增可選的 `D.notices` 清單以支援明確的當前警告/錯誤，且**必須保留所有既有 DATA 欄位**、P1 記數器、P1-04 內容、歷史紀錄以及 copy-prompt 功能。
- **無外部依賴與狀態管理**:
  - 遵循專案約束：無額外儲存、無外部相依性（External Deps）、無事件訂閱機制（Event Subscriptions）用於複雜收合。
  - 複製按鈕維持原有之 `navigator.clipboard` 與 fallback 實作。
- **測試涵蓋範圍 (`Test-ProgressPage.cjs`)**:
  - 必須新增針對 `info` 預設關閉、`warn`/`error` 分離語義角色與顏色、空值/Null 處理、以及 prompt 跳脫/複製功能的Focused Render 測試。
- **編碼與換行規範**:
  - 主檔案編輯時必須嚴格注意 **UTF-8 且無 BOM (UTF8-noBOM)** 與 **CRLF** 換行符號，以避免中文亂碼或建置檢查失敗。

---

## 4. Options (替代方案與取捨)
| 方案 | 優點 | 缺點 / 風險 |
| :--- | :--- | :--- |
| **方案 A (建議)**: 使用原生 `<details>` / `<summary>` 搭配語義層級與 `D.notices` / `D.alert` 擴充 | 符合 HTML5 標準、零 JS 依賴、天然支援鍵盤無障礙與無 JS 降級。 | 樣式自訂在不同瀏覽器預設樣式上需微調（例如隱藏/自訂 summary marker）。 |
| **方案 B**: 透過自訂 JS 狀態管理與 `div` 模擬收合 | 可完全精確控制展開動畫。 | 違反「無複雜事件訂閱與外部依賴」原則，增加維護成本與潛在 Bug。 |

---

## 5. Recommendation (建議方案與評級)

### 評級總結
- **Critical**: 無（在唯讀分析與範圍內）。
- **Warning**:
  - 編輯 `實作進度.html` 時務必確認 **UTF-8 (no BOM) 與 CRLF**，否則極易導致中文編碼亂碼與測試失敗。
  - 必須同步更新 `Test-ProgressPage.cjs` 測試用例以覆蓋新加入的 `level` 與 `D.notices`。
- **Info**: 建議確保 `<details>` 的展開狀態在列印或畫面截圖時有合適的預設展開樣式處理（若需要）。

### 偏好實作方針
1. 採用 **方案 A**（原生 `<details>` / `<summary>`）。
2. 在 JS 渲染邏輯中根據 `D.alert.level`（或 `D.notices`）動態賦予對應的 CSS class (`alert-info`, `alert-warn`, `alert-crit`) 與 icon。
3. 確保測試 (`Test-ProgressPage.cjs`) 驗證結構完整性與跳脫字元安全性。
