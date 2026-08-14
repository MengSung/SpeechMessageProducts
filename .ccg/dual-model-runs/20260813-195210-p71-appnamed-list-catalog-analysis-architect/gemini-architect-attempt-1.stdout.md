# P7.1 app-named list catalog typed read：唯讀設計分析報告

本報告針對 `ORG-CALL-00014` 建立 `list.catalog.retrieve.app.named` 之 server-owned、bounded、DTO-only Data8 / ProductClient 唯讀架構與安全進行分析，並提出 Critical / Warning / Info 結論。

---

## 結論分類與發現

### Critical (危急)

#### 1. 嚴格禁止與 `ORG-CALL-00065` 共享快取與記憶體實體
* **檔案路徑**：`ToolUtility/ListOperations/ListService.cs` (Legacy) 及新設計之 `ProductClient` 實作。
* **原理與風險**：Legacy 中 `ORG-CALL-00065` 的現有 consumer 使用了共享的 `EntityCollection` memory cache。本 child (`ORG-CALL-00014`) 必須完全隔離，**不得使用任何快取機制**，亦不得返回 `Entity` 或 `EntityCollection` 實體。ProductClient 必須將 records 防禦性複製為 request-local DTO，避免因共享快取導致資料污染或生命週期混亂。

#### 2. 查詢條件與排序一致性硬編碼 (Hardcoded Constraints)
* **檔案路徑**：`SpeechMessage.Dynamics.Abstractions/Operations/Package01OperationRegistry.cs` (待新增定義)
* **原理與風險**：為確保與 legacy `RetrieveLists()` 行為完全一致，Data8 端的 `QueryExpression` 必須硬編碼以下過濾條件與排序，絕不允許 caller 傳入參數進行修改：
  * `statuscode = 0` (Active)
  * `purpose = "小組名單"` (對應 legacy 亂碼 `撠??` 解碼後之繁體中文)
  * `new_app_named = 1`
  * 排序：`listname` 降冪排序 (`descending = true`)。
  若 caller 企圖傳入任何查詢參數，系統必須實施 **Fail-Closed** 拋出異常。

---

### Warning (警告)

#### 1. 邊界限制與超限防禦 (Paging & Size Bounds)
* **檔案路徑**：`SpeechMessage.Dynamics.Abstractions/Operations/OperationDefinition.cs`
* **原理與風險**：合約規定最多 4 頁 / 每頁 64 KiB / 累計 256 KiB / 最多 4096 筆結果。Data8 執行器在進行 `RetrieveMultiple` 時，必須嚴格監控 `EntityCollection.MoreRecords` 與累計資料大小。一旦超過此硬性限制，必須進行防禦性截斷或拋出超限異常，防止記憶體耗盡 (OOM) 或惡意大數據查詢攻擊。

#### 2. A/B Profile 隔離與多租戶安全
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient/ListManagement/Package02ListManagementClient.cs` (參考設計)
* **原理與風險**：ProductClient 必須接受 `profileAlias` 與 `workloadSubjectId`，並將其傳遞至底層的 `IDynamicsOperationExecutor`。必須確保不同部署環境 (Deployment-owned profile) 之間的連線完全隔離，嚴禁跨 profile 混用連線或洩漏資料。

---

### Info (提示)

#### 1. 欄位對應與 DTO 唯讀設計
* **檔案路徑**：`SpeechMessage.Dynamics.ProductClient/Models/ListCatalogRecordDto.cs` (待新增)
* **說明**：`ListCatalogRecordDto` 應為封閉的唯讀 record，僅包含以下欄位：
  * `ListId` (Guid, 必填)
  * `ListName` (string)
  * `CreatedFromCode` (int?, 對應 `createdfromcode` OptionSetValue)
  * `LastUsedOn` (DateTimeOffset? / DateTime? UTC, 對應 `lastusedon`)
  * `Purpose` (string, 對應 `purpose`)

#### 2. 專案範疇與無侵入性聲明 (No-Go Claims)
* **說明**：
  * 目前 repository 中無外部對 `RetrieveLists()` 的呼叫，因此**不進行 ChurchReport consumer cutover**，亦不建立 feature gate 或 CE request。
  * 歷史 P7.2 Slice C 已清理完成，本 child 完全不予觸碰。
  * 本分析不包含、亦不建議任何 CE 變更、feature enablement、流量切換、P7.5 移除或 P8 相關工作。
