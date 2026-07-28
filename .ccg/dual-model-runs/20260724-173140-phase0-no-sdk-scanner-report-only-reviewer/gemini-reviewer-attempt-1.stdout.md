# Dynamics 365 no-SDK Phase 0 驗證與審查報告

本報告針對 Dynamics 365 no-SDK Phase 0 任務中新增的 report-only 掃描器、CI 整合、以及相關規劃與驗證文件進行審查。

---

## 1. Summary (整體評估)

本次變更成功建立了 Dynamics 365 no-SDK 遷移的 Phase 0 基準線。新增的掃描器 `Verify-NoDynamicsSdk.ps1` 與配置 `no-sdk-source-roots.json` 能夠精確識別現有的 legacy SDK 參照，且在 CI 流程中以 `report-only` 模式運行，不會中斷現有的建置與測試流程。相關的架構設計文件（`design.md`、`phase0-runtime-capacity-adr.md`）與呼叫矩陣（`phase0-organization-call-matrix.json`）結構嚴謹，為後續 Phase 1 的實作提供了清晰且安全的指引。

---

## 2. Accessibility Issues (無障礙性評估)

* **評估結果**：不適用（N/A）。
* **說明**：本次變更皆為後端腳本、CI 工作流配置、JSON 矩陣與 Markdown 文件，不包含任何前端 UI 元件或使用者介面，因此無無障礙性（a11y）相關問題。

---

## 3. Design Issues (設計與架構一致性)

### 【Info】解決方案拓撲決策的一致性
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
* **評估**：設計文件明確拒絕了強制建立獨立的 `SpeechMessage.Dynamics.sln` 作為預設邊界的提案，改為將新的 Dynamics 專案群組加入現有的 `SpeechMessageProducts.sln` 中。這避免了專案參照的多重真實來源（multiple sources of truth）問題，並保持了開發與測試發現的單一視圖，符合現有解決方案的拓撲結構。

### 【Info】雙主機單核心（Two-Host, One-Core）架構
* **檔案路徑**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
* **評估**：設計中定義了 `Gateway`（預設生產環境邊界）與 `Embedded`（本機開發/測試特例）兩種執行模式，兩者共享相同的 no-SDK 核心與組織容量協調器（Organization Admission Coordinator）。此設計在提供彈性的同時，嚴格限制了 Embedded 模式不得繞過全域容量限制或持有使用者特定狀態，確保了架構的安全性與一致性。

---

## 4. Suggestions (改進建議與風險提示)

### 【Warning】子字串比對可能帶來的雜訊與 False Positives
* **檔案路徑**：`eng/Verify-NoDynamicsSdk.ps1` (第 130 行)
* **說明**：掃描器使用 `IndexOf` 進行簡單的子字串比對。如果程式碼中的註解、變數名稱或無關的字串字面量剛好包含 banned patterns（例如在註解中提到 `IOrganizationService`），也會被判定為違規。
* **建議**：在 Phase 0 的 report-only 階段，這種雜訊是可以接受的。但當未來 Phase 升級為 `failing-gate` 時，建議在 `no-sdk-source-roots.json` 中引入 `falsePositivePaths` 或 `ignoredLines` 的排除機制，以避免開發人員因為註解或測試 Mock 命名而被阻擋建置。

### 【Warning】檔案讀取編碼風險
* **檔案路徑**：`eng/Verify-NoDynamicsSdk.ps1` (第 125 行)
* **說明**：使用 `[System.IO.File]::ReadLines($file)` 讀取檔案時，若檔案包含非 UTF-8 編碼的特殊字元，可能會因為編碼解析問題導致掃描行為異常或遺漏。
* **建議**：雖然專案中的原始碼與配置檔通常為 UTF-8，但建議在讀取時明確指定編碼，例如使用 `[System.IO.File]::ReadLines($file, [System.Text.Encoding]::UTF8)`，以確保在不同語系環境下的 Windows PowerShell 5.1 中執行時的一致性。

---

## 5. Positive Notes (值得肯定的地方)

* **嚴格的 report-only 語意**：在 `.github/workflows/toolutility-tests.yml` 中，掃描步驟設定了 `continue-on-error: true` 且未傳遞 `-FailOnFindings` 參數，確保了 Phase 0 階段不會因為偵測到 legacy SDK 而中斷 CI，完全符合「不破壞現有 legacy SDK 路徑」的非妥協要求。
* **Windows PowerShell 5.1 相容性**：腳本未使用 .NET Core 獨有的 API，而是使用 `[System.IO.File]::ReadLines` 與標準的 PowerShell 5.1 語法，確保了在 Windows 本地開發環境與 GitHub Actions (windows-latest) 上的相容性。
* **精確的排除路徑**：`no-sdk-source-roots.json` 中明確排除了 `bin` 與 `obj` 目錄，避免了編譯產物（stale generated artifacts）干擾掃描報告，使 findings 數量保持高信號。
* **嚴謹的 Schema 驗證**：建立了 `phase0-organization-call-matrix.schema.json`，對呼叫矩陣進行了強型別的結構約束，確保了遷移資料的完整性與後續自動化工具讀取的可靠性。

---

## 6. Validation Report (驗證報告)

針對本次 Phase 0 驗證任務的評分如下：

```
VALIDATION REPORT
=================
User Experience: 19/20 - 掃描器輸出格式清晰，支援 JSON 與 Write-Host 兩種模式，便於開發人員本地排查與 CI 記錄閱讀。
Visual Consistency: N/A - 本次任務無 UI 變更。
Accessibility: N/A - 本次任務無 UI 變更。
Performance: 19/20 - 掃描器使用流式讀取 (ReadLines) 避免記憶體暴漲，且精確排除了 bin/obj 目錄，掃描速度極快。
Browser Compatibility: N/A - 本次任務無瀏覽器相容性問題。

TOTAL SCORE: 95/100 (排除不適用項目後之綜合評估)

ISSUES FOUND:
- [Warning] Verify-NoDynamicsSdk.ps1 中的 IndexOf 子字串比對在未來啟用 failing-gate 時可能產生 false positives 雜訊。
- [Warning] [System.IO.File]::ReadLines 未明確指定 UTF-8 編碼，在非標準編碼檔案上可能存在解析風險。

RECOMMENDATION: PASS
```
