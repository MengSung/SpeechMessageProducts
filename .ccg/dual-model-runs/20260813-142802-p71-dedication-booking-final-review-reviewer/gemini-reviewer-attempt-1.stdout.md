# P7.1 認獻單強型別讀取能力最終審查報告 (p71-dedication-booking-final-review)

本報告針對工作樹中 **P7.1 ORG-CALL-00041** 的變更進行審查。審查範圍限於 `payments.dedication.retrieve.by.contact` 的 registry、Data8 executor、ProductClient DTO 讀取、Phase-0 契約一致性，以及相關的本機隔離與資源釋放測試。

---

## 審查發現與決策分類

### 🔴 Critical Findings (關鍵缺陷)
* **無**。
  * **跨使用者/Profile 隔離性**：`Package01DedicationBookingReadClient` 實作為無狀態（Stateless）且 Request-Local。每次呼叫皆將 `profileAlias` 與 `workloadSubjectId` 傳遞至底層 Executor，且回傳的 DTO 列表（`DedicationBookingRecordDto`）為全新反序列化且唯讀的防禦性複本，無任何靜態快取或跨 request 狀態殘留，有效防止跨 Profile A/B 數據洩漏。
  * **Raw CRM Entity 洩漏防範**：`OperationResponseData` 採用封閉聯集（Closed Union）設計。在 `ExecuteDedicationBookingByContact` 中，CRM `Entity` 在 Connector 內部即被投影為 `Package01DedicationBookingRecord` 標量記錄，並在 `TryValidateDedicationBookingRecords` 中進行嚴格的單一分支驗證與 byte budget 檢查，確保原始 SDK `Entity` 或 `EntityCollection` 不會越過邊界外洩至產品端。

### 🟡 Warning Findings (警告事項)
* **無**。
  * **Query 可控性與安全邊界**：`CreateDedicationBookingByContactQuery` 採用完全寫死的固定 `QueryExpression`，僅以強型別的 `contactId` (Guid) 作為過濾條件。相容性參數 `contactName` 被安全忽略，不影響查詢範圍，caller 無法注入自訂的 FetchXML、QueryBase 或 logical name。
  * **資源釋放與租約生命週期**：Data8 執行器在 `ExecuteDedicationBookingByContact` 中使用單一 `RetrieveMultiple` 進行有界分頁投影，並在 request 結束時透過既有的 `await using` 機制立即釋放/歸還 connector 租約，無 N+1 查詢，避免了連線池租約耗盡風險。
  * **回歸風險與 Feature Gate**：本階段（P7.1）僅建立底層強型別讀取能力，未修改 ChurchReport 消費端（`DonationBookingService` 仍維持 legacy 狀態），且 `Package01FeeReadsEnabled` 等 Feature Gate 均保持為 `false`，確保 rollback 路徑完整，無影子流量或雙讀回歸風險。

### 🔵 Info Findings (一般資訊)
* **Matrix Drift 驗證**：`OperationRegistryAgreementTests` 已成功驗證 compiled registry 與 Phase-0 JSON matrix (`phase0-organization-call-matrix.json`) 的一致性，包含 `payments.dedication.retrieve.by.contact` 的參數、response kind、page/byte budget 等，無 matrix drift。
* **編碼與文件規範**：新增與修改的 C# 檔案均包含完整繁體中文 XML 註解，且檔案格式符合 UTF-8 no-BOM 與 CRLF 規範。

---

## 審查結論

本機變更已完整且正確地實作了 `ORG-CALL-00041` 所需的強型別讀取能力，並通過了 A/B 隔離、錯誤分支、參數驗證、Data8 投影等 TDD 測試。本階段未切換 consumer 且 Feature Gate 保持關閉，符合 P7.1 階段性交付與 rollback 規範。本機測試結果未被視為 CE、consumer cutover、P7.5 或 P8 的部署憑證。
