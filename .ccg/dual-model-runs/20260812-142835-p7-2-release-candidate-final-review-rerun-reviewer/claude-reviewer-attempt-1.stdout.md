# P7.2 continuation Release Candidate 最終審查（rerun）

審查方式：直接讀取 `git diff HEAD`（17 個已修改檔＋新增測試檔）、Trellis task artifacts（`design.md`／`implement.md`／`release-candidate.md`／`check-progress-2026-08-12.md`）。另外以本機 `dotnet build`／`dotnet test` 驗證編譯與既有測試綠燈；並以一個暫時性、僅使用 fake `IOrganizationService` 的 unit test（不涉 CRM／CE／流量）驗證下方 Critical 發現的實際行為，驗證後已刪除該暫存檔（`git status` 確認樹狀狀態與驗證前一致）。未執行、未要求任何 CRM、CE、feature flag、流量、CE 8.2 或 Official Worker 動作。

---

## Critical

### 1. `DownloadIntegrateData.SetupIntegrateData(..., IOrganizationService)` 在登入失敗時仍會以呼叫端提供、未經授權的 `ListEntityId`／`WeeklyReportEntityId` 對 CRM 執行成員與週報讀取 — 違反本次變更自己宣稱的 fail-closed 契約

- **檔案／方法**：
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Core.cs:163-201`（新 `SetupIntegrateData(...,organizationService)` 入口）
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Setup.cs:106-166`，特別是第 120-126 行 `SetupHeaderData` 對 `loginContact == null` 的 `return;` 早退
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Members.cs:118-158`（`SetupOperationLocalLeaderMembers`）

- **可重現條件**：
  以任意錯誤帳密、`LoginType = "小組長"`、以及攻擊者自選的 `ListEntityId`（不需與該帳密有任何 CRM 關係）呼叫新入口：
  ```csharp
  subject.SetupIntegrateData(
      "wrong-account", "wrong-password", "小組長",
      someDate, attackerChosenListId, "",
      ref report, borrowedService);
  ```
  `Core.cs:198` 的 `SetupOperationLocalLeaderMembers(ListEntityId, WeeklyReportEntityId, ref operationReport, organizationService)` 使用的是**呼叫端原始傳入的 `ListEntityId` 參數**，而不是 `operationReport.ListEntityId`（該欄位在登入失敗時從未被 `SetupHeaderData` 寫入，因為 `RetrieveOperationLocalLeaderList` — 唯一的伺服器端名單－小組長授權檢查 — 根本沒被呼叫到，`SetupHeaderData` 在呼叫它之前就已經 `return`）。因此無論登入是否成功，`Core.cs` 都無條件接著呼叫 `SetupOperationLocalLeaderMembers` → `GetOperationLocalMembersFromList`／`GetOperationLocalMembersFromPresentRecords` → 直接對 CRM 執行 `listmember`／`new_present_record` 查詢，讀出成員姓名、電話、地址、生日等 PII；若同時帶入非空 `WeeklyReportEntityId`，`SetupWeeklyReportData` 也會用同一個未授權 ID 讀出週報備忘與分析內容。

  **已以實測驗證**：暫時加入一個 fake `IOrganizationService`（無任何符合帳密的 contact、`listmember` 查詢會被 flag），呼叫上述路徑後 `listmember` 查詢確實被執行（斷言「不應執行」的測試 FAIL，附帶訊息顯示查詢已跑且最終才被 `InvalidOperationException` 中止）。驗證後已移除該暫存測試檔，未留在工作樹。

- **實際影響**：
  這正是本次變更文件自己聲稱已解決、且審查要求逐項核對的安全邊界（`Setup.cs:210-218` docstring：「即使有效小組長將另一個小組的 GUID 傳入，也無法讀取其內容」；`Identity.cs`／`FollowUp.cs`／`Members.cs` 多處註解也重複「必須在任何登入、名單、週報或圖表 CRM I/O 之前拒絕」）。實際控制流並未兌現此承諾：只有「登入成功但名單不屬於該小組長」的情境會被 `RetrieveOperationLocalLeaderList` 擋下（因為它會擲例外中止整條同步呼叫鏈）；「帳密根本不存在」的情境完全繞過授權檢查，直接對 CRM 執行讀取。
  目前之所以 `ref aListSmallGroupWeeklyReport` 最終沒有回傳到呼叫端，只是因為 `operationReport.ListEntityId` 恰好是 `null`，導致下游 `SetupWeeklyReportChartData`（`Setup.cs` 新 overload）的 `Guid.TryParse` 失敗而擲出例外，屬於**巧合**而非設計出的守門機制——這正是本審查明確要求排除的「以暫存欄位或例外湊巧達成隔離」模式。CRM I/O（含 PII 讀取）已經在無任何身分驗證下真實發生，只是尚未透過此路徑洩漏回呼叫端。
  **目前無正式呼叫端**：已確認 `EquipmentController`、`SmallGroupController`、`PersonalController`、`NewPersonController`、`AuthenticationController`、`ListManagerCacheExtensions` 等所有現有呼叫點仍使用舊版 `ListManager.SetupIntegrateData(string)` 單參數 overload，尚未接上新的 service-aware 入口，因此**目前生產流量無法觸發**。但 `release-candidate.md` 已將此路徑列為「group-leader 唯讀路徑…fail-closed 均有回歸測試」的既定事實，作為未來 P7.4 切流的依據之一，而該宣稱與實際程式碼行為不符。

- **不擴大範圍的修正建議**：
  在 `SetupHeaderData`（`Setup.cs`）登入失敗分支，比照同檔案其他新 helper 的既有慣例（`ArgumentException`／`InvalidOperationException` fail closed），改為擲出例外而非 `return;`；或在 `Core.cs:198` 呼叫 `SetupOperationLocalLeaderMembers` 之前，立即檢查 `operationReport.LoadFlag`，false 則直接擲例外，不再繼續呼叫成員／週報／圖表方法。兩者擇一即可，不需重構其餘已驗證正確的 A/B 隔離、Dispose、fault-preservation 邏輯。

- **測試覆蓋缺口**：`DownloadIntegrateDataOperationServiceIntegrationTests.cs` 已涵蓋「錯誤 LoginType」「登入成功但不擁有名單」「服務中途故障」「A/B 交錯」四種情境，唯獨遺漏「帳密不存在＋合法 LoginType＋任意 ListEntityId」這個情境，而這正是漏洞觸發路徑。建議補一個等價於本次暫存驗證用例的回歸測試（斷言：登入失敗時 `listmember`／`new_present_record`／週報查詢呼叫次數必須為 0）。

---

## Warning

### 2. 本次變更引入多個「尚未接上 Core 入口」的 operation-local helper，其中包含一個 mutation helper，卻無任何呼叫端與測試覆蓋

- **檔案／方法**：
  - `DownloadIntegrateData.Identity.cs:51-113`：`RetrieveIdentityPresentRecords`、**`UpdateIdentityContact`**（會呼叫 `organizationService.Update(contact)`）
  - `DownloadIntegrateData.FollowUp.cs:47-152`：`RetrieveFollowUpContact`、`RetrieveFollowUpPresentRecords`、**`UpdateFollowUpPresentRecord`**（同樣呼叫 `.Update`）
  - `DownloadIntegrateData.Members.cs:1106-1121`：`RetrieveMemberContact(Guid, IOrganizationService)` overload

- **狀況**：這些方法在整個 repo（含所有測試）中都沒有任何呼叫點，屬於死碼（design.md／check-progress 也承認「尚未由 Core 入口啟用」）。其中 `UpdateIdentityContact` 與 `UpdateFollowUpPresentRecord` 是**寫入**方法，出現在一個文件與 commit 訊息都強調「read-only」「group-leader 唯讀路徑」的變更集裡；雖然目前不可達、不影響本次審查要求的 read-only 不變量，但它們沒有對應的 fail-closed／owner 驗證測試，一旦未來被草率接上（例如直接複製貼上呼叫），會在缺乏審查的情況下引入寫入路徑。
- **建議**：在真正要接上 Core 入口的那個 slice 再一併引入這些 helper 與其測試，或至少為這兩個 mutation helper 補上「呼又端必須先驗證登入與 owner」的顯式測試與呼叫端骨架，避免它們以「已完成」姿態存在於 RC 分支中卻缺乏可驗證的呼叫路徑（與本任務先前 Slice C cycle 的 no-go 教訓一致：不要讓「helper 已寫好」被誤讀為「CE/流程已完成」）。

---

## Info

### 3. `GetMemberCollection` 例外訊息有多餘尾隨空白
- `DownloadIntegrateData.Members.cs`（新 `GetMemberCollection(Guid, bool, IOrganizationService)` overload）：`"operation-local 動態名單缺少 query；已拒絕回落至共用 ToolUtility。 "` 結尾多一個全形句號後的空白字元。不影響行為，建議清理。

### 4. `ListSmallGroupWeeklyReport`、`ListManager` 的延遲初始化與 fail-closed overload 正確
- `GetUploadIntegrateDataForMutation()`（`ListSmallGroupWeeklyReport.cs`）僅在實際 mutation（Upload/UploadAsync/DeleteMember）時才建立 `new UploadIntegrateData()`，不涉及呼叫端借用的 `IOrganizationService`，未改變既有寫入路徑或資源所有權。
- `ListManager.SetupIntegrateData(string, IOrganizationService)` 一律在讀取任何 session mutable state 或執行 CRM I/O 之前 `throw new InvalidOperationException`，沒有 fallback 到共用 ToolUtility，符合要求。
- `DownloadIntegrateData.SetupIntegrateData(...,organizationService)` 對「LoginType ≠ 小組長」與「登入小組長不擁有指定名單」兩種情境，確實在任何 CRM I/O 前／中途正確 fail closed 並保留呼叫端舊輸出引用（已有對應通過的回歸測試，本機執行 5/5 passed）；service 未被保存、Dispose 或寫入 instance/static 欄位（`AssertDoesNotRetainBorrowedService` 反射檢查通過）。
- D–H local-only catalog／executor 與 Data8 evidence sanitization（`LivePackage02Data8ListManagementEvidenceTests.cs`、`Invoke-Package02Data8ListManagementEvidence.Tests.ps1`）新增的 raw-data／stream 洩漏防護測試設計完整，涵蓋正向與負向情境，未發現問題。
- `dotnet build`（Release）與既有 isolation 測試在本機執行皆為 0 warnings / 0 errors、全數通過，與文件所述一致。

---

## 結論

**不可視為可發布**：Critical #1 是一個具體、可重現、已透過本機測試驗證的 fail-closed 缺口，直接牴觸本 RC 文件宣稱「group-leader 唯讀路徑…fail-closed 均有回歸測試」的說法，也牴觸審查要求的安全契約第 3、4 點。雖然目前無生產呼叫端、CE 未執行，本機測試通過**不能**視為此路徑已通過驗證的證據——本次審查發現的正是「本機測試綠燈但安全邊界仍有缺口」的情況。建議：修正 Critical #1（範圍很小，僅需在 `SetupHeaderData` 登入失敗分支或 `Core.cs` 呼叫序列補一個 fail-closed 檢查）並補齊對應回歸測試後，方可將此 slice 視為對 P7.4/P7.5 切流有意義的證據；Warning #2 建議在正式接線前一併處理或至少記錄為已知風險。

---
SESSION_ID: e1136857-648b-4a52-a947-cb7b578cf22d
