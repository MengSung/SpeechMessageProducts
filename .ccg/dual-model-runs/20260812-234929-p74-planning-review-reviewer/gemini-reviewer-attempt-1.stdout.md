# P7.4 Planning Review: ChurchReport ProductClient Cutover 分析報告

本報告針對 `P7.4` 規劃工件及儲存庫證據進行安全審查，評估 `ChurchReport` `ProductClient` 能力切換（cutover）的安全性與合規性。

---

## Critical Findings (嚴重問題)

### 1. SDK `EntityCollection` 橋接與 N+1 查詢效能隱患
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Services\StorLessonQueryService.cs`
* **程式碼行號**：第 114-129 行 (`GetEntityCollectionByContact` / `GetEntityCollectionByDiscipleLesson`)、第 318-344 行 (`ToEntityCollection`)
* **判定理由**：
  `GetEntityCollectionByContact` 與 `GetEntityCollectionByDiscipleLesson` 雖然在內部呼叫了型別化的 `IPackage01FeeReadClient`，但為了相容舊有呼叫端，隨後呼叫了 `ToEntityCollection`。該方法在迴圈中針對每個投影出的 ID 呼叫 `_utility.RetrieveEntity("new_stor_lessons", id)`。
  這違反了**「不得接受 SDK Entity/EntityCollection 橋接作為已完成型別化遷移」**的硬性約束，且在運行時會產生嚴重的 N+1 次 CRM 查詢，造成極大的效能隱患。此路徑絕不能被視為已完成遷移。

### 2. Package01 啟用路徑中殘留隱式 SDK 呼叫
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Services\StorLessonQueryService.cs`
* **程式碼行號**：第 238-254 行 (`MapDtos` 豐富化邏輯)
* **判定理由**：
  在 `MapDtos` 方法中，當處理來自 ProductClient 的 `StorLessonRecordDto` 時，若 `discipleId` 存在，程式碼會呼叫 `_utility.RetrieveEntity("new_disciple_lessons", dId)` 來取得 `classStartDate` 與 `stageName`。
  這意味著即使 `_package01Enabled` 設為 `true`，該唯讀路徑依然會觸發 legacy SDK 呼叫，未能達成完全去 SDK 化（no-SDK）的隔離邊界要求。

### 3. 消費端進入點強依賴 SDK `Entity` 類型
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Services\DonationFeeQueryService.cs`
* **程式碼行號**：第 57-63 行 (`FillFeeListAsync`)
* **判定理由**：
  `FillFeeListAsync` 的方法簽章強制要求傳入 `Microsoft.Xrm.Sdk.Entity contact`。這導致上游呼叫端（例如 `DonationDedicationFeeFormService.cs` 第 111 行 `RetrieveEntity`）在呼叫此服務前，必須先執行 SDK 查詢以獲取 `Entity` 物件。能力邊界劃分不徹底，使得消費端無法真正與 SDK 解耦。

---

## Warning Findings (警告事項)

### 1. 缺乏自動化等價性（Parity）測試
* **檔案路徑**：`.trellis\tasks\08-12-churchreport-productclient-cutover\implement.md`
* **規劃位置**：Phase 2 & Phase 3 測試規劃
* **判定理由**：
  規劃中僅安排了針對單一服務的單元測試（如 `FullyQualifiedName~DonationFeeQueryService`），並未規劃新舊路徑（Legacy vs Package01）輸出結果的自動化比對（shadow read / parity test）。這使得切換過程高度依賴手動驗證，容易遺漏欄位對應不一致的問題。

### 2. 准入授權與 Drain Runbook 缺乏具體落地步驟
* **檔案路徑**：`.trellis\tasks\08-12-churchreport-productclient-cutover\design.md`
* **規劃位置**：第 43-47 行 (enablement gate)
* **判定理由**：
  設計文件雖正確指出啟用 gate 需要「durable distributed admission/host-slot authority」或「drain-first non-overlap runbook」，但實施步驟（`implement.md`）中並未包含這些機制的具體配置、部署順序或演練計畫，僅將其作為 "no-go" 的判斷條件，缺乏實質可操作性。

---

## Info Findings (參考資訊)

### 1. 設定檔預設值安全防護
* **檔案路徑**：`SpeechMessageProducts.ChurchReport\Services\DonationFeeQueryService.cs`
* **程式碼行號**：第 46 行
* **說明**：
  建構子參數 `package01FeeReadsEnabled` 預設值為 `false`，且 `appsettings.json` 中相關旗標亦保持為 `false`，符合「第一批次僅限唯讀且所有 gate 保持 false」的安全約束。

### 2. 程式碼格式與編碼掃描
* **檔案路徑**：`.trellis\tasks\08-12-churchreport-productclient-cutover\implement.md`
* **說明**：
  實施步驟中已規劃 `git diff --check` 與 UTF-8 no-BOM/CRLF 掃描，有助於維持整個工作區程式碼格式的一致性。
