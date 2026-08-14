# P7.4 ORG-CALL-00057 邊界讀取可行性分析報告

本報告針對 `ORG-CALL-00057` (`list.membership.retrieve.appnamed.by.contact`) 作為下一個獨立、僅限本地的 P7 功能進行可行性評估。

---

### 1. 是否安全實作 (Go/No-Go 決策)
**決策：No-Go (目前不安全，無法立即實作)**

**核心理由：**
*   **寫入鄰接性阻礙 (Write Adjacency Blocker)**：主要呼叫端 `ContactService.AddContactToListAsync` 與 `DownloadListManager` 在呼叫此查詢後，會立即觸發一系列寫入操作（如新增/移除成員、建立出席紀錄、更新聯絡人屬性、指派 Owner）及外部副作用（發送 LINE 通知）。在沒有分散式交易一致性、等冪性與回滾機制前，無法安全地將此讀取操作拆分為獨立的 Gateway 邊界。
*   **非確定性首筆匹配 (Non-deterministic First-match)**：`QueryListOfContactManyToMany` 查詢未指定任何排序欄位（`Order By`），而呼叫端（如 `GetContactCurrentGroup` 與 `DownloadListManager`）皆採用 First-match（取結果的第一筆）行為。在多群組關聯下，這會導致非確定性的業務結果。
*   **缺乏 Request-Local 授權上下文**：現有查詢僅依賴傳入的 `contactId` 進行全域檢索，未傳遞登入者身分（如 `loginContactId`）進行權限隔離，存在越權風險。
*   **CRM SDK 實體耦合**：現有方法傳入與返回 raw `Entity` 及 `EntityCollection`，違反了 DTO-only 邊界的鬆耦合要求。

---

### 2. 強制輸入與回應基數/邊界 (Input & Response Cardinality/Bounds)
若未來獲准實作，必須遵循以下規格：
*   **強制輸入參數**：
    *   `contactId` (Guid, 必填)
    *   `loginContactId` (Guid, 必填，用於 request-local 授權過濾)
*   **回應基數與邊界限制**：
    *   必須使用不可變的 DTO（例如 `ContactGroupDto`），嚴禁返回 `Entity` 或 `EntityCollection`。
    *   分頁與流量限制：最大分頁數 `maximumPageCount: 4`，單頁最大位元組 `maximumPageBytes: 65536`，累積最大位元組 `maximumCumulativeResponseBytes: 262144`，最大結果筆數 `maximumResultItemCount: 2`。
*   **重複/歧義語義 (Duplicate Semantics)**：
    *   查詢必須加入明確的排序（例如依 `createdon` 降冪）。
    *   若查詢到多個符合 `new_app_named == true` 的群組，應有明確的歧義處理策略（例如返回 `ambiguous` 狀態或依排序確定性取首筆），禁止隨機匹配。

---

### 3. 嚴禁的呼叫路徑與消費者 (Prohibited Consumers/Call Paths)
在未完成重構前，嚴禁將以下路徑切換至 Gateway 或進行 ProductClient 割接：
*   `ContactService.AddContactToListAsync` -> `ContactService.GetContactCurrentGroup` -> `QueryListOfContactManyToMany`
*   `DownloadListManager.GetListManager` -> `QueryListOfContactManyToMany` -> `FilterPersonalListEntity` -> `ProcessPersonalListEntity`
*   `NewPerson.cs` 中涉及 `QueryListOfContactManyToMany` 的所有 N:N 群組查詢與寫入鄰接路徑。

---

### 4. 復原條件 (Recovery Conditions)
若要使此功能達到 Go 狀態，必須滿足以下復原條件：
1.  **授權隔離**：重構 `QueryListOfContactManyToMany` 簽章，強制要求傳入 `loginContactId`，並在 CRM 查詢中加入權限過濾，確保符合 request-local 授權原則。
2.  **確定性排序**：在 `QueryExpression` 中加入 `OrderExpression`（例如依 `createdon` 降冪排序），確保 First-match 行為是確定的。
3.  **寫入解耦**：將讀取操作與後續的寫入/通知副作用進行架構解耦，建立補償機制（Rollback/Cleanup）或等冪性保證。
4.  **隔離測試**：在 `ChurchReport.MemberInfo.Tests` 中建立單元測試，模擬多群組關聯情境，驗證確定性排序、邊界限制及 DTO 轉換邏輯。

---

### 5. 關鍵發現分類 (Findings)

#### Critical (關鍵)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs` (`GetContactCurrentGroup` / `AddContactToListAsync`)
    *   **說明**：讀取與寫入高度鄰接（Write Adjacency）。讀取結果直接決定後續的多個寫入分支與 LINE 通知發送，缺乏分散式交易保護，強行切離讀取邊界將導致嚴重的資料不一致與競態條件。

#### Warning (警告)
*   **檔案路徑**：`ToolUtility/QueryOperations/RelationshipQueryService.cs` (`QueryListOfContactManyToMany`)
    *   **說明**：查詢未使用任何排序（`Order By`），且 ColumnSet 設為 `AllColumns = true`（過度選取）。這在多群組關聯下會產生非確定性結果，且不符合 bounded-read 的欄位投影規範。
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs` (第 214 行)
    *   **說明**：直接呼叫 `QueryListOfContactManyToMany` 並將結果存入 `this.m_Lists`，隨後進行 `FilterPersonalListEntity` 與 `ProcessPersonalListEntity` 等包含狀態變更與副作用的操作，同樣存在讀寫邊界耦合與非確定性首筆匹配問題。

#### Info (資訊)
*   **檔案路徑**：`.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/authoritative-gap-matrix.json`
    *   **說明**：`ORG-CALL-00057` 目前在矩陣中標記為 `not-migrated` 且 `data8Executor` 為 `not-implemented`，與本次 No-Go 評估結果一致。
