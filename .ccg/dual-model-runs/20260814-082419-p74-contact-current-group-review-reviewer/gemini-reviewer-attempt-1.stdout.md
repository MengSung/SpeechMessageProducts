# 原始碼審查與設計決策驗證報告

本報告針對任務 `.trellis/tasks/08-14-08-14-p74-contact-current-group-read-boundary/` 及其引用的源代碼 `ContactService.GetContactCurrentGroup` 與 `AddContactToListAsync` 進行審查，驗證 `source-only-local-design-no-go` 決策的合理性，並確認任務記錄是否正確反映進度。

---

## 1. 總體評估 (Summary)

經審查，**`source-only-local-design-no-go` 的決策完全合理且正當**。
`ContactService.GetContactCurrentGroup` 存在可變實體邊界、首筆匹配歧義以及與多個寫入副作用緊密相鄰（Write Adjacency）等架構缺陷。在未解決 request-local 授權、邊界 DTO 化以及寫入解耦之前，無法安全地將此讀取邊界獨立切換至 Gateway。

此外，任務記錄（`task.json`、`source-audit.md`、`check.md`）正確地將狀態標記為 `planning`，並明確聲明無任何 runtime、CE、feature gate、consumer、traffic、P7.5 或 P8 的進度，未發現任何不實聲明。

---

## 2. 審查發現 (Findings)

### Critical
*無*

### Warning

#### 寫入相鄰性與缺乏事務保護 (Write Adjacency & Lack of Transactional Integrity)
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs` (`AddContactToListAsync`)
* **原由**：`AddContactToListAsync` 在呼叫 `GetContactCurrentGroup` 讀取當前群組後，會執行一系列的寫入操作（包括 `AddContactToListAsync`、`RemoveContactFromListAsync`、`CreatePresentRecordAsync`、`UpdateEntity`、`AssignOwner` 以及發送 LINE 通知）。這些操作缺乏統一的事務（Transaction）保護、等冪性（Idempotency）保證以及失敗時的回滾（Rollback）或清理機制。若在執行過程中發生異常，會導致 CRM 資料與系統狀態不一致。

#### 歧義查詢語意 (Ambiguous Query Semantics)
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs` (`GetContactCurrentGroup`)
* **原由**：該方法遍歷 `lists.Entities` 並直接返回第一個 `new_app_named` 為 `true` 的實體（first-match）。若聯絡人同時關聯多個符合條件的群組，此邏輯會隱式忽略其他群組，且沒有任何歧義處理機制（Ambiguity Policy），可能導致業務邏輯判斷錯誤。

### Info

#### 可變實體邊界與缺乏 Request-Local 授權 (Mutable Entity Boundary & Missing Request-Local Authorization)
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs` (`GetContactCurrentGroup`)
* **原由**：方法簽章直接接收並處理可變的 CRM SDK `Entity` 物件，且未在方法內部或呼叫鏈中建立基於 authenticated-principal 的不可變 request-local 授權範圍，這限制了將其安全重構為獨立 Gateway 唯讀邊界的能力。

#### 任務記錄正確性驗證 (Task Record Verification)
* **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-contact-current-group-read-boundary/task.json`
* **原由**：經核對，該任務記錄正確地將狀態標記為 `planning`，並明確聲明此任務僅限於 source/design 審查，未執行任何 runtime 程式碼修改、CE 測試、Feature Gate 啟用、流量切換或 P7.5/P8 相關進度。`source-only-local-design-no-go` 的決策完全合理且符合現狀。

---

## 3. 決策驗證與建議 (Decision Validation)

* **`source-only-local-design-no-go` 是否合理？**
  **是**。由於讀取操作與複雜的寫入副作用緊密耦合，且缺乏 DTO 邊界與歧義處理，目前無法進行獨立的讀取切換。
* **是否錯誤聲稱進度？**
  **否**。記錄中明確指出無任何 runtime、CE、feature gate、traffic、P7.5 或 P8 進度，且標註了雙模型運行未完成（dual-model-incomplete），符合實際情況。
