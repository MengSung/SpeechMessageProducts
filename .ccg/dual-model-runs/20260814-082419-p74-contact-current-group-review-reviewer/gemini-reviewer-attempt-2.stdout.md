# P7.4 ORG-CALL-00052 原始碼審查報告 (Source-Audit Review)

本報告針對任務 `.trellis/tasks/08-14-08-14-p74-contact-current-group-read-boundary/` 及其引用的源代碼 `ContactService.GetContactCurrentGroup` 與 `AddContactToListAsync` 進行審查，驗證 `source-only-local-design-no-go` 決策的合理性，並確認任務記錄是否正確反映進度。

---

## 1. 審查摘要 (Summary)

經審查，`source-only-local-design-no-go` 的決策**完全合理且被充分證實**。
`ContactService.GetContactCurrentGroup` 存在可變實體邊界、首筆匹配歧義以及與多個寫入副作用緊密相鄰（Write Adjacency）等架構缺陷。在未解決 request-local 授權、邊界 DTO 化以及寫入解耦之前，無法安全地將此讀取邊界獨立切換至 Gateway。

此外，任務記錄（`task.json`）正確地將狀態設為 `planning`，並明確指出「no runtime, CE, feature gate, traffic, P7.5 or P8 action」，**未錯誤宣稱**任何執行期、CE、功能閘、流量或後續階段的進度。

---

## 2. 審查發現 (Findings)

### Critical (關鍵)

#### 1. 寫入鄰接 (Write Adjacency) 阻礙讀寫分離
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs` (第 373-431 行 `AddContactToListAsync`)
* **原由**：
  `AddContactToListAsync` 在呼叫 `GetContactCurrentGroup` 讀取當前群組後，會根據讀取結果執行不同的寫入分支：
  * 若無當前群組，呼叫 `AddContactToNewListAsync`。
  * 若有當前群組且符合特定條件，呼叫 `TransferContactBetweenListsAsync`。
  
  這兩個分支包含多個寫入操作（包括 `AddContactToListAsync`、`RemoveContactFromListAsync`、`CreatePresentRecordAsync`、`UpdateEntity`、`AssignOwner` 以及發送 LINE 通知）。這些操作散落在多個服務中，缺乏統一的事務（Transaction）保護、等冪性（Idempotency）保證以及失敗時的回滾（Rollback）或清理機制。
  
  若在執行過程中發生異常，會導致 CRM 資料與系統狀態不一致。如果將 `GetContactCurrentGroup` 讀取操作拆分為獨立的 Gateway 邊界，而寫入操作仍留在 Legacy 系統中，將會面臨嚴重的分散式交易一致性問題與競態條件。因此，在寫入解耦與等冪性機制建立前，無法進行部分讀取切換（partial read cutover）。

---

### Warning (警告)

#### 1. 非確定性 First-match 查詢語義
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs` (第 518-540 行 `GetContactCurrentGroup`)
* **原由**：
  `GetContactCurrentGroup` 內部呼叫 `_toolUtility.QueryListOfContactManyToMany(contact.Id)` 查詢聯絡人所屬的群組，並遍歷結果返回第一個 `new_app_named == true` 的群組。由於該查詢未指定任何排序欄位（`Order By`），在多群組關聯的情況下，這會導致非確定性（Non-deterministic）的結果，違反了獨立邊界的確定性語義。

#### 2. 缺乏 Request-Local 授權上下文
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs` (第 518-540 行 `GetContactCurrentGroup`)
* **原由**：
  `GetContactCurrentGroup` 僅依賴傳入的 `contact.Id` 進行全域查詢，並未將登入者身分（如 `loginContact.Id`）或任何 request-local 授權上下文傳遞至查詢中，存在越權風險。

#### 3. CRM SDK 實體耦合
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs` (第 518-540 行 `GetContactCurrentGroup`)
* **原由**：
  `GetContactCurrentGroup` 接收並返回 `Microsoft.Xrm.Sdk.Entity`，這與 DTO-only 邊界所需的鬆耦合、序列化友善的 DTO 設計相違背。

---

### Info (提示)

#### 1. 任務記錄進度驗證
* **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-contact-current-group-read-boundary/task.json`
* **原由**：
  經核對，任務記錄的狀態為 `planning`，且明確指出「no runtime, CE, feature gate, traffic, P7.5 or P8 action」，並未錯誤宣稱任何執行期、CE、功能閘、流量或後續階段的進度。雙模型執行狀態（Gemini 可用，Claude 無可用輸出）亦被正確記錄。

---

## 3. 審查決定 (Decision)

* **`source-only-local-design-no-go` 決策合理性**：**合理 (PASS)**
* **任務記錄進度宣稱正確性**：**正確 (PASS)**
