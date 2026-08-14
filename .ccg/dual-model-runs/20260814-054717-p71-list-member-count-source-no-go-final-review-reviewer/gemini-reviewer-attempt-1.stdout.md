# P7.1 ORG-CALL-00047 列表成員數類型化唯讀介面：本地設計 No-Go 最終審查報告

本報告針對 `.trellis/tasks/08-14-p71-list-member-count-typed-read/` 與 `.ccg/tasks/p71-list-member-count-typed-read/` 目錄下的任務記錄（Task Records）進行最終審查。

---

## 1. 總體評估 (Summary)
本次變更屬於 **Source-only local design no-go** 歸檔記錄。經審查，所有任務記錄完整且準確地記錄了 `ORG-CALL-00047` (`list.members.count.by.listid`) 因安全隔離性不足而無法直接遷移至 Gateway 的技術細節。

變更內容嚴格限制在任務記錄檔案中，未修改任何 production 程式碼，未啟用任何 Feature Gate，亦未引入任何 CE (Control Evidence) 或流量切換。設計文檔中明確禁止將 stored CRM FetchXML 轉換為 Gateway 可執行輸入，並完整列出了未來恢復此功能所需的授權、模板化與隔離條件。

**審查結論：無 Critical 或 Warning 缺陷，本任務符合歸檔標準。**

---

## 2. 審查發現 (Findings)

### Critical 缺陷
* **無 Critical 缺陷。**

### Warning 缺陷
* **無 Warning 缺陷。**

### Info 資訊
* **Info**: `.trellis/tasks/08-14-p71-list-member-count-typed-read/prd.md`
  * **確認事項**：明確將 `ORG-CALL-00047` 定義為 `temporary-legacy` 並宣告為 `source-only local design no-go`。文檔中嚴格禁止了部分靜態遷移、CE 測試、Feature Gate 啟用、流量切換以及 P7.5/P8 的提前變更。
* **Info**: `.trellis/tasks/08-14-p71-list-member-count-typed-read/source-audit.md`
  * **確認事項**：詳細審計了 `DownloadListManager.GetSmallGroupMemberNumber` 與 `ListService` 的動態 FetchXML 執行路徑，指出其缺乏 server-derived 授權邊界且依賴 shared ToolUtility 實例。文檔末尾列出了 4 項未來恢復所需的授權與隔離條件（如 request-local scope 授權、server-owned named template 等），但未進行實作。
* **Info**: `.trellis/tasks/08-14-p71-list-member-count-typed-read/design.md`
  * **確認事項**：設計方案採用 fail-closed 原則，明確禁止將 stored CRM FetchXML 轉換為 Gateway 可執行輸入，並規劃了未來安全的授權邊界傳遞路徑。
* **Info**: `.trellis/tasks/08-14-p71-list-member-count-typed-read/task.json` 與 `.ccg/tasks/p71-list-member-count-typed-read/task.json`
  * **確認事項**：任務狀態與元數據正確，變更範圍嚴格限制在 task records，未影響任何 production 程式碼。

---

## 3. 優秀實踐 (Positive Notes)
1. **技術原因記錄詳盡**：清晰地指出了 legacy 程式碼中 `listId` 權限邊界模糊、mutable workflow 以及 dynamic FetchXML 執行對 Gateway 安全性造成的威脅。
2. **範圍控制嚴格**：完全符合 source-only 限制，沒有任何 production 程式碼的修改或配置變更。
3. **未來恢復條件明確**：在不進行實作的前提下，為未來的安全遷移奠定了清晰的架構與授權規範。
