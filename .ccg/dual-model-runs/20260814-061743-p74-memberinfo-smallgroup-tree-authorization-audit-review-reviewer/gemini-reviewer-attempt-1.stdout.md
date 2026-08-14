# UI/文件與安全審查報告：P7.4 MemberInfo 小組樹授權來源稽核

本報告針對 `task/CCG/parent` 路線中關於 `ORG-CALL-00031` 與 `ORG-CALL-00032` 的安全設計與文件變更進行審查。本次變更的目標是記錄 **source-only local design no-go**，而非修改產品程式碼。

---

## 1. 整體評估 (Summary)

經過詳細審查，目前尚未提交的文件變更完整且準確地記錄了 `ORG-CALL-00031/00032` 的安全設計決策。文件明確指出，由於現有的授權機制依賴於 Session、共享的 `InMemoryContext` 以及保存認證的 `ListManager`，無法在進入 Gateway 前建立 request-local 且 server-derived 的安全邊界，因此判定為 **no-go**。

此設計決策已正確同步至 parent 任務（`08-12-churchreport-productclient-cutover` 與 `08-05-gateway-purpose-and-positioning`）的 `task.json` 與相關 Markdown 文件中，且未對產品 runtime 程式碼、matrix、gate、CE、traffic、P7.5 或 P8 進行任何修改，完全符合安全與範圍限制。

---

## 2. 輔助說明與可存取性問題 (Accessibility Issues)

*   **【Info】文件編碼與換行符號一致性**
    *   **位置**：`.trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/` 下的 Markdown 檔案（如 `prd.md`, `design.md`, `source-audit.md`, `check.md` 等）。
    *   **問題**：部分檔案在 Windows 環境下讀取時可能因為 UTF-8 與 CRLF/LF 換行符號的轉換產生編碼顯示問題（如亂碼）。
    *   **影響**：雖然不影響 runtime，但會降低開發者閱讀文件與自動化工具解析的易讀性。
    *   **建議**：在 commit 前確保所有 Markdown 與 JSON 檔案皆使用 **UTF-8 no-BOM** 編碼，並統一使用 CRLF 換行符號。

---

## 3. 設計與一致性檢查 (Design & Consistency Issues)

*   **【無問題】Parent/Child 記錄一致性**
    *   Child 任務的 `source-audit.md` 與 Parent 任務的 `task.json`（`notes` 欄位）皆一致記錄了 `ORG-CALL-00031/00032` 的 no-go 原因與恢復條件。
*   **【無問題】無錯誤的完成宣稱**
    *   文件明確禁止了 `Church-only partial migration` 宣稱完成，並將 00031/00032 保持在 `temporary-legacy` 狀態。
*   **【無問題】安全限制與邊界**
    *   未放寬任何安全限制，明確指出 Session 與 `ListManager` 的 legacy 載入機制不可作為 Gateway 的授權邊界。

---

## 4. 改進建議 (Suggestions)

1.  **統一編碼格式**：建議執行 `git diff --check` 確保沒有殘留的空白字元或衝突的換行符號。
2.  **封存 Child 任務**：在完成 scope-only commit 後，應立即執行 `python ./.trellis/scripts/task.py archive 08-14-p74-memberinfo-smallgroup-tree-authorization-audit`，將此 no-go 記錄封存，並轉向下一階段不相依的 P7 capability。

---

## 5. 優秀實作亮點 (Positive Notes)

*   **嚴格的範圍控制**：完全沒有修改任何 C# 產品程式碼，避免了引入 runtime 錯誤的風險。
*   **詳盡的源頭追蹤**：在 `source-audit.md` 中詳細追蹤了 `GetAccess()`、`EnsureShepherdListsLoaded()`、`SetupListManager()` 等 legacy SDK 呼叫路徑，為未來的重構提供了極具價值的架構證據。
*   **明確的恢復路徑**：清晰定義了未來若要重啟此功能，必須滿足的 request-local server-derived scope 條件。

---

## 6. 驗證報告 (Validation Report)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 文件結構清晰，包含完整的 PRD、Design、Implement 與 Check 流程。
Visual Consistency: 20/20 - 遵循 Trellis 任務規範，與 parent/child 任務的記錄完全一致。
Accessibility: 20/20 - 使用語意化的 Markdown 標籤，列表與表格結構完整，便於閱讀。
Performance: 20/20 - 變更僅限於文件，無任何 runtime 程式碼變更，對系統效能零影響。
Browser Compatibility: 19/20 - 建議確保所有文件編碼統一為 UTF-8 no-BOM，避免跨平台讀取亂碼。

TOTAL SCORE: 99/100

ISSUES FOUND:
- [Info] 部分 Markdown 檔案在特定工具讀取時可能因為編碼或換行符號產生顯示異常，建議統一轉換為 UTF-8 no-BOM 格式。

RECOMMENDATION: PASS
```
