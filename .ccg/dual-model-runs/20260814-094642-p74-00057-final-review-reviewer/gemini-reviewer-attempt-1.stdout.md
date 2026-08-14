# P7.4 ORG-CALL-00057 最終審查報告

本報告針對 P7.4 本地專用（local-only）App-named 會員資格讀取數據平面（ORG-CALL-00057）進行最終審查。審查範圍涵蓋 Abstractions、Connectors、ProductClient 及相關單元測試文件。

---

## 審查發現與決策

### Critical 發現
* **無**：未發現影響跨用戶隔離、資源所有權或違反安全合約的嚴重漏洞。

---

### Warning 發現

#### 1. 響應位元組大小估算使用硬編碼常數
* **文件路徑**：`SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs` (第 183 行)
* **理由**：`AppNamedMembershipFixedRowBytes` 被硬編碼為 `32`。雖然目前 `AppNamedMembershipRecord` 僅包含 `ListId` (Guid) 與 `ListName` (string)，但此硬編碼的固定行開銷在未來 DTO 結構擴展時若未同步修改，將導致 `TryAddAppNamedMembershipRecordBytes` 的位元組預算估算失效，進而可能繞過 32-KiB 的安全限制。
* **建議**：在此常數定義處加入代碼註釋，提醒未來修改 DTO 結構時必須同步更新此估算值，或透過單元測試動態驗證序列化大小。

---

### Info 發現

#### 1. 確定性排序與嚴格的 Inner Join 關聯
* **文件路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs` (第 700-725 行)
* **理由**：`CreateAppNamedMembershipByContactQuery` 嚴格實現了合約要求的查詢邏輯。它使用 `JoinOperator.Inner` 關聯 `listmember` 實體，並將條件限制在傳入的 `contactId`。此外，排序採用 `listname` 升序與 `listid` 升序的雙重排序，確保了查詢結果的確定性（deterministic sorting），避免了 Dynamics 數據庫因物理存儲順序變化而導致的隨機性。

#### 2. 完善的雙重 Fail-Closed 驗證機制
* **文件路徑**：
  * `SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs` (第 183-216 行)
  * `SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs` (第 1092-1113 行)
* **理由**：系統在數據讀取與 DTO 構建兩個階段皆實施了嚴格的驗證。在 Data8 讀取階段，若發現分頁（`MoreRecords` 或 `PagingCookie` 不為空）、記錄數超限（>32）或位元組超限（>32-KiB），會立即拋出 `InvalidOperationException`；在 `OperationResponseData` 的建構子中，亦會透過 `IsValidAppNamedMembershipRecords` 再次驗證重複的 `ListId` 與位元組大小。此雙重防禦確保了任何異常情況下系統皆能 fail closed。

#### 3. 嚴格的跨用戶與 Profile 隔離
* **文件路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs` (第 114-120 行)
* **理由**：Executor 僅接受由 `IProfileResolver` 解析的部署擁有（deployment-owned）的 profile，不允許 caller 傳入自定義的連線資訊或 CRM 路由。傳入的 `contactId` 亦在入口處進行了非空驗證，確保了多租戶/多用戶環境下的資源隔離安全性。

---

## 審查結論
本設計完全符合 `ORG-CALL-00057` 的合約約束：預設停用、僅限 DTO、本地讀取，且具備完善的確定性排序與 Fail-Closed 機制。測試覆蓋率良好（Focused 測試與 Dynamics 測試全數通過）。在修正或記錄 Warning 提及的硬編碼常數後，即可安全併入主分支。
